import React, { useMemo, useRef, useState } from "react";
import { AppServices, ScreenId } from "../../bootstrap";
import { isPlainObject, JsonObject } from "../../contracts/types";
import { ActivityPresenter } from "../../presentation/activityPresenter";
import { MapLayout, MapLayoutProfile } from "../../presentation/mapLayout";
import { ActivityController } from "../controllers/controllers";

export const MapScreen: React.FC<{
  services: AppServices;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onNavigate }) => {
  const { session, i18n, audio } = services;
  const [selectedNodeId, setSelectedNodeId] = useState<string>("");
  const [filterKind, setFilterKind] = useState<string>("ALL");

  const controllerRef = useRef<ActivityController | null>(null);
  if (!controllerRef.current) {
    controllerRef.current = new ActivityController(session);
  }
  const controller = controllerRef.current;

  const snapshot = session.captureViewSnapshot();
  const choices = session.activityChoices();
  controller.bind(snapshot.run, choices);

  const presenter = useMemo(
    () =>
      new ActivityPresenter(
        snapshot.run,
        choices,
        services.localizedPresentation(),
        i18n,
        snapshot.activityCardPresentations,
        snapshot.activityModifierItems
      ),
    [snapshot, choices, services, i18n]
  );

  const routeNodes = useMemo(() => presenter.route(), [presenter]);
  const layoutEngine = useMemo(() => new MapLayout(), []);
  const profile = useMemo(() => new MapLayoutProfile(), []);
  const geometry = useMemo(
    () => layoutEngine.calculate(routeNodes, profile),
    [layoutEngine, routeNodes, profile]
  );

  const activeNodeId =
    selectedNodeId ||
    String(
      routeNodes.find((n) => Boolean(n.current))?.id ??
        routeNodes[0]?.id ??
        ""
    );
  const inspectedNode = routeNodes.find((n) => String(n.id) === activeNodeId);

  const kinds = useMemo(() => {
    const s = new Set<string>(["ALL"]);
    for (const n of routeNodes) {
      const k = String(n.kind ?? "");
      if (k) s.add(k);
    }
    return Array.from(s);
  }, [routeNodes]);

  const handleTravel = async (node: JsonObject) => {
    const travel = isPlainObject(node.travel) ? node.travel : {};
    if (Object.keys(travel).length === 0) return;
    audio.ui();
    const ok = await controller.execute(travel);
    if (ok) {
      if (session.hasActiveCombat()) onNavigate("combat");
      else if (session.hasActiveDialogue()) onNavigate("dialogue");
      else onNavigate("journey");
    }
  };

  return (
    <div className="flex flex-col h-full w-full ivory-surface overflow-hidden">
      {/* Header */}
      <header className="flex flex-wrap items-center justify-between gap-3 px-6 py-3 bg-gradient-to-r from-[#1c1b17] via-[#28251e] to-[#1c1b17] text-[#faf5eb] border-b-2 border-[#c99e3a] shadow-md">
        <div className="flex items-center gap-3">
          <h2 className="font-display text-lg font-bold m-0 text-[#edd282]">
            {i18n.text("EXPEDITION ATLAS")}
          </h2>
          {geometry.cyclic && (
            <span className="px-2.5 py-0.5 text-[10px] font-display font-bold uppercase rounded-full bg-[#5f4b8b] text-[#faf5eb] border border-[#edd282]/40">
              {i18n.text("NON-LINEAR ROUTE")}
            </span>
          )}
        </div>

        <div className="flex items-center gap-2.5">
          <select
            aria-label={i18n.text("Filter stops")}
            value={filterKind}
            onChange={(e) => setFilterKind(e.target.value)}
            className="px-2.5 py-1 text-xs rounded border border-[#c99e3a]/60 bg-[#faf5eb] text-[#1e1d19] font-semibold"
          >
            {kinds.map((k) => (
              <option key={k} value={k}>
                {k === "ALL" ? i18n.text("All Stops") : i18n.text(k)}
              </option>
            ))}
          </select>

          <button
            type="button"
            onClick={() => onNavigate("journey")}
            className="ivory-button px-3.5 py-1 text-xs rounded"
          >
            {i18n.text("RETURN TO STAGE")}
          </button>
        </div>
      </header>

      {/* Main Content: Graph Canvas + Inspection Sidebar */}
      <div className="flex-1 grid grid-cols-1 lg:grid-cols-[1fr_360px] overflow-hidden">
        {/* Scrollable SVG Map Graph */}
        <div className="overflow-auto p-6 flex items-start justify-center">
          <svg
            width={Math.max(640, geometry.bounds.x + 48)}
            height={Math.max(480, geometry.bounds.y + 48)}
            className="ivory-panel rounded-lg shadow-md border-2 border-[#c99e3a]/70"
          >
            <defs>
              <marker
                id="map-arrow"
                viewBox="0 0 10 10"
                refX="7"
                refY="5"
                markerWidth="6"
                markerHeight="6"
                orient="auto-start-reverse"
              >
                <path d="M 0 1 L 10 5 L 0 9 z" fill="#8d8270" />
              </marker>
              <marker
                id="map-arrow-active"
                viewBox="0 0 10 10"
                refX="7"
                refY="5"
                markerWidth="6"
                markerHeight="6"
                orient="auto-start-reverse"
              >
                <path d="M 0 1 L 10 5 L 0 9 z" fill="#c03d22" />
              </marker>
            </defs>

            {/* Edges */}
            {geometry.edges.map((edge, idx) => {
              const points = edge.path.map((p) => `${p.x},${p.y}`).join(" ");
              const isFromCurrent =
                routeNodes.find((n) => String(n.id) === edge.from)?.current ===
                true;
              return (
                <polyline
                  key={idx}
                  points={points}
                  fill="none"
                  stroke={isFromCurrent ? "#c03d22" : "#8d8270"}
                  strokeWidth={isFromCurrent ? 2.75 : 1.6}
                  strokeDasharray={edge.return ? "5,4" : undefined}
                  markerEnd={
                    isFromCurrent
                      ? "url(#map-arrow-active)"
                      : "url(#map-arrow)"
                  }
                />
              );
            })}

            {/* Nodes */}
            {routeNodes.map((node) => {
              const id = String(node.id ?? "");
              const rect = geometry.rects[id];
              if (!rect) return null;
              const isCurrent = Boolean(node.current);
              const isResolved = Boolean(node.resolved);
              const canTravel =
                isPlainObject(node.travel) &&
                Object.keys(node.travel).length > 0;
              const isSelected = id === activeNodeId;
              const dimmed =
                filterKind !== "ALL" && String(node.kind ?? "") !== filterKind;

              const fill = isCurrent
                ? "#1e1d19"
                : canTravel
                ? "#fbf6ec"
                : isResolved
                ? "#e6dac3"
                : "#efe5d2";
              const stroke = isSelected
                ? "#c03d22"
                : canTravel
                ? "#1f6f54"
                : "#1e1d19";

              return (
                <g
                  key={id}
                  className="cursor-pointer transition-opacity"
                  style={{ opacity: dimmed ? 0.3 : 1 }}
                  onClick={() => {
                    audio.ui();
                    setSelectedNodeId(id);
                  }}
                  onDoubleClick={() => {
                    if (canTravel) handleTravel(node);
                  }}
                >
                  <rect
                    x={rect.x}
                    y={rect.y}
                    width={rect.width}
                    height={rect.height}
                    rx={8}
                    fill={fill}
                    stroke={stroke}
                    strokeWidth={isSelected || canTravel ? 2.75 : 1.5}
                  />
                  <text
                    x={rect.center.x}
                    y={rect.y + 26}
                    textAnchor="middle"
                    fill={isCurrent ? "#edd282" : "#59544b"}
                    fontSize="9"
                    fontWeight="bold"
                    fontFamily="Cinzel, serif"
                  >
                    {String(node.kind ?? "STOP").toUpperCase()}
                  </text>
                  <text
                    x={rect.center.x}
                    y={rect.y + 52}
                    textAnchor="middle"
                    fill={isCurrent ? "#faf5eb" : "#1e1d19"}
                    fontSize="11"
                    fontWeight="bold"
                  >
                    {String(node.name ?? id).slice(0, 14)}
                  </text>
                  {canTravel && (
                    <text
                      x={rect.center.x}
                      y={rect.y + 82}
                      textAnchor="middle"
                      fill="#1f6f54"
                      fontSize="9"
                      fontWeight="bold"
                    >
                      ● {i18n.text("AVAILABLE")}
                    </text>
                  )}
                  {isCurrent && (
                    <text
                      x={rect.center.x}
                      y={rect.y + 82}
                      textAnchor="middle"
                      fill="#edd282"
                      fontSize="9"
                      fontWeight="bold"
                    >
                      ★ {i18n.text("HERE")}
                    </text>
                  )}
                </g>
              );
            })}
          </svg>
        </div>

        {/* Right Node Dossier Sidebar */}
        <aside className="ivory-panel border-l-2 border-[#c99e3a] p-6 flex flex-col justify-between overflow-y-auto">
          {inspectedNode ? (
            <div className="space-y-4">
              <div className="border-b border-[#8d8270]/40 pb-3">
                <div className="text-[10px] font-display font-bold uppercase tracking-widest text-[#c03d22]">
                  {String(inspectedNode.kind ?? "STAGE")}
                </div>
                <h3 className="font-display text-xl font-bold m-0 mt-1 text-[#1e1d19]">
                  {String(inspectedNode.name ?? inspectedNode.id)}
                </h3>
              </div>

              {String(inspectedNode.description ?? "").length > 0 && (
                <p className="text-sm text-[#59544b] leading-relaxed">
                  {String(inspectedNode.description)}
                </p>
              )}

              <div className="space-y-1.5 text-xs border-t border-[#8d8270]/40 pt-3">
                <div className="flex items-center justify-between">
                  <span className="font-semibold text-[#59544b]">
                    {i18n.text("Status")}:
                  </span>
                  <span className="font-bold text-[#1e1d19]">
                    {inspectedNode.current
                      ? i18n.text("Current Stop")
                      : inspectedNode.resolved
                      ? i18n.text("Visited")
                      : isPlainObject(inspectedNode.travel) &&
                        Object.keys(inspectedNode.travel).length > 0
                      ? i18n.text("Reachable")
                      : i18n.text("Locked / Future")}
                  </span>
                </div>
                {String(inspectedNode.risk ?? "").length > 0 && (
                  <div className="flex items-center justify-between">
                    <span className="font-semibold text-[#59544b]">
                      {i18n.text("Risk")}:
                    </span>
                    <span className="font-bold text-[#a92e28]">
                      {String(inspectedNode.risk)}
                    </span>
                  </div>
                )}
              </div>

              {isPlainObject(inspectedNode.travel) &&
                Object.keys(inspectedNode.travel).length > 0 && (
                  <button
                    type="button"
                    disabled={session.busy}
                    onClick={() => handleTravel(inspectedNode)}
                    className="w-full ivory-button-primary py-2.5 px-4 text-xs rounded-md mt-4"
                  >
                    {presenter.choiceLabel(inspectedNode.travel, inspectedNode)}
                  </button>
                )}
            </div>
          ) : (
            <div className="text-sm text-[#59544b] italic">
              {i18n.text("Select a stop on the map to inspect its details.")}
            </div>
          )}

          {geometry.diagnostics.length > 0 && (
            <div className="mt-6 p-3 rounded bg-[#a92e28]/10 border border-[#a92e28]/40 text-xs text-[#a92e28]">
              {geometry.diagnostics.map((d, i) => (
                <div key={i}>• {d}</div>
              ))}
            </div>
          )}
        </aside>
      </div>
    </div>
  );
};
