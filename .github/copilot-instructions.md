# Quartz.NET

The instructions for this repository live in [`AGENTS.md`](../AGENTS.md) at the repository root.

This file exists for **Copilot in Visual Studio and JetBrains IDEs**, which read
`.github/copilot-instructions.md` and have no `AGENTS.md` support at all — without it they would be
given nothing. Copilot's cloud agent, its CLI and its VS Code integration read `AGENTS.md` directly
and do not need this file.

Do not copy instructions into it. Copilot *combines* the instruction files it finds rather than
picking one, so anything duplicated here is applied twice on the surfaces that read both — and drifts
from `AGENTS.md` on all of them.
