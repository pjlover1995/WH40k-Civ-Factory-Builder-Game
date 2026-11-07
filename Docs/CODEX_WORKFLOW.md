# Codex game development workflow

Open a Codex chat in this repository and say **Run the game feature workflow for [your idea]. Use the configured agents when available.** You can also point it to a GitHub feature issue. GitHub Projects is the queue and progress view, Actions shows checks, and Codex shows the work in progress. A Project card does not start a Codex session. Resume a paused feature from its branch, record and issue.

## Roles and routing

| Role | Responsibility | Default route |
| --- | --- | --- |
| Feature Architect | Turn the idea into acceptance criteria, scope and rollback | GPT-6.1 Sol, medium |
| Design Reviewer | Check interactions and classify risk | GPT-6.1 Sol, medium |
| Game Design Authority | Maintain canonical rules with the Game Director | Project brief and design index |
| Orchestrator | Own stage changes, dependencies, file ownership and handoff | GPT-6.1 Sol, medium |
| Implementation agents | Deliver one scoped package each | GPT-6 Luna for bounded changes, GPT-6.1 Sol for gameplay |
| Lead Reviewer | Independently check the implementation against the spec | GPT-6.1 Sol, high |
| Test Agent | Run and record applicable checks | GPT-6 Luna, medium |
| Test Manager | Independently check evidence and coverage | GPT-6.1 Sol, medium |
| GitHub Manager | Draft PR, update issue and Project, prepare integration | GPT-6 Luna, medium |

Use GPT-6 Astra only for unresolved systemic or design risk, through the frontier review profile. For a routine change, the parent can handle intake and coordination, with independent lead review and test acceptance. Batch read-only reviews where independent. Assign disjoint paths to concurrent implementers, otherwise serialize them. Keep up to three delegated workers alongside the coordinating session. Give each worker only the relevant spec, files, dependencies and output contract. Cap automatic rework at two attempts for the same blocker, then surface the unresolved decision or environment issue. Record any fallback to an available model and why. Model selection uses models available to the current Codex account. The repository does not collect account usage.

