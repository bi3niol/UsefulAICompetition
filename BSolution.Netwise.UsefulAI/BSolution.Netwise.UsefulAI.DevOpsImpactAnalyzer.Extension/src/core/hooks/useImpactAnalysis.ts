import { useCallback, useEffect, useRef, useState } from "react";
import { ImpactAnalysisClient } from "../services/ImpactAnalysisClient";
import { AnalysisState } from "../types";

export interface UseImpactAnalysisResult extends AnalysisState {
  /** Runs the multi-agent pipeline (POST .../generate). Used for both first-time generation and re-generation. */
  generate: () => void;
}

export function useImpactAnalysis(
  client: ImpactAnalysisClient,
  workItemId: number | null
): UseImpactAnalysisResult {
  const [state, setState] = useState<AnalysisState>({ status: "checking" });
  const abortRef = useRef<AbortController | null>(null);

  const generate = useCallback(() => {
    if (workItemId == null) return;

    abortRef.current?.abort();
    const ctrl = new AbortController();
    abortRef.current = ctrl;

    setState({ status: "generating" });
    client
      .generateReport(workItemId, ctrl.signal)
      .then((result) => {
        if (!ctrl.signal.aborted) setState({ status: "ready", result });
      })
      .catch((e: Error) => {
        if (ctrl.signal.aborted) return;
        setState({ status: "error", error: e.message });
      });
  }, [client, workItemId]);

  // When the work item changes, look up an existing report — never auto-generate.
  useEffect(() => {
    if (workItemId == null) return;

    abortRef.current?.abort();
    const ctrl = new AbortController();
    abortRef.current = ctrl;

    setState({ status: "checking" });
    client
      .getReport(workItemId, ctrl.signal)
      .then((result) => {
        if (ctrl.signal.aborted) return;
        if (result) setState({ status: "ready", result });
        else setState({ status: "missing" });
      })
      .catch((e: Error) => {
        if (ctrl.signal.aborted) return;
        setState({ status: "error", error: e.message });
      });

    return () => ctrl.abort();
  }, [client, workItemId]);

  return { ...state, generate };
}
