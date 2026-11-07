import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { execFileSync, spawn, spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkpoint, classifyFailure, publicAlertReason, recordFailure, retryDelay } from './recovery.mjs';

const temp = mkdtempSync(join(tmpdir(), 'wh30k-recovery-'));
const path = join(temp, 'feature.json');
const root = join(temp, 'repo');
const previousCwd = process.cwd();
mkdirSync(root, { recursive: true });
execFileSync('git', ['init', '-b', 'main', root], { stdio: 'ignore' });
execFileSync('git', ['-C', root, 'config', 'user.name', 'Recovery fixture']);
execFileSync('git', ['-C', root, 'config', 'user.email', 'fixture@example.invalid']);
writeFileSync(join(root, 'README.md'), 'Recovery fixture\n');
execFileSync('git', ['-C', root, 'add', '.']);
execFileSync('git', ['-C', root, 'commit', '-m', 'Recovery fixture'], { stdio: 'ignore' });
const sha = execFileSync('git', ['-C', root, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
process.chdir(root);
writeFileSync(path, JSON.stringify({ schema: 1, id: 'fixture', stage: 'In progress' }));
try {
  assert.equal(classifyFailure({ status: 429, message: 'Too many requests' }).kind, 'throttled');
  assert.equal(classifyFailure({ message: 'Codex weekly usage limit reached' }).kind, 'subscription_exhausted');
  assert.equal(classifyFailure({ status: 401, message: 'invalid_grant' }).manual, true);
  assert.equal(classifyFailure({ message: 'Compilation failed' }).kind, 'unrelated');
  assert.equal(classifyFailure({ status: 429, headers: { 'retry-after': '17' } }).retryAfterMs, 17_000);
  assert.equal(retryDelay(3, null, () => 0.5), 180_000);

  checkpoint(path, 'fixture', { packageId: 'worldgen', state: 'complete', commit: sha, tests: { command: 'fixture', result: 'pass' } });
  checkpoint(path, 'fixture', { packageId: 'worldgen', state: 'in_progress', commit: sha, tests: null });
  let saved = JSON.parse(readFileSync(path, 'utf8'));
  assert.equal(saved.recovery.checkpoints.length, 1, 'checkpoint updates must be idempotent by package');
  assert.equal(saved.recovery.checkpoints[0].state, 'in_progress');

  const paused = recordFailure(path, 'fixture', { message: 'Codex subscription usage limit reached' }, 1_000_000, () => 0.5);
  assert.equal(paused.status, 'paused_usage_limit');
  assert.equal(paused.attempts, 1);
  assert.equal(Date.parse(paused.nextAttemptAt), 1_045_000);
  saved = JSON.parse(readFileSync(path, 'utf8'));
  assert.equal(saved.recovery.checkpoints[0].commit, sha, 'failure persistence must retain confirmed work');

  const throttled = recordFailure(path, 'fixture', { status: 429, headers: { 'retry-after': '9' }, message: 'rate limit' }, 2_000_000);
  assert.equal(throttled.status, 'paused_throttled');
  assert.equal(Date.parse(throttled.nextAttemptAt), 2_009_000);
  const auth = recordFailure(path, 'fixture', { status: 401, message: 'authentication required' }, 3_000_000);
  assert.equal(auth.status, 'manual_intervention');
  assert.equal(auth.nextAttemptAt, null);
  assert.throws(() => checkpoint(path, 'fixture', { packageId: 'bad-sha', state: 'complete', commit: 'b'.repeat(40) }));
  const publicReason = publicAlertReason({ lastFailure: { kind: 'unrelated', message: 'private path and sk-secret-value' } }, 'Blocked');
  assert.doesNotMatch(publicReason, /private path|sk-secret-value/, 'GitHub issue alerts must not include raw CLI output');

  const retryPath = join(temp, 'retry-feature.json');
  writeFileSync(retryPath, JSON.stringify({ schema: 1, id: 'retry-fixture', stage: 'In progress' }));
  let exhausted;
  for (let i = 0; i < 9; i += 1) exhausted = recordFailure(retryPath, 'retry-fixture', { message: 'Codex subscription usage limit reached' }, 4_000_000, () => 0);
  assert.equal(exhausted.status, 'manual_intervention', 'retry count must stop at the configured bound');
  assert.equal(exhausted.attempts, 9);

  if (process.platform === 'win32') {
    const featureDir = join(root, 'Docs', 'Features');
    mkdirSync(featureDir, { recursive: true });
    const resumeRecord = join(featureDir, 'resume-fixture.json');
    writeFileSync(resumeRecord, JSON.stringify({ schema: 1, id: 'resume-fixture', stage: 'In progress', recovery: {
      status: 'paused_usage_limit', attempts: 3, totalAttempts: 3, nextAttemptAt: new Date(0).toISOString(), checkpoints: [{ packageId: 'worldgen', state: 'complete', commit: sha }],
    } }));
    const stub = join(temp, 'resume-stub.ps1');
    const marker = join(temp, 'resume-count.txt');
    writeFileSync(stub, `Start-Sleep -Seconds 3\nAdd-Content -LiteralPath '${marker}' -Value $args[0]\n`);
    const cli = fileURLToPath(new URL('./recovery.mjs', import.meta.url));
    const env = { ...process.env, CODEX_RECOVERY_COMMAND: `powershell -NoProfile -ExecutionPolicy Bypass -File "${stub}"` };
    delete env.OPENAI_API_KEY;
    delete env.OPENAI_ADMIN_KEY;
    const launch = () => new Promise(resolve => {
      const child = spawn(process.execPath, [cli, 'tick', 'resume-fixture'], { cwd: root, env, windowsHide: true });
      let stderr = '';
      child.stderr.setEncoding('utf8').on('data', chunk => { stderr += chunk; });
      child.on('close', status => resolve({ status, stderr }));
    });
    const concurrent = await Promise.all([launch(), launch()]);
    assert.equal(concurrent.filter(item => item.status === 0).length, 1, 'only one overlapping tick may launch a runner');
    assert.equal(concurrent.filter(item => item.status !== 0).length, 1, 'a second overlapping tick must be rejected');
    assert.equal(readFileSync(marker, 'utf8').trim().split(/\r?\n/).length, 1, 'the runner must execute once');
    const resumedRecord = JSON.parse(readFileSync(resumeRecord, 'utf8'));
    assert.equal(resumedRecord.recovery.status, 'running');
    assert.equal(resumedRecord.recovery.attempts, 0);
    assert.equal(resumedRecord.recovery.checkpoints[0].commit, sha);

    const failedRecord = join(featureDir, 'failed-fixture.json');
    writeFileSync(failedRecord, JSON.stringify({ schema: 1, id: 'failed-fixture', stage: 'In progress', recovery: {
      status: 'paused_usage_limit', attempts: 1, totalAttempts: 1, nextAttemptAt: new Date(0).toISOString(), checkpoints: [],
    } }));
    const failingStub = join(temp, 'failing-resume-stub.ps1');
    writeFileSync(failingStub, "[Console]::Error.WriteLine('Codex subscription usage limit reached')\nexit 1\n");
    const failed = spawnSync(process.execPath, [cli, 'tick', 'failed-fixture'], {
      cwd: root, env: { ...env, CODEX_RECOVERY_COMMAND: `powershell -NoProfile -ExecutionPolicy Bypass -File "${failingStub}"` }, encoding: 'utf8', windowsHide: true,
    });
    assert.notEqual(failed.status, 0, 'a failing runner must not be marked as resumed');
    const rescheduled = JSON.parse(readFileSync(failedRecord, 'utf8')).recovery;
    assert.equal(rescheduled.status, 'paused_usage_limit');
    assert.equal(rescheduled.attempts, 2);
    assert.equal(rescheduled.resumeLeaseUntil, undefined, 'failed runners must release their lease');
    assert.ok(Date.parse(rescheduled.nextAttemptAt) > Date.now(), 'failed retries must be scheduled into the future');

    resumedRecord.recovery.status = 'resuming';
    resumedRecord.recovery.resumeLeaseUntil = new Date(0).toISOString();
    writeFileSync(resumeRecord, JSON.stringify(resumedRecord));
    const stale = spawnSync(process.execPath, [cli, 'tick', 'resume-fixture'], { cwd: root, env, encoding: 'utf8', windowsHide: true });
    assert.equal(stale.status, 0, stale.stderr);
    assert.equal(readFileSync(marker, 'utf8').trim().split(/\r?\n/).length, 2, 'expired runner leases must permit recovery');
  }
  console.log('Recovery tests passed: classification, retries, bounded attempts, atomic checkpoints, alert redaction and runner leases.');
} finally {
  process.chdir(previousCwd);
  rmSync(temp, { recursive: true, force: true, maxRetries: 3 });
}
