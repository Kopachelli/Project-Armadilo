/**
 * Thin client for the Armadillo .NET Core's loopback control API (`POST /api/request_agent`).
 * This is the single seam between the TS protocol bridges and the C# brain/orchestrator.
 */

export interface RequestAgentInput {
  task: string;
  persona?: string;
  tool?: string;
  count?: number;
  /** Caller lineage "root:depth"; forwarded so the Core's fork-bomb guard applies. */
  lineage?: string;
}

export interface AgentOutcome {
  jobId: string;
  ok: boolean;
  reason?: string;
  finalText: string;
  verdict: string;
  confidence: number;
  inputTokens: number;
  outputTokens: number;
}

export class ControlClient {
  constructor(
    private readonly baseUrl: string = process.env.ARMADILLO_URL ?? "http://127.0.0.1:8787",
    private readonly token: string = process.env.ARMADILLO_TOKEN ?? "",
  ) {}

  async health(): Promise<boolean> {
    try {
      const r = await fetch(new URL("/api/health", this.baseUrl));
      return r.ok;
    } catch {
      return false;
    }
  }

  async requestAgent(input: RequestAgentInput): Promise<AgentOutcome[]> {
    const headers: Record<string, string> = { "content-type": "application/json" };
    if (this.token) headers["X-Armadillo-Token"] = this.token;
    if (input.lineage) headers["X-Armadillo-Lineage"] = input.lineage;

    const res = await fetch(new URL("/api/request_agent", this.baseUrl), {
      method: "POST",
      headers,
      body: JSON.stringify({
        task: input.task,
        persona: input.persona,
        tool: input.tool,
        count: input.count ?? 1,
      }),
    });
    if (!res.ok) throw new Error(`control API ${res.status}: ${await res.text()}`);
    const data = (await res.json()) as { results: AgentOutcome[] };
    return data.results;
  }
}
