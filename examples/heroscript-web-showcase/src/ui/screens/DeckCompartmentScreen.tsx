import React, { useEffect, useState } from "react";
import { AppServices, ScreenId } from "../../bootstrap";
import { isPlainObject, JsonObject } from "../../contracts/types";

export const DeckCompartmentScreen: React.FC<{
  services: AppServices;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onNavigate }) => {
  const { deckCompartmentWorkspace, session, i18n, audio } = services;
  const [, setTick] = useState(0);

  const launch = isPlainObject(session.selectedSetting.launch)
    ? session.selectedSetting.launch
    : {};
  const modeId = String(launch.modeId ?? "default");
  const playerId = String(launch.playerEntityId ?? "player");

  useEffect(() => {
    if (session.engineOnline) {
      deckCompartmentWorkspace
        .loadMode(modeId, playerId, Number(launch.seed ?? 20260911))
        .then(() => setTick((t) => t + 1));
    }
  }, [session.engineOnline, deckCompartmentWorkspace, modeId, playerId, launch.seed]);

  const snap = deckCompartmentWorkspace.snapshot();
  const compartments = Array.isArray(snap.compartments)
    ? (snap.compartments as JsonObject[])
    : [];
  const cards = Array.isArray(snap.cards) ? (snap.cards as JsonObject[]) : [];
  const allocations = isPlainObject(snap.allocations)
    ? (snap.allocations as Record<string, Record<string, number>>)
    : {};

  return (
    <div className="flex flex-col h-full w-full ivory-surface overflow-hidden">
      <header className="flex flex-wrap items-center justify-between gap-3 px-6 py-3.5 bg-[#181715] border-b-2 border-[#c99e3a]/40 text-[#fbf6ec] shadow-md z-10">
        <div className="flex items-center gap-3">
          <div className="w-8 h-8 rounded-full bg-gradient-to-br from-[#c99e3a] to-[#8d6219] flex items-center justify-center shadow border border-[#edd282]/40 text-[#181715] font-display font-bold text-xs shrink-0">
            DC
          </div>
          <div>
            <span className="text-[10px] font-display font-bold uppercase tracking-widest text-[#edd282]">
              DECK COMPARTMENT ALLOCATOR
            </span>
            <h2 className="font-display text-lg font-bold m-0 text-[#fbf6ec] tracking-wide">
              {i18n.text("DECK COMPARTMENTS")}
            </h2>
          </div>
        </div>

        <div className="flex items-center gap-2.5">
          <button
            type="button"
            disabled={!deckCompartmentWorkspace.configured || deckCompartmentWorkspace.busy}
            onClick={async () => {
              audio.ui();
              await deckCompartmentWorkspace.preview();
              setTick((t) => t + 1);
            }}
            className="ivory-button px-3.5 py-1.5 text-xs rounded shadow-sm"
          >
            {i18n.text("VALIDATE ALLOCATION")}
          </button>
          <button
            type="button"
            disabled={!deckCompartmentWorkspace.configured || deckCompartmentWorkspace.busy}
            onClick={async () => {
              audio.reward();
              const res = await deckCompartmentWorkspace.startRun(
                Number(launch.seed ?? 20260911)
              );
              if (res.ok && isPlainObject(res.data)) {
                const runId = String(res.data.runId ?? "");
                if (runId) {
                  await session.resumeRun(runId);
                  onNavigate(session.hasActiveCombat() ? "combat" : "journey");
                }
              }
            }}
            className="ivory-button-primary px-4 py-1.5 text-xs rounded shadow-md"
          >
            {i18n.text("START WITH COMPARTMENTS")}
          </button>
          <button
            type="button"
            onClick={() => onNavigate("menu")}
            className="ivory-button px-3.5 py-1.5 text-xs rounded shadow-sm"
          >
            {i18n.text("MAIN MENU")}
          </button>
        </div>
      </header>

      <div className="flex-1 overflow-y-auto p-6 max-w-5xl w-full mx-auto space-y-4">
        {!deckCompartmentWorkspace.configured ? (
          <div className="ivory-panel rounded-lg p-8 text-center text-sm text-[#59544b] border-2 border-[#8d806b]/50 shadow-md">
            {deckCompartmentWorkspace.error ||
              i18n.text("No deck compartment schema published for the current setting.")}
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            {compartments.map((comp) => {
              const compId = String(comp.compartmentId ?? "");
              const compAlloc = allocations[compId] ?? {};
              return (
                <div
                  key={compId}
                  className="ivory-panel rounded-lg p-5 space-y-3.5 border-2 border-[#8d806b]/60 shadow-md"
                >
                  <div className="flex items-center justify-between border-b-2 border-[#8d806b]/30 pb-2.5">
                    <h3 className="font-display text-sm font-bold m-0 text-[#1e1d19] uppercase tracking-wide">
                      {String(comp.name ?? compId)}
                    </h3>
                    <span className="text-[10px] font-mono px-2 py-0.5 rounded bg-[#1e1d19] text-[#edd282] font-bold">
                      COMPARTMENT
                    </span>
                  </div>
                  <div className="space-y-2">
                    {cards.map((card) => {
                      const defId = String(card.cardDefinitionId ?? "");
                      const count = Number(compAlloc[defId] ?? 0);
                      return (
                        <div
                          key={defId}
                          className="flex items-center justify-between text-xs py-1.5 px-2 rounded bg-[#faf5eb] border border-[#8d806b]/30"
                        >
                          <span className="font-semibold text-[#1e1d19]">
                            {i18n.contentName(defId)}
                          </span>
                          <div className="flex items-center gap-2">
                            <button
                              type="button"
                              onClick={() => {
                                deckCompartmentWorkspace.adjust(
                                  compId,
                                  defId,
                                  -1
                                );
                                setTick((t) => t + 1);
                              }}
                              className="ivory-button w-6 h-6 p-0 flex items-center justify-center rounded font-bold text-xs"
                            >
                              -
                            </button>
                            <span className="font-mono font-bold w-6 text-center text-sm text-[#1e1d19]">
                              {count}
                            </span>
                            <button
                              type="button"
                              onClick={() => {
                                deckCompartmentWorkspace.adjust(
                                  compId,
                                  defId,
                                  1
                                );
                                setTick((t) => t + 1);
                              }}
                              className="ivory-button w-6 h-6 p-0 flex items-center justify-center rounded font-bold text-xs"
                            >
                              +
                            </button>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
};
