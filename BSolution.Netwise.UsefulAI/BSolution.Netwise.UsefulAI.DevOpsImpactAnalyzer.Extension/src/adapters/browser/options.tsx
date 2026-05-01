import * as React from "react";
import * as ReactDOM from "react-dom";

import "azure-devops-ui/Core/override.css";
import { Header, TitleSize } from "azure-devops-ui/Header";
import { Card } from "azure-devops-ui/Card";
import { TextField } from "azure-devops-ui/TextField";
import { Button } from "azure-devops-ui/Button";
import { MessageCard, MessageCardSeverity } from "azure-devops-ui/MessageCard";
import { Surface, SurfaceBackground } from "azure-devops-ui/Surface";

import { ExtensionSettings, getSettings, saveSettings } from "./storage";

const OptionsPage: React.FC = () => {
  const [settings, setSettings] = React.useState<ExtensionSettings | null>(null);
  const [saved, setSaved] = React.useState(false);

  React.useEffect(() => {
    void getSettings().then(setSettings);
  }, []);

  if (!settings) return <div style={{ padding: 24 }}>Loading…</div>;

  const update = <K extends keyof ExtensionSettings>(key: K, value: ExtensionSettings[K]) => {
    setSettings({ ...settings, [key]: value });
    setSaved(false);
  };

  const onSave = async () => {
    await saveSettings(settings);
    setSaved(true);
  };

  return (
    <Surface background={SurfaceBackground.neutral}>
      <div style={{ maxWidth: 720, margin: "24px auto", padding: 16 }}>
        <Header title="DevOps Impact Analyzer" titleSize={TitleSize.Large} description="Extension settings" />

        <Card titleProps={{ text: "Backend (Azure Functions)" }}>
          <div style={{ display: "flex", flexDirection: "column", gap: 12, padding: 12 }}>
            <TextField
              label="Function App URL"
              value={settings.functionUrl}
              onChange={(_e, v) => update("functionUrl", v)}
              placeholder="https://your-func.azurewebsites.net"
            />
            <TextField
              label="Function Key (optional)"
              value={settings.functionKey ?? ""}
              onChange={(_e, v) => update("functionKey", v)}
              inputType="password"
            />
          </div>
        </Card>

        <div style={{ display: "flex", gap: 8, marginTop: 16 }}>
          <Button text="Save" primary onClick={onSave} />
          {saved && (
            <MessageCard severity={MessageCardSeverity.Info} onDismiss={() => setSaved(false)}>
              Saved.
            </MessageCard>
          )}
        </div>
      </div>
    </Surface>
  );
};

ReactDOM.render(<OptionsPage />, document.getElementById("options-root"));
