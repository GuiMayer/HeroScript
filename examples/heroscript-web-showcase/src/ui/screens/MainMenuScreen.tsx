import React, { useEffect, useState } from "react";
import { AppServices, ScreenId } from "../../bootstrap";
import { isPlainObject, JsonObject } from "../../contracts/types";
import { CircuitTrace } from "../components/ArtSlot";

export const MainMenuScreen: React.FC<{
  services: AppServices;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onNavigate }) => {
  const { session, preferences, i18n, audio } = services;
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    let active = true;
    if (!session.engineOnline && !session.busy) {
      setLoading(true);
      session.loadCatalog().finally(() => {
        if (active) setLoading(false);
      });
    }
    return () => {
      active = false;
    };
  }, [session]);

  const settings = session.settings;
  const currentSetting = session.selectedSetting;
  const launch = isPlainObject(currentSetting.launch) ? currentSetting.launch : {};
  const resumeId = preferences.resumeIndex.runFor(
    preferences.apiUrl,
    String(launch.playerEntityId ?? "player"),
    session.selectedSettingId,
    String(launch.modeId ?? ""),
    preferences.lastRunId
  );

  const handleStartNewJourney = async () => {
    audio.ui();
    if (session.selectedSettingAllowsLoadout() && session.engineOnline) {
      onNavigate("loadout");
      return;
    }
    setLoading(true);
    const ok = await session.startSelectedSetting();
    setLoading(false);
    if (ok) {
      if (session.hasActiveCombat()) onNavigate("combat");
      else if (session.hasActiveDialogue()) onNavigate("dialogue");
      else onNavigate("journey");
    }
  };

  const handleContinueJourney = async () => {
    audio.ui();
    if (!resumeId) return;
    setLoading(true);
    const ok = await session.resumeRun(resumeId);
    setLoading(false);
    if (ok) {
      if (session.hasActiveCombat()) onNavigate("combat");
      else if (session.hasActiveDialogue()) onNavigate("dialogue");
      else onNavigate("journey");
    }
  };

  const handleReconnect = async () => {
    audio.ui();
    setLoading(true);
    await session.loadCatalog();
    setLoading(false);
  };

  return (
    <div className="relative min-h-full w-full ivory-surface flex flex-col justify-between p-6 md:p-12 overflow-y-auto">
      <CircuitTrace
        mirrored={true}
        className="absolute top-6 right-8 w-[420px] h-[72px] hidden md:block"
      />

      {/* Top Editorial Masthead */}
      <div className="max-w-5xl w-full mx-auto space-y-6 z-10">
        <div className="flex flex-wrap items-center justify-between gap-4 p-5 rounded-lg bg-[#181715] border-2 border-[#c99e3a]/40 shadow-xl text-[#fbf6ec]">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-full bg-gradient-to-br from-[#c99e3a] to-[#8d6219] flex items-center justify-center shadow-md border border-[#edd282]/50 shrink-0">
              <span className="font-display font-extrabold text-[#181715] text-lg">HS</span>
            </div>
            <div>
              <div className="text-[10px] font-display font-bold uppercase tracking-[0.25em] text-[#edd282]">
                HEROSCRIPT // EMBER ARCHIVE
              </div>
              <h1 className="font-display text-2xl md:text-4xl font-bold m-0 mt-0.5 text-[#fbf6ec] tracking-wide">
                {i18n.text("IVORY EDITION SHOWCASE")}
              </h1>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <div
              className={`px-3 py-1.5 rounded-full text-xs font-display font-bold border tracking-wider flex items-center gap-2 ${
                session.engineOnline
                  ? "bg-[#1f6f54]/25 border-[#1f6f54] text-[#7ae0be]"
                  : "bg-[#a92e28]/25 border-[#a92e28] text-[#fca5a5]"
              }`}
            >
              <span
                className={`w-2 h-2 rounded-full ${
                  session.engineOnline ? "bg-[#28c792] animate-pulse" : "bg-[#ef4444]"
                }`}
              />
              {session.engineOnline
                ? i18n.text("ENGINE ONLINE")
                : i18n.text("ENGINE OFFLINE")}
            </div>
            <button
              type="button"
              onClick={handleReconnect}
              disabled={loading || session.busy}
              className="ivory-button px-3.5 py-1.5 text-xs rounded shadow-sm"
            >
              {loading ? i18n.text("CONNECTING…") : i18n.text("RECONNECT")}
            </button>
          </div>
        </div>

        {/* Main Two-Column Grid */}
        <div className="grid grid-cols-1 lg:grid-cols-[1.35fr_1fr] gap-8 items-start pt-2">
          {/* Left Column: Expedition & Setting Selection */}
          <div className="ivory-panel rounded-lg p-6 space-y-5 border-2 border-[#8d806b]/60 shadow-lg">
            <div className="border-b-2 border-[#8d806b]/40 pb-3">
              <div className="flex items-center justify-between">
                <span className="text-[11px] font-display font-bold uppercase tracking-widest text-[#9c2d17]">
                  {i18n.text("AUTHORITATIVE CAMPAIGN")}
                </span>
                <span className="text-[10px] font-mono px-2 py-0.5 rounded bg-[#1e1d19] text-[#edd282] font-bold">
                  TCG PROTOCOL
                </span>
              </div>
              <h2 className="font-display text-2xl font-bold m-0 mt-1 text-[#1e1d19]">
                {String(
                  currentSetting.name ??
                    i18n.text("Select a Published Setting")
                )}
              </h2>
              <p className="text-sm text-[#59544b] mt-1.5 mb-0 leading-relaxed font-body">
                {String(
                  currentSetting.description ??
                    i18n.text(
                      "Connect to the HeroScript REST engine to browse published worlds, combat rules, and narrative journeys."
                    )
                )}
              </p>
            </div>

            {settings.length > 0 && (
              <div className="space-y-1.5">
                <label className="block text-xs font-display font-bold uppercase tracking-wider text-[#59544b]">
                  {i18n.text("PUBLISHED SETTING")}
                </label>
                <select
                  value={session.selectedSettingId}
                  onChange={(e) => {
                    audio.ui();
                    session.selectSetting(e.target.value);
                  }}
                  className="w-full p-2.5 rounded border-2 border-[#1e1d19] bg-[#faf5eb] font-body text-sm font-semibold shadow-inner focus:outline-none focus:ring-2 focus:ring-[#c03d22]"
                >
                  {settings.map((st: JsonObject) => {
                    const id = String(st.settingId ?? "");
                    return (
                      <option key={id} value={id}>
                        {String(st.name ?? id)}
                      </option>
                    );
                  })}
                </select>
              </div>
            )}

            <div className="flex flex-wrap gap-3 pt-2">
              <button
                type="button"
                disabled={!session.engineOnline || loading || session.busy}
                onClick={handleStartNewJourney}
                className="ivory-button-primary px-6 py-3 text-sm rounded flex-1 min-w-[180px] shadow-md"
              >
                {i18n.text("NEW JOURNEY")}
              </button>

              {resumeId && (
                <button
                  type="button"
                  disabled={!session.engineOnline || loading || session.busy}
                  onClick={handleContinueJourney}
                  className="ivory-button px-5 py-3 text-sm rounded flex-1 min-w-[180px] shadow-sm"
                >
                  {i18n.text("CONTINUE JOURNEY")}
                </button>
              )}
            </div>

            {session.selectedSettingAllowsLoadout() && (
              <div className="pt-3 border-t-2 border-[#8d806b]/30 flex flex-wrap gap-2.5">
                <button
                  type="button"
                  disabled={!session.engineOnline}
                  onClick={() => {
                    audio.ui();
                    onNavigate("loadout");
                  }}
                  className="ivory-button px-3.5 py-1.5 text-xs rounded shadow-sm"
                >
                  {i18n.text("CONFIGURE LOADOUT")}
                </button>
                <button
                  type="button"
                  disabled={!session.engineOnline}
                  onClick={() => {
                    audio.ui();
                    onNavigate("compartments");
                  }}
                  className="ivory-button px-3.5 py-1.5 text-xs rounded shadow-sm"
                >
                  {i18n.text("DECK COMPARTMENTS")}
                </button>
              </div>
            )}
          </div>

          {/* Right Column: Laboratories, Archive & Tools */}
          <div className="space-y-4">
            <div className="ivory-panel rounded-lg p-5 space-y-3 border-2 border-[#8d806b]/60 shadow-lg">
              <div className="flex items-center justify-between border-b border-[#8d806b]/40 pb-2">
                <div className="text-xs font-display font-bold uppercase tracking-widest text-[#9c2d17]">
                  {i18n.text("OFFLINE VISUAL LABORATORIES")}
                </div>
                <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-[#ece2cf] text-[#59544b] font-bold">
                  LOCAL
                </span>
              </div>
              <p className="text-xs text-[#59544b] m-0 leading-relaxed font-body">
                {i18n.text(
                  "Inspect shared Ivory UI components, card typography, fan arcs, burn shaders, and inscription loadouts without an active engine."
                )}
              </p>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5 pt-1">
                <button
                  type="button"
                  onClick={() => {
                    audio.ui();
                    onNavigate("card_hand_lab");
                  }}
                  className="ivory-button p-3 text-xs rounded-lg text-left flex flex-col gap-1 hover:border-[#c03d22] transition-colors"
                >
                  <span className="font-bold text-[#9c2d17] font-display">
                    {i18n.text("CARD & HAND LAB")}
                  </span>
                  <span className="font-body text-[11px] text-[#59544b] font-normal">
                    {i18n.text("0–100 cards, fan geometry & burn cues")}
                  </span>
                </button>

                <button
                  type="button"
                  onClick={() => {
                    audio.ui();
                    onNavigate("loadout_lab");
                  }}
                  className="ivory-button p-3 text-xs rounded-lg text-left flex flex-col gap-1 hover:border-[#c03d22] transition-colors"
                >
                  <span className="font-bold text-[#9c2d17] font-display">
                    {i18n.text("INSCRIPTION LOADOUT LAB")}
                  </span>
                  <span className="font-body text-[11px] text-[#59544b] font-normal">
                    {i18n.text("3-column editorial slots & attribute tables")}
                  </span>
                </button>
              </div>
            </div>

            <div className="ivory-panel rounded-lg p-5 space-y-3 border-2 border-[#8d806b]/60 shadow-lg">
              <div className="text-xs font-display font-bold uppercase tracking-widest text-[#59544b] border-b border-[#8d806b]/40 pb-2">
                {i18n.text("ARCHIVE & DIAGNOSTICS")}
              </div>
              <div className="grid grid-cols-2 gap-2.5">
                <button
                  type="button"
                  onClick={() => {
                    audio.ui();
                    onNavigate("history");
                  }}
                  className="ivory-button px-3 py-2 text-xs rounded shadow-sm"
                >
                  {i18n.text("RUN HISTORY")}
                </button>
                <button
                  type="button"
                  onClick={() => {
                    audio.ui();
                    onNavigate("sandbox");
                  }}
                  className="ivory-button px-3 py-2 text-xs rounded shadow-sm"
                >
                  {i18n.text("RULES SANDBOX")}
                </button>
                <button
                  type="button"
                  onClick={() => {
                    audio.ui();
                    onNavigate("codex");
                  }}
                  className="ivory-button px-3 py-2 text-xs rounded shadow-sm"
                >
                  {i18n.text("CONTENT CODEX")}
                </button>
                <button
                  type="button"
                  onClick={() => {
                    audio.ui();
                    onNavigate("settings");
                  }}
                  className="ivory-button px-3 py-2 text-xs rounded shadow-sm"
                >
                  {i18n.text("SETTINGS")}
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Footer */}
      <footer className="max-w-5xl w-full mx-auto pt-8 border-t border-[#8d8270]/50 flex flex-wrap items-center justify-between text-xs text-[#5b574f]">
        <span>
          {i18n.text("Endpoint")}:{" "}
          <code className="font-mono text-[#20201c]">{preferences.apiUrl}</code>
        </span>
        <span>
          {i18n.text("Passive Projection Architecture · Zero Client Rule Math")}
        </span>
      </footer>
    </div>
  );
};
