import { AnalysisResult, BackendConfig } from "../types";

export type TokenProvider = () => Promise<string | null>;

/** Calls the report endpoints on the .NET backend. Framework-agnostic. */
export class ImpactAnalysisClient {
  constructor(
    private readonly config: BackendConfig,
    private readonly tokenProvider: TokenProvider = async () => null
  ) {}

  /** GET stored report. Returns null when no report has been generated yet (404). */
  async getReport(workItemId: number, signal?: AbortSignal): Promise<AnalysisResult | null> {
    const url = `${this.baseUrl()}/api/workitems/${workItemId}/report`;
    const res = await fetch(url, {
      method: "GET",
      headers: await this.buildHeaders(),
      signal
    });

    if (res.status === 404) return null;
    if (!res.ok) {
      const body = await res.text().catch(() => "");
      throw new Error(`Get report failed (${res.status} ${res.statusText}). ${body}`);
    }
    return { markdown: await res.text() };
  }

  /** POST — runs the multi-agent pipeline and returns the freshly generated report. */
  async generateReport(workItemId: number, signal?: AbortSignal): Promise<AnalysisResult> {
    const url = `${this.baseUrl()}/api/workitems/${workItemId}/report`;
    const res = await fetch(url, {
      method: "POST",
      headers: await this.buildHeaders(),
      signal
    });

    if (!res.ok) {
      const body = await res.text().catch(() => "");
      throw new Error(`Generate report failed (${res.status} ${res.statusText}). ${body}`);
    }
    return { markdown: await res.text() };
  }

  private baseUrl(): string {
    if (!this.config.functionUrl) {
      throw new Error("Function URL is not configured. Open extension options and set it.");
    }
    const base = this.config.functionUrl.endsWith("/")
      ? this.config.functionUrl.slice(0, -1)
      : this.config.functionUrl;
    if (/\/api(\/|$)/i.test(base)) {
      throw new Error(
        `Function URL should be the base URL only (e.g. http://localhost:7272), not include "/api/...". Got: ${base}`
      );
    }
    return base;
  }

  private async buildHeaders(): Promise<Record<string, string>> {
    const headers: Record<string, string> = {
      Accept: "text/markdown, text/plain, */*"
    };
    const token = await this.tokenProvider();
    if (token) headers["Authorization"] = `Bearer ${token}`;
    else if (this.config.functionKey) headers["x-functions-key"] = this.config.functionKey;
    return headers;
  }
}
