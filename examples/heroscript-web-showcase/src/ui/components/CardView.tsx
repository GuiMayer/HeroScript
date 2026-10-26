import React from "react";
import { isPlainObject, JsonObject } from "../../contracts/types";
import { CostToken } from "../../presentation/cardFactsPresenter";
import { EffectRow, RequirementRow } from "../../presentation/cardSectionPresenter";
import { ArtCatalog } from "../../presentation/visualAndLayout";
import { ArtSlot } from "./ArtSlot";

export interface CardViewProps {
  model: JsonObject;
  selected?: boolean;
  playable?: boolean;
  hovered?: boolean;
  burning?: boolean;
  compact?: boolean;
  manifestPath?: string;
  onClick?: () => void;
  onInspect?: () => void;
  className?: string;
}

const TONE_ACCENTS: Record<string, { border: string; badgeBg: string; badgeText: string }> = {
  attack: {
    border: "#a4322c",
    badgeBg: "#a4322c",
    badgeText: "#faf5eb",
  },
  skill: {
    border: "#286853",
    badgeBg: "#286853",
    badgeText: "#faf5eb",
  },
  power: {
    border: "#625383",
    badgeBg: "#625383",
    badgeText: "#faf5eb",
  },
};

export const CardView: React.FC<CardViewProps> = ({
  model,
  selected = false,
  playable = true,
  hovered = false,
  burning = false,
  compact = false,
  manifestPath = ArtCatalog.IVORY_MANIFEST,
  onClick,
  onInspect,
  className = "",
}) => {
  const definitionId = String(model.definitionId ?? "");
  const name = String(model.name ?? definitionId);
  const tone = String(model.tone ?? "skill").toLowerCase();
  const accent = TONE_ACCENTS[tone] ?? TONE_ACCENTS.skill;
  const upgraded = Boolean(model.upgraded);
  const cardType = String(model.cardType ?? tone.toUpperCase());
  const rarity = String(model.rarity ?? "");
  const costs = Array.isArray(model.costs) ? (model.costs as unknown as CostToken[]) : [];
  const costsKnown = model.costsKnown !== false;
  const costUnknownLabel = String(model.costUnknownLabel ?? "?");
  const alternativeCostCount = Number(model.alternativeCostCount ?? 0);
  const identityTags = Array.isArray(model.identityTags)
    ? model.identityTags.map(String)
    : [];
  const requirements = Array.isArray(model.requirements)
    ? (model.requirements as unknown as RequirementRow[])
    : [];
  const effectRows = Array.isArray(model.effectRows)
    ? (model.effectRows as unknown as EffectRow[])
    : [];
  const summary = String(model.summary ?? "");
  const behaviors = Array.isArray(model.behaviors)
    ? model.behaviors.map(String)
    : [];
  const changeBadges = Array.isArray(model.changeBadges)
    ? (model.changeBadges as JsonObject[])
    : [];
  const availability = String(model.availability ?? "");
  const acquisitionPrice = String(model.acquisitionPrice ?? model.cost ?? "");

  return (
    <div
      onClick={onClick}
      onContextMenu={(e) => {
        if (onInspect) {
          e.preventDefault();
          onInspect();
        }
      }}
      className={`group relative flex flex-col rounded-[6px] bg-[#faf5eb] text-[#20201c] transition-all duration-150 select-none ${
        compact ? "w-[196px] min-h-[276px]" : "w-[224px] min-h-[324px]"
      } ${burning ? "card-burning" : ""} ${
        onClick ? "cursor-pointer" : ""
      } ${className}`}
      style={{
        border: selected
          ? "2.5px solid #b83e25"
          : hovered
          ? `2px solid ${accent.border}`
          : "1.5px solid #20201c",
        boxShadow: selected
          ? "0 0 0 3px rgba(184, 62, 37, 0.28), 0 12px 24px rgba(32, 32, 28, 0.22)"
          : hovered
          ? "0 10px 20px rgba(32, 32, 28, 0.18)"
          : "0 3px 8px rgba(32, 32, 28, 0.10)",
        opacity: playable ? 1 : 0.86,
      }}
    >
      {/* Top Tone Rule */}
      <div
        className="h-[4px] w-full rounded-t-[4px]"
        style={{ backgroundColor: accent.border }}
      />

      {/* Header: Name + Upgrade + Cost Tokens */}
      <div className="flex items-start justify-between gap-1.5 px-2.5 pt-2 pb-1.5 border-b border-[#8d8270]/50 bg-[#f2ebdd]/70">
        <div className="flex items-center gap-1 min-w-0 flex-1">
          <span
            className="font-display font-semibold text-[13px] leading-tight truncate"
            title={name}
          >
            {name}
          </span>
          {upgraded && (
            <span
              className="inline-flex items-center justify-center px-1 py-0.2 text-[10px] font-bold rounded bg-[#286853] text-[#faf5eb] shrink-0"
              title="Upgraded"
            >
              ↑
            </span>
          )}
        </div>

        {/* Cost Badges */}
        <div className="flex items-center gap-1 shrink-0">
          {!costsKnown ? (
            <span
              className="px-1.5 py-0.5 text-[10px] font-semibold rounded bg-[#20201c] text-[#faf5eb]"
              title={costUnknownLabel}
            >
              ?
            </span>
          ) : costs.length === 0 ? (
            <span className="px-1.5 py-0.5 text-[11px] font-bold rounded bg-[#eee4d1] border border-[#8d8270] text-[#20201c]">
              0
            </span>
          ) : (
            costs.map((cost, idx) => (
              <span
                key={idx}
                className="inline-flex items-center gap-0.5 px-1.5 py-0.5 text-[11px] font-bold rounded bg-[#20201c] text-[#faf5eb]"
                title={cost.name}
              >
                <span>{cost.amount}</span>
                <span className="text-[#f2ebdd]/80 text-[10px]">
                  {cost.symbol}
                </span>
              </span>
            ))
          )}
          {alternativeCostCount > 1 && (
            <span
              className="px-1 py-0.5 text-[9px] font-bold rounded bg-[#b83e25] text-[#faf5eb]"
              title={`${alternativeCostCount} cost options`}
            >
              +{alternativeCostCount - 1}
            </span>
          )}
        </div>
      </div>

      {/* Art Slot + Type/Rarity Strip */}
      <div className="px-2.5 pt-2">
        <ArtSlot
          category="cards"
          contentId={definitionId}
          manifestPath={manifestPath}
          fallbackLabel={name}
          aspectRatio={compact ? "16/8.5" : "16/9.5"}
          className="rounded-[3px]"
        />
        <div className="flex items-center justify-between mt-1.5 text-[10px] uppercase tracking-wider font-semibold text-[#5b574f]">
          <span
            className="px-1.5 py-0.5 rounded-[2px] text-[9px] font-display"
            style={{
              backgroundColor: accent.badgeBg,
              color: accent.badgeText,
            }}
          >
            {cardType}
          </span>
          {rarity && <span className="truncate ml-1">{rarity}</span>}
        </div>
      </div>

      {/* Identity Tags & Change Badges */}
      {(identityTags.length > 0 || changeBadges.length > 0) && (
        <div className="flex flex-wrap gap-1 px-2.5 pt-1.5">
          {identityTags.map((tag, idx) => (
            <span
              key={`tag-${idx}`}
              className="px-1.5 py-0.5 text-[9px] font-semibold uppercase tracking-wide rounded bg-[#eee4d1] text-[#20201c] border border-[#8d8270]/50"
            >
              {tag}
            </span>
          ))}
          {changeBadges.map((badge, idx) => {
            if (!isPlainObject(badge)) return null;
            const kind = String(badge.kind ?? "");
            const isUp = kind === "upgrade" || kind === "upgraded";
            return (
              <span
                key={`chg-${idx}`}
                className={`px-1.5 py-0.5 text-[9px] font-bold uppercase tracking-wide rounded ${
                  isUp
                    ? "bg-[#286853]/15 text-[#286853] border border-[#286853]/40"
                    : "bg-[#b83e25]/15 text-[#b83e25] border border-[#b83e25]/40"
                }`}
              >
                {String(badge.symbol ?? "")} {String(badge.label ?? "")}
              </span>
            );
          })}
        </div>
      )}

      {/* Requirements */}
      {requirements.length > 0 && (
        <div className="px-2.5 pt-1.5 space-y-0.5">
          {requirements.map((req, idx) => (
            <div
              key={idx}
              className={`text-[10px] px-1.5 py-0.5 rounded border ${
                req.passed === false
                  ? "bg-[#a4322c]/10 border-[#a4322c]/40 text-[#a4322c]"
                  : req.passed === true
                  ? "bg-[#286853]/10 border-[#286853]/40 text-[#286853]"
                  : "bg-[#eee4d1] border-[#8d8270]/50 text-[#5b574f]"
              }`}
            >
              • {req.text}
            </div>
          ))}
        </div>
      )}

      {/* Dark Ivory Effects Band (#20201c) */}
      <div className="mx-2 my-2 flex-1 rounded-[4px] bg-[#20201c] text-[#faf5eb] p-2.5 flex flex-col justify-between shadow-inner">
        <div className="space-y-1.5 text-[12px] leading-snug">
          {effectRows.length > 0 ? (
            effectRows.map((row, rIdx) => (
              <p key={rIdx} className="m-0">
                {row.segments.map((seg, sIdx) => {
                  const hasVal = typeof seg.value === "number";
                  const hasBase = typeof seg.baseValue === "number";
                  let valClass = "font-bold text-[#f2ebdd]";
                  if (hasVal && hasBase) {
                    if (seg.value! > seg.baseValue!) {
                      valClass = "font-bold text-[#5cd6a9] underline decoration-dotted";
                    } else if (seg.value! < seg.baseValue!) {
                      valClass = "font-bold text-[#ff8a84] underline decoration-dotted";
                    }
                  } else if (hasVal) {
                    valClass = "font-bold text-[#f5c26b]";
                  }
                  return (
                    <span
                      key={sIdx}
                      className={hasVal ? valClass : "text-[#faf5eb]/95"}
                    >
                      {seg.text}
                    </span>
                  );
                })}
              </p>
            ))
          ) : (
            <p className="m-0 text-[#faf5eb]/90 whitespace-pre-line">
              {summary}
            </p>
          )}
        </div>

        {behaviors.length > 0 && (
          <div className="mt-2 pt-1.5 border-t border-[#faf5eb]/20 flex flex-wrap gap-1">
            {behaviors.map((beh, idx) => (
              <span
                key={idx}
                className="text-[10px] italic text-[#f2ebdd]/80 bg-[#faf5eb]/10 px-1.5 py-0.5 rounded"
              >
                {beh}
              </span>
            ))}
          </div>
        )}
      </div>

      {/* Footer: Availability / Price / Inspect button */}
      <div className="px-2.5 pb-2 pt-0.5 flex items-center justify-between gap-1 text-[10px]">
        <span
          className={`truncate font-semibold uppercase tracking-wider ${
            playable ? "text-[#286853]" : "text-[#5b574f]"
          }`}
          title={availability}
        >
          {acquisitionPrice ? acquisitionPrice : availability}
        </span>

        {onInspect && (
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation();
              onInspect();
            }}
            className="px-1.5 py-0.5 rounded text-[10px] font-display font-semibold border border-[#8d8270] bg-[#eee4d1] hover:border-[#b83e25] hover:text-[#b83e25] transition-colors"
            title="Inspect Card Details"
          >
            ⓘ
          </button>
        )}
      </div>
    </div>
  );
};
