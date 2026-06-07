/**
 * A2A (Agent2Agent) bridge — exposes the Armadillo harness as an A2A-speaking agent so external
 * agents can delegate tasks to it. Simplified to the essentials (Agent Card + message send);
 * translate to the Core via the control API. Spec: https://a2a-protocol.org
 */
import type { Express, Request, Response } from "express";
import { ControlClient } from "./controlClient.js";

export function mountA2A(app: Express, control: ControlClient, publicUrl: string): void {
  // Agent Card — how other agents discover what this agent can do.
  app.get("/.well-known/agent-card.json", (_req: Request, res: Response) => {
    res.json({
      name: "Armadillo Registrator",
      description:
        "Local self-learning AI core. Delegate a self-contained task and it spawns/captures/reviews a headless CLI agent.",
      version: "0.1.0",
      url: `${publicUrl}/a2a`,
      capabilities: { streaming: false },
      defaultInputModes: ["text/plain"],
      defaultOutputModes: ["text/plain"],
      skills: [
        {
          id: "request_agent",
          name: "Request an agent",
          description: "Run a self-contained task on a headless CLI agent and return the reviewed result.",
          tags: ["delegation", "coding", "orchestration"],
        },
      ],
    });
  });

  // message:send — accept an A2A message, run it, return a message with the result.
  app.post("/a2a/message:send", async (req: Request, res: Response) => {
    try {
      const text = extractText(req.body);
      if (!text) return res.status(400).json({ error: "no text part in message" });

      const [outcome] = await control.requestAgent({ task: text });
      res.json({
        kind: "message",
        role: "agent",
        parts: [{ kind: "text", text: outcome?.finalText ?? "(no result)" }],
        metadata: {
          jobId: outcome?.jobId,
          verdict: outcome?.verdict,
          confidence: outcome?.confidence,
          ok: outcome?.ok,
        },
      });
    } catch (err) {
      res.status(502).json({ error: String(err) });
    }
  });
}

function extractText(body: unknown): string | null {
  // Accept either {message:{parts:[{kind:'text',text}]}} or {parts:[...]} or {text}.
  const msg = (body as any)?.message ?? body;
  const parts = msg?.parts;
  if (Array.isArray(parts)) {
    const texts = parts.filter((p) => p?.kind === "text" || typeof p?.text === "string").map((p) => p.text);
    if (texts.length) return texts.join("\n");
  }
  if (typeof msg?.text === "string") return msg.text;
  return null;
}
