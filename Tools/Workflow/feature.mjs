import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, renameSync, unlinkSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

const risks = ['routine', 'gameplay', 'major'];
const stages = ['Queued', 'In progress', 'Testing', 'Awaiting approval'];
const idPattern = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;
const shaPattern = /^[0-9a-f]{40}$/;

function requireThat(condition, message) {
  if (!condition) throw new Error(message);
}

function run(command, args) {
  return execFileSync(command, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] }).trim();
}

function read(path) {
  return JSON.parse(readFileSync(path, 'utf8'));
}

function nonempty(value) {
  return typeof value === 'string' && value.trim().length > 0;
}

function fields(object, names, label) {
  for (const name of names) requireThat(nonempty(object?.[name]), label + ' needs ' + name);
}

function list(value, label) {
  requireThat(Array.isArray(value) && value.length && value.every(nonempty), label + ' needs at least one entry');
}

function decision(value, label) {
  fields(value, ['by', 'reference'], label);
  requireThat(value.result === 'approved', label + ' needs an explicit approval');
}

function validate(record, id) {
  requireThat(record.schema === 1 && record.id === id && idPattern.test(id), 'Invalid task ID or schema: ' + id);
  fields(record, ['title', 'outcome', 'rollback'], id);
  requireThat(risks.includes(record.risk), 'Invalid risk for ' + id);
  list(record.scope, 'Scope');
  list(record.acceptance, 'Acceptance');
  list(record.nonGoals, 'Non-goals');
  const step = stages.indexOf(record.stage);
  requireThat(step >= 0, 'Invalid stage for ' + id);
  if (step >= 1) {
    fields(record.design, ['reviewer', 'reference'], 'Design review');
    requireThat(record.design.result === 'pass', 'Design review must pass');
    if (record.risk === 'major') decision(record.designApproval, 'Director design approval');
  }
  if (step >= 2) {
    fields(record.review, ['reviewer', 'commit', 'reference'], 'Lead review');
    requireThat(record.review.result === 'pass' && shaPattern.test(record.review.commit), 'Lead review must pass for a full commit SHA');
  }
  if (step >= 3) {
    fields(record.validation, ['tester', 'command', 'manager', 'reference'], 'Test evidence');
    requireThat(record.validation.commit === record.review.commit, 'Tests and lead review must cover the same commit');
    requireThat(record.validation.result === 'pass' && record.validation.coverage === 'pass', 'Tests and test manager review must pass');
    requireThat(record.validation.tester !== record.validation.manager, 'Test manager must be independent of the tester');
    fields(record.validation.unity, ['result', 'reference'], 'Unity evidence');
    const allowed = record.risk === 'routine' ? ['pass', 'not applicable'] : ['pass'];
    requireThat(allowed.includes(record.validation.unity.result), 'Unity validation is required for gameplay or major work');
  }
  if (record.recovery !== undefined) {
    const recovery = record.recovery;
    requireThat(Number.isInteger(recovery.attempts) && recovery.attempts >= 0, 'Invalid recovery attempt count for ' + id);
    requireThat(['running', 'paused_usage_limit', 'paused_throttled', 'manual_intervention'].includes(recovery.status), 'Invalid recovery status for ' + id);
    requireThat(Array.isArray(recovery.checkpoints), 'Recovery checkpoints must be an array');
    if (recovery.github !== undefined) {
      requireThat(/^[1-9]\d*$/.test(String(recovery.github.issue ?? '')) &&
        /^[1-9]\d*$/.test(String(recovery.github.project ?? '')) &&
        /^[a-z\d][a-z\d-]*$/i.test(recovery.github.owner ?? ''),
      'Recovery GitHub sync needs an issue number, Project owner and Project number');
    }
    for (const checkpoint of recovery.checkpoints) {
      requireThat(/^[a-z][a-z0-9-]{0,79}$/.test(checkpoint.packageId ?? '') &&
        ['complete', 'in_progress'].includes(checkpoint.state) && shaPattern.test(checkpoint.commit ?? ''),
      'Invalid package checkpoint for ' + id);
    }
    if (['paused_usage_limit', 'paused_throttled'].includes(recovery.status)) {
      fields(recovery, ['nextAttemptAt'], 'Retryable recovery state');
      requireThat(Number.isFinite(Date.parse(recovery.nextAttemptAt)), 'Invalid recovery retry time');
    }
  }
}