A supported Codex client loads profiles from `.codex/agents` in a trusted project. The parent invokes them where delegation is available and authorized. If unavailable, run explicit role passes and have a separate reviewer check the result where independence is required. Record the actual reviewer and evidence. Profiles do not start processes or watch the queue on their own. [Official Codex subagent documentation](https://learn.chatgpt.com/docs/agent-configuration/subagents)

## Lifecycle

| Project status | Evidence or action |
| --- | --- |
| Queued | Issue and scoped feature record. Architect and Design Reviewer can refine the spec here. |
| In progress | Design review passed. Core mechanics, progression direction, save contracts, or significant scope changes require an explicit Game Director decision before implementation. |
| Testing | Implementation is committed and Lead Reviewer has passed the same payload the Test Agent will check. |
| Awaiting approval | Automated checks, independent test acceptance, and applicable Unity evidence are recorded. The director sees the current PR, validation, rollback and playtest steps. |
| Completed | The linked PR merged into main and closed its issue. |

Routine means docs or isolated changes with no gameplay effect. Gameplay means behavior within approved design. Major means core mechanics, balance direction, save contracts, architecture, removals, or release scope. Escalate ambiguous risk. Major changes need a design decision before implementation and a separate integration decision. Use the director's exact decision link or record reference. A scope change invalidates design review and requires a fresh pass. For gameplay, follow `Docs/VERIFICATION.md` and record Unity evidence. The Game Director playtests significant features on the proposed version. A new payload or material change in the base calls for revalidation.

The owner controls all merges by default. The GitHub Manager can prepare a draft and handle routine fixes without a new decision. Only the Game Director can authorize release, tag or production deployment. For approval, send one concise handoff in the active Codex chat, and request a GitHub reviewer if a separate reviewer account is configured. Avoid repeated notifications. Record blockers on the issue or PR with the failed check, next action and evidence link. The Project can use a separate Blocked field or issue label to show blockers without changing the current stage.

## Run a feature

Use Node.js 22, Git and .NET 9. Unity 2022.3.2f1 and the prototype scene are required for engine checks. From a checkout containing this workflow, run:

```powershell
node Tools/Workflow/feature.mjs worktree colony-guide-copy ../colony-guide-copy
```

The command fetches main and creates a fresh `feature/colony-guide-copy` branch and worktree. Do not reuse a dirty workspace. From the new worktree, create a JSON file outside tracked source with these spec fields:

```json
{
  "title": "Clarify the colony guide",
  "outcome": "New players can identify the founding guide",
  "risk": "routine",
  "scope": ["README.md"],
  "acceptance": ["The guide description names the founding objectives"],
  "nonGoals": ["Change controls or gameplay"],
  "rollback": "Revert the PR"
}
```

Run `node Tools/Workflow/feature.mjs new colony-guide-copy <spec.json>`. The record lives at `Docs/Features/colony-guide-copy.json`. Use `show <id>` to inspect it and `advance <id> <evidence.json>` once per stage. Provide only the evidence for the stage being advanced:

| Stage leaving | Evidence object |
| --- | --- |
| Queued | `design: {result: "pass", reviewer, reference}`. For major work also provide `designApproval: {result: "approved", by, reference}` from the director. |
| In progress | `review: {result: "pass", reviewer, commit, reference}`. Commit the payload and design record first, then use the full reviewed commit SHA. |
| Testing | `validation: {result: "pass", coverage: "pass", tester, manager, commit, command, reference, unity: {result, reference}}`. Use the same SHA and separate reviewer identities. Unity result is `pass` or, for routine work only, `not applicable`. |

Treat the pseudo-objects in the table as field names and write valid JSON. The evidence reference should explain where a reviewer can inspect the result. Commit the updated record after validation. CI checks that the payload and approved spec match the reviewed commit for ready PRs from `feature/*`. Structured records are evidence attestations. Human authority comes from the referenced decision and GitHub controls. If a payload needs rework, run `rework <id> "reason"`, commit the revised payload, and repeat review and test. If design changes, run redesign <id> <spec.json> to invalidate the previous decision and return the record to Queued. Sync the card and obtain a new review and any required director decision before implementing the revision.

## GitHub activation and routine operations

1. Open a draft PR for the setup branch. The **Game CI / Game validation** check runs on the draft, then on ready PR updates and on main. It compiles and runs the existing .NET simulation and C# syntax suite. Use the documented local Unity checks for engine compilation, scene behavior and playtest. Keep hosted CI free of Unity licenses and write credentials.
2. In the repository ruleset settings, target `main`. Require PRs, the observed **Game validation** check from GitHub Actions, up-to-date branches and resolved conversations. Block force pushes and deletion. Use no routine bypass. Require independent PR approval and dismiss stale approvals when a second authorized reviewer is available. If the director is the sole maintainer, leave reviewer count at zero and reserve actual merge access for the director. Review the ruleset before activating it and check the rule on a disposable PR. [GitHub ruleset setup](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/creating-rulesets-for-a-repository)
3. Create or reuse a GitHub Project owned by the maintainer. Use a Board layout grouped by the built-in **Status** field with the five exact names above, ordered as shown. Link the Project from the repository Projects tab. Use Issues as cards. The owner may enable the built-in closed-item workflow if it maps to Completed, but that also moves issues closed without a merge. Leave it off if you want the stricter rule here.
4. On the workstation, install GitHub CLI if needed, run `gh auth login` and `gh auth refresh -s project` for Project write access. Check the active account, repo access and Project edit access. Use the credential manager. Repository admin access is needed to change rulesets. [GitHub Projects authentication and API](https://docs.github.com/en/issues/planning-and-tracking-with-projects/automating-your-project/using-the-api-to-manage-projects)
5. The GitHub Manager creates the issue, then syncs it at intake and each successful transition with `node Tools/Workflow/feature.mjs board <id> <issue-number> <project-owner> <project-number>`. Retry this command if a board write fails. The local record remains the feature evidence. Keep one coordinating writer per feature so an older checkout cannot overwrite the current card.
6. Push only the verified feature branch with `git push -u origin HEAD` from the feature worktree. Create or reuse a draft PR with `gh pr create --draft --base main --head feature/<id> --title "<title>" --body-file <body-file>`. Include the spec, risk, evidence, rollback and `Closes #N`. After passing the checks and review, mark ready and hand it to the director. When the approved PR merges, rerun `board` with its PR number appended to set Completed. Use a revert PR for rollback.
7. Follow the issue or PR to receive updates. Enable Actions notifications under personal GitHub notification settings, with failures only if preferred. The PR checks and Actions tab show run details. Use the existing Codex chat for approvals and blockers that need immediate attention. [GitHub notification settings](https://docs.github.com/en/account-and-profile/managing-subscriptions-and-notifications-on-github/setting-up-notifications/configuring-notifications)

## Local smoke test

Run `node Tools/Workflow/smoke.mjs` and the simulation command in `AGENTS.md`. The smoke test creates a temporary Git repository, a local remote and a separate worktree. It uses a documentation-only change to exercise the lifecycle through the approval handoff, including a rejected major design, stale evidence and rework. It does not post a public test issue or run hosted Actions. After activation, verify one draft PR, one Project card, required checks and the director's approval path before relying on remote status.

## Usage-limit recovery

Recovery state is stored inside the feature record at `Docs/Features/<id>.json`; atomic replacement and a per-record lock prevent a partial JSON write or simultaneous local update. A due retry claims a durable two-hour lease before starting Codex. Concurrent ticks are rejected, Task Scheduler ignores overlapping scans, and an expired lease can be recovered after a process or machine interruption. Each package checkpoint contains its stable package ID, state, confirmed full commit SHA and optional test evidence. The commit must exist and be an ancestor of the current feature branch. Updating the same package replaces its previous checkpoint, so a resumed run can reconcile committed files and completed tests without repeating a completed package. Uncommitted files remain in the worktree for the resume agent to inspect; they are not represented as confirmed work.

Classify a captured failure and persist it with `node Tools/Workflow/recovery.mjs classify <error.json>` and `node Tools/Workflow/recovery.mjs fail <feature-id> <error.json>`. The JSON may contain `status`, `message`, `code`, and/or a `headers` object with `retry-after`. The classifier distinguishes `throttled`, `subscription_exhausted`, `auth`, and `unrelated`. A 429 alone is treated as short-term throttling; subscription exhaustion needs explicit usage/allowance language. Authentication failures and unrelated errors stop for manual intervention. API billing quota errors are not assumed to mean a subscription renewal. Retryable failures use capped exponential backoff with equal jitter and respect `Retry-After`; the system allows at most eight retries after the initial failure, then requires manual intervention. A successful resume resets the consecutive count while retaining `totalAttempts`.

Record a confirmed package with `node Tools/Workflow/recovery.mjs checkpoint <feature-id> <checkpoint.json>`. Example checkpoint JSON: `{"packageId":"colony-guide","state":"complete","commit":"<40 character commit SHA>","tests":{"command":"dotnet run ...","result":"pass"}}`. Inspect with `node Tools/Workflow/recovery.mjs status <feature-id>`. The normal feature lifecycle remains the authority for design approval, review, validation and merge; a checkpoint does not advance a feature stage or approve a change.

### Optional local resume runner

The bundled Windows runner is opt-in. It starts a logged-in user's local Codex CLI, reads the feature record and repository state, and asks Codex to continue from confirmed checkpoints. It does not specify an alternate model. Before a run it rejects API key/access-token, workload identity and custom-base-URL environment overrides; rejects configured custom model providers; then requires `codex login status` to report `Logged in using ChatGPT`. Otherwise it stops before starting the feature request. OpenAI documents this command for checking the CLI authentication method. [Codex workload identity guidance](https://developers.openai.com/api/docs/guides/workload-identity-federation) First verify that `codex` is installed, ChatGPT subscription authentication is the approved account method, and the configured default model is the one you approve. Then set the per-user `CODEX_RECOVERY_COMMAND` to `powershell -NoProfile -ExecutionPolicy Bypass -File <repository>\Tools\Workflow\resume.ps1` and install the 15-minute local task from the repository root:

```powershell
$env:CODEX_RECOVERY_COMMAND = 'powershell -NoProfile -ExecutionPolicy Bypass -File C:\path\to\WH30K-Civ\Tools\Workflow\resume.ps1'
[Environment]::SetEnvironmentVariable('CODEX_RECOVERY_COMMAND', $env:CODEX_RECOVERY_COMMAND, 'User')
powershell -ExecutionPolicy Bypass -File Tools\Workflow\Install-RecoveryTask.ps1
```

The task runs only while that Windows user is signed in. Windows must be awake, network access and the authenticated CLI session must work, and the Codex app/account must allow the CLI session to proceed. It checks persisted retry times every 15 minutes and does not poll the service early. The first actual retry is a normal Codex request and is still subject to the subscription's available allowance. No GitHub-hosted runner is used to access the user's Codex subscription; GitHub Actions has no subscription-session credential and only runs validation tests.

This does **not** make the existing interactive Codex app turn resumable. Codex does not expose a durable callback from the app UI to this repository queue. A usage failure during a manually coordinated GUI turn must be recorded by the orchestrator with `fail`; unattended resume is available only after the local CLI runner is deliberately configured and verified. The runner cannot guarantee that an external session renewal becomes visible immediately, that login survives app or OS changes, or that the subscription remains eligible. If the CLI reports an authentication issue, an unclassified failure, or eight failed attempts, the record moves to manual intervention and will not be retried automatically.

To publish pause state, configure each feature's record with a recovery object such as `{"attempts":0,"totalAttempts":0,"status":"running","checkpoints":[],"github":{"issue":"<issue number>","owner":"<project owner>","project":"<project number>"}}` and authenticate `gh` with repository and Project write access. The local scanner then syncs `Paused — Usage Limit`, `Paused — Rate Limit`, or `Blocked` to the Project Status field and posts one issue comment per status change. Comments use fixed public-safe text and never copy CLI output or failure details. Add those exact status options (plus `Blocked`) to the existing Project field. Without that optional metadata and GitHub CLI authorization, durable local recovery still works but GitHub status and alerts do not. GitHub Actions uses read-only repository permissions and cannot update the Project.

The recovery tests exercise failure classification, retry-after, jittered backoff, package checkpoint idempotence and preservation across a pause. `node Tools/Workflow/smoke.mjs` remains the non-destructive end-to-end lifecycle check in a temporary Git repository and local remote. Neither test consumes Codex usage, touches the real GitHub Project, invokes the Codex CLI, or proves that a subscription renewal will succeed. Enable the scheduled task only after a real local CLI session and a feature record have been checked by the maintainer.
