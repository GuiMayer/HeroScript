import React from "react";
import { isPlainObject, JsonObject } from "../../contracts/types";
import { I18n } from "../../presentation/i18n";
import { ArtSlot } from "./ArtSlot";
import { CardView } from "./CardView";

export const ResourceView: React.FC<{
  pool: JsonObject;
  translator: I18n;
  compact?: boolean;
}> = ({ pool, translator, compact = false }) => {
  const resourceId = String(pool.resourceId ?? "");
  const name = translator.contentName(resourceId);
  const current = Number(pool.current ?? 0);
  const min = Number(pool.minimum ?? 0);
  const max = Number(pool.maximum ?? 0);
  const hasBounds = "minimum" in pool && "maximum" in pool && max > min;
  const ratio = hasBounds
    ? Math.min(1, Math.max(0, (current - min) / (max - min)))
    : 1;

  const lowerId = resourceId.toLowerCase();
  const isHealth = lowerId.includes("health") || lowerId.includes("hp");
  const isShield =
    lowerId.includes("shield") ||
    lowerId.includes("guard") ||
    lowerId.includes("block");
  const barGradient = isHealth
    ? "linear-gradient(90deg, #8c201b 0%, #c93b32 100%)"
    : isShield
    ? "linear-gradient(90deg, #1b5c46 0%, #2d8f6e 100%)"
    : "linear-gradient(90deg, #9c3119 0%, #d45335 100%)";

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center justify-between text-xs font-semibold">
        <span className="text-[#59544b] truncate flex items-center gap-1">
          <span
            className="inline-block w-2 h-2 rounded-full shrink-0"
            style={{
              background: isHealth
                ? "#c93b32"
                : isShield
                ? "#2d8f6e"
                : "#c99e3a",
            }}
          />
          {name}
        </span>
        <span className="font-mono font-bold text-[#1e1d19]">
          {translator.number(current)}
          {hasBounds ? ` / ${translator.number(max)}` : ""}
        </span>
      </div>
      {hasBounds && !compact && (
        <div className="w-full h-2.5 rounded-full bg-[#ded2bc] border border-[#8d806b]/70 overflow-hidden shadow-inner">
          <div
            className="h-full rounded-full transition-all duration-200"
            style={{
              width: `${Math.round(ratio * 100)}%`,
              background: barGradient,
            }}
          />
        </div>
      )}
    </div>
  );
};

