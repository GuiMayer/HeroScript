import React, { useEffect, useRef, useState } from "react";
import { JsonObject } from "../../contracts/types";
import { I18n } from "../../presentation/i18n";
import { ArtCatalog, HandLayout, HandVisualStyle } from "../../presentation/visualAndLayout";
import { CardView } from "./CardView";

export interface CardHandViewProps {
  cards: JsonObject[];
  selectedId?: string;
  burningIds?: string[];
  mode?: "adaptive" | "reading";
  emphasis?: number;
  readOnly?: boolean;
  manifestPath?: string;
  translator: I18n;
  label?: string;
  onSelectCard?: (instanceId: string) => void;
  onHoverCard?: (instanceId: string) => void;
  onInspectCard?: (instanceId: string) => void;
}

export const CardHandView: React.FC<CardHandViewProps> = ({
  cards,
  selectedId = "",
  burningIds = [],
  mode = "adaptive",
  emphasis = 1.0,
  readOnly = false,
  manifestPath = ArtCatalog.IVORY_MANIFEST,
  translator,
  label = "",
  onSelectCard,
  onHoverCard,
  onInspectCard,
}) => {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [availableWidth, setAvailableWidth] = useState(960);
  const [hoveredId, setHoveredId] = useState("");
  const [overviewOpen, setOverviewOpen] = useState(false);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const updateWidth = () => {
      setAvailableWidth(Math.max(320, el.clientWidth));
    };
    updateWidth();
    const observer = new ResizeObserver(updateWidth);
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  const cardSize = { x: 230, y: 336 };
  const style = new HandVisualStyle();
  const layout = HandLayout.calculate(
    cards.length,
    availableWidth,
    cardSize,
    style,
    mode
  );

  const activeId = hoveredId || selectedId || String(cards[0]?.cardInstanceId ?? "");

  return (
    <div className="flex flex-col w-full select-none" ref={containerRef}>
      {/* Tabletop Hand Rail Header */}
      <div className="flex items-center justify-between px-4 py-1.5 bg-gradient-to-r from-[#24221d] via-[#2d2a23] to-[#24221d] text-[#faf5eb] border-t border-b border-[#c99e3a]/40 text-xs shadow-sm">
        <div className="flex items-center gap-2.5">
          <span className="inline-block w-2 h-2 rounded-full bg-[#c99e3a]" />
          <span className="font-display font-bold tracking-wider text-[#edd282]">
            {label || translator.text("PLAYABLE CARDS")}
          </span>
          <span className="px-2 py-0.5 rounded-full bg-[#faf5eb]/12 text-[#faf5eb] font-mono text-[11px] font-bold">
            ({cards.length})
          </span>
        </div>

        <div className="flex items-center gap-2">
          {activeId && onInspectCard && (
            <button
              type="button"
              onClick={() => onInspectCard(activeId)}
              className="ivory-button px-2.5 py-0.5 text-[11px] rounded"
            >
              {translator.text("Inspect card")}
            </button>
          )}
          {cards.length > 0 && (
            <button
              type="button"
              onClick={() => setOverviewOpen(true)}
              className="ivory-button px-2.5 py-0.5 text-[11px] rounded"
            >
              {translator.text("Hand overview")}
            </button>
          )}
        </div>
      </div>

      {/* Hand Fan / Reading Surface */}
      {cards.length === 0 ? (
        <div className="h-[220px] flex items-center justify-center text-[#5b574f] italic text-sm bg-[#e8dcc6]/60 inner-shadow">
          {translator.text("No cards in hand.")}
        </div>
      ) : (
        <div className="w-full overflow-x-auto overflow-y-hidden pb-3 pt-3 bg-gradient-to-b from-[#e4d7be]/70 to-[#d8c8aa]/90">
          <div
            className="relative mx-auto transition-all duration-150"
            style={{
              width: `${Math.max(availableWidth, layout.extent)}px`,
              height: `${Math.round(layout.height)}px`,
            }}
          >
            {cards.map((card, index) => {
              const instanceId = String(
                card.cardInstanceId ?? card.definitionId ?? index
              );
              const isSelected = selectedId === instanceId;
              const isHovered = hoveredId === instanceId;
              const isBurning = burningIds.includes(instanceId);
              const pos = layout.positions[index] ?? { x: 0, y: 0 };
              const deg = layout.rotationDegrees[index] ?? 0;

              const liftY =
                isSelected || isHovered
                  ? -style.lift * Math.max(0.4, emphasis)
                  : 0;
              const rot = isSelected || isHovered ? 0 : deg;
              const scale =
                isSelected || isHovered
                  ? 1 + 0.065 * Math.max(0.3, emphasis)
                  : 1;
              const zIndex = isHovered
                ? 200
                : isSelected
                ? 180
                : 10 + index;

              const playable =
                !readOnly &&
                String(card.availability ?? "") ===
                  translator.text("SELECT TO PLAY");

              return (
                <div
                  key={instanceId}
                  style={{
                    position: "absolute",
                    left: `${pos.x}px`,
                    top: `${pos.y}px`,
                    transform: `translateY(${liftY}px) rotate(${rot}deg) scale(${scale})`,
                    transformOrigin: "center bottom",
                    zIndex,
                    transition:
                      "transform 0.16s cubic-bezier(0.22, 1, 0.36, 1), left 0.16s cubic-bezier(0.22, 1, 0.36, 1), top 0.16s cubic-bezier(0.22, 1, 0.36, 1)",
                  }}
                  onMouseEnter={() => {
                    setHoveredId(instanceId);
                    onHoverCard?.(instanceId);
                  }}
                  onMouseLeave={() => {
                    setHoveredId((prev) => (prev === instanceId ? "" : prev));
                    onHoverCard?.("");
                  }}
                >
                  <CardView
                    model={card}
                    selected={isSelected}
                    hovered={isHovered}
                    playable={playable || readOnly}
                    burning={isBurning}
                    manifestPath={manifestPath}
                    onClick={() => onSelectCard?.(instanceId)}
                    onInspect={
                      onInspectCard
                        ? () => onInspectCard(instanceId)
                        : undefined
                    }
                  />
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* Hand Overview Modal */}
      {overviewOpen && (
        <div
          className="fixed inset-0 z-50 bg-[#05060b]/80 backdrop-blur-xs flex items-center justify-center p-6"
          onClick={() => setOverviewOpen(false)}
        >
          <div
            role="dialog"
            aria-modal="true"
            className="ivory-panel max-w-6xl w-full max-h-[88vh] rounded-lg flex flex-col overflow-hidden border-2 border-[#c99e3a]"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-center justify-between px-5 py-3 bg-gradient-to-r from-[#24221d] to-[#322e26] text-[#faf5eb] border-b border-[#c99e3a]/50">
              <h3 className="font-display text-lg font-bold m-0 text-[#edd282]">
                {translator.text("Hand overview")} ({cards.length})
              </h3>
              <button
                type="button"
                onClick={() => setOverviewOpen(false)}
                className="ivory-button px-3 py-1 text-xs rounded"
              >
                {translator.text("Close")}
              </button>
            </div>
            <div className="p-6 overflow-y-auto flex flex-wrap gap-5 justify-center bg-[#efe5d2]">
              {cards.map((card, idx) => {
                const instanceId = String(
                  card.cardInstanceId ?? card.definitionId ?? idx
                );
                return (
                  <CardView
                    key={instanceId}
                    model={card}
                    selected={selectedId === instanceId}
                    manifestPath={manifestPath}
                    onClick={() => {
                      onSelectCard?.(instanceId);
                      setOverviewOpen(false);
                    }}
                    onInspect={
                      onInspectCard
                        ? () => {
                            onInspectCard(instanceId);
                            setOverviewOpen(false);
                          }
                        : undefined
                    }
                  />
                );
              })}
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
