# Agent instructions and agent definitions

Which files instruct an AI agent, how they stay consistent, and which Claude Code subagent to hand work to.

`AGENTS.md` states each rule here in brief and routes to this document, which holds the full text
and wins where the two differ.

## Instruction files

`AGENTS.md` is the single source of truth. There are no per-area instruction files: a nested one is
loaded by some tools and ignored by the rest. The detail an area needs is in `maintainers/`, which the
routing table in `AGENTS.md` names and which every tool can read. `AGENTS.md` is read directly by Codex,
Cursor, Amp, Windsurf, Devin and Copilot's cloud agent, CLI and VS Code integrations. The rest arrive
through a file that exists only to route them to it:

| File | Exists for |
|---|---|
| `CLAUDE.md` | Claude Code, which does not read `AGENTS.md` (anthropics/claude-code#6235). It imports `AGENTS.md` with `@AGENTS.md` |
| `.github/copilot-instructions.md` | Copilot in Visual Studio and JetBrains, which read that path only and have no `AGENTS.md` support |
| `.gemini/settings.json` | Gemini CLI, whose context file is `GEMINI.md` until `context.fileName` names another |
| `.aider.conf.yml` | Aider, which loads no instruction file on its own; `read:` puts this one in every session |

**Edit `AGENTS.md`; the other four stay pointers.** A pointer that grows instructions of its own drifts
from it, and Copilot combines the instruction files it finds rather than picking one, so
anything duplicated is applied twice. `AgentInstructionsTest` fails a pointer that stops naming
`AGENTS.md`, and any instruction file over 32,768 bytes — Codex's `project_doc_max_bytes`, which is a
running budget across the whole root-to-working-directory chain and the only documented cap that
binds this repository.

**`AGENTS.md` holds rules and the routing table, nothing else.** Every session and every subagent loads
it, so a fact only one area needs goes in that area's document here. The topic documents are not
instruction files: no tool loads them unasked, and the 32,768-byte budget does not bind them.
`AgentInstructionsTest` fails a routing link that names no file.

## Agent definitions

`.claude/agents/` holds the Claude Code subagents for this repository. Each one loads `CLAUDE.md`, and so
`AGENTS.md`, when it starts, and reads the topic documents its task needs.

| Agent | Model, effort | Works in | Hands back |
|---|---|---|---|
| `quartz-implementer` | Opus, xhigh | Its own worktree | One scoped issue or spec as a pull request |
| `quartz-grunt` | Sonnet, high | Its own worktree | One fully specified coding task, committed on its branch |
| `quartz-backporter` | Opus, xhigh | Its own worktree | One merged change ported between `main` and `3.x`, as a pull request |
| `quartz-docs-researcher` | Opus, xhigh | Its own worktree | A page that surveys prior art and recommends practice, as a pull request |
| `quartz-reviewer` | Opus, xhigh | Read-only | A verdict on a pull request or a hand-off, before it merges |
| `quartz-ci-triage` | Sonnet, high | Read-only | A diagnosis of a failing check, or a recommendation on a dependency bump |
| `quartz-ui-verifier` | Sonnet, medium | Report-only | What the dashboard renders in a browser, against an exact checklist |

## Who decides, who executes

**Opus decides and validates; Sonnet carries out what is already decided.** A Sonnet agent saves usage
and context on work that is long but settled. It never makes a design call, and nothing it produces is
pushed or merged before Opus has read it.

| The work | Agent |
|---|---|
| A design is still open: a bug to diagnose, a public API shape, the fire path or a store's locking, SQL, cluster behaviour, security | `quartz-implementer` |
| Every decision is in the brief: the files, the names, the expected values, the exact fix | `quartz-grunt` |
| Tests from a table of cases, or review findings that give file, line and fix | `quartz-grunt` |
| Renames and moves the brief lists, regenerated files, a docs page's fences converted to snippets | `quartz-grunt` |
| A dependency bump whose code change the brief names | `quartz-grunt` |
| A port whose cherry-pick applies, with each rename listed in the brief | `quartz-grunt` |
| A port that has to be re-derived on the other line | `quartz-backporter` |
| Reading CI logs or release notes across a version range | `quartz-ci-triage`, whose recommendation the caller decides on |

- **The caller writes a grunt's brief after reading the code**, and names every file, every decision and
  the verification commands. A brief that cannot name them is an implementer's task.
- **A grunt stops on any question the brief leaves open.** The caller answers it and resumes the agent
  with `SendMessage`, which keeps its model.
- **A grunt's branch is validated before it leaves the machine.** Run `quartz-reviewer` on
  `git diff <base>..<branch>`, or read a short diff yourself. Then push it and open the pull request.

## Delegating

- **Omit `model` and `effort` on the `Agent` call.** The call's `model` overrides the definition's.
- **Name the base sha in the brief.** A worktree starts from the default branch, not the caller's
  `HEAD`; the agent resets to the sha it is given.
- **The brief is the agent's only context** beyond `AGENTS.md`: the issue, the base, the scope, the
  files to stay out of, and what is already decided.
- **No `git stash`.** Every worktree of a clone shares one stash stack.
- **An agent waits for every test run it starts**, and stops a process it started by PID, never by
  image name. A run left behind holds the worktree and its containers.
- **Review is separate from implementation.** An implementer or backporter opens the pull request, a grunt
  commits for the caller to push, `quartz-reviewer` validates either, and merging stays with the caller.
- **Definitions load when a session starts.** Claude Code picks up an edit to an existing file; a
  session that started before `.claude/agents/` existed needs a restart.