export const ActorPanel: React.FC<{
  item: JsonObject;
  selectedTarget?: boolean;
  validTarget?: boolean;
  floatingTexts?: { id: string; text: string; tone: string }[];
  translator: I18n;
  onSelectTarget?: (actorId: string) => void;
}> = ({
  item,
  selectedTarget = false,
  validTarget = false,
  floatingTexts = [],
  translator,
  onSelectTarget,
}) => {
  const actor = isPlainObject(item.actor) ? item.actor : item;
  const instanceId = String(actor.instanceId ?? "");
  const definitionId = String(actor.definitionId ?? instanceId);
  const name = translator.contentName(
    definitionId,
    String(actor.name ?? instanceId)
  );
  const relationship = String(
    item.relationship ?? actor.relationship ?? "Neutral"
  );
  const isInputActor = Boolean(actor.isInputActor);
  const intent = String(item.intent ?? "");
  const resources = Array.isArray(actor.resources)
    ? (actor.resources as JsonObject[])
    : [];
  const statuses = Array.isArray(actor.statuses)
    ? (actor.statuses as JsonObject[])
    : [];
  const modifiers = Array.isArray(actor.modifiers)
    ? (actor.modifiers as JsonObject[])
    : [];

  const isHostile = relationship === "Hostile";
  const accentColor = isInputActor
    ? "#1f6f54"
    : isHostile
    ? "#a92e28"
    : "#5f4b8b";

  return (
    <div
      onClick={() => {
        if (validTarget && onSelectTarget) {
          onSelectTarget(instanceId);
        }
      }}
      className={`relative ivory-panel rounded-lg p-3.5 flex flex-col gap-2.5 transition-all duration-150 ${
        validTarget
          ? "cursor-pointer target-reticle-glow hover:-translate-y-0.5"
          : ""
      } ${
        selectedTarget
          ? "ring-4 ring-[#c03d22] bg-[#f4ead7]"
          : ""
      }`}
      style={{
        borderTop: `4px solid ${accentColor}`,
      }}
    >
      {/* Floating combat feedback */}
      {floatingTexts.length > 0 && (
        <div className="absolute -top-3 right-3 z-20 flex flex-col items-end gap-1 pointer-events-none">
          {floatingTexts.map((ft) => (
            <span
              key={ft.id}
              className="px-2.5 py-0.5 rounded-full text-xs font-bold shadow-md bg-[#1e1d19] text-[#edd282] border border-[#c99e3a] animate-bounce"
            >
              {ft.text}
            </span>
          ))}
        </div>
      )}

      {/* Actor Header */}
      <div className="flex items-center gap-3">
        <div
          className="p-0.5 rounded-md shadow-xs shrink-0"
          style={{
            background: `linear-gradient(135deg, ${accentColor} 0%, #c99e3a 100%)`,
          }}
        >
          <ArtSlot
            category="actors"
            contentId={definitionId}
            fallbackLabel={name}
            aspectRatio="1/1"
            className="w-14 h-14 rounded-[5px] border-0"
          />
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5">
            <h4 className="font-display font-bold text-sm m-0 truncate text-[#1e1d19]">
              {name}
            </h4>
            {isInputActor && (
              <span className="px-1.5 py-0.5 text-[9px] font-display font-bold uppercase rounded-full bg-[#1f6f54] text-[#faf5eb] shadow-2xs">
                {translator.text("ACTIVE")}
              </span>
            )}
          </div>
          <span
            className="text-[10px] font-display font-bold uppercase tracking-widest"
            style={{ color: accentColor }}
          >
            {translator.text(relationship.toUpperCase())}
          </span>
        </div>
      </div>

      {/* Telegraphed Intent Box */}
      {intent && (
        <div className="px-3 py-1.5 rounded-md bg-gradient-to-r from-[#1e1d19] to-[#2c2922] text-[#faf5eb] text-xs border-l-3 border-[#c99e3a] shadow-inner">
          <div className="text-[9px] font-display uppercase tracking-widest text-[#edd282] font-bold">
            {translator.text("INTENT")}
          </div>
          <div className="whitespace-pre-line font-semibold">{intent}</div>
        </div>
      )}

      {/* Resource Pools */}
      {resources.length > 0 && (
        <div className="space-y-1.5 pt-1">
          {resources.map((pool, idx) => (
            <ResourceView
              key={String(pool.resourceId ?? idx)}
              pool={pool}
              translator={translator}
            />
          ))}
        </div>
      )}

      {/* Statuses & Modifiers */}
      {(statuses.length > 0 || modifiers.length > 0) && (
        <div className="flex flex-wrap gap-1 pt-1.5 border-t border-[#8d8270]/40">
          {statuses.map((st, idx) => {
            const stId = String(st.statusId ?? "");
            const stacks = Number(st.stacks ?? 1);
            return (
              <span
                key={`st-${idx}`}
                className="px-2 py-0.5 text-[10px] font-bold rounded-full bg-[#e8dcc4] border border-[#8d8270] text-[#1e1d19] shadow-2xs"
                title={stId}
              >
                {translator.contentName(stId)} {stacks > 1 ? `×${stacks}` : ""}
              </span>
            );
          })}
          {modifiers.map((mod, idx) => {
            const modId = String(mod.modifierId ?? "");
            const stacks = Number(mod.stacks ?? 1);
            return (
              <span
                key={`mod-${idx}`}
                className="px-2 py-0.5 text-[10px] font-bold rounded-full bg-[#5f4b8b]/15 border border-[#5f4b8b]/45 text-[#5f4b8b]"
                title={modId}
              >
                {translator.contentName(modId)} {stacks > 1 ? `×${stacks}` : ""}
              </span>
            );
          })}
        </div>
      )}

      {validTarget && (
        <div className="mt-1 text-center py-1 rounded-md bg-gradient-to-r from-[#c03d22] to-[#9c2d17] text-[#faf5eb] font-display text-[11px] font-bold tracking-widest shadow-sm">
          {translator.text("SELECT TARGET")}
        </div>
      )}
    </div>
  );
};

