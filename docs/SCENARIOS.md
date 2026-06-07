# Scenarios — every flow, how it works and why

Each scenario below is a real, working capability. For each: the **command**, the **flow** (what
actually happens), the **why**, and **status** (what's been verified live). See
[ARCHITECTURE.md](ARCHITECTURE.md) for the models these use.

Workspace for all of it: `%LOCALAPPDATA%\Armadillo` (SQLite `armadillo.db` + per-session transcripts +
config). Everything is loopback-only and token-protected.

---

## 1. Detect installed tools — `armadillo doctor`
**Flow:** probe PATH (search-dirs × names × PATHEXT) for each CLI; probe Ollama's HTTP API for models;
probe the Windows uninstall registry for desktop apps by display-name; render a matrix with kind +
capabilities + a "drivable" count.
**Why:** the brain can't route to tools it can't see, and it must never try to "run" a desktop GUI —
hence the `Cli`/`Runtime`/`Desktop` split and the registry-based desktop detection (no PATH-name
collision with CLIs).
**Status:** ✅ live — detects Claude, Gemini CLI, Ollama (drivable) + Cursor IDE / OpenCode Desktop /
Claude app (desktop, reported not driven).

## 2. Fetch everything per tool — `armadillo assets [filter]`
**Flow:** for each tool *variant*, scan its skill roots (count `SKILL.md`), parse MCP servers from JSON
(`.mcp.json`, `mcp.json`, `claude_desktop_config.json`) and TOML (`config.toml`, top-level
`mcp_servers.*`), and read plugins (`plugins/cache/**/.claude-plugin|.codex-plugin/plugin.json` +
Claude `installed_plugins.json`).
**Why:** a CLI and its desktop app keep assets in different locations, so each variant is scanned at its
own paths — that's what makes "fetch everything" honest.
**Status:** ✅ live — Claude Code (CLI configs + 6 plugins) vs Claude Desktop (`%APPDATA%` config) shown
separately; Codex surfaced 6 skills + 7 MCP servers + 2 plugins.

## 3. Run one agent — `armadillo run "<task>" [--tool T] [--count N] [--review-model M]`
**Flow:** `Registrator` builds the spine → Governor authorizes → Router picks the tool (explicit, or
**learned routing** if `--tool` omitted) → brain retrieves relevant past learnings into the persona →
spawn headless (stdin task, stdout cap, kill-tree) → capture transcript → local reviewer scores it →
brain captures a learning → result returned. `--count N` fans out N siblings concurrently.
**Why:** this is the spine + brain in one call — every run feeds the memory that improves later runs.
**Status:** ✅ live — Claude haiku/“hello” with qwen review **Approve (1.00)**, learning embedded.

## 4. "Call the administrator" — `armadillo serve` + MCP `request_agent`
**Flow:** the daemon hosts an MCP server (Streamable HTTP, 127.0.0.1, ephemeral port, shared token) and
writes an operator `.mcp.json`. A running agent (e.g. Claude) calls the `request_agent` tool → the
dispatcher spawns a sub-agent → captures + reviews → returns the result through the tool call. The
spawned child gets its own `.mcp.json` wired back to the server with an **incremented lineage header**,
so it can itself call `request_agent` — bounded by the Governor.
**Why:** MCP is the universal, native way for these CLIs to reach an external capability; lineage is how
on-demand recursion stays safe.
**Status:** ✅ live — operator Claude → `request_agent` → sub-agent returned a poem; child wired back
with `root:0`. Fork-bomb guard unit-tested.

## 5. Cross-tool / cross-engine chains — `armadillo chain "<goal>" [--repo PATH]` or `chain --spec file.json`
**Flow:** run ordered steps; each step's output feeds the next (`{goal}` = original, `{input}` =
previous output; no placeholder → previous output appended). Each step may use a **different tool**.
With `--repo`, each step runs in an isolated **git worktree** off that repo (auto-removed). Stops on the
first failed step.
**Why:** real pipelines (implement → review → test) span tools/engines; worktrees make parallel/sequential
work on one repo safe.
**Status:** ✅ live — (a) Claude→Claude in git worktrees, reviewer APPROVE, worktrees cleaned up;
(b) **cross-engine** Claude (cloud) → local qwen via Ollama, Verdict REVISE — free, on-device.

