import { useCallback, useEffect, useRef, useState } from "react";
import { ImpactAnalysisClient } from "../services/ImpactAnalysisClient";
import { AnalysisState } from "../types";

export interface UseImpactAnalysisResult extends AnalysisState {
  run: () => void;
}

export function useImpactAnalysis(
  client: ImpactAnalysisClient,
  workItemId: number | null
): UseImpactAnalysisResult {
  const [state, setState] = useState<AnalysisState>({ status: "idle" });
  const abortRef = useRef<AbortController | null>(null);

  const run = useCallback(() => {
    if (workItemId == null) return;

    abortRef.current?.abort();
    const ctrl = new AbortController();
    abortRef.current = ctrl;

    setState({ status: "loading" });
    client
      .analyze(workItemId, ctrl.signal)
      .then(result => {
        if (!ctrl.signal.aborted) setState({ status: "success", result });
      })
      .catch((e: Error) => {
        if (ctrl.signal.aborted) return;
        setState({ status: "error", error: e.message });
      });
  }, [client, workItemId]);

  // Auto-run when work item changes.
  useEffect(() => {
    if (workItemId != null) run();
    return () => abortRef.current?.abort();
  }, [workItemId, run]);

  return { ...state, run };
}
