import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const maxAttempts = 8;
const baseDelayMs = 60_000;
const maxDelayMs = 6 * 60 * 60_000;
const maxRetryAfterMs = 30 * 24 * 60 * 60_000;
const runnerLeaseMs = 2 * 60 * 60_000;

export function classifyFailure(error = {}) {
  const status = Number(error.status ?? error.statusCode ?? error.code);
  const message = [error.message, error.body, error.code].filter(Boolean).join(' ').toLowerCase();
  const retryAfter = error.retryAfter ?? error.headers?.['retry-after'] ?? error.headers?.['Retry-After'] ?? error.headers?.get?.('retry-after');

  if (status === 401 || status === 403 || /invalid[_ ]grant|unauthori[sz]ed|authentication required|login required|credentials? (?:expired|invalid)/i.test(message)) {
    return { kind: 'auth', retryable: false, manual: true, retryAfterMs: null };
  }
  if (/subscription.{0,30}(?:usage|limit|allowance)|(?:usage|weekly|monthly) limit.{0,30}(?:reached|exhausted|reset)|(?:codex|chatgpt).{0,30}(?:limit|allowance).{0,30}(?:reached|exhausted)|you.ve (?:reached|hit|exceeded) your (?:usage|message|plan) limit|(?:reached|hit|exceeded|exhausted).{0,30}(?:subscription|usage|allowance).{0,20}limit/i.test(message)) {
    return { kind: 'subscription_exhausted', retryable: true, manual: false, retryAfterMs: parseRetryAfter(retryAfter) };
  }
  if (status === 429 || /rate.?limit|too many requests|temporarily throttled|slow down/i.test(message)) {
    return { kind: 'throttled', retryable: true, manual: false, retryAfterMs: parseRetryAfter(retryAfter) };
  }
  return { kind: 'unrelated', retryable: false, manual: true, retryAfterMs: null };
}

function parseRetryAfter(value, now = Date.now()) {
  if (value === undefined || value === null || value === '') return null;
  const seconds = Number(value);
  if (Number.isFinite(seconds) && seconds >= 0) return Math.min(seconds * 1000, maxRetryAfterMs);
  const date = Date.parse(value);
  return Number.isFinite(date) ? Math.max(0, Math.min(date - now, maxRetryAfterMs)) : null;
}

export function retryDelay(attempt, retryAfterMs = null, random = Math.random) {
  const exponential = Math.min(maxDelayMs, baseDelayMs * (2 ** Math.max(0, attempt - 1)));
  // Equal jitter keeps retries away from zero while spreading them across the capped exponential window.
  return retryAfterMs === null ? Math.floor(exponential / 2) + Math.floor(random() * exponential / 2) : retryAfterMs;
}

export function publicAlertReason(recovery, status) {
  if (status === 'Paused — Usage Limit') return 'A Codex usage limit was detected. The feature is paused until its next scheduled retry.';
  if (status === 'Paused — Rate Limit') return 'Short-term Codex throttling was detected. The feature is paused until its next scheduled retry.';
  if (recovery?.lastFailure?.kind === 'auth') return 'Codex authentication or account access needs attention. Manual intervention is required.';
  if ((recovery?.attempts ?? 0) > maxAttempts) return 'Automatic retries stopped after reaching the configured limit. Manual intervention is required.';
  return 'An unclassified workflow failure needs review. Manual intervention is required.';
}

function atomicWrite(path, value) {
  mkdirSync(resolve(path, '..'), { recursive: true });
  const temp = path + '.' + process.pid + '.' + Math.random().toString(16).slice(2) + '.tmp';
  writeFileSync(temp, JSON.stringify(value, null, 2) + '\n', { flag: 'wx' });
  try { renameSync(temp, path); } finally { if (existsSync(temp)) rmSync(temp); }
}

function readRecord(path, id) {
  const record = JSON.parse(readFileSync(path, 'utf8'));
  if (record.schema !== 1 || record.id !== id) throw new Error('Feature record identity is invalid');
  return record;
}

