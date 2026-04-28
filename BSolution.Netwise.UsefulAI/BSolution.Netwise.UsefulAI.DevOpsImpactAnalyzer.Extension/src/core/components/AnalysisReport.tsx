import * as React from "react";
import { renderMarkdown } from "../markdown/renderMarkdown";

export const AnalysisReport: React.FC<{ markdown: string }> = ({ markdown }) => {
  const html = React.useMemo(() => renderMarkdown(markdown), [markdown]);
  return (
    <div
      className="impact-analysis-report"
      style={{ padding: "8px 12px", lineHeight: 1.5, fontSize: 13 }}
      dangerouslySetInnerHTML={{ __html: html }}
    />
  );
};
