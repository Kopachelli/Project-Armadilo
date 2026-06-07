# Project Armadillo

A local, privacy-first **self-learning AI core + CLI-agent orchestration harness** for Windows.

A running AI session (Claude Code, Codex, Cursor, Gemini, Qwen, GitHub Copilot, Pi, OpenCode, OpenClaw,
Hermes, Google Antigravity, Kimi, MiniMax, z.ai/GLM…) that hits a sub-problem can **"call the
administrator"** — the Registrator detects what's installed (CLIs, local runtimes, and — via a Windows
registry scan — desktop apps it reports but won't drive), spawns the
right headless agent(s), **captures and reviews everything** (locally, to save tokens), **learns**
from each run, and returns the result. It works with **one** tool or **many**, on the same or
different tasks. Local now; cloud later. Protocol-, tool-, and provider-agnostic by design.

> Status: **Phases 0–4 working** end-to-end (spine, brain, multi-tool, autonomous self-improvement),
> **Phase 2 protocols** A2A live + ACP preview via the TS sidecar. See the roadmap below.

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
| `Armadillo.Runtime` | Composition root (`Registrator`) shared by the CLI and the GUI |
| `Armadillo.Host` | `armadillo` console: `doctor` / `assets` / `run` / `chain` / `supervise` / `serve` / `improve` / `playbooks` / `kill-switch` |
| `Armadillo.App` | **Standalone native-Windows WPF dashboard** (WPF-UI): tools, assets, activity, run panel |
| `Armadillo.Tests` | xUnit tests (governor, reviewer, adapter, resolver, self-improvement, assets, chains) |
| `sidecar/` (Node/TS) | Protocol bridges — A2A (live) + Zed ACP (preview) → Core control API |

## Desktop dashboard (Armadillo.App)

A standalone, native-Windows WPF app (portable + installable) over the same in-process engine:

```powershell
# Portable single-file exe (no .NET needed to run it):
dotnet publish src/Armadillo.App -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish-app
# -> publish-app\Armadillo.exe

# Installer (Inno Setup): after the publish above,
ISCC.exe installer\Armadillo.iss   # -> dist\Armadillo-Setup.exe
```

Tabs: **Tools** (capability matrix), **Assets** (skills/MCP/plugins/configs per variant), **Activity**
(recent jobs from SQLite), **Run** (spawn a task on a chosen tool). It reads the same workspace
(`%LOCALAPPDATA%\Armadillo`) as the CLI.

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

- **Phase 2 (done: A2A live, ACP preview)** — Protocol layer via the TS `sidecar/`: A2A agent card +
  `message:send` proven end-to-end; Zed ACP stdio bridge is a typechecked preview. MCP stays native .NET.
- **Phase 3 (done)** — Adapter framework + Cursor/Gemini/Qwen/Codex adapters (Claude live, Gemini
  mechanically verified, others preview), Router auto-select, parallel fan-out (`count`).
- **Phase 4 (done)** — Autonomous self-improving brain: propose → A/B → promote on measured win, with
  versioning, auto-rollback, append-only audit, and a kill switch (`improve` / `playbooks` / `kill-switch`).
- **Also done** — per-tool **asset discovery** (`assets`: skills + MCP servers + **plugins** + configs,
  CLI vs Desktop distinct); **git worktree isolation** + **cross-tool chains** (`chain`);
  **Claude-hook live supervision** (`supervise`: observe + interrupt a running session via PreToolUse);
  registry-based desktop-app detection; expanded catalog (Antigravity, Hermes, OpenClaw, Kimi, MiniMax + desktop apps).
- **Also done** — Phase 5 **standalone WPF dashboard** (portable + Inno installer); **A2A streaming**
  (JSON-RPC `message/stream` over SSE, verified live) + hardened **Zed ACP** bridge in the sidecar.
- **Next** — richer learned routing priors; cross-platform (`IProcessHost`/`IPathProvider` POSIX impls);
  cloud/multi-machine; verify preview CLI adapters against real binaries.

## License

MIT.