function safeMessage(value) {
  return String(value ?? '').replace(/(bearer\s+)[a-z0-9._~+\/-]+/ig, '$1[redacted]')
    .replace(/\bsk-[a-z0-9_-]{12,}\b/ig, '[redacted-key]')
    .replace(/(OPENAI_(?:ADMIN_)?API_KEY\s*[=:]\s*)[^\s,;]+/ig, '$1[redacted]').slice(0, 1000);
}

function recordPath(root, id) {
  if (!/^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/.test(id ?? '')) throw new Error('Invalid feature ID');
  return resolve(root, 'Docs', 'Features', id + '.json');
}

function withLock(path, callback) {
  const lock = path + '.recovery-lock';
  try { mkdirSync(lock); } catch {
    try {
      if (Date.now() - statSync(lock).mtimeMs > 30 * 60_000) rmSync(lock, { recursive: true, force: true });
      mkdirSync(lock);
    } catch { throw new Error('Recovery record is being updated by another process; retry on the next scheduled run'); }
  }
  try { return callback(); } finally { rmSync(lock, { recursive: true, force: true }); }
}

export function recordFailure(path, id, error, now = Date.now(), random = Math.random) {
  return withLock(path, () => {
    const record = readRecord(path, id);
    const failure = classifyFailure(error);
    record.recovery ??= { attempts: 0, totalAttempts: 0, checkpoints: [], status: 'running' };
    record.recovery.lastFailure = { kind: failure.kind, at: new Date(now).toISOString(), message: safeMessage(error.message ?? error) };
    delete record.recovery.resumeLeaseUntil;
    if (!failure.retryable) {
      record.recovery.status = 'manual_intervention';
      record.recovery.nextAttemptAt = null;
      record.recovery.manualReason = failure.kind === 'auth' ? 'Codex authentication or account access needs attention' : 'An unrelated failure needs review';
    } else {
      record.recovery.attempts += 1;
      record.recovery.totalAttempts = (record.recovery.totalAttempts ?? 0) + 1;
      if (record.recovery.attempts > maxAttempts) {
        record.recovery.status = 'manual_intervention';
        record.recovery.nextAttemptAt = null;
        record.recovery.manualReason = 'Retry limit reached';
      } else {
        const delay = retryDelay(record.recovery.attempts, failure.retryAfterMs, random);
        record.recovery.status = failure.kind === 'subscription_exhausted' ? 'paused_usage_limit' : 'paused_throttled';
        record.recovery.nextAttemptAt = new Date(now + delay).toISOString();
      }
    }
    atomicWrite(path, record);
    return record.recovery;
  });
}

export function checkpoint(path, id, checkpointRecord) {
  const { packageId, state, commit, tests } = checkpointRecord ?? {};
  if (!/^[a-z][a-z0-9-]{0,79}$/.test(packageId ?? '') || !['complete', 'in_progress'].includes(state) || !/^[0-9a-f]{40}$/.test(commit ?? '')) {
    throw new Error('Checkpoint needs a packageId, in_progress/complete state, and full commit SHA');
  }
  const root = process.cwd();
  execFileSync('git', ['cat-file', '-e', commit + '^{commit}'], { cwd: root, stdio: 'ignore' });
  execFileSync('git', ['merge-base', '--is-ancestor', commit, 'HEAD'], { cwd: root, stdio: 'ignore' });
  return withLock(path, () => {
    const record = readRecord(path, id);
    record.recovery ??= { attempts: 0, totalAttempts: 0, checkpoints: [], status: 'running' };
    const item = { packageId, state, commit, tests: tests ?? null, confirmedAt: new Date().toISOString() };
    const index = record.recovery.checkpoints.findIndex(entry => entry.packageId === packageId);
    if (index < 0) record.recovery.checkpoints.push(item);
    else record.recovery.checkpoints[index] = item;
    atomicWrite(path, record);
    return item;
  });
}

