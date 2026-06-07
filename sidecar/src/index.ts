/**
 * Armadillo protocol sidecar (HTTP). Hosts the A2A bridge and translates to the .NET Core's control
 * API. Run the Core first (`armadillo serve`), then start this with ARMADILLO_URL + ARMADILLO_TOKEN
 * pointed at it. The Zed ACP bridge runs separately over stdio (see acp.ts).
 */
import express from "express";
import { ControlClient } from "./controlClient.js";
import { mountA2A } from "./a2a.js";

const port = Number(process.env.SIDECAR_PORT ?? 8788);
const publicUrl = process.env.SIDECAR_PUBLIC_URL ?? `http://127.0.0.1:${port}`;
const control = new ControlClient();

const app = express();
app.use(express.json({ limit: "4mb" }));

app.get("/health", async (_req, res) => {
  res.json({ ok: true, coreReachable: await control.health() });
});

mountA2A(app, control, publicUrl);

app.listen(port, "127.0.0.1", async () => {
  const core = (await control.health()) ? "reachable" : "NOT reachable (start `armadillo serve`)";
  console.log(`Armadillo sidecar on http://127.0.0.1:${port}`);
  console.log(`  Core control API: ${core}`);
  console.log(`  A2A agent card:   ${publicUrl}/.well-known/agent-card.json`);
});
