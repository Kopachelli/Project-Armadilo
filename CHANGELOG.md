# Changelog

All notable changes to Armadillo are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/), and the project uses
[semantic versioning](https://semver.org/).

## [0.1.0] — 2026-06-07

First packaged release: the self-learning AI core + CLI-agent orchestration harness, as both a
**standalone GUI** and a **CLI**.

### Core / brain
- Spine: detect installed AI CLIs/runtimes → spawn headless → capture transcript → local review → learn.
- Memory (episodic + semantic + procedural), local-first Ollama reviewer, learned **routing priors**.
- Autonomous **self-improvement**: propose → A/B → promote on measured win, with versioning,
  auto-rollback, audit, and a kill switch.
- **Governor** fork-bomb/recursion guard (per-session lineage + caps).

### Tools & assets
- Adapters for Claude, Codex, Cursor, Gemini, Qwen, Copilot, Pi, OpenCode, OpenClaw, Hermes,
  Antigravity, Kimi, MiniMax, and local Ollama (preview adapters where untested).
- Registry-based **desktop-app detection**; per-variant **asset discovery** (skills + MCP servers +
  plugins + config files).

### Orchestration
- MCP server (`request_agent`) — agents "call the administrator"; cross-tool **chains**;
  git **worktree isolation**; Claude-hook **live supervision** (observe + interrupt a running session).

### Protocols
- Native MCP (.NET). TS `sidecar/`: **A2A** (JSON-RPC + `message/stream` over SSE) and a Zed **ACP** bridge.

### Apps & packaging
- **Armadillo.App** — standalone native-Windows WPF dashboard (Tools / Assets / Activity / Run).
- **armadillo** — CLI (`doctor`, `assets`, `run`, `chain`, `supervise`, `serve`, `improve`,
  `playbooks`, `kill-switch`).
- Portable self-contained single-file exes (GUI + CLI), per-user PowerShell installer, and an
  Inno Setup installer that also puts the CLI on PATH.

[0.1.0]: https://github.com/Kopachelli/Project-Armadilo/releases/tag/v0.1.0
