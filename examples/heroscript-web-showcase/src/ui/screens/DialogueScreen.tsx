import React, { useEffect, useMemo, useRef, useState } from "react";
import { AppServices, ScreenId } from "../../bootstrap";
import { isPlainObject, JsonObject } from "../../contracts/types";
import { DialoguePresenter } from "../../presentation/dialoguePresenter";
import { ArtSlot } from "../components/ArtSlot";
import { DialogueController } from "../controllers/controllers";

export const DialogueScreen: React.FC<{
  services: AppServices;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onNavigate }) => {
  const { session, preferences, i18n, audio } = services;
  const [, setTick] = useState(0);
  const [revealedCount, setRevealedCount] = useState(99999);
  const [historyOpen, setHistoryOpen] = useState(false);

  const controllerRef = useRef<DialogueController | null>(null);
  if (!controllerRef.current) {
    controllerRef.current = new DialogueController(session);
  }
  const controller = controllerRef.current;

  useEffect(() => {
    const unsub = controller.changed.connect(() => setTick((t) => t + 1));
    return () => unsub();
  }, [controller]);

  const snapshot = session.captureViewSnapshot();
  const choices = session.activityChoices();
  const model = useMemo(
    () => DialoguePresenter.build(snapshot.run, choices, i18n),
    [snapshot.run, choices, i18n]
  );

  controller.bind(snapshot.run, choices, model);

  const fullText = String(model.text ?? "");

  useEffect(() => {
    if (!preferences.revealTextGradually || preferences.reducedMotion) {
      setRevealedCount(fullText.length);
      return;
    }
    setRevealedCount(0);
    const interval = setInterval(() => {
      setRevealedCount((c) => {
        if (c >= fullText.length) {
          clearInterval(interval);
          return fullText.length;
        }
        return c + 3;
      });
    }, 18);
    return () => clearInterval(interval);
  }, [
    fullText,
    model.nodeId,
    preferences.revealTextGradually,
    preferences.reducedMotion,
  ]);

  const options = Array.isArray(model.options)
    ? (model.options as JsonObject[])
    : [];
  const history = Array.isArray(model.history)
    ? (model.history as JsonObject[])
    : [];

  const handleChooseOption = async (option: JsonObject) => {
    audio.ui();
    const ref = String(option.reference ?? "");
    const ok = await controller.submit(ref);
    if (ok) {
      if (session.hasActiveCombat()) onNavigate("combat");
      else if (!session.hasActiveDialogue()) onNavigate("journey");
    }
  };

  const resolveCmd = controller.command("RESOLVE_NODE");

  return (
    <div className="flex flex-col h-full w-full ivory-surface overflow-hidden">
      {/* Header */}
      <header className="flex items-center justify-between px-6 py-3 bg-gradient-to-r from-[#1c1b17] via-[#28251e] to-[#1c1b17] text-[#faf5eb] border-b-2 border-[#c99e3a] shadow-md">
        <div>
          <span className="text-[10px] font-display font-bold uppercase tracking-widest text-[#edd282]">
            {i18n.text("NARRATIVE ENCOUNTER")}
          </span>
          <h2 className="font-display text-lg font-bold m-0 text-[#faf5eb]">
            {String(model.title || i18n.text("DIALOGUE"))}
          </h2>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => setHistoryOpen(true)}
            className="ivory-button px-3 py-1 text-xs rounded"
          >
            {i18n.text("CONVERSATION HISTORY")} ({history.length})
          </button>
          <button
            type="button"
            onClick={() => onNavigate("journey")}
            className="ivory-button px-3 py-1 text-xs rounded"
          >
            {i18n.text("JOURNEY")}
          </button>
        </div>
      </header>

      {/* Main Dialogue Stage */}
      <div className="flex-1 overflow-y-auto p-6 md:p-10 flex items-center justify-center">
        <div className="max-w-4xl w-full ivory-panel rounded-lg p-6 md:p-8 space-y-6 border-2 border-[#c99e3a]/70">
          {/* Speaker & Portrait */}
          <div className="flex flex-col sm:flex-row items-start gap-6">
            <div className="p-1 rounded-lg bg-gradient-to-b from-[#c99e3a] to-[#7c5f1f] shadow-md shrink-0">
              <ArtSlot
                category="actors"
                contentId={String(
                  model.portrait || model.speakerId || "speaker"
                )}
                fallbackLabel={String(model.speaker ?? "NPC")}
                aspectRatio="1/1"
                className="w-24 h-24 rounded-md border-0"
              />
            </div>
            <div className="space-y-2.5 flex-1">
              <div className="inline-flex items-center gap-2 px-3 py-0.5 rounded-full bg-[#1e1d19] text-[#edd282] font-display text-xs font-bold uppercase tracking-widest">
                {String(model.speaker || i18n.text("Speaker"))}
              </div>
              <div
                onClick={() => setRevealedCount(fullText.length)}
                className="text-base md:text-lg text-[#1e1d19] leading-relaxed whitespace-pre-line min-h-[96px] cursor-pointer p-4 rounded-md bg-[#f4ead7]/80 border border-[#8d8270]/40 shadow-inner"
              >
                {fullText.slice(0, revealedCount)}
              </div>
            </div>
          </div>

          {/* Choices */}
          <div className="border-t border-[#8d8270]/50 pt-5 space-y-2.5">
            {options.map((opt, idx) => {
              const available = Boolean(opt.available);
              const cost = String(opt.cost ?? "");
              const reason = String(opt.reason ?? "");
              return (
                <button
                  key={String(opt.id ?? idx)}
                  type="button"
                  disabled={!available || controller.locked()}
                  onClick={() => handleChooseOption(opt)}
                  className="w-full text-left ivory-button p-3.5 rounded-lg flex items-center justify-between gap-4"
                >
                  <div className="space-y-0.5">
                    <div className="font-body font-bold text-sm text-[#1e1d19]">
                      › {String(opt.text ?? "")}
                    </div>
                    {!available && reason && (
                      <div className="text-xs text-[#a92e28] font-normal">
                        {reason}
                      </div>
                    )}
                  </div>
                  {cost && (
                    <span className="px-2.5 py-0.5 rounded-full bg-[#1e1d19] text-[#edd282] font-mono text-xs font-bold shrink-0">
                      {cost}
                    </span>
                  )}
                </button>
              );
            })}

            {options.length === 0 && Object.keys(resolveCmd).length > 0 && (
              <button
                type="button"
                disabled={controller.locked()}
                onClick={async () => {
                  const ok = await controller.executePublished("RESOLVE_NODE");
                  if (ok) onNavigate("journey");
                }}
                className="ivory-button-primary px-6 py-2.5 text-xs rounded-md"
              >
                {i18n.text("COMPLETE STAGE")}
              </button>
            )}
          </div>
        </div>
      </div>

      {/* Transcript History Modal */}
      {historyOpen && (
        <div
          className="fixed inset-0 z-50 bg-[#05060b]/80 backdrop-blur-xs flex items-center justify-center p-6"
          onClick={() => setHistoryOpen(false)}
        >
          <div
            role="dialog"
            aria-modal="true"
            className="ivory-panel max-w-2xl w-full max-h-[82vh] rounded-lg flex flex-col overflow-hidden border-2 border-[#c99e3a]"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="px-5 py-3 bg-gradient-to-r from-[#24221d] to-[#322e26] text-[#faf5eb] border-b border-[#c99e3a]/50 flex items-center justify-between">
              <h3 className="font-display text-base font-bold m-0 text-[#edd282]">
                {i18n.text("CONVERSATION HISTORY")}
              </h3>
              <button
                type="button"
                onClick={() => setHistoryOpen(false)}
                className="ivory-button px-2.5 py-1 text-xs rounded"
              >
                {i18n.text("Close")}
              </button>
            </div>
            <div className="p-5 overflow-y-auto space-y-3 text-sm bg-[#f4ebd9]">
              {history.length === 0 ? (
                <div className="text-[#59544b] italic">
                  {i18n.text("No prior transcript lines recorded.")}
                </div>
              ) : (
                history.map((entry, idx) => {
                  if (!isPlainObject(entry)) return null;
                  return (
                    <div
                      key={idx}
                      className="border-b border-[#8d8270]/30 pb-2"
                    >
                      <div className="font-display text-xs font-bold text-[#c03d22]">
                        {String(entry.speaker ?? "")}
                      </div>
                      <div className="text-[#1e1d19] whitespace-pre-line">
                        {String(entry.text ?? "")}
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
