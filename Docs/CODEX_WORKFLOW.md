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
