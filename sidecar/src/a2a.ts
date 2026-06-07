/**
 * A2A (Agent2Agent) bridge — exposes the Armadillo harness as an A2A agent. Implements the JSON-RPC
 * surface at POST /a2a: `message/send`, `message/stream` (SSE), and `tasks/get`, plus the Agent Card.
 * Translates to the Core via the control API. Spec: https://a2a-protocol.org
 */
import { randomUUID } from "node:crypto";
import type { Express, Request, Response } from "express";
import { ControlClient } from "./controlClient.js";

interface TaskRecord {
  id: string;
  state: "submitted" | "working" | "completed" | "failed";
  text: string;
}

const tasks = new Map<string, TaskRecord>();

export function mountA2A(app: Express, control: ControlClient, publicUrl: string): void {
  app.get("/.well-known/agent-card.json", (_req: Request, res: Response) => {
    res.json({
      protocolVersion: "0.3.0",
      name: "Armadillo Registrator",
      description:
        "Local self-learning AI core. Delegate a self-contained task; it spawns/captures/reviews a headless CLI agent.",
      version: "0.1.0",
      url: `${publicUrl}/a2a`,
      preferredTransport: "JSONRPC",
      capabilities: { streaming: true, pushNotifications: false, stateTransitionHistory: false },
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

  // JSON-RPC 2.0 endpoint (message/send, message/stream, tasks/get).
  app.post("/a2a", async (req: Request, res: Response) => {
    const { id, method, params } = req.body ?? {};
    try {
      switch (method) {
        case "message/send": {
          const text = extractText(params?.message);
          if (!text) return res.json(rpcErr(id, -32602, "no text part in message"));
          const task = newTask();
          const [outcome] = await control.requestAgent({ task: text });
          task.state = outcome?.ok ? "completed" : "failed";
          task.text = outcome?.finalText ?? "";
          return res.json(rpcOk(id, agentMessage(task)));
        }
        case "message/stream": {
          const text = extractText(params?.message);
          if (!text) return res.json(rpcErr(id, -32602, "no text part in message"));
          return streamTask(res, id, control, text);
        }
        case "tasks/get": {
          const t = tasks.get(params?.id);
          return res.json(t ? rpcOk(id, taskObject(t)) : rpcErr(id, -32001, "task not found"));
        }
        default:
          return res.json(rpcErr(id, -32601, `method not found: ${method}`));
      }
    } catch (err) {
      res.json(rpcErr(id, -32000, String(err)));
    }
  });

  // Convenience REST alias for message/send.
  app.post("/a2a/message:send", async (req: Request, res: Response) => {
    const text = extractText(req.body?.message ?? req.body);
    if (!text) return res.status(400).json({ error: "no text part in message" });
    const task = newTask();
    const [outcome] = await control.requestAgent({ task: text });
    task.state = outcome?.ok ? "completed" : "failed";
    task.text = outcome?.finalText ?? "";
    res.json(agentMessage(task));
  });
}

async function streamTask(res: Response, id: unknown, control: ControlClient, text: string): Promise<void> {
  res.setHeader("Content-Type", "text/event-stream");
  res.setHeader("Cache-Control", "no-cache");
  res.setHeader("Connection", "keep-alive");
  res.flushHeaders?.();

  const task = newTask();
  const frame = (result: unknown) => res.write(`data: ${JSON.stringify({ jsonrpc: "2.0", id, result })}\n\n`);

  // initial submitted/working status
  frame(statusUpdate(task, "working", false));
  try {
    const [outcome] = await control.requestAgent({ task: text });
    task.state = outcome?.ok ? "completed" : "failed";
    task.text = outcome?.finalText ?? "";
    frame(agentMessage(task));                 // the result message
    frame(statusUpdate(task, task.state, true)); // final status (terminal)
  } catch (err) {
    task.state = "failed";
    task.text = String(err);
    frame(statusUpdate(task, "failed", true));
  }
  res.end();
}

function newTask(): TaskRecord {
  const t: TaskRecord = { id: randomUUID(), state: "submitted", text: "" };
  tasks.set(t.id, t);
  return t;
}

function agentMessage(task: TaskRecord) {
  return {
    kind: "message",
    role: "agent",
    messageId: randomUUID(),
    taskId: task.id,
    parts: [{ kind: "text", text: task.text }],
  };
}

function taskObject(task: TaskRecord) {
  return { kind: "task", id: task.id, status: { state: task.state }, };
}

function statusUpdate(task: TaskRecord, state: string, final: boolean) {
  return { kind: "status-update", taskId: task.id, status: { state }, final };
}

function rpcOk(id: unknown, result: unknown) { return { jsonrpc: "2.0", id, result }; }
function rpcErr(id: unknown, code: number, message: string) { return { jsonrpc: "2.0", id, error: { code, message } }; }

function extractText(message: unknown): string | null {
  const msg = (message as any) ?? {};
  const parts = msg?.parts;
  if (Array.isArray(parts)) {
    const texts = parts.filter((p) => p?.kind === "text" || typeof p?.text === "string").map((p) => p.text);
    if (texts.length) return texts.join("\n");
  }
  if (typeof msg?.text === "string") return msg.text;
  return null;
}
