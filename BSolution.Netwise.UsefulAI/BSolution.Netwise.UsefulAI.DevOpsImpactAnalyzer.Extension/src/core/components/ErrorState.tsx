import * as React from "react";
import { MessageCard, MessageCardSeverity } from "azure-devops-ui/MessageCard";

export const ErrorState: React.FC<{ message: string; onRetry?: () => void }> = ({
  message,
  onRetry
}) => (
  <div style={{ padding: 12 }}>
    <MessageCard
      severity={MessageCardSeverity.Error}
      onDismiss={onRetry}
      buttonProps={onRetry ? [{ text: "Retry", onClick: onRetry }] : undefined}
    >
      {message}
    </MessageCard>
  </div>
);