function write(path, record, exclusive = false) {
  const data = JSON.stringify(record, null, 2) + '\n';
  if (exclusive) {
    writeFileSync(path, data, { flag: 'wx' });
    return;
  }
  const temp = path + '.' + process.pid + '.tmp';
  writeFileSync(temp, data, { flag: 'wx' });
  try {
    renameSync(temp, path);
  } finally {
    if (existsSync(temp)) unlinkSync(temp);
  }
}

function featureBranch(id) {
  requireThat(run('git', ['branch', '--show-current']) === 'feature/' + id, 'Use the matching feature/' + id + ' worktree');
}

function github(repo, ...args) {
  return JSON.parse(run('gh', [...args, '--repo', repo]));
}

function board(record, issue, owner, project, pr, recordFile) {
  requireThat(/^[1-9]\d*$/.test(issue ?? '') && /^[1-9]\d*$/.test(project ?? '') &&
    /^[a-z\d][a-z\d-]*$/i.test(owner ?? ''), 'Provide an issue number, Project owner and Project number');
  const remote = run('git', ['remote', 'get-url', 'origin']);
  const match = /^(?:https:\/\/github\.com\/|git@github\.com:)([\w.-]+\/[\w.-]+?)(?:\.git)?$/i.exec(remote);
  requireThat(match, 'Origin must be a GitHub repository');
  const repo = match[1];
  const { url, state } = github(repo, 'issue', 'view', issue, '--json', 'url,state');
  let status = record.stage;
  const recoveryState = record.recovery?.status;
  if (recoveryState === 'paused_usage_limit') status = 'Paused — Usage Limit';
  else if (recoveryState === 'paused_throttled') status = 'Paused — Rate Limit';
  else if (recoveryState === 'manual_intervention') status = 'Blocked';
  if (pr !== undefined) {
    requireThat(/^[1-9]\d*$/.test(pr) && state === 'CLOSED' && status === stages[3], 'Completion requires a closed issue and an approval-ready task');
    const merged = github(repo, 'pr', 'view', pr, '--json', 'state,baseRefName,headRefName,isCrossRepository,closingIssuesReferences');
    requireThat(merged.state === 'MERGED' && merged.baseRefName === 'main' &&
      merged.headRefName === 'feature/' + record.id && !merged.isCrossRepository &&
      merged.closingIssuesReferences.some(item => item.url === url), 'No matching merged PR that closes this issue');
    status = 'Completed';
  } else {
    requireThat(state === 'OPEN', 'Closed issues require a merged PR number for completion');
  }
  const projectArgs = [project, '--owner', owner];
  const { id: projectId } = JSON.parse(run('gh', ['project', 'view', ...projectArgs, '--format', 'json']));
  const { fields: projectFields } = JSON.parse(run('gh', ['project', 'field-list', ...projectArgs, '--limit', '100', '--format', 'json']));
  const field = projectFields.find(item => item.name === 'Status' && Array.isArray(item.options));
  const option = field?.options.find(item => item.name === status);
  requireThat(projectId && option, 'Configure the Project Status option: ' + status);
  // GitHub returns the existing item when the issue is already on this Project.
  const { id: itemId } = JSON.parse(run('gh', ['project', 'item-add', ...projectArgs, '--url', url, '--format', 'json']));
  requireThat(itemId, 'GitHub did not return a Project item ID');
  run('gh', ['project', 'item-edit', '--id', itemId, '--project-id', projectId,
    '--field-id', field.id, '--single-select-option-id', option.id]);
  const previousStatus = record.recovery?.githubNotifiedStatus;
  if (['Paused — Usage Limit', 'Paused — Rate Limit', 'Blocked'].includes(status) && previousStatus !== status) {
    const reason = record.recovery?.manualReason ?? record.recovery?.lastFailure?.message ?? 'A Codex retry is paused.';
    run('gh', ['issue', 'comment', issue, '--body', `Workflow status: **${status}**.\n\n${reason}\n\nThe durable task record and package checkpoints remain in the feature branch. See Docs/Features/${record.id}.json and Docs/CODEX_WORKFLOW.md for recovery steps.`]);
  }
  if (record.recovery) {
    record.recovery.githubNotifiedStatus = status;
    write(recordFile, record);
  }
  console.log(url + ' -> ' + status);
}

