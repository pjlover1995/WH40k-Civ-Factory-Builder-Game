import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { checkpoint, classifyFailure, recordFailure, retryDelay } from './recovery.mjs';

const temp = mkdtempSync(join(tmpdir(), 'wh30k-recovery-'));
const path = join(temp, 'feature.json');
const sha = 'a'.repeat(40);
writeFileSync(path, JSON.stringify({ schema: 1, id: 'fixture', stage: 'In progress' }));
try {
  assert.equal(classifyFailure({ status: 429, message: 'Too many requests' }).kind, 'throttled');
  assert.equal(classifyFailure({ message: 'Codex weekly usage limit reached' }).kind, 'subscription_exhausted');
  assert.equal(classifyFailure({ status: 401, message: 'invalid_grant' }).manual, true);
  assert.equal(classifyFailure({ message: 'Compilation failed' }).kind, 'unrelated');
  assert.equal(classifyFailure({ status: 429, headers: { 'retry-after': '17' } }).retryAfterMs, 17_000);
  assert.equal(retryDelay(3, null, () => 0.5), 120_000);

  checkpoint(path, 'fixture', { packageId: 'worldgen', state: 'complete', commit: sha, tests: { command: 'fixture', result: 'pass' } });
  checkpoint(path, 'fixture', { packageId: 'worldgen', state: 'in_progress', commit: sha, tests: null });
  let saved = JSON.parse(readFileSync(path, 'utf8'));
  assert.equal(saved.recovery.checkpoints.length, 1, 'checkpoint updates must be idempotent by package');
  assert.equal(saved.recovery.checkpoints[0].state, 'in_progress');

  const paused = recordFailure(path, 'fixture', { message: 'Codex subscription usage limit reached' }, 1_000_000, () => 0.5);
  assert.equal(paused.status, 'paused_usage_limit');
  assert.equal(paused.attempts, 1);
  assert.equal(Date.parse(paused.nextAttemptAt), 1_030_000);
  saved = JSON.parse(readFileSync(path, 'utf8'));
  assert.equal(saved.recovery.checkpoints[0].commit, sha, 'failure persistence must retain confirmed work');

  const throttled = recordFailure(path, 'fixture', { status: 429, headers: { 'retry-after': '9' }, message: 'rate limit' }, 2_000_000);
  assert.equal(throttled.status, 'paused_throttled');
  assert.equal(Date.parse(throttled.nextAttemptAt), 2_009_000);
  const auth = recordFailure(path, 'fixture', { status: 401, message: 'authentication required' }, 3_000_000);
  assert.equal(auth.status, 'manual_intervention');
  assert.equal(auth.nextAttemptAt, null);

  if (process.platform === 'win32') {
    const root = join(temp, 'repo');
    const featureDir = join(root, 'Docs', 'Features');
    mkdirSync(featureDir, { recursive: true });
    const resumeRecord = join(featureDir, 'resume-fixture.json');
    writeFileSync(resumeRecord, JSON.stringify({ schema: 1, id: 'resume-fixture', stage: 'In progress', recovery: {
      status: 'paused_usage_limit', attempts: 3, totalAttempts: 3, nextAttemptAt: new Date(0).toISOString(), checkpoints: [{ packageId: 'worldgen', state: 'complete', commit: sha }],
    } }));
    const stub = join(temp, 'resume-stub.cmd');
    writeFileSync(stub, '@echo off\r\nexit /b 0\r\n');
    const cli = fileURLToPath(new URL('./recovery.mjs', import.meta.url));
    const env = { ...process.env, CODEX_RECOVERY_COMMAND: stub };
    delete env.OPENAI_API_KEY;
    delete env.OPENAI_ADMIN_KEY;
    const resumed = spawnSync(process.execPath, [cli, 'tick', 'resume-fixture'], { cwd: root, env, encoding: 'utf8' });
    assert.equal(resumed.status, 0, resumed.stderr);
    const resumedRecord = JSON.parse(readFileSync(resumeRecord, 'utf8'));
    assert.equal(resumedRecord.recovery.status, 'running');
    assert.equal(resumedRecord.recovery.attempts, 0);
    assert.equal(resumedRecord.recovery.checkpoints[0].commit, sha);
  }
  console.log('Recovery tests passed: failure classes, retry-after, bounded backoff, durable atomic checkpoints and retained progress.');
} finally {
  rmSync(temp, { recursive: true, force: true, maxRetries: 3 });
}
