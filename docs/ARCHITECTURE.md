# Architecture & Models — how it works, and *why*

This explains every model and component in Project Armadillo: what it is, how it works, and the
reasoning behind the design. Pair it with [SCENARIOS.md](SCENARIOS.md), which walks each user-facing
flow end to end.

---

## The shape of the system (and why)

Armadillo is a **self-learning AI core (the "brain") that orchestrates AI CLI tools**. The guiding
principle is *the brain is the product; orchestration is its body*. Four layers keep that separation
clean so each can change independently:

```
Protocol layer   →  how an agent/editor reaches us       (MCP native .NET · A2A + Zed ACP via TS sidecar)
The Core / Brain →  memory · learning · routing · review · governance        (.NET, Armadillo.Core)
Tool layer       →  one adapter per CLI/runtime (pluggable, provider-agnostic)
Orchestration    →  spawn · supervise · isolate · chain                       (the body)
                    Persistence (SQLite + files) · Audit underneath everything
```

**Why layered?** Each axis the user cares about — *which protocol*, *which tool*, *which model
provider* — becomes a swap point, not a rewrite. Adding a tool is one class; adding a protocol is one
sidecar route; switching a model is one config object.

**Why hybrid .NET + TypeScript?** The durable brain/orchestrator is .NET (matches the user's Quiver
product line, native Windows, official C# MCP SDK). The protocol/SDK ecosystem (A2A, Zed ACP) is most
mature in TS, so those live in a thin `sidecar/` that calls the .NET Core over a loopback control API.

**Projects:** `Armadillo.Core` (brain + body, no UI/transport) · `Armadillo.Detection` (probing) ·
`Armadillo.Runtime` (the `Registrator` composition root, shared) · `Armadillo.Mcp` (MCP server +
control API + supervisor host, ASP.NET) · `Armadillo.Host` (CLI/daemon) · `Armadillo.App` (WPF
dashboard) · `sidecar/` (TS protocol bridges).

---

## Data models

### Tool identity — `ToolId`, `ToolKind`, `ToolCapabilities`, `ToolDescriptor`
- **`ToolId`** — the stable enum key for every tool/runtime/desktop app. Persisted in the DB, so it
  never changes meaning.
- **`ToolKind` { Cli, Runtime, Desktop }** — *why it exists:* a desktop GUI **cannot** be driven as a
  headless child process, so it must be detected/reported but never given an adapter. This one
  distinction prevents the harness from ever trying to "run" Claude Desktop.
- **`ToolCapabilities`** (flags: Headless, StreamJson, Hooks, Resume, McpClient, HttpDaemon,
  LocalProvider, Acp) — what a tool *can* do. Mostly static per tool; the Router/brain read these to
  decide what's possible (e.g. only Claude has `Hooks`, so live supervision is Claude-first).
- **`ToolDescriptor`** — the compile-time catalog entry: executable names, dotfolders, `AppPaths`/
  `AppNamePatterns` (desktop), adapter family, capabilities, MCP-config hint, notes. *Why a static
  catalog:* detection and routing need ground truth about each tool that doesn't depend on it being
  installed.

### `DetectedTool` / `CapabilitySnapshot`
The result of probing one tool on *this* machine (installed? path? version? models? signals?) and a
timestamped set of all of them. `Drivable = Installed && Kind != Desktop` is the single predicate the
orchestrator trusts before spawning.

### `ToolAssets`
Per-variant inventory: **skills + MCP servers + plugins + config files**. *Why per-variant:* a CLI and
its desktop app keep assets in different places (e.g. `~/.claude` vs `%APPDATA%\Claude`), so they're
scanned separately — "fetch everything" means everything, for each variant.

### `AgentEvent` (normalized union)
`SessionStarted · TextDelta · ToolCall · ToolResult · FileWritten · TurnEnded · UsageReported ·
SessionStopped · RawLine`. *Why normalize:* every CLI emits a different NDJSON/JSON shape; each adapter
maps its native stream into this one union so **everything downstream** (persistence, supervision,
review, learning) is tool-agnostic.

### `ProviderProfile`
`(Name, Kind, BaseUrl, ApiKey, Model)` + `EnvOverrides()`. *Why:* it decouples *which CLI binary* from
*which model endpoint*. The same Claude/Gemini/Qwen adapter can target cloud Anthropic/OpenAI/Google,
z.ai/GLM, or a local Ollama server by swapping this — delivering privacy (local), cost (cheap clouds),
and flexibility through one seam.

### `SpawnLineage`
`(RootId, Depth)`. *Why:* it's the accounting token that bounds recursion. A spawned agent that itself
calls `request_agent` carries an incremented lineage (via a header in its generated `.mcp.json`), so
the Governor can enforce depth/per-root caps and a single agent can't fork-bomb the machine.

### Persistence records (SQLite)
| Record | Purpose / why |
|---|---|
| `JobRecord` | one inbound request; `parent_job_id`+`root_id` give the spawn tree for safety + audit |
| `SessionRecord` | one spawned process + outcome (tokens, exit, transcript path) |
| `ReviewRecord` | a verdict + confidence on a session — the raw signal the brain learns from |
| `LearningRecord` | a distilled, embedded learning (semantic memory) for RAG retrieval |
| `PlaybookRecord` (versioned) | a persona/system-prompt the brain self-improves; one `Active` per name |
| `ExperimentRecord` | an A/B comparison (incumbent vs candidate) — the audit trail of self-improvement |
| `ToolPrior` | per-tool approve-weighted score — the learned signal that feeds routing |
*Why SQLite + files:* the DB holds structured/queryable state (WAL, single writer); raw transcripts go
to files so they're replayable and never bloat the DB. Everything is append-on-transition → auditable.

---

## Components

### Detection — `IToolDetector` / `ToolDetector` / `ExecutableResolver` / `AssetScanner` / `WindowsInstalledApps`
Probes PATH (search-dirs × names × PATHEXT) for CLIs, the Ollama HTTP API for models, and the **Windows
uninstall registry** for desktop apps (by display-name pattern — *why:* so a GUI is found reliably and
never confused with a same-named CLI). `AssetScanner` reads each tool's skills/MCP/plugins/configs.

### `ProcessRunner` (the spawner)
Runs a child with: **task via stdin** (not argv — avoids the Windows cmdline cap, quoting breakage, and
flag-injection), **stdout cap** (a runaway child can't OOM us), **timeout → kill the whole process
tree**, and live line streaming for NDJSON. These are the hard-won safety behaviors ported from the
prior TS orchestrator.

### `IToolAdapter` + families
One driver per tool: `BuildRunSpec` (argv/stdin/env) + `Parse` (stdout → normalized result). *Why
families:* Gemini CLI and Qwen Code share a lineage, so they share a base; OpenCode/Copilot share a
"daemon" base. Every adapter has a **text fallback** so format drift degrades instead of breaking.
Adapters for not-yet-verified tools ship as "preview."

### `AgentDispatcher` (the heart) — lifecycle
`RECEIVED → Governor.Authorize → route tool → CONTEXTUALIZED (persona + brain-retrieved learnings) →
isolate (git worktree or scratch dir) → SPAWNING → capture transcript → REVIEWED (local model) →
brain captures learning → RETURNED`. Every transition is persisted. *Why this order:* safety first
(authorize before any work), brain context before spawning (so the agent benefits from past runs),
review + capture after (so the brain learns from the outcome).

### `Governor` (safety valve)
Enforces `maxDepth`, `maxConcurrent`, per-root spawn budget. *Why critical:* the on-demand design lets a
spawned agent request more agents; without bounds one misbehaving agent fork-bombs the machine and the
token budget. The kill switch + lineage tree make runaway trees killable and self-modification haltable.

### `Router` (learned routing)
Resolves the tool: explicit-and-available → else rank installed+drivable tools by **learned priors**
(`ToolPrior` approve-weighted score, neutral 0.5 baseline for untested tools, ≥3 samples to trust)
→ tie-break by a static priority. *Why a baseline:* an untested tool sits mid-rank, so a *measured-good*
tool rises above it and a *measured-bad* one sinks below — the self-learning loop closing into
selection without recklessly preferring whatever has any data.

### Brain — `IMemory` (via `IStore`), `Brain`, `IReviewer`
Three memory tiers: **episodic** (every run + transcript), **semantic** (distilled, embedded learnings
retrieved by similarity → injected into future personas), **procedural** (versioned playbooks/policies).
`OllamaReviewer` reviews every output **locally** (verdict + 0–100 confidence) — *why local-first:* it's
$0 and private, which is what makes reviewing *every* run affordable. Verdict parsing is defensive
(JSON → keyword → "no verdict = escalate").

### `SelfImprovementEngine` (autonomous, but earned)
`propose (local model) → A/B evaluate vs incumbent on held-out tasks → promote ONLY on a measured win →
versioned + auto-rollback + audit + kill switch`. Graduated `AutonomyLevel` (CaptureOnly / Adaptive /
Autonomous). *Why this shape:* "fully autonomous" without a measurement gate would let the system
silently degrade itself; here autonomy is *earned by measurement* and always reversible/observable.

### Orchestration body — `ChainRunner`, `WorktreeManager`, `McpEndpoint`, `SupervisorHost`
- **`ChainRunner`** — ordered cross-tool pipelines; output of step N feeds step N+1 (`{goal}`/`{input}`),
  stops on first failure. Decoupled from the dispatcher (takes a delegate) so it's testable.
- **`WorktreeManager`** — per-session git worktree so parallel sessions on one repo can't collide;
  auto-removed. *Why:* isolation is the precondition for safe fan-out on real code.
- **`McpEndpoint`** — late-bound handle to our own MCP server so spawned children can be wired back to
  it (with an incremented lineage) to "call the administrator" recursively, safely.
- **`SupervisorHost`** — loopback endpoint Claude's hooks POST to; observes every event and can BLOCK a
  tool mid-run (PreToolUse `permissionDecision: deny`). *Why hooks:* it's the sanctioned way to steer a
  *running* Claude session; the blocked-tool reason is fed back so the model adapts.

### `Registrator` (composition root)
Wires the whole spine behind one object shared by the CLI (`Armadillo.Host`) and the GUI
(`Armadillo.App`). *Why a shared root:* the dashboard and the CLI must be the *same engine*, not two
implementations that drift.

---

## Safety model (cross-cutting)
Recursion/fork-bomb guard (lineage + caps + circuit breaker) · budgets ($0 local review by default) ·
permission gating (`--yolo`/`--full-auto` behind per-persona autonomy) · isolation (worktrees) ·
kill/timeout (kill-tree) · self-improvement gated by measured A/B + auto-rollback + kill switch ·
append-only audit · loopback-only + token-protected transports. See [SCENARIOS.md](SCENARIOS.md) for how
each surfaces in practice.
