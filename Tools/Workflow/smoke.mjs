import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const fixture = mkdtempSync(join(tmpdir(), 'wh30k-workflow-'));
const repo = join(fixture, 'repo');
const worktree = join(fixture, 'feature');
const remote = join(fixture, 'local-remote.git');
const script = fileURLToPath(new URL('./feature.mjs', import.meta.url));
const id = 'sample-guide';
const record = 'Docs/Features/' + id + '.json';

function git(cwd, ...args) {
  return execFileSync('git', ['-C', cwd, ...args], { encoding: 'utf8' }).trim();
}

function workflow(cwd, ...args) {
  const result = spawnSync(process.execPath, [script, ...args], { cwd, encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  return result.stdout.trim();
}

function denied(cwd, expected, ...args) {
  const result = spawnSync(process.execPath, [script, ...args], { cwd, encoding: 'utf8' });
  assert.equal(result.status, 1, result.stderr);
  assert.match(result.stderr, expected);
}

function fixtureJson(name, value) {
  const path = join(fixture, name + '.json');
  writeFileSync(path, JSON.stringify(value));
  return path;
}

try {
  mkdirSync(repo);
  git(repo, 'init', '-b', 'main');
  git(repo, 'init', '--bare', '-b', 'main', remote);
  git(repo, 'config', 'user.name', 'Workflow fixture');
  git(repo, 'config', 'user.email', 'fixture@example.invalid');
  git(repo, 'config', 'commit.gpgsign', 'false');
  writeFileSync(join(repo, 'Guide.md'), '# Colony Guide\n');
  git(repo, 'add', 'Guide.md');
  git(repo, 'commit', '-m', 'Fixture base');
  const base = git(repo, 'rev-parse', 'HEAD');
  git(repo, 'remote', 'add', 'origin', remote);
  git(repo, 'push', 'origin', 'main');
  workflow(repo, 'worktree', id, worktree);
  const spec = {
    title: 'Clarify guide heading', outcome: 'Identify the guide in one line',
    risk: 'major', scope: ['Guide.md'], nonGoals: ['Gameplay changes'],
    acceptance: ['Guide heading names founding'], rollback: 'Revert the heading commit',
  };
  const major = fixtureJson('major', spec);
  const routine = fixtureJson('routine', { ...spec, risk: 'routine' });
  denied(repo, /matching feature/, 'new', id, major);
  assert.equal(workflow(worktree, 'new', id, major), 'Queued');
  const design = { design: { result: 'pass', reviewer: 'Design Reviewer', reference: 'fixture' } };
  const unapproved = fixtureJson('design', design);
  denied(worktree, /Director design approval/, 'advance', id, unapproved);
  // This synthetic approval is used only to exercise the major gate. The sample change is routine.
  const approved = fixtureJson('approved', { ...design, designApproval: { by: 'Fixture director', result: 'approved', reference: 'fixture' } });
  assert.equal(workflow(worktree, 'advance', id, approved), 'In progress');
  rmSync(join(worktree, record));
  assert.equal(workflow(worktree, 'new', id, routine), 'Queued');
  assert.equal(workflow(worktree, 'advance', id, unapproved), 'In progress');
  writeFileSync(join(worktree, 'Guide.md'), '# Colony Founding Guide\n');
  git(worktree, 'add', '.');
  git(worktree, 'commit', '-m', 'Clarify the guide heading');
  const reviewed = git(worktree, 'rev-parse', 'HEAD');
  denied(worktree, /Awaiting approval/, 'gate', base, reviewed, 'feature/' + id);
  const review = fixtureJson('review', { review: { result: 'pass', reviewer: 'Lead Reviewer', commit: reviewed, reference: 'fixture' } });
  assert.equal(workflow(worktree, 'advance', id, review), 'Testing');
  assert.equal(git(worktree, 'diff', '--check', base, reviewed), '');
  const validation = {
    result: 'pass', coverage: 'pass', tester: 'Test Agent', manager: 'Test Manager',
    command: 'git diff --check and inspect Guide.md', reference: 'fixture',
    unity: { result: 'not applicable', reference: 'Markdown only' }, commit: reviewed,
  };
  const stale = fixtureJson('stale', { validation: { ...validation, commit: base } });
  denied(worktree, /same commit/, 'advance', id, stale);
  const passed = fixtureJson('passed', { validation });
  assert.equal(workflow(worktree, 'advance', id, passed), 'Awaiting approval');
  assert.match(workflow(worktree, 'validate'), /Validated 1 feature record/);
  git(worktree, 'add', record);
  git(worktree, 'commit', '-m', 'Record sample review and validation');
  const ready = git(worktree, 'rev-parse', 'HEAD');
  assert.match(workflow(worktree, 'gate', base, ready, 'feature/' + id), /matches the reviewed payload/);
  git(worktree, 'push', 'origin', 'HEAD:refs/heads/feature/' + id);
  assert.equal(git(repo, 'ls-remote', 'origin', 'refs/heads/main').split(/\s+/)[0], base);
  writeFileSync(join(worktree, 'Guide.md'), '# Changed after review\n');
  git(worktree, 'commit', '-am', 'Introduce a stale review');
  const changed = git(worktree, 'rev-parse', 'HEAD');
  denied(worktree, /fresh review/, 'gate', base, changed, 'feature/' + id);
  assert.equal(workflow(worktree, 'rework', id, 'Refresh evidence after a change'), 'In progress');
  assert.equal(workflow(worktree, 'redesign', id, major), 'Queued');
  denied(worktree, /Director design approval/, 'advance', id, unapproved);
  console.log('Workflow smoke passed: isolated worktree and local remote, approval gates, evidence, stale changes and rework.');
} finally {
  // Only remove the fresh directory returned by mkdtemp for this test.
  rmSync(fixture, { recursive: true, force: true, maxRetries: 3 });
}
