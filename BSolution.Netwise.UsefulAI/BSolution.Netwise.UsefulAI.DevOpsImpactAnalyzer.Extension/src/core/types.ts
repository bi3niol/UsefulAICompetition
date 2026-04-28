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
  /** Full markdown report returned by AnalyzeWorkItemFunction. */
  markdown: string;
}

export type AnalysisStatus = "idle" | "loading" | "success" | "error";

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
