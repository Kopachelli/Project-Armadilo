/**
 * Zed ACP (Agent Client Protocol) bridge — PREVIEW.
 *
 * ACP lets an editor (Zed, JetBrains, VS Code, Neovim…) drive an agent over JSON-RPC on stdio.
 * This is a minimal, honest starting point: it speaks newline-delimited JSON-RPC, answers
 * `initialize` / `session/new`, and translates `session/prompt` into an Armadillo control-API call.
 * Full conformance (streaming session/update notifications, permissions, fs methods) is the next step.
 * Spec: https://agentclientprotocol.com
 *
 * Run:  ARMADILLO_URL=... ARMADILLO_TOKEN=... npm run acp     (then connect an ACP-capable editor)
 */
import { createInterface } from "node:readline";
import { ControlClient } from "./controlClient.js";

const control = new ControlClient();
const sessions = new Map<string, true>();

function send(msg: unknown): void {
  process.stdout.write(JSON.stringify(msg) + "\n");
}

function reply(id: unknown, result: unknown): void {
  send({ jsonrpc: "2.0", id, result });
}

function fail(id: unknown, message: string): void {
  send({ jsonrpc: "2.0", id, error: { code: -32000, message } });
}

async function handle(req: any): Promise<void> {
  const { id, method, params } = req;
  switch (method) {
    case "initialize":
      reply(id, {
        protocolVersion: 1,
        agentCapabilities: { promptCapabilities: { image: false, audio: false } },
      });
      return;

    case "session/new": {
      const sessionId = "acp_" + Math.random().toString(36).slice(2, 10);
      sessions.set(sessionId, true);
      reply(id, { sessionId });
      return;
    }

    case "session/prompt": {
      const text: string = (params?.prompt ?? [])
        .filter((p: any) => p?.type === "text" || typeof p?.text === "string")
        .map((p: any) => p.text)
        .join("\n");
      if (!text) return fail(id, "empty prompt");
      try {
        const [outcome] = await control.requestAgent({ task: text });
        // Stream the result back as a session update, then end the turn.
        send({ jsonrpc: "2.0", method: "session/update", params: {
          sessionId: params?.sessionId,
          update: { sessionUpdate: "agent_message_chunk", content: { type: "text", text: outcome?.finalText ?? "" } },
        }});
        reply(id, { stopReason: "end_turn" });
      } catch (err) {
        fail(id, String(err));
      }
      return;
    }

    default:
      if (id !== undefined) fail(id, `method not implemented: ${method}`);
  }
}

const rl = createInterface({ input: process.stdin });
rl.on("line", (line) => {
  const trimmed = line.trim();
  if (!trimmed) return;
  let req: any;
  try { req = JSON.parse(trimmed); } catch { return; }
  void handle(req);
});

process.stderr.write("Armadillo ACP bridge (preview) ready on stdio.\n");
