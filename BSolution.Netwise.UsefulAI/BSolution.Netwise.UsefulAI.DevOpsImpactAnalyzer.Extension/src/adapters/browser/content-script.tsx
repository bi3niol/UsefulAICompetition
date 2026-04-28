import * as React from "react";
import * as ReactDOM from "react-dom";

import "azure-devops-ui/Core/override.css";
import "../../styles/panel.scss";

import { ImpactPanel } from "../../core/components/ImpactPanel";
import { ImpactAnalysisClient } from "../../core/services/ImpactAnalysisClient";
import { BrowserWorkItemHost } from "./BrowserWorkItemHost";
import { getSettings, onSettingsChanged } from "./storage";

async function bootstrap() {
  const host = new BrowserWorkItemHost();
  await host.ready();

  let settings = await getSettings();
  let client = new ImpactAnalysisClient({
    functionUrl: settings.functionUrl,
    functionKey: settings.functionKey
  });

  const mount = host.getMountPoint();

  const handleClose = () => {
    ReactDOM.unmountComponentAtNode(mount);
    mount.remove();
  };

  const render = () => {
    ReactDOM.render(
      <ImpactPanel host={host} client={client} onClose={handleClose} />,
      mount
    );
  };

  render();

  // Re-create the client when the user updates settings in the options page.
  onSettingsChanged(updated => {
    settings = updated;
    client = new ImpactAnalysisClient({
      functionUrl: settings.functionUrl,
      functionKey: settings.functionKey
    });
    render();
  });
}

void bootstrap();
