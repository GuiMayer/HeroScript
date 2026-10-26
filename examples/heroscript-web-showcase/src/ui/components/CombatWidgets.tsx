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

  const isHealth = resourceId.toLowerCase().includes("health") || resourceId.toLowerCase().includes("hp");
  const isShield = resourceId.toLowerCase().includes("shield") || resourceId.toLowerCase().includes("guard") || resourceId.toLowerCase().includes("block");
  const barColor = isHealth
    ? "#a4322c"
    : isShield
    ? "#286853"
    : "#b83e25";

  return (
    <div className="flex flex-col gap-0.5">
      <div className="flex items-center justify-between text-xs font-semibold">
        <span className="text-[#5b574f] truncate">{name}</span>
        <span className="font-mono font-bold text-[#20201c]">
          {translator.number(current)}
          {hasBounds ? ` / ${translator.number(max)}` : ""}
        </span>
      </div>
      {hasBounds && !compact && (
        <div className="w-full h-2 rounded-sm bg-[#eee4d1] border border-[#8d8270]/60 overflow-hidden">
          <div
            className="h-full transition-all duration-200"
            style={{ width: `${Math.round(ratio * 100)}%`, backgroundColor: barColor }}
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
  const name = translator.contentName(definitionId, String(actor.name ?? instanceId));
  const relationship = String(item.relationship ?? actor.relationship ?? "Neutral");
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

  return (
    <div
      onClick={() => {
        if (validTarget && onSelectTarget) {
          onSelectTarget(instanceId);
        }
      }}
      className={`relative ivory-panel rounded-md p-3.5 flex flex-col gap-2.5 transition-all duration-150 ${
        validTarget
          ? "cursor-pointer ring-2 ring-[#b83e25] hover:bg-[#eee4d1]/60"
          : ""
      } ${selectedTarget ? "ring-4 ring-[#b83e25] bg-[#eee4d1]" : ""}`}
      style={{
        borderTop: `4px solid ${
          isInputActor
            ? "#286853"
            : isHostile
            ? "#a4322c"
            : "#625383"
        }`,
      }}
    >
      {/* Floating combat feedback */}
      {floatingTexts.length > 0 && (
        <div className="absolute -top-3 right-3 z-20 flex flex-col items-end gap-1 pointer-events-none">
          {floatingTexts.map((ft) => (
            <span
              key={ft.id}
              className="px-2 py-0.5 rounded text-xs font-bold shadow bg-[#20201c] text-[#faf5eb] animate-bounce"
            >
              {ft.text}
            </span>
          ))}
        </div>
      )}

      {/* Actor Header */}
      <div className="flex items-center gap-3">
        <ArtSlot
          category="actors"
          contentId={definitionId}
          fallbackLabel={name}
          aspectRatio="1/1"
          className="w-14 h-14 rounded shrink-0"
        />
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5">
            <h4 className="font-display font-semibold text-sm m-0 truncate text-[#20201c]">
              {name}
            </h4>
            {isInputActor && (
              <span className="px-1.5 py-0.5 text-[9px] font-bold uppercase rounded bg-[#286853] text-[#faf5eb]">
                {translator.text("ACTIVE")}
              </span>
            )}
          </div>
          <span className="text-[11px] text-[#5b574f] uppercase tracking-wider">
            {translator.text(relationship.toUpperCase())}
          </span>
        </div>
      </div>

      {/* Intent Box */}
      {intent && (
        <div className="px-2.5 py-1.5 rounded bg-[#20201c] text-[#faf5eb] text-xs border-l-3 border-[#b83e25]">
          <div className="text-[10px] uppercase tracking-wider text-[#f2ebdd]/70 font-semibold">
            {translator.text("INTENT")}
          </div>
          <div className="whitespace-pre-line font-medium">{intent}</div>
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
        <div className="flex flex-wrap gap-1 pt-1 border-t border-[#8d8270]/40">
          {statuses.map((st, idx) => {
            const stId = String(st.statusId ?? "");
            const stacks = Number(st.stacks ?? 1);
            return (
              <span
                key={`st-${idx}`}
                className="px-1.5 py-0.5 text-[10px] font-semibold rounded bg-[#eee4d1] border border-[#8d8270] text-[#20201c]"
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
                className="px-1.5 py-0.5 text-[10px] font-semibold rounded bg-[#625383]/15 border border-[#625383]/40 text-[#625383]"
                title={modId}
              >
                {translator.contentName(modId)} {stacks > 1 ? `×${stacks}` : ""}
              </span>
            );
          })}
        </div>
      )}

      {validTarget && (
        <div className="mt-1 text-center py-1 rounded bg-[#b83e25] text-[#faf5eb] font-display text-[11px] font-semibold tracking-wider">
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
    <div className="ivory-panel rounded-md p-4 flex flex-col gap-4">
      <div>
        <div className="text-[10px] font-semibold uppercase tracking-widest text-[#5b574f]">
          {translator.text("EXPEDITION STATUS")}
        </div>
        <h3 className="font-display text-base font-bold m-0 text-[#20201c]">
          {translator.contentName(
            String(run.playerEntityId ?? "player"),
            translator.text("Wayfarer")
          )}
        </h3>
      </div>

      {resources.length > 0 && (
        <div className="space-y-2">
          <div className="text-[11px] font-display font-semibold uppercase tracking-wider text-[#5b574f] border-b border-[#8d8270]/40 pb-1">
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
        <div className="text-[11px] font-display font-semibold uppercase tracking-wider text-[#5b574f] border-b border-[#8d8270]/40 pb-1">
          {translator.text("RELICS")} ({relics.length})
        </div>
        {relics.length === 0 ? (
          <div className="text-xs text-[#5b574f] italic">
            {translator.text("No relics claimed yet.")}
          </div>
        ) : (
          <div className="flex flex-wrap gap-1.5">
            {relics.map((relic, idx) => {
              const id = String(relic.definitionId ?? relic.relicId ?? idx);
              return (
                <span
                  key={id}
                  className="px-2 py-1 text-xs font-semibold rounded bg-[#eee4d1] border border-[#8d8270] text-[#20201c]"
                >
                  {translator.contentName(id)}
                </span>
              );
            })}
          </div>
        )}
      </div>

      <div className="pt-2 border-t border-[#8d8270]/40 flex items-center justify-between text-xs">
        <span className="font-semibold text-[#5b574f]">
          {translator.text("DECK CARDS")}
        </span>
        <span className="font-mono font-bold text-[#20201c]">
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
  const text = inspectionText || String(model.inspectionText ?? model.summary ?? "");

  return (
    <div
      className="fixed inset-0 z-50 bg-[#05060b]/75 flex items-center justify-center p-6"
      onClick={onClose}
    >
      <div
        className="ivory-panel max-w-3xl w-full rounded-md overflow-hidden flex flex-col max-h-[88vh]"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-5 py-3 bg-[#eee4d1] border-b border-[#8d8270]">
          <h3 className="font-display text-lg font-semibold m-0">
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

        <div className="p-6 overflow-y-auto grid grid-cols-1 md:grid-cols-[240px_1fr] gap-6 items-start">
          <div className="flex justify-center">
            <CardView model={model} playable={true} />
          </div>
          <div className="space-y-3">
            <div className="text-xs font-display font-semibold uppercase tracking-wider text-[#5b574f]">
              {translator.text("PUBLISHED ENGINE FACTS")}
            </div>
            <pre className="p-3.5 rounded bg-[#20201c] text-[#faf5eb] font-mono text-xs whitespace-pre-wrap leading-relaxed m-0 overflow-x-auto">
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
      className="fixed inset-0 z-50 bg-[#05060b]/75 flex items-center justify-center p-6"
      onClick={onClose}
    >
      <div
        className="ivory-panel max-w-5xl w-full max-h-[86vh] rounded-md flex flex-col overflow-hidden"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between px-5 py-3 bg-[#eee4d1] border-b border-[#8d8270]">
          <h3 className="font-display text-lg font-semibold m-0">
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

        <div className="p-5 overflow-y-auto flex-1">
          {!visible ? (
            <div className="py-12 text-center text-sm text-[#5b574f] italic">
              {translator.text("Contents of this zone are hidden by game rules.")}
            </div>
          ) : cards.length === 0 ? (
            <div className="py-12 text-center text-sm text-[#5b574f] italic">
              {translator.text("This zone is currently empty.")}
            </div>
          ) : (
            <div className="flex flex-wrap gap-4 justify-center">
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
