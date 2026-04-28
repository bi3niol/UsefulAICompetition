import { useEffect, useState } from "react";
import { IWorkItemHost } from "../ports/IWorkItemHost";
import { WorkItemContext } from "../types";

export interface UseWorkItemContextResult {
  workItem: WorkItemContext | null;
  loading: boolean;
  error?: string;
}

export function useWorkItemContext(host: IWorkItemHost): UseWorkItemContextResult {
  const [workItem, setWorkItem] = useState<WorkItemContext | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | undefined>();

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      setLoading(true);
      setError(undefined);
      try {
        const ctx = await host.getCurrentWorkItem();
        if (!cancelled) setWorkItem(ctx);
      } catch (e) {
        if (!cancelled) setError((e as Error).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    void load();
    const unsubscribe = host.onWorkItemChanged(ctx => {
      if (!cancelled) setWorkItem(ctx);
    });

    return () => {
      cancelled = true;
      unsubscribe();
    };
  }, [host]);

  return { workItem, loading, error };
}
