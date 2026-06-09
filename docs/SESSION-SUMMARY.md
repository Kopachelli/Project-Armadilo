# Build Session Summary — Project Armadillo

A handoff record of the session that took this repo from an empty README to a working, packaged
product. For *how/why* each piece works, see [ARCHITECTURE.md](ARCHITECTURE.md) and
[SCENARIOS.md](SCENARIOS.md).

## What was built

Starting point: empty repo (`# Project Armadilo`). End state: a local, privacy-first **self-learning
AI core that orchestrates AI coding CLIs**, shipping as a **standalone Windows GUI** and a **CLI**,
installable + portable, wired into the user's Claude Code.

**Phases / features delivered (all on `dev`, 50 xUnit tests passing):**

1. **Spine + brain (Phase 0–1)** — .NET 8 solution; tool detection (ported from Quiver-Pro);
   headless spawn (stdin task, stdout cap, kill-tree); SQLite memory + transcripts; local-first Ollama
   reviewer; learning capture/retrieval; Governor fork-bomb guard; MCP server (`request_agent`).
2. **Protocols (Phase 2)** — native .NET MCP; TS `sidecar/` with **A2A** (JSON-RPC + `message/stream`
   SSE) and a **Zed ACP** bridge; .NET control API as the single seam.
3. **Multi-tool + brain (Phase 3–4)** — adapter framework + families; Router with **learned routing
   priors**; parallel fan-out; **autonomous self-improvement** (propose → A/B → promote on measured
   win, versioned, auto-rollback, kill switch).
4. **Extended catalog** — Antigravity, Hermes, OpenClaw, Kimi, MiniMax, Ollama-as-tool; **registry-based
   desktop-app detection**; per-variant **asset discovery** (skills + MCP + plugins + configs).
5. **Orchestration body** — cross-tool / cross-engine **chains**; **git-worktree isolation**;
   **Claude-hook live supervision** (observe + interrupt a running session).
6. **Phase 5 — standalone GUI** (`Armadillo.App`, WPF/WPF-UI): Tools / Assets / Activity / Run tabs over
   the same in-process engine.
7. **Packaging (Quiver-style)** — portable single-file GUI + CLI exes; **Inno Setup installer**;
   `build.ps1`; CI + tag-driven `release.yml` (SignPath OSS signing pre-wired/inert).
8. **Connect existing sessions** — stable serve port (`127.0.0.1:8787`) + persisted token; `/api/hook`;
   `armadillo connect`/`disconnect` (user-scope MCP + global hooks, existing hooks preserved);
   `armadillo autostart on|off` (hidden Startup-folder launcher).
9. **Docs + launch** — GitHub-style README, MIT LICENSE, GitHub Pages landing (`docs/index.html`),
   ARCHITECTURE.md, SCENARIOS.md.

## Key decisions

- **Stack:** hybrid **.NET 8** core/GUI/CLI + **TypeScript** sidecar for A2A/ACP.
- **Self-improvement:** "fully autonomous" = autonomy *earned by measured A/B wins*, always
  versioned/reversible, with a kill switch.
- **Protocols:** MCP now (native), A2A + ACP via sidecar.
- **Desktop apps:** detected/reported, never driven (a GUI can't be a headless child).
- **Cross-platform:** deferred — polished Windows first.
- **Licensing/business (recommendation):** ship **MIT open source** now; monetize later via
  **open-core Pro + cloud** (same model as Quiver/Quiver-Pro).

## Current verified state (on this machine)

- App **opens** (fixed the `InvariantGlobalization` WPF-binding crash; added crash logging).
- **Installed** via `Armadillo-Setup.exe` (per-user) → GUI in Start Menu + `armadillo` CLI on PATH.
- **Connected**: MCP + hooks point at the installed CLI; `claude mcp list` → `armadillo ✓ Connected`.
- **Autostart on**: hidden daemon at login; daemon `/api/health` = ok.
- Live-verified: a fresh `claude -p` (no flags) used `request_agent` **and** fired hooks; cross-engine
  chain Claude→local-qwen; live supervision blocked `Bash` mid-run; self-improvement rejected a
  non-improvement.
- Tools drivable here: **Claude, Gemini CLI, Ollama** (others are preview adapters, not installed).

## Artifacts (gitignored; produced by `./build.ps1 -Installer` → `dist/`)

`Armadillo-Setup.exe` (installer) · `Armadillo-portable.exe` (GUI) · `armadillo.exe` (CLI).

## How to resume / run

```powershell
git clone https://github.com/Kopachelli/Project-Armadilo && cd Project-Armadilo
dotnet build && dotnet test          # 50 tests
./build.ps1 -Installer               # produce dist/ artifacts
armadillo doctor | run | chain | supervise | connect | serve | autostart
```
Workspace + DB + transcripts: `%LOCALAPPDATA%\Armadillo`. Daemon: `127.0.0.1:8787`.

## Open items / next steps

- **Merge `dev` → `main` and tag `v0.1.0`** → release workflow publishes installer + portable + CLI.
- **Enable GitHub Pages** (Settings → Pages → branch → `/docs`).
- Verify **preview adapters** (Antigravity/Hermes/OpenClaw/Kimi/MiniMax) against real binaries.
- **Richer learned routing**; **cross-platform** (POSIX `IProcessHost`/`IPathProvider`); **cloud** tier.
- For a permanent install: the current wiring points at the installed CLI; if you reinstall, re-run
  `armadillo disconnect && armadillo connect`.
