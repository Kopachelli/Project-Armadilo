<div align="center">

# 🦔 Armadillo

### A local, privacy-first **self-learning AI core** that orchestrates your AI coding CLIs.

[![CI](https://github.com/Kopachelli/Project-Armadilo/actions/workflows/ci.yml/badge.svg)](https://github.com/Kopachelli/Project-Armadilo/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Kopachelli/Project-Armadilo?include_prereleases&sort=semver)](https://github.com/Kopachelli/Project-Armadilo/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D6)

*Detect your installed AI agents → run them headlessly → review + learn from every result →
let any agent “call the administrator” for help. All on your machine.*

[Install](#-install) · [How it works](#-how-it-works) · [Docs](docs/ARCHITECTURE.md) · [Scenarios](docs/SCENARIOS.md)

</div>

---

## What is Armadillo?

Armadillo is the **brain and dispatcher** for the AI coding tools you already have. Instead of babysitting
one agent in one terminal, you get a local engine that:

- 🔎 **Detects** every installed AI CLI, local model runtime, and even desktop app.
- 🚀 **Runs** any of them headlessly on a task — one agent, or many in parallel.
- 🧪 **Reviews** every result with a **local** model (private, $0 tokens) and **remembers** it.
- 🧠 **Learns** which tool/approach works, and routes future work accordingly.
- 🤝 **Lets agents call for help** — a running Claude Code session can delegate sub-tasks back to Armadillo (MCP).
- 🔌 **Plugs into protocols** — MCP (native), plus A2A + Zed ACP via a TypeScript sidecar.
- 🛡️ **Stays safe** — fork-bomb/recursion guards, budgets, kill switch, full audit. Local-first, loopback-only.

It ships as a **standalone Windows app** and a **CLI** (`armadillo`) over the same engine.

## ✨ Highlights

| | |
|---|---|
| **One brain, many tools** | Claude Code, Codex, Cursor, Gemini, Qwen, GitHub Copilot, Pi, OpenCode, OpenClaw, Hermes, Antigravity, Kimi, MiniMax, Ollama |
| **Cross-tool chains** | Pipelines where step N's output feeds step N+1 — across *different* engines (e.g. Claude → local Qwen) |
| **Live supervision** | Observe and **interrupt a running Claude session** via hooks |
| **Autonomous self-improvement** | Proposes better playbooks, A/B-tests them, promotes only on a measured win — with auto-rollback + kill switch |
| **Works with your existing sessions** | `armadillo connect` wires Armadillo into Claude Code (user-scope MCP + global hooks) |
| **Fetch everything** | Per-tool skills + MCP servers + plugins + configs (CLI vs Desktop kept distinct) |

## 🔧 How it works

```
        editors / external agents / running CLI sessions
   ┌──────── Protocols ────────┐   MCP (native .NET) · A2A + Zed ACP (TS sidecar)
   ▼
   THE CORE / BRAIN (.NET)      memory · learning · routing · local review · governor
   ▼
   Tool adapters (pluggable)    one driver per CLI/runtime · provider-agnostic (cloud or local)
   ▼
   Orchestration body           spawn · supervise · git-worktree isolation · chains
        Persistence (SQLite + transcripts) · append-only audit
```

When you run a task: the **Governor** authorizes it → the **Router** picks the tool (your choice, or
**learned priors**) → the **brain** injects relevant past learnings → the agent runs **headless** in an
isolated dir → the **reviewer** scores it locally → the brain **captures a learning** → you get the result.
Full write-up: **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** and **[docs/SCENARIOS.md](docs/SCENARIOS.md)**.

## 📦 Install

**Option A — Installer** (per-user, no admin): download `Armadillo-Setup.exe` from
[Releases](https://github.com/Kopachelli/Project-Armadilo/releases) and run it. Installs the **app**
(Start Menu) and the **`armadillo` CLI** (on PATH).

**Option B — Portable**: download `Armadillo-portable.exe` (GUI) or `armadillo.exe` (CLI) and just run it.

**Build it yourself:**
```powershell
git clone https://github.com/Kopachelli/Project-Armadilo
cd Project-Armadilo
./build.ps1 -Installer    # -> dist\Armadillo-Setup.exe, Armadillo-portable.exe, armadillo.exe
```

> Requires .NET 8 SDK to build. Local review/learning uses [Ollama](https://ollama.com) (e.g. `ollama pull qwen2.5-coder`).

## 🚀 Quick start

```powershell
armadillo doctor                     # what AI tools are installed
armadillo assets                     # skills / MCP servers / plugins / configs per tool
armadillo run "Write a haiku."       # spawn an agent, get a reviewed result
armadillo chain "Build X" --repo .   # cross-tool pipeline in an isolated git worktree

# Wire Armadillo into your existing Claude Code sessions:
armadillo connect                    # user-scope MCP + global hooks
armadillo autostart on               # run the daemon hidden at login
armadillo serve                      # (or it auto-starts) — keep the brain available
```

Now any Claude Code session you open can call the `request_agent` tool and is observed by Armadillo.
Undo with `armadillo disconnect`.

## 🖥️ The app

A native WPF dashboard with four tabs: **Tools** (capability matrix), **Assets**
(skills/MCP/plugins/configs), **Activity** (recent jobs), **Run** (spawn a task). Same engine, same
workspace (`%LOCALAPPDATA%\Armadillo`) as the CLI.

## 🗺️ Roadmap

- [x] Core spine + brain, local review, learned routing
- [x] Multi-tool adapters, parallel fan-out, cross-tool chains, git-worktree isolation
- [x] Autonomous self-improvement (A/B promotion, rollback, kill switch)
- [x] MCP server, A2A streaming, Zed ACP bridge
- [x] Standalone GUI + CLI, installer, connect existing Claude sessions
- [ ] Richer learned routing; cross-platform (Linux/macOS); cloud / multi-machine

## 🤝 Contributing

Issues and PRs welcome. `dotnet build` + `dotnet test` (xUnit). See the docs for architecture.

## 📄 License

[MIT](LICENSE) — free and open source.
