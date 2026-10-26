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

const TONE_ACCENTS: Record<
  string,
  {
    border: string;
    crownGradient: string;
    badgeBg: string;
    badgeText: string;
    glow: string;
  }
> = {
  attack: {
    border: "#a92e28",
    crownGradient: "linear-gradient(90deg, #7c1d18 0%, #c4382f 50%, #7c1d18 100%)",
    badgeBg: "#a4322c",
    badgeText: "#faf5eb",
    glow: "rgba(184, 54, 42, 0.35)",
  },
  skill: {
    border: "#1f6f54",
    crownGradient: "linear-gradient(90deg, #164f3c 0%, #2b8868 50%, #164f3c 100%)",
    badgeBg: "#286853",
    badgeText: "#faf5eb",
    glow: "rgba(40, 104, 83, 0.35)",
  },
  power: {
    border: "#5f4b8b",
    crownGradient: "linear-gradient(90deg, #423266 0%, #7860ad 50%, #423266 100%)",
    badgeBg: "#625383",
    badgeText: "#faf5eb",
    glow: "rgba(98, 83, 131, 0.35)",
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
      className={`tcg-card-frame group relative flex flex-col rounded-[10px] text-[#20201c] select-none ${
        compact ? "w-[200px] min-h-[286px]" : "w-[230px] min-h-[336px]"
      } ${burning ? "card-burning" : ""} ${
        onClick ? "cursor-pointer" : ""
      } ${className}`}
      style={{
        border: selected
          ? "2.5px solid #c03d22"
          : hovered
          ? `2px solid ${accent.border}`
          : playable
          ? "2px solid #2b2923"
          : "1.5px solid #756c5d",
        boxShadow: selected
          ? "0 0 0 3px rgba(192, 61, 34, 0.38), 0 18px 34px rgba(15, 14, 12, 0.42)"
          : hovered
          ? `0 0 0 2px ${accent.glow}, 0 14px 28px rgba(15, 14, 12, 0.34)`
          : playable
          ? "0 6px 16px rgba(20, 18, 14, 0.22)"
          : "0 3px 8px rgba(20, 18, 14, 0.14)",
        opacity: playable ? 1 : 0.84,
      }}
    >
      {/* Top Tone Crown Bar */}
      <div
        className="h-[6px] w-full rounded-t-[8px]"
        style={{ background: accent.crownGradient }}
      />

      {/* Card Banner Header: Cost Orbs + Title Ribbon + Upgrade Crest */}
      <div className="relative z-10 flex items-center justify-between gap-1.5 px-2.5 pt-2 pb-1.5 bg-gradient-to-b from-[#faf4e6] to-[#ebe0ca] border-b border-[#8d8270]/60 shadow-2xs">
        {/* Cost Orbs */}
        <div className="flex items-center gap-1 shrink-0">
          {!costsKnown ? (
            <span
              className="tcg-cost-orb inline-flex items-center justify-center min-w-[24px] h-[24px] px-1.5 text-[11px] font-bold rounded-full text-[#faf5eb]"
              title={costUnknownLabel}
            >
              ?
            </span>
          ) : costs.length === 0 ? (
            <span className="tcg-cost-orb inline-flex items-center justify-center min-w-[24px] h-[24px] px-1.5 text-[11px] font-bold rounded-full text-[#edd282]">
              0
            </span>
          ) : (
            costs.map((cost, idx) => (
              <span
                key={idx}
                className="tcg-cost-orb inline-flex items-center justify-center gap-0.5 min-w-[26px] h-[24px] px-1.5 text-[11px] font-bold rounded-full text-[#faf5eb]"
                title={cost.name}
              >
                <span>{cost.amount}</span>
                <span className="text-[#edd282] text-[10px] leading-none">
                  {cost.symbol}
                </span>
              </span>
            ))
          )}
          {alternativeCostCount > 1 && (
            <span
              className="px-1.5 py-0.5 text-[9px] font-bold rounded-full bg-[#b83e25] text-[#faf5eb] border border-[#edd282]/60 shadow-2xs"
              title={`${alternativeCostCount} cost options`}
            >
              +{alternativeCostCount - 1}
            </span>
          )}
        </div>

        {/* Card Name + Upgrade Seal */}
        <div className="flex items-center justify-end gap-1 min-w-0 flex-1">
          <span
            className={`font-display font-bold text-[13px] leading-tight truncate ${
              upgraded ? "text-[#195e47]" : "text-[#1e1d19]"
            }`}
            title={name}
          >
            {name}
          </span>
          {upgraded && (
            <span
              className="inline-flex items-center justify-center w-4 h-4 text-[10px] font-bold rounded-full bg-[#1f6f54] text-[#faf5eb] border border-[#edd282]/70 shrink-0 shadow-2xs"
              title="Upgraded"
            >
              ↑
            </span>
          )}
        </div>
      </div>

      {/* Framed Art Well + Type Medallion Bridge */}
      <div className="relative px-2.5 pt-2">
        <div className="rounded-[5px] p-[2px] bg-gradient-to-b from-[#b99b52] via-[#8d806b] to-[#5e5444] shadow-sm">
          <ArtSlot
            category="cards"
            contentId={definitionId}
            manifestPath={manifestPath}
            fallbackLabel={name}
            aspectRatio={compact ? "16/8.5" : "16/9.5"}
            className="rounded-[4px] border-0"
          />
        </div>

        {/* Overlapping Type Pill & Rarity Strip */}
        <div className="relative z-10 -mt-2.5 px-2 flex items-center justify-between">
          <span
            className="px-2.5 py-0.5 rounded-full text-[9px] font-display font-bold tracking-widest uppercase border border-[#edd282]/60 shadow-sm"
            style={{
              backgroundColor: accent.badgeBg,
              color: accent.badgeText,
            }}
          >
            {cardType}
          </span>
          {rarity && (
            <span className="px-2 py-0.5 rounded-full bg-[#20201c]/90 text-[#edd282] border border-[#c99e3a]/50 text-[9px] font-display font-semibold uppercase tracking-wider truncate ml-1 shadow-2xs">
              {rarity}
            </span>
          )}
        </div>
      </div>

      {/* Identity Tags & Transformation Change Badges */}
      {(identityTags.length > 0 || changeBadges.length > 0) && (
        <div className="flex flex-wrap gap-1 px-2.5 pt-1.5">
          {identityTags.map((tag, idx) => (
            <span
              key={`tag-${idx}`}
              className="px-1.5 py-0.5 text-[9px] font-semibold uppercase tracking-wide rounded bg-[#e8dcc4] text-[#20201c] border border-[#8d8270]/60"
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
                    ? "bg-[#1f6f54]/15 text-[#1f6f54] border border-[#1f6f54]/40"
                    : "bg-[#c03d22]/15 text-[#c03d22] border border-[#c03d22]/40"
                }`}
              >
                {String(badge.symbol ?? "")} {String(badge.label ?? "")}
              </span>
            );
          })}
        </div>
      )}

      {/* Play Requirements */}
      {requirements.length > 0 && (
        <div className="px-2.5 pt-1.5 space-y-0.5">
          {requirements.map((req, idx) => (
            <div
              key={idx}
              className={`text-[10px] px-1.5 py-0.5 rounded border font-medium ${
                req.passed === false
                  ? "bg-[#a4322c]/12 border-[#a4322c]/45 text-[#a4322c]"
                  : req.passed === true
                  ? "bg-[#286853]/12 border-[#286853]/45 text-[#286853]"
                  : "bg-[#eee4d1] border-[#8d8270]/50 text-[#5b574f]"
              }`}
            >
              • {req.text}
            </div>
          ))}
        </div>
      )}

      {/* Obsidian Rules Box */}
      <div className="mx-2.5 my-2 flex-1 rounded-[6px] bg-gradient-to-b from-[#23221d] to-[#171613] text-[#faf5eb] p-2.5 flex flex-col justify-between border border-[#8d806b]/45 shadow-inner">
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
                      valClass = "font-bold text-[#63e2b7] underline decoration-dotted";
                    } else if (seg.value! < seg.baseValue!) {
                      valClass = "font-bold text-[#ff8a84] underline decoration-dotted";
                    }
                  } else if (hasVal) {
                    valClass = "font-bold text-[#f7c86c]";
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
          <div className="mt-2 pt-1.5 border-t border-[#faf5eb]/15 flex flex-wrap gap-1">
            {behaviors.map((beh, idx) => (
              <span
                key={idx}
                className="text-[10px] font-medium italic text-[#edd282] bg-[#faf5eb]/10 px-1.5 py-0.5 rounded border border-[#edd282]/20"
              >
                {beh}
              </span>
            ))}
          </div>
        )}
      </div>

      {/* Footer: Playability Status / Price / Inspect Medallion */}
      <div className="relative z-10 px-2.5 pb-2 pt-0.5 flex items-center justify-between gap-1 text-[10px]">
        <span
          className={`truncate font-display font-bold uppercase tracking-wider ${
            playable ? "text-[#1f6f54]" : "text-[#59544b]"
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
            className="w-5 h-5 inline-flex items-center justify-center rounded-full text-[11px] font-display font-bold border border-[#8d8270] bg-[#eee4d1] text-[#20201c] hover:border-[#c03d22] hover:bg-[#c03d22] hover:text-[#faf5eb] transition-colors shadow-2xs"
            title="Inspect Card Details"
          >
            ⓘ
          </button>
        )}
      </div>
    </div>
  );
};