function main() {
  const [action, id, argument] = process.argv.slice(2);
  const root = process.cwd();
  const path = recordPath(root, id);
  if (action === 'classify') {
    const input = JSON.parse(readFileSync(argument, 'utf8'));
    console.log(JSON.stringify(classifyFailure(input)));
    return;
  }
  if (action === 'fail') {
    const input = JSON.parse(readFileSync(argument, 'utf8'));
    console.log(JSON.stringify(recordFailure(path, id, input)));
    return;
  }
  if (action === 'checkpoint') {
    const input = JSON.parse(readFileSync(argument, 'utf8'));
    console.log(JSON.stringify(checkpoint(path, id, input)));
    return;
  }
  if (action === 'status') {
    const record = readRecord(path, id);
    console.log(JSON.stringify({ id, stage: record.stage, recovery: record.recovery ?? { status: 'running', attempts: 0, checkpoints: [] } }, null, 2));
    return;
  }
  if (action === 'tick') {
    // Only the explicitly configured Codex CLI command can execute work. No API key or model override is accepted.
    const command = process.env.CODEX_RECOVERY_COMMAND;
    if (!command) throw new Error('Set CODEX_RECOVERY_COMMAND to an approved local Codex CLI resume command');
    if (process.env.OPENAI_API_KEY || process.env.OPENAI_ADMIN_KEY) throw new Error('Refusing to run with API key credentials; use the approved Codex subscription session');
    withLock(path, () => {
      const record = readRecord(path, id);
      const recovery = record.recovery;
      const now = Date.now();
      const staleLease = recovery?.status === 'resuming' && Number.isFinite(Date.parse(recovery.resumeLeaseUntil)) && Date.parse(recovery.resumeLeaseUntil) <= now;
      const paused = ['paused_usage_limit', 'paused_throttled'].includes(recovery?.status);
      if (!paused && !staleLease) throw new Error(recovery?.status === 'resuming' ? 'A Codex resume is already running for this feature' : 'Feature is not paused for a retryable usage limit or throttle');
      if (paused) {
        const due = Date.parse(recovery.nextAttemptAt);
        if (!Number.isFinite(due)) throw new Error('Recovery retry time is invalid; manual intervention is required');
        if (now < due) throw new Error('Retry is not due until ' + recovery.nextAttemptAt);
      }
      recovery.status = 'resuming';
      recovery.resumeLeaseUntil = new Date(now + runnerLeaseMs).toISOString();
      atomicWrite(path, record);
    });
    const result = spawnSync(command, [id, path], { cwd: root, encoding: 'utf8', shell: true, maxBuffer: 20 * 1024 * 1024 });
    if (result.stdout) process.stdout.write(result.stdout);
    if (result.stderr) process.stderr.write(result.stderr);
    if (result.error) {
      recordFailure(path, id, { message: result.error.message });
      throw result.error;
    }
    if (result.status !== 0) {
      const error = new Error((result.stderr || result.stdout || 'Codex resume command failed').slice(-2000));
      const failure = classifyFailure({ message: error.message, status: result.status });
      recordFailure(path, id, { ...failure, message: error.message, status: failure.kind === 'throttled' ? 429 : undefined });
      throw error;
    }
    withLock(path, () => {
      const latest = readRecord(path, id);
      latest.recovery.status = 'running';
      latest.recovery.attempts = 0;
      latest.recovery.nextAttemptAt = null;
      delete latest.recovery.resumeLeaseUntil;
      latest.recovery.lastResumedAt = new Date().toISOString();
      atomicWrite(path, latest);
    });
    console.log('Resume command completed for ' + id);
    return;
  }
  throw new Error('Use classify, fail, checkpoint, status or tick');
}

if (process.argv[1] && resolve(process.argv[1]) === resolve(fileURLToPath(import.meta.url))) {
  try { main(); } catch (error) { console.error(error.message); process.exitCode = 1; }
}
