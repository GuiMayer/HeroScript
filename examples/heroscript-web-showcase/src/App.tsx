import React, { useEffect, useState } from "react";
import { ScreenId, useAppServices } from "./bootstrap";
import { ActivityScreen } from "./ui/screens/ActivityScreen";
import {
  CodexScreen,
  RunHistoryScreen,
  RunReplayScreen,
  SandboxScreen,
  SettingsScreen,
  TimelineScreen,
} from "./ui/screens/ArchiveAndSettingsScreens";
import { CombatScreen } from "./ui/screens/CombatScreen";
import { DeckCompartmentScreen } from "./ui/screens/DeckCompartmentScreen";
import { DialogueScreen } from "./ui/screens/DialogueScreen";
import {
  CardHandLabScreen,
  InscriptionLoadoutScreen,
} from "./ui/screens/LabAndLoadoutScreens";
import { MainMenuScreen } from "./ui/screens/MainMenuScreen";
import { MapScreen } from "./ui/screens/MapScreen";

export const App: React.FC<{ services?: import("./bootstrap").AppServices }> = ({
  services: propServices,
}) => {
  const defaultHook = useAppServices();
  const services = propServices ?? defaultHook.services;
  const { session, preferences, i18n } = services;

  const [screen, setScreen] = useState<ScreenId>("menu");
  const [replayRunId, setReplayRunId] = useState<string>("");
  const [toastError, setToastError] = useState<string>("");
  const [toastNotice, setToastNotice] = useState<string>("");

  useEffect(() => {
    document.documentElement.style.setProperty(
      "--text-scale",
      String(preferences.textScale)
    );
    if (preferences.highContrast) {
      document.documentElement.classList.add("high-contrast");
    } else {
      document.documentElement.classList.remove("high-contrast");
    }
  }, [preferences.textScale, preferences.highContrast]);

  useEffect(() => {
    const unsubErr = session.errorReported.connect((msg) => {
      setToastError(msg);
      setTimeout(() => setToastError(""), 5000);
    });
    const unsubNotice = session.noticeReported.connect((msg) => {
      setToastNotice(msg);
      setTimeout(() => setToastNotice(""), 3500);
    });
    return () => {
      unsubErr();
      unsubNotice();
    };
  }, [session]);

  const renderScreen = () => {
    switch (screen) {
      case "menu":
        return <MainMenuScreen services={services} onNavigate={setScreen} />;
      case "combat":
        return <CombatScreen services={services} onNavigate={setScreen} />;
      case "journey":
        return <ActivityScreen services={services} onNavigate={setScreen} />;
      case "map":
        return <MapScreen services={services} onNavigate={setScreen} />;
      case "dialogue":
        return <DialogueScreen services={services} onNavigate={setScreen} />;
      case "loadout":
        return (
          <InscriptionLoadoutScreen
            services={services}
            onlineMode={true}
            onNavigate={setScreen}
          />
        );
      case "loadout_lab":
        return (
          <InscriptionLoadoutScreen
            services={services}
            onlineMode={false}
            onNavigate={setScreen}
          />
        );
      case "compartments":
        return (
          <DeckCompartmentScreen services={services} onNavigate={setScreen} />
        );
      case "card_hand_lab":
        return <CardHandLabScreen services={services} onNavigate={setScreen} />;
      case "timeline":
        return <TimelineScreen services={services} onNavigate={setScreen} />;
      case "history":
        return (
          <RunHistoryScreen
            services={services}
            onSelectReplay={(id) => {
              setReplayRunId(id);
              setScreen("replay");
            }}
            onNavigate={setScreen}
          />
        );
      case "replay":
        return (
          <RunReplayScreen
            services={services}
            runId={replayRunId}
            onNavigate={setScreen}
          />
        );
      case "sandbox":
        return <SandboxScreen services={services} onNavigate={setScreen} />;
      case "codex":
        return <CodexScreen services={services} onNavigate={setScreen} />;
      case "settings":
        return <SettingsScreen services={services} onNavigate={setScreen} />;
      default:
        return <MainMenuScreen services={services} onNavigate={setScreen} />;
    }
  };

  return (
    <div className="relative w-full h-full flex flex-col overflow-hidden">
      {/* Toast Banners */}
      {toastError && (
        <div
          role="alert"
          aria-live="assertive"
          className="fixed top-4 right-4 z-50 max-w-md px-4 py-3 rounded-lg shadow-2xl bg-[#181715] border-2 border-[#a92e28] text-[#faf5eb] text-xs font-semibold flex items-center justify-between gap-3"
        >
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-[#ef4444] animate-ping shrink-0" />
            <span>{toastError}</span>
          </div>
          <button
            type="button"
            onClick={() => setToastError("")}
            className="font-bold underline text-[#edd282] hover:text-[#fff] shrink-0"
          >
            {i18n.text("Dismiss")}
          </button>
        </div>
      )}
      {toastNotice && (
        <div
          role="status"
          aria-live="polite"
          className="fixed bottom-4 right-4 z-50 max-w-md px-4 py-2.5 rounded-lg shadow-2xl bg-[#181715] border-2 border-[#c99e3a]/60 text-[#fbf6ec] text-xs font-semibold flex items-center gap-2"
        >
          <span className="w-2 h-2 rounded-full bg-[#c99e3a] shrink-0" />
          <span>{toastNotice}</span>
        </div>
      )}

      {renderScreen()}
    </div>
  );
};
