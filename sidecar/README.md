# Armadillo Protocol Sidecar (Phase 2)

The TypeScript half of the hybrid harness. It bridges agent-interop protocols to the .NET Core's
loopback **control API** (`POST /api/request_agent`), so the C# brain/orchestrator stays the single
source of truth while TS handles the protocol/SDK ecosystem.

- **A2A (Agent2Agent)** — `src/a2a.ts`: exposes Armadillo as an A2A agent (Agent Card + `message:send`)
  so other agents can delegate tasks to it. *Working scaffold.*
- **Zed ACP (Agent Client Protocol)** — `src/acp.ts`: lets editors (Zed/JetBrains/VS Code/Neovim)
  drive Armadillo over stdio JSON-RPC. *Preview — minimal `initialize` / `session/new` / `session/prompt`.*
- **MCP** stays native in the .NET Core (`Armadillo.Mcp`) — not duplicated here.

## Run

```bash
# 1) Start the Core (writes config/sidecar.env with URL + token):
#    armadillo serve
# 2) Load those values and start the HTTP sidecar (A2A):
$env:ARMADILLO_URL = "http://127.0.0.1:<port>"     # from sidecar.env
$env:ARMADILLO_TOKEN = "<token>"
npm install
npm start
#    -> A2A agent card at http://127.0.0.1:8788/.well-known/agent-card.json

# ACP (editor over stdio), preview:
npm run acp
```

## Contract

`ControlClient` (`src/controlClient.ts`) is the only coupling to the Core:
`POST /api/request_agent { task, persona?, tool?, count? }` with headers `X-Armadillo-Token` and
optional `X-Armadillo-Lineage` (so the Core's fork-bomb guard applies to protocol-driven spawns too).

## Status / next

- A2A: add task/get + streaming (SSE) + signed Agent Cards.
- ACP: full conformance (session/update streaming, permission + fs methods) via `@zed-industries/agent-client-protocol`.
- Reuse the existing TS orchestrator adapters here where they beat the .NET ones.
