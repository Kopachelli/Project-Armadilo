/**
 * Zed ACP (Agent Client Protocol) bridge.
 *
 * Lets an editor (Zed, JetBrains, VS Code, Neovim…) drive Armadillo over JSON-RPC on stdio. Handles
 * the core agent lifecycle: `initialize`, `authenticate`, `session/new`, `session/load`,
 * `session/prompt` (streams `session/update` chunks then returns a stopReason), and `session/cancel`.
 * Translates prompts to the Armadillo control API. Spec: https://agentclientprotocol.com
 *
 * Run:  ARMADILLO_URL=... ARMADILLO_TOKEN=... npm run acp   (then connect an ACP-capable editor)
 */
import { createInterface } from "node:readline";
import { randomUUID } from "node:crypto";
import { ControlClient } from "./controlClient.js";

const control = new ControlClient();
const sessions = new Map<string, { cancelled: boolean }>();
const PROTOCOL_VERSION = 1;

function send(msg: unknown): void {
  process.stdout.write(JSON.stringify(msg) + "\n");
}
function reply(id: unknown, result: unknown): void { send({ jsonrpc: "2.0", id, result }); }
function fail(id: unknown, code: number, message: string): void { send({ jsonrpc: "2.0", id, error: { code, message } }); }
function notify(method: string, params: unknown): void { send({ jsonrpc: "2.0", method, params }); }

async function handle(req: any): Promise<void> {
  const { id, method, params } = req;
  switch (method) {
    case "initialize":
      reply(id, {
        protocolVersion: PROTOCOL_VERSION,
        agentCapabilities: {
          loadSession: true,
          promptCapabilities: { image: false, audio: false, embeddedContext: true },
        },
        authMethods: [],
      });
      return;

    case "authenticate":
      reply(id, {});
      return;

    case "session/new": {
      const sessionId = "acp_" + randomUUID();
      sessions.set(sessionId, { cancelled: false });
      reply(id, { sessionId });
      return;
    }

    case "session/load": {
      const sid = params?.sessionId;
      if (sid && !sessions.has(sid)) sessions.set(sid, { cancelled: false });
      reply(id, {});
      return;
    }

    case "session/cancel": {
      const s = sessions.get(params?.sessionId);
      if (s) s.cancelled = true;
      // session/cancel is a notification in ACP; only reply if it carried an id.
      if (id !== undefined) reply(id, {});
      return;
    }

    case "session/prompt": {
      const sessionId = params?.sessionId;
      const session = sessions.get(sessionId) ?? { cancelled: false };
      session.cancelled = false;
      sessions.set(sessionId, session);

      const text = (params?.prompt ?? [])
        .filter((p: any) => p?.type === "text" || typeof p?.text === "string")
        .map((p: any) => p.text)
        .join("\n");
      if (!text) return fail(id, -32602, "empty prompt");

      try {
        const [outcome] = await control.requestAgent({ task: text });
        if (session.cancelled) { reply(id, { stopReason: "cancelled" }); return; }
        // Stream the result back as an assistant message chunk, then end the turn.
        notify("session/update", {
          sessionId,
          update: {
            sessionUpdate: "agent_message_chunk",
            content: { type: "text", text: outcome?.finalText ?? "" },
          },
        });
        reply(id, { stopReason: outcome?.ok ? "end_turn" : "refusal" });
      } catch (err) {
        fail(id, -32000, String(err));
      }
      return;
    }

    default:
      if (id !== undefined) fail(id, -32601, `method not implemented: ${method}`);
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

process.stderr.write("Armadillo ACP bridge ready on stdio.\n");