export const CharacterSidebarView: React.FC<{
  run: JsonObject;
  translator: I18n;
}> = ({ run, translator }) => {
  const actor = isPlainObject(run.playerActor)
    ? run.playerActor
    : isPlainObject(run.character)
    ? run.character
    : {};
  const resources = Array.isArray(run.resources)
    ? (run.resources as JsonObject[])
    : Array.isArray(actor.resources)
    ? (actor.resources as JsonObject[])
    : [];
  const relics = Array.isArray(run.relics) ? (run.relics as JsonObject[]) : [];
  const deck = isPlainObject(run.deck) ? run.deck : {};
  const cards = Array.isArray(deck.cardInstances)
    ? (deck.cardInstances as JsonObject[])
    : [];

  return (
    <div className="ivory-panel rounded-lg p-4 flex flex-col gap-4 border-t-4 border-t-[#c99e3a]">
      <div>
        <div className="text-[10px] font-display font-bold uppercase tracking-widest text-[#b83e25]">
          {translator.text("EXPEDITION STATUS")}
        </div>
        <h3 className="font-display text-lg font-bold m-0 text-[#1e1d19]">
          {translator.contentName(
            String(run.playerEntityId ?? "player"),
            translator.text("Wayfarer")
          )}
        </h3>
      </div>

      {resources.length > 0 && (
        <div className="space-y-2">
          <div className="text-[11px] font-display font-bold uppercase tracking-wider text-[#59544b] border-b border-[#8d8270]/40 pb-1">
            {translator.text("RESOURCES")}
          </div>
          {resources.map((pool, idx) => (
            <ResourceView
              key={String(pool.resourceId ?? idx)}
              pool={pool}
              translator={translator}
            />
          ))}
        </div>
      )}

      <div className="space-y-1.5">
        <div className="text-[11px] font-display font-bold uppercase tracking-wider text-[#59544b] border-b border-[#8d8270]/40 pb-1">
          {translator.text("RELICS")} ({relics.length})
        </div>
        {relics.length === 0 ? (
          <div className="text-xs text-[#59544b] italic">
            {translator.text("No relics claimed yet.")}
          </div>
        ) : (
          <div className="flex flex-wrap gap-1.5">
            {relics.map((relic, idx) => {
              const id = String(relic.definitionId ?? relic.relicId ?? idx);
              return (
                <span
                  key={id}
                  className="px-2.5 py-1 text-xs font-bold rounded-full bg-[#e8dcc4] border border-[#c99e3a] text-[#1e1d19] shadow-2xs"
                >
                  ✦ {translator.contentName(id)}
                </span>
              );
            })}
          </div>
        )}
      </div>

      <div className="pt-2 border-t border-[#8d8270]/40 flex items-center justify-between text-xs">
        <span className="font-display font-bold text-[#59544b]">
          {translator.text("DECK CARDS")}
        </span>
        <span className="px-2 py-0.5 rounded-full bg-[#1e1d19] text-[#edd282] font-mono font-bold">
          {cards.length}
        </span>
      </div>
    </div>
  );
};

