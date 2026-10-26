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
      <header className="flex flex-wrap items-center justify-between gap-3 px-6 py-3 bg-[#eee4d1] border-b-2 border-[#20201c]">
        <div className="flex items-center gap-3">
          <h2 className="font-display text-lg font-bold m-0">
            {i18n.text("EXPEDITION ATLAS")}
          </h2>
          {geometry.cyclic && (
            <span className="px-2 py-0.5 text-[10px] font-bold uppercase rounded bg-[#625383] text-[#faf5eb]">
              {i18n.text("NON-LINEAR ROUTE")}
            </span>
          )}
        </div>

        <div className="flex items-center gap-2">
          <select
            value={filterKind}
            onChange={(e) => setFilterKind(e.target.value)}
            className="px-2.5 py-1 text-xs rounded border border-[#8d8270] bg-[#faf5eb]"
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
        <div className="overflow-auto p-6 bg-[#f2ebdd] flex items-start justify-center">
          <svg
            width={Math.max(640, geometry.bounds.x + 48)}
            height={Math.max(480, geometry.bounds.y + 48)}
            className="ivory-panel rounded-md shadow-inner"
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
                <path d="M 0 1 L 10 5 L 0 9 z" fill="#b83e25" />
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
                  stroke={isFromCurrent ? "#b83e25" : "#8d8270"}
                  strokeWidth={isFromCurrent ? 2.5 : 1.5}
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
                ? "#20201c"
                : canTravel
                ? "#faf5eb"
                : isResolved
                ? "#eee4d1"
                : "#f2ebdd";
              const stroke = isSelected
                ? "#b83e25"
                : canTravel
                ? "#286853"
                : "#20201c";

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
                    rx={6}
                    fill={fill}
                    stroke={stroke}
                    strokeWidth={isSelected || canTravel ? 2.5 : 1.5}
                  />
                  <text
                    x={rect.center.x}
                    y={rect.y + 26}
                    textAnchor="middle"
                    fill={isCurrent ? "#faf5eb" : "#5b574f"}
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
                    fill={isCurrent ? "#faf5eb" : "#20201c"}
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
                      fill="#286853"
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
                      fill="#f5c26b"
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

        {/* Right Node Details Panel */}
        <aside className="ivory-panel border-l border-[#8d8270] p-5 flex flex-col justify-between overflow-y-auto">
          {inspectedNode ? (
            <div className="space-y-4">
              <div>
                <div className="text-[10px] font-display font-semibold uppercase tracking-widest text-[#b83e25]">
                  {String(inspectedNode.kind ?? "STAGE")}
                </div>
                <h3 className="font-display text-xl font-bold m-0 mt-1 text-[#20201c]">
                  {String(inspectedNode.name ?? inspectedNode.id)}
                </h3>
              </div>

              {String(inspectedNode.description ?? "").length > 0 && (
                <p className="text-sm text-[#5b574f] leading-relaxed">
                  {String(inspectedNode.description)}
                </p>
              )}

              <div className="space-y-1 text-xs border-t border-[#8d8270]/40 pt-3">
                <div>
                  <span className="font-semibold text-[#5b574f]">
                    {i18n.text("Status")}:{" "}
                  </span>
                  <span className="font-bold">
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
                  <div>
                    <span className="font-semibold text-[#5b574f]">
                      {i18n.text("Risk")}:{" "}
                    </span>
                    <span className="font-bold text-[#a4322c]">
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
                    className="w-full ivory-button-primary py-2.5 px-4 text-xs rounded mt-4"
                  >
                    {presenter.choiceLabel(inspectedNode.travel, inspectedNode)}
                  </button>
                )}
            </div>
          ) : (
            <div className="text-sm text-[#5b574f] italic">
              {i18n.text("Select a stop on the map to inspect its details.")}
            </div>
          )}

          {geometry.diagnostics.length > 0 && (
            <div className="mt-6 p-3 rounded bg-[#a4322c]/10 border border-[#a4322c]/40 text-xs text-[#a4322c]">
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
