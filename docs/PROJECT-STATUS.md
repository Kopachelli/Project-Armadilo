# Project Armadillo — Status

_A living snapshot of where the project stands. For the full build history see
[SESSION-SUMMARY.md](SESSION-SUMMARY.md); for design see [ARCHITECTURE.md](ARCHITECTURE.md) and
[SCENARIOS.md](SCENARIOS.md)._

## TL;DR
Armadillo is a local, privacy-first **self-learning AI core that orchestrates AI coding CLIs** —
shipping as a Windows **GUI** + **CLI**. It's **built, tested, installed, and publicly released as v0.1.0.**

- **Release:** https://github.com/Kopachelli/Project-Armadilo/releases/tag/v0.1.0
  (`Armadillo-Setup.exe`, `Armadillo-portable.exe`, `armadillo.exe`)
- **Site:** https://kopachelli.github.io/Project-Armadilo/
- **License:** MIT · **Tests:** 50 passing · **Default branch:** `main`

## What works today
- **Detect** installed AI CLIs / runtimes / desktop apps (`doctor`) and their skills, MCP servers, plugins, configs (`assets`).
- **Run** any tool headlessly with local review + learning (`run`); fan out N; **chain** across tools/engines (`chain`).
- **Supervise** a running Claude session live — observe + interrupt via hooks (`supervise`).
- **Autonomous self-improvement** — propose → A/B → promote on a measured win, with rollback + kill switch (`improve`).
- **Connect existing Claude Code sessions** — user-scope MCP + global hooks (`connect`), daemon at login (`autostart`), always-on brain (`serve`, `127.0.0.1:8787`).
- **GUI dashboard** (`Armadillo.App`): Tools / Assets / Activity / Run, over the same engine.

## Verified live
Fresh `claude -p` (no flags) used `request_agent` **and** fired hooks; cross-engine Claude→local-qwen
chain (Verdict REVISE); live supervision blocked Bash; self-improvement correctly rejected a
non-improvement. Installed via Setup.exe → `claude mcp list` shows **armadillo ✓ Connected**.

## Install & use (Windows, per-user, no admin)
1. Download `Armadillo-Setup.exe` from Releases → installs the GUI (Start Menu) + `armadillo` CLI (PATH).
2. `armadillo connect` → wires Armadillo into Claude Code (MCP + hooks).
3. `armadillo autostart on` → daemon runs hidden at login.
4. Open any Claude Code session — it can delegate to and is observed by Armadillo.

(Optional local review/learning: run Ollama + `ollama pull qwen2.5-coder`.)

## Build from source
```powershell
dotnet build && dotnet test       # 50 tests
./build.ps1 -Installer            # dist/: Setup.exe + portable GUI + CLI
```

## Repo layout
`src/Armadillo.Core` (brain + body) · `.Detection` · `.Runtime` (composition root) · `.Mcp`
(MCP + control API + supervisor) · `.Host` (CLI/daemon `armadillo`) · `.App` (WPF GUI) ·
`tests/Armadillo.Tests` · `sidecar/` (TS A2A + ACP) · `installer/` · `docs/`.

## What's next (tracked in Linear → Software Projects)
- 🚩 **Second engine — install OpenAI Codex CLI** (Urgent, blocker) → verify the adapter → two engines for daily use.
- **GTM:** code signing (removes SmartScreen warnings), winget, pricing, launch.
- **Cloud:** hosted/remote daemon + auth/TLS, A2A across machines, shared team memory.
- **Core:** verify preview adapters, richer learned routing, cross-platform (Linux/macOS).

See [LINEAR-BOARD.md](LINEAR-BOARD.md) for the issue board snapshot.
