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
| `quartz-backporter` | Opus, xhigh | Its own worktree | One merged change ported between `main` and `3.x`, as a pull request |
| `quartz-docs-researcher` | Opus, xhigh | Its own worktree | A page that surveys prior art and recommends practice, as a pull request |
| `quartz-reviewer` | Opus, xhigh | Read-only | A verdict on a pull request or a hand-off, before it merges |
| `quartz-ci-triage` | Opus, xhigh | Read-only | A diagnosis of a failing check, or a recommendation on a dependency bump |
| `quartz-ui-verifier` | Sonnet, medium | Report-only | What the dashboard renders in a browser, against an exact checklist |

Judgement stays on Opus at xhigh. Only driving a browser and reporting what it shows runs on Sonnet,
because the checklist in the brief carries the judgement.

## Delegating

- **Omit `model` and `effort` on the `Agent` call.** The call's `model` overrides the definition's.
- **Name the base sha in the brief.** A worktree starts from the default branch, not the caller's
  `HEAD`; the agent resets to the sha it is given.
- **The brief is the agent's only context** beyond `AGENTS.md`: the issue, the base, the scope, the
  files to stay out of, and what is already decided.
- **No `git stash`.** Every worktree of a clone shares one stash stack.
- **An agent waits for every test run it starts**, and stops a process it started by PID, never by
  image name. A run left behind holds the worktree and its containers.
- **Review is separate from implementation.** An implementer or backporter opens the pull request,
  `quartz-reviewer` validates it, and merging stays with the caller.
- **Definitions load when a session starts.** Claude Code picks up an edit to an existing file; a
  session that started before `.claude/agents/` existed needs a restart.
