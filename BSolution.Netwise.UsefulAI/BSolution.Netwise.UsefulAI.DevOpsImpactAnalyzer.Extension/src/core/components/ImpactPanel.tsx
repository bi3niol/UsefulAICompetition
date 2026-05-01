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

  const isBusy = analysis.status === "checking" || analysis.status === "generating";
  const hasReport = analysis.status === "ready" && !!analysis.result;
  const generateLabel = hasReport ? "Re-generate" : "Generate";

  return (
    <Surface background={SurfaceBackground.neutral}>
      <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
        <Header
          title="Impact Analysis"
          titleSize={TitleSize.Medium}
          description={
            workItem
              ? `Work Item #${workItem.id}`
              : "No work item in context"
          }
        />
        <div style={{ padding: "4px 16px 0", display: "flex", justifyContent: "flex-end", gap: 8 }}>
          <Button
            text={generateLabel}
            iconProps={{ iconName: hasReport ? "Refresh" : "Play" }}
            disabled={workItem == null || isBusy}
            primary={!hasReport}
            onClick={() => analysis.generate()}
          />
          {onClose && (
            <Button
              text="Close"
              iconProps={{ iconName: "Cancel" }}
              onClick={() => onClose()}
            />
          )}
        </div>

        <div style={{ flex: 1, overflow: "auto" }}>
          {ctxLoading && <LoadingState label="Reading work item context…" />}

          {!ctxLoading && ctxError && <ErrorState message={ctxError} />}

          {!ctxLoading && !ctxError && !workItem && (
            <div style={{ padding: 12 }}>
              <MessageCard severity={MessageCardSeverity.Info}>
                Open an Azure DevOps work item to see its impact analysis.
              </MessageCard>
            </div>
          )}

          {!ctxLoading && workItem && (
            <Card className="impact-analysis-card" contentProps={{ contentPadding: false }}>
              {analysis.status === "checking" && (
                <LoadingState label="Looking for an existing report…" />
              )}

              {analysis.status === "generating" && (
                <LoadingState label="Generating report — this may take a minute…" />
              )}

              {analysis.status === "error" && (
                <ErrorState
                  message={analysis.error ?? "Unknown error"}
                  onRetry={analysis.generate}
                />
              )}

              {analysis.status === "missing" && (
                <div style={{ padding: 16, display: "flex", flexDirection: "column", gap: 12 }}>
                  <MessageCard severity={MessageCardSeverity.Info}>
                    No impact analysis report has been generated for this work item yet.
                  </MessageCard>
                  <div>
                    <Button text="Generate" primary onClick={() => analysis.generate()} />
                  </div>
                </div>
              )}

              {analysis.status === "ready" && analysis.result && (
                <AnalysisReport markdown={analysis.result.markdown} />
              )}
            </Card>
          )}
        </div>
      </div>
    </Surface>
  );
};
