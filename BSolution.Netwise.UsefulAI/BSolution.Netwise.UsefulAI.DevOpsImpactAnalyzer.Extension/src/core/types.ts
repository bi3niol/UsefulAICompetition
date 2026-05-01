export interface WorkItemContext {
  id: number;
  title?: string;
  type?: string;
  description?: string;
  acceptanceCriteria?: string;
  areaPath?: string;
  tags?: string;
  url?: string;
}

export interface AnalysisResult {
  /** Full markdown report returned by the backend. */
  markdown: string;
}

/**
 *  - checking   — GET to backend in flight (looking for an existing report)
 *  - missing    — backend returned 404 (no report yet, waiting for user action)
 *  - generating — POST .../generate in flight (multi-agent pipeline running)
 *  - ready      — report available (either fetched or freshly generated)
 *  - error      — last operation failed
 */
export type AnalysisStatus =
  | "checking"
  | "missing"
  | "generating"
  | "ready"
  | "error";

export interface AnalysisState {
  status: AnalysisStatus;
  result?: AnalysisResult;
  error?: string;
}

export interface BackendConfig {
  /** e.g. https://your-func.azurewebsites.net */
  functionUrl: string;
  /** Optional Azure Functions key. Sent as x-functions-key header. */
  functionKey?: string;
}
