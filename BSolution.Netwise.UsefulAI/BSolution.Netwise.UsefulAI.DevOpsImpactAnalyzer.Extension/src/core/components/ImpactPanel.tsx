import * as React from "react";
import { Header, TitleSize } from "azure-devops-ui/Header";
import { Card } from "azure-devops-ui/Card";
import { Button } from "azure-devops-ui/Button";
import { Surface, SurfaceBackground } from "azure-devops-ui/Surface";
import { MessageCard, MessageCardSeverity } from "azure-devops-ui/MessageCard";

import { IWorkItemHost } from "../ports/IWorkItemHost";
import { ImpactAnalysisClient } from "../services/ImpactAnalysisClient";
import { useWorkItemContext } from "../hooks/useWorkItemContext";
import { useImpactAnalysis } from "../hooks/useImpactAnalysis";
import { LoadingState } from "./LoadingState";
import { ErrorState } from "./ErrorState";
import { AnalysisReport } from "./AnalysisReport";

export interface ImpactPanelProps {
  host: IWorkItemHost;
  client: ImpactAnalysisClient;
  onClose?: () => void;
}

export const ImpactPanel: React.FC<ImpactPanelProps> = ({ host, client, onClose }) => {
  const { workItem, loading: ctxLoading, error: ctxError } = useWorkItemContext(host);
  const analysis = useImpactAnalysis(client, workItem?.id ?? null);

  return (
    <Surface background={SurfaceBackground.neutral}>
      <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
        <Header
          title="Impact Analysis"
          titleSize={TitleSize.Medium}
          description={
            workItem
              ? `#${workItem.id}${workItem.title ? " · " + workItem.title : ""}`
              : "No work item in context"
          }
          commandBarItems={[
            {
              id: "rerun",
              text: "Re-run",
              iconProps: { iconName: "Refresh" },
              disabled: workItem == null || analysis.status === "loading",
              onActivate: () => analysis.run()
            },
            ...(onClose
              ? [
                  {
                    id: "close",
                    text: "Close",
                    iconProps: { iconName: "Cancel" },
                    onActivate: () => onClose()
                  }
                ]
              : [])
          ]}
        />

        <div style={{ flex: 1, overflow: "auto" }}>
          {ctxLoading && <LoadingState label="Reading work item context…" />}

          {!ctxLoading && ctxError && (
            <ErrorState message={ctxError} />
          )}

          {!ctxLoading && !ctxError && !workItem && (
            <div style={{ padding: 12 }}>
              <MessageCard severity={MessageCardSeverity.Info}>
                Open an Azure DevOps work item to see its impact analysis.
              </MessageCard>
            </div>
          )}

          {!ctxLoading && workItem && (
            <Card className="impact-analysis-card" contentProps={{ contentPadding: false }}>
              {analysis.status === "loading" && <LoadingState />}
              {analysis.status === "error" && (
                <ErrorState message={analysis.error ?? "Unknown error"} onRetry={analysis.run} />
              )}
              {analysis.status === "success" && analysis.result && (
                <AnalysisReport markdown={analysis.result.markdown} />
              )}
              {analysis.status === "idle" && (
                <div style={{ padding: 16 }}>
                  <Button text="Analyze" primary onClick={() => analysis.run()} />
                </div>
              )}
            </Card>
          )}
        </div>
      </div>
    </Surface>
  );
};