## 6. Live single-session supervision — `armadillo supervise "<task>" [--deny Tool1,Tool2]`
**Flow:** start a loopback `SupervisorHost`; generate a Claude hooks settings file whose hook command is
`armadillo hook` (it forwards each event to the supervisor and returns the decision); spawn
`claude -p --settings <file>`. Every PreToolUse/PostToolUse/Stop event streams to the console live; a
denied tool is **blocked mid-run** via PreToolUse `permissionDecision: deny`, and the reason is fed back
to Claude so it adapts.
**Why:** hooks are the sanctioned way to observe and steer a *running* Claude session (Claude-first,
because only Claude exposes hooks).
**Status:** ✅ live — denied `Bash`, blocked it in real time, Claude acknowledged and offered an
alternative.

## 7. Autonomous self-improvement — `armadillo improve <playbook> --tasks "a||b"` · `playbooks <name>` · `kill-switch on|off`
**Flow:** the engine proposes an improved persona/system-prompt (local model) → A/B evaluates candidate
vs incumbent on the eval tasks (each scored by the local reviewer) → **promotes only on a measured win
by margin** → versions it, with **auto-rollback** if a later version regresses, an append-only
experiment/audit trail, and a global **kill switch**. `playbooks` lists versions; `kill-switch` halts
all self-modification.
**Why:** "fully autonomous" is implemented as *autonomy earned by measurement* — powerful but unable to
silently degrade itself.
**Status:** ✅ live — a cycle correctly **rejected** a non-improvement (incumbent 1.00 vs candidate
0.95). Promotion/rollback/kill-switch unit-tested.

## 8. Learned routing (implicit in #3/#4/#5)
**Flow:** when a request omits the tool, the Router ranks installed+drivable tools by `ToolPrior`
(approve-weighted review score from past runs; neutral 0.5 baseline; ≥3 samples to trust), tie-broken by
a static priority. The dispatcher audits `auto-routed -> <tool>`.
**Why:** the review signal the brain already collects should decide *which tool to use next* — the
self-learning loop closing into selection. The baseline stops it preferring a tool just because it has
any data.
**Status:** ✅ unit-tested — a tool measured 0.9 is chosen over Claude's higher static priority; with no
data, falls back to static order.

## 9. Other protocols — the TS sidecar (`sidecar/`)
**Flow:** `npm start` runs an HTTP sidecar that translates **A2A** (JSON-RPC `message/send`,
`message/stream` over SSE, `tasks/get`, Agent Card) to the .NET **control API** (`POST
/api/request_agent`, token + lineage headers). `npm run acp` runs the **Zed ACP** stdio bridge
(`initialize`/`session.new`/`session.prompt` streaming `session/update` + `session.cancel`). MCP stays
native in .NET.
**Why:** the A2A/ACP SDK ecosystem is strongest in TS; keeping the Core as the single source of truth
behind one control API means protocols are thin translators, not forks of the engine.
**Status:** ✅ live — A2A `message:send` and **`message/stream` SSE** verified end-to-end
(working → message → completed). ACP typechecked; full editor round-trip pending an ACP editor.

## 10. The dashboard — `Armadillo.App` (standalone WPF)
**Flow:** a native-Windows WPF-UI app over the *same in-process `Registrator`*. Tabs: **Tools**
(capability matrix), **Assets** (skills/MCP/plugins/configs per variant), **Activity** (recent jobs from
SQLite), **Run** (spawn a task on a chosen tool). Reads the same `%LOCALAPPDATA%\Armadillo` workspace.
**Why:** Phase 5 as a *separate* app (not a Quiver view) keeps the Core headless and lets the GUI ship
independently and go portable/installable.
**Packaging:** portable self-contained single-file `Armadillo.exe` + `dist\Armadillo-portable-*.zip`
(unzip & run); per-user `installer\install.ps1` (no admin; Start-Menu + Apps & features) and
`installer\Armadillo.iss` (Inno Setup → `dist\Armadillo-Setup.exe`).
**Status:** ✅ builds + publishes (portable 67 MB exe); installer dry-run verified. Visual run is yours
to confirm (`publish-app\Armadillo.exe`).
