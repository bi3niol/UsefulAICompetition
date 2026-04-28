import { AnalysisResult, BackendConfig } from "../types";

export type TokenProvider = () => Promise<string | null>;

/** Calls AnalyzeWorkItemFunction on the .NET backend. Framework-agnostic. */
export class ImpactAnalysisClient {
  constructor(
    private readonly config: BackendConfig,
    private readonly tokenProvider: TokenProvider = async () => null
  ) {}

  async analyze(workItemId: number, signal?: AbortSignal): Promise<AnalysisResult> {
    if (!this.config.functionUrl) {
      throw new Error("Function URL is not configured. Open extension options and set it.");
    }

    const headers: Record<string, string> = {
      "Content-Type": "application/json",
      "Accept": "text/markdown, text/plain, */*"
    };

    const token = await this.tokenProvider();
    if (token) {
      headers["Authorization"] = `Bearer ${token}`;
    } else if (this.config.functionKey) {
      headers["x-functions-key"] = this.config.functionKey;
    }

    const url = `${this.trimUrl(this.config.functionUrl)}/api/AnalyzeWorkItem/${workItemId}`;
    const res = await fetch(url, { method: "POST", headers, signal });

    if (!res.ok) {
      const body = await res.text().catch(() => "");
      throw new Error(`Analysis request failed (${res.status} ${res.statusText}). ${body}`);
    }

    const markdown = await res.text();
    return { markdown };
  }

  private trimUrl(u: string): string {
    return u.endsWith("/") ? u.slice(0, -1) : u;
  }
}
