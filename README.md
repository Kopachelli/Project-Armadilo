# Project Armadillo

A local, privacy-first **self-learning AI core + CLI-agent orchestration harness** for Windows.

A running AI session (Claude Code, Codex, Cursor, Gemini, Qwen, Copilot, Pi, OpenCode…) that hits a
sub-problem can **"call the administrator"** — the Registrator detects what's installed, spawns the
right headless agent(s), **captures and reviews everything** (locally, to save tokens), **learns**
from each run, and returns the result. It works with **one** tool or **many**, on the same or
different tasks. Local now; cloud later. Protocol-, tool-, and provider-agnostic by design.

> Status: **Phase 0 + Phase 1 complete** — the spine + brain work end-to-end over MCP. See the
> roadmap below. The full design lives in the approved plan.

## Architecture (four layers, brain at the center)

1. **Core / brain** — memory (episodic runs + distilled learnings), local-first review, the
   Router/Registrator, and the safety Governor.
2. **Protocol layer (pluggable)** — **MCP** today (how an agent calls the administrator); Zed **ACP**
   and Google **A2A** are designed-in for later.
3. **Tool layer (pluggable, provider-agnostic)** — one adapter per CLI; a `ProviderProfile` decouples
   *which binary* from *which model endpoint* (cloud, z.ai/GLM, Qwen, or local Ollama).
4. **Orchestration body** — headless spawn / capture / review / persist, with worktree isolation.

## Projects

| Project | Role |
|---|---|
| `Armadillo.Core` | Brain, spawning, persistence, adapters, governor, dispatcher (no UI/transport) |
| `Armadillo.Detection` | Detects installed CLIs + Ollama models (ported from Quiver-Pro) |
| `Armadillo.Mcp` | MCP server (`request_agent`) over Streamable HTTP on `127.0.0.1` |
| `Armadillo.Host` | `armadillo` console: `doctor` / `run` / `serve` |
| `Armadillo.Tests` | xUnit tests (governor, reviewer, adapter, resolver) |

## Quick start

```powershell
dotnet build
dotnet run --project src/Armadillo.Host -- doctor          # capability matrix
dotnet run --project src/Armadillo.Host -- run "Write a haiku about Windows."

# Run the Registrator daemon (hosts the MCP server) and point a CLI at it:
dotnet run --project src/Armadillo.Host -- serve
#   then, in another shell, using the printed operator config:
claude --mcp-config "%LOCALAPPDATA%\Armadillo\config\operator.mcp.json" --strict-mcp-config `
       --allowedTools "mcp__armadillo__request_agent" `
       -p "Use request_agent to have a helper write a poem, then report it."
```

Optional local review/learning (private, $0 tokens): run `ollama serve`, `ollama pull <model>`, then
pass `--review-model <m>` / `--embed-model <m>` to `run`/`serve`.

Workspace (DB, transcripts, configs, logs): `%LOCALAPPDATA%\Armadillo`.

## Safety

A spawned agent can itself call `request_agent`, so the **Governor** bounds recursion: a per-session
**lineage token** (carried in each child's generated `.mcp.json`) plus max-depth, max-concurrent, and
per-root spawn caps. MCP binds loopback-only and requires a shared token. Everything is persisted
(SQLite + transcripts + append-only audit).

## Roadmap

- **Phase 2** — Protocol layer: Zed **ACP** + **A2A** via a TypeScript sidecar.
- **Phase 3** — More adapters (Codex, Cursor, Gemini/Qwen family, Copilot, Pi, OpenCode) + parallel
  fan-out, worktree isolation, cross-tool chains, single-session supervision (Claude hooks).
- **Phase 4** — The autonomous self-improving brain: shadow A/B promotion of playbooks with
  auto-rollback, versioning, audit, and a kill switch.
- **Phase 5** — GUI dashboard, cross-platform, cloud.

## License

MIT.