export const CardDetailsModal: React.FC<{
  model: JsonObject | null;
  inspectionText?: string;
  translator: I18n;
  onClose: () => void;
}> = ({ model, inspectionText = "", translator, onClose }) => {
  if (!model || Object.keys(model).length === 0) return null;
  const text =
    inspectionText || String(model.inspectionText ?? model.summary ?? "");

  return (
    <div
      className="fixed inset-0 z-50 bg-[#05060b]/80 backdrop-blur-xs flex items-center justify-center p-6"
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-modal="true"
        className="ivory-panel max-w-3xl w-full rounded-lg overflow-hidden flex flex-col max-h-[88vh] border-2 border-[#c99e3a]"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-5 py-3 bg-gradient-to-r from-[#24221d] to-[#322e26] text-[#faf5eb] border-b border-[#c99e3a]/50">
          <h3 className="font-display text-lg font-bold m-0 text-[#edd282]">
            {translator.text("CARD INSPECTION")} — {String(model.name ?? "")}
          </h3>
          <button
            type="button"
            onClick={onClose}
            className="ivory-button px-3 py-1 text-xs rounded"
          >
            {translator.text("Close")}
          </button>
        </div>

        <div className="p-6 overflow-y-auto grid grid-cols-1 md:grid-cols-[240px_1fr] gap-6 items-start bg-[#f4ebd9]">
          <div className="flex justify-center">
            <CardView model={model} playable={true} />
          </div>
          <div className="space-y-3">
            <div className="text-xs font-display font-bold uppercase tracking-wider text-[#59544b]">
              {translator.text("PUBLISHED ENGINE FACTS")}
            </div>
            <pre className="p-4 rounded-lg bg-[#1c1b18] text-[#faf5eb] border border-[#8d806b]/50 font-mono text-xs whitespace-pre-wrap leading-relaxed m-0 overflow-x-auto shadow-inner">
              {text || translator.text("Detailed inspection is unavailable.")}
            </pre>
          </div>
        </div>
      </div>
    </div>
  );
};

export const CardZoneDialog: React.FC<{
  title: string;
  cards: JsonObject[];
  visible?: boolean;
  translator: I18n;
  onInspectCard?: (instanceId: string) => void;
  onClose: () => void;
}> = ({
  title,
  cards,
  visible = true,
  translator,
  onInspectCard,
  onClose,
}) => {
  return (
    <div
      className="fixed inset-0 z-50 bg-[#05060b]/80 backdrop-blur-xs flex items-center justify-center p-6"
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-modal="true"
        className="ivory-panel max-w-5xl w-full max-h-[86vh] rounded-lg flex flex-col overflow-hidden border-2 border-[#c99e3a]"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-5 py-3 bg-gradient-to-r from-[#24221d] to-[#322e26] text-[#faf5eb] border-b border-[#c99e3a]/50">
          <h3 className="font-display text-lg font-bold m-0 text-[#edd282]">
            {title} ({cards.length})
          </h3>
          <button
            type="button"
            onClick={onClose}
            className="ivory-button px-3 py-1 text-xs rounded"
          >
            {translator.text("Close")}
          </button>
        </div>

        <div className="p-6 overflow-y-auto flex-1 bg-[#f4ebd9]">
          {!visible ? (
            <div className="py-12 text-center text-sm text-[#59544b] italic">
              {translator.text(
                "Contents of this zone are hidden by game rules."
              )}
            </div>
          ) : cards.length === 0 ? (
            <div className="py-12 text-center text-sm text-[#59544b] italic">
              {translator.text("This zone is currently empty.")}
            </div>
          ) : (
            <div className="flex flex-wrap gap-5 justify-center">
              {cards.map((card, idx) => {
                const instanceId = String(
                  card.cardInstanceId ?? card.definitionId ?? idx
                );
                return (
                  <CardView
                    key={instanceId}
                    model={card}
                    compact={true}
                    onClick={() => onInspectCard?.(instanceId)}
                    onInspect={
                      onInspectCard
                        ? () => onInspectCard(instanceId)
                        : undefined
                    }
                  />
                );
              })}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
