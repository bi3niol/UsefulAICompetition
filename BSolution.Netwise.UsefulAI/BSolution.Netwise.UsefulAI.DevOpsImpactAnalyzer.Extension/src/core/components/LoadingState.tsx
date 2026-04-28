import * as React from "react";
import { Spinner, SpinnerSize } from "azure-devops-ui/Spinner";

export const LoadingState: React.FC<{ label?: string }> = ({ label }) => (
  <div style={{ padding: 24, display: "flex", justifyContent: "center" }}>
    <Spinner size={SpinnerSize.large} label={label ?? "Analyzing work item…"} />
  </div>
);