function main() {
  const [action, id, input, owner, project, pr] = process.argv.slice(2);
  const root = run('git', ['rev-parse', '--show-toplevel']);
  process.chdir(root);
  const records = resolve('Docs', 'Features');
  if (action === 'gate') {
    const [base, head, branch] = [id, input, owner];
    requireThat(shaPattern.test(base) && shaPattern.test(head), 'Provide full base and head SHAs');
    if (!branch.startsWith('feature/')) return;
    const task = branch.slice('feature/'.length);
    requireThat(idPattern.test(task), 'Invalid feature branch name');
    const path = 'Docs/Features/' + task + '.json';
    const paths = run('git', ['diff', '--name-only', base + '...' + head, '--']).split(/\r?\n/);
    requireThat(paths.includes(path), 'Feature PR must add or update its matching record');
    const record = JSON.parse(run('git', ['show', head + ':' + path]));
    validate(record, task);
    requireThat(record.stage === stages[3], 'Feature PR must reach Awaiting approval');
    const reviewed = record.review.commit;
    run('git', ['merge-base', '--is-ancestor', reviewed, head]);
    requireThat(run('git', ['diff', '--name-only', reviewed, head, '--', '.', ':(exclude)' + path]) === '',
      'Changes after review require fresh review and test evidence');
    const reviewedRecord = JSON.parse(run('git', ['show', reviewed + ':' + path]));
    for (const key of ['risk', 'outcome', 'scope', 'acceptance', 'nonGoals', 'rollback', 'design', 'designApproval']) {
      requireThat(JSON.stringify(record[key]) === JSON.stringify(reviewedRecord[key]), 'Spec changed after review: ' + key);
    }
    console.log('Feature PR evidence matches the reviewed payload');
    return;
  }
  if (action === 'validate') {
    const files = readdirSync(records).filter(name => name.endsWith('.json'));
    for (const name of files) validate(read(resolve(records, name)), name.slice(0, -5));
    console.log('Validated ' + files.length + ' feature record(s)');
    return;
  }
  requireThat(id?.length <= 80 && idPattern.test(id), 'Use a lowercase task ID such as colony-guide-copy');
  const path = resolve(records, id + '.json');
  if (action === 'worktree') {
    requireThat(input, 'Supply a fresh worktree path');
    run('git', ['fetch', 'origin', 'main']);
    run('git', ['worktree', 'add', '-b', 'feature/' + id, '--', resolve(input), 'origin/main']);
    console.log(resolve(input));
    return;
  }
  if (action === 'new') {
    featureBranch(id);
    const record = { ...read(input), schema: 1, id, stage: stages[0] };
    for (const key of ['design', 'designApproval', 'review', 'validation']) delete record[key];
    validate(record, id);
    mkdirSync(records, { recursive: true });
    write(path, record, true);
    console.log(record.stage);
    return;
  }
  const record = read(path);
  validate(record, id);
  if (action === 'show') {
    console.log(JSON.stringify(record, null, 2));
    return;
  }
  if (action === 'board') {
    board(record, input, owner, project, pr, path);
    return;
  }
  featureBranch(id);
  if (action === 'advance') {
    const step = stages.indexOf(record.stage);
    requireThat(step < stages.length - 1, 'This task is ready for a director decision on the PR');
    const evidence = read(input);
    if (step === 0) {
      record.design = evidence.design;
      record.designApproval = evidence.designApproval;
    } else if (step === 1) {
      record.review = evidence.review;
    } else {
      record.validation = evidence.validation;
    }
    record.stage = stages[step + 1];
  } else if (action === 'redesign') {
    const spec = read(input);
    for (const key of ['title', 'outcome', 'risk', 'scope', 'acceptance', 'nonGoals', 'rollback']) record[key] = spec[key];
    record.stage = stages[0];
    for (const key of ['design', 'designApproval', 'review', 'validation']) delete record[key];
  } else if (action === 'rework') {
    requireThat(stages.indexOf(record.stage) >= 1, 'Task is already queued');
    const reason = input?.trim();
    requireThat(reason, 'Supply the reason for rework');
    record.rework = [...(record.rework ?? []), { from: record.stage, reason }];
    record.stage = stages[1];
    delete record.review;
    delete record.validation;
  } else {
    throw new Error('Use worktree, new, show, advance, redesign, rework, validate or board');
  }
  validate(record, id);
  write(path, record);
  console.log(record.stage);
}

try {
  main();
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
