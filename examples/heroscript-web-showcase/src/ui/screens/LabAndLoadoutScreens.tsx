import React, { useMemo, useRef, useState } from "react";
import { AppServices, ScreenId } from "../../bootstrap";
import { isPlainObject, JsonObject } from "../../contracts/types";
import cardHandLabData from "../../data/labs/card_hand_lab.json";
import inscriptionLoadoutLabData from "../../data/labs/inscription_loadout_lab.json";
import { InscriptionLoadoutDraft } from "../../labs/inscriptionLoadoutDraft";
import { CardFactsPresenter } from "../../presentation/cardFactsPresenter";
import { ArtCatalog, CardAnimationProfile } from "../../presentation/visualAndLayout";
import { CircuitTrace } from "../components/ArtSlot";
import { CardHandView } from "../components/CardHandView";
import { CardDetailsModal } from "../components/CombatWidgets";

export const CardHandLabScreen: React.FC<{
  services: AppServices;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onNavigate }) => {
  const { i18n, preferences, audio } = services;
  const catalog = cardHandLabData as unknown as JsonObject;
  const presets = Array.isArray(catalog.presets)
    ? (catalog.presets as JsonObject[])
    : [];

  const [presetIdx, setPresetIdx] = useState(0);
  const [count, setCount] = useState(5);
  const [mode, setMode] = useState<"adaptive" | "reading">("adaptive");
  const [selectedId, setSelectedId] = useState("");
  const [burningIds, setBurningIds] = useState<string[]>([]);
  const [inspected, setInspected] = useState<JsonObject | null>(null);
  const [burnSeconds, setBurnSeconds] = useState(0.55);
  const [burnDirection, setBurnDirection] = useState<"center" | "edges">("center");
  const [statusMsg, setStatusMsg] = useState("");

  const currentPreset = presets[presetIdx] ?? presets[0] ?? {};
  const sampleFacts = Array.isArray(currentPreset.cards)
    ? (currentPreset.cards as JsonObject[])
    : Array.isArray(catalog.cards)
    ? (catalog.cards as JsonObject[])
    : [];

  const cards = useMemo(() => {
    const presenter = new CardFactsPresenter(
      i18n,
      services.localizedPresentation()
    );
    const result: JsonObject[] = [];
    for (let i = 0; i < count; i++) {
      const source = sampleFacts[i % Math.max(1, sampleFacts.length)] ?? {
        identity: {
          definitionId: `sample_card_${i + 1}`,
          cardInstanceId: `lab_card_${i + 1}`,
        },
        rarity: "Common",
        tags: ["attack"],
        costs: [{ costs: { costs: [{ resourceId: "energy", amount: 1 }] } }],
        effects: [
          {
            effectPath: "e0",
            definition: {
              type: "DAMAGE",
              targetResource: "health",
              target: "TARGET",
            },
            parameters: [{ parameter: "Amount", values: { composedBase: 8 } }],
          },
        ],
      };
      const projected = isPlainObject(source.identity)
        ? presenter.project(source)
        : { ...source };
      projected.cardInstanceId = `lab_inst_${presetIdx}_${i + 1}`;
      projected.availability = i18n.text("SELECT TO PLAY");
      result.push(projected);
    }
    return result;
  }, [count, presetIdx, sampleFacts, i18n, services]);

  const triggerBurnDemo = () => {
    if (cards.length === 0) return;
    const targetId =
      selectedId || String(cards[0].cardInstanceId ?? "");
    if (!targetId) return;
    audio.card();
    setBurningIds([targetId]);
    setTimeout(() => {
      setBurningIds([]);
    }, Math.max(300, burnSeconds * 1000));
  };

  return (
    <div className="relative flex flex-col h-full w-full ivory-surface overflow-hidden">
      <CircuitTrace
        mirrored={true}
        className="absolute top-3 right-6 w-[360px] h-[58px] hidden lg:block"
      />

      {/* Header */}
      <header className="flex flex-wrap items-center justify-between gap-3 px-6 py-3 bg-[#eee4d1] border-b-2 border-[#20201c] z-10">
        <div>
          <span className="text-[10px] font-display font-semibold uppercase tracking-widest text-[#b83e25]">
            OFFLINE VISUAL HARNESS · ZERO API CALLS
          </span>
          <h2 className="font-display text-lg font-bold m-0 text-[#20201c]">
            {i18n.text("IVORY / CARD HAND LAB")}
          </h2>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() =>
              i18n.setLocale(i18n.locale === "en" ? "pt_BR" : "en")
            }
            className="ivory-button px-3 py-1 text-xs rounded"
          >
            {i18n.locale === "en" ? "PT-BR" : "EN"}
          </button>
          <button
            type="button"
            onClick={() => onNavigate("menu")}
            className="ivory-button px-3.5 py-1 text-xs rounded"
          >
            {i18n.text("MAIN MENU")}
          </button>
        </div>
      </header>

      {/* Controls Bar */}
      <div className="p-5 overflow-y-auto flex-1 space-y-5">
        <div className="ivory-panel rounded-md p-4 flex flex-wrap items-end gap-4 text-xs">
          {presets.length > 0 && (
            <div>
              <label className="block font-display font-semibold mb-1">
                {i18n.text("Fixture Preset")}
              </label>
              <select
                value={presetIdx}
                onChange={(e) => setPresetIdx(Number(e.target.value))}
                className="p-2 rounded border border-[#20201c] bg-[#faf5eb] font-semibold"
              >
                {presets.map((p, idx) => (
                  <option key={idx} value={idx}>
                    {String(p.name ?? `Preset ${idx + 1}`)}
                  </option>
                ))}
              </select>
            </div>
          )}

          <div>
            <label className="block font-display font-semibold mb-1">
              {i18n.text("Card Count")} (0–100)
            </label>
            <input
              type="number"
              min={0}
              max={100}
              value={count}
              onChange={(e) =>
                setCount(
                  Math.min(100, Math.max(0, Math.trunc(Number(e.target.value))))
                )
              }
              className="w-24 p-2 rounded border border-[#20201c] bg-[#faf5eb] font-mono font-bold"
            />
          </div>

          <div>
            <label className="block font-display font-semibold mb-1">
              {i18n.text("Hand Layout")}
            </label>
            <select
              value={mode}
              onChange={(e) =>
                setMode(e.target.value as "adaptive" | "reading")
              }
              className="p-2 rounded border border-[#20201c] bg-[#faf5eb] font-semibold"
            >
              <option value="adaptive">{i18n.text("Adaptive Fan Arc")}</option>
              <option value="reading">{i18n.text("Flat Reading Strip")}</option>
            </select>
          </div>

          <div>
            <label className="block font-display font-semibold mb-1">
              {i18n.text("Burn Duration (s)")}
            </label>
            <input
              type="number"
              step={0.05}
              min={0.1}
              max={5}
              value={burnSeconds}
              onChange={(e) => setBurnSeconds(Number(e.target.value))}
              className="w-24 p-2 rounded border border-[#20201c] bg-[#faf5eb] font-mono"
            />
          </div>

          <div>
            <label className="block font-display font-semibold mb-1">
              {i18n.text("Burn Origin")}
            </label>
            <select
              value={burnDirection}
              onChange={(e) =>
                setBurnDirection(e.target.value as "center" | "edges")
              }
              className="p-2 rounded border border-[#20201c] bg-[#faf5eb]"
            >
              <option value="center">Center</option>
              <option value="edges">Edges</option>
            </select>
          </div>

          <button
            type="button"
            onClick={triggerBurnDemo}
            className="ivory-button-primary px-4 py-2 rounded"
          >
            {i18n.text("TEST EXILE BURN")}
          </button>

          <button
            type="button"
            onClick={() => {
              const ok = CardAnimationProfile.save(
                "burn",
                burnSeconds,
                burnDirection
              );
              setStatusMsg(
                ok
                  ? i18n.text("Saved burn animation profile.")
                  : i18n.text("Could not save profile.")
              );
            }}
            className="ivory-button px-3.5 py-2 rounded"
          >
            {i18n.text("SAVE PROFILE")}
          </button>
        </div>

        {statusMsg && (
          <div className="text-xs font-semibold text-[#286853] px-2">
            {statusMsg}
          </div>
        )}
      </div>

      {/* Hand Preview Dock */}
      <div className="bg-[#f2ebdd] border-t border-[#8d8270]">
        <CardHandView
          cards={cards}
          selectedId={selectedId}
          burningIds={burningIds}
          mode={mode}
          emphasis={preferences.handEmphasis}
          manifestPath={ArtCatalog.PROGRAMMER_MANIFEST}
          translator={i18n}
          label={i18n.text("LAB SAMPLE HAND")}
          onSelectCard={(id) =>
            setSelectedId((prev) => (prev === id ? "" : id))
          }
          onInspectCard={(id) => {
            const found = cards.find((c) => String(c.cardInstanceId) === id);
            if (found) setInspected(found);
          }}
        />
      </div>

      {inspected && (
        <CardDetailsModal
          model={inspected}
          translator={i18n}
          onClose={() => setInspected(null)}
        />
      )}
    </div>
  );
};

export const InscriptionLoadoutScreen: React.FC<{
  services: AppServices;
  onlineMode?: boolean;
  onNavigate: (screen: ScreenId) => void;
}> = ({ services, onlineMode = false, onNavigate }) => {
  const { i18n, session, loadoutWorkspace, audio } = services;
  const [, setTick] = useState(0);

  const draftRef = useRef<InscriptionLoadoutDraft | null>(null);
  if (!draftRef.current) {
    const d = new InscriptionLoadoutDraft();
    d.configure(inscriptionLoadoutLabData as unknown as JsonObject);
    draftRef.current = d;
  }
  const draft = draftRef.current;

  const isOnline =
    onlineMode && session.engineOnline && loadoutWorkspace.configured;
  const snap = isOnline ? loadoutWorkspace.snapshot() : draft.snapshot();

  const slots = Array.isArray(snap.slots) ? snap.slots.map(String) : [];
  const items = Array.isArray(snap.items) ? (snap.items as JsonObject[]) : [];
  const assignments = isPlainObject(snap.assignments)
    ? (snap.assignments as Record<string, string>)
    : {};
  const selectedItem = String(snap.selectedItem ?? draft.selectedItem ?? "");
  const selectedSlot = String(snap.selectedSlot ?? draft.selectedSlot ?? "");
  const usedCapacity = Number(snap.usedCapacity ?? 0);
  const capacity = Number(snap.capacity ?? 0);
  const overBudget = Boolean(snap.overBudget);
  const character = isPlainObject(snap.character) ? snap.character : {};
  const attributes = Array.isArray(character.attributes)
    ? (character.attributes as JsonObject[])
    : [];
  const presets = Array.isArray(snap.presets)
    ? (snap.presets as JsonObject[])
    : [];

  const [search, setSearch] = useState("");

  const filteredItems = items.filter((item) => {
    if (!search.trim()) return true;
    const q = search.toLowerCase();
    return (
      String(item.name ?? "").toLowerCase().includes(q) ||
      String(item.description ?? "").toLowerCase().includes(q) ||
      String(item.category ?? "").toLowerCase().includes(q)
    );
  });

  const refresh = () => setTick((t) => t + 1);

  return (
    <div className="flex flex-col h-full w-full ivory-surface overflow-hidden">
      {/* Header */}
      <header className="flex flex-wrap items-center justify-between gap-3 px-6 py-3 bg-[#eee4d1] border-b-2 border-[#20201c]">
        <div>
          <span className="text-[10px] font-display font-semibold uppercase tracking-widest text-[#b83e25]">
            {isOnline
              ? i18n.text("AUTHORITATIVE LOADOUT WORKSPACE")
              : i18n.text("IVORY / INSCRIPTION LOADOUT LAB")}
          </span>
          <h2 className="font-display text-lg font-bold m-0 text-[#20201c]">
            {i18n.text("INSCRIPTION & EQUIPMENT SLOTS")}
          </h2>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {!isOnline && presets.length > 0 && (
            <select
              value={draft.presetIndex}
              onChange={(e) => {
                draft.applyPreset(Number(e.target.value));
                refresh();
              }}
              className="px-2.5 py-1 text-xs rounded border border-[#20201c] bg-[#faf5eb] font-semibold"
            >
              {presets.map((p, idx) => (
                <option key={idx} value={idx}>
                  {String(p.name ?? `Preset ${idx + 1}`)}
                </option>
              ))}
            </select>
          )}

          <button
            type="button"
            onClick={() => {
              audio.ui();
              if (!isOnline) {
                draft.clear();
                refresh();
              }
            }}
            className="ivory-button px-3 py-1 text-xs rounded"
          >
            {i18n.text("CLEAR")}
          </button>

          {onlineMode && session.engineOnline && (
            <button
              type="button"
              onClick={async () => {
                audio.reward();
                const ok = await session.startSelectedSetting(assignments);
                if (ok) {
                  if (session.hasActiveCombat()) onNavigate("combat");
                  else onNavigate("journey");
                }
              }}
              className="ivory-button-primary px-4 py-1.5 text-xs rounded"
            >
              {i18n.text("LAUNCH EXPEDITION")}
            </button>
          )}

          <button
            type="button"
            onClick={() => onNavigate("menu")}
            className="ivory-button px-3.5 py-1 text-xs rounded"
          >
            {i18n.text("MAIN MENU")}
          </button>
        </div>
      </header>

      {/* 3-Column Editorial Layout: Inventory | 6 Slots | Character Attributes */}
      <div className="flex-1 overflow-y-auto p-6 grid grid-cols-1 lg:grid-cols-[1.15fr_1.15fr_1fr] gap-6 items-start">
        {/* Column 1: Available Inscriptions Inventory */}
        <div className="ivory-panel rounded-md p-4 flex flex-col gap-3">
          <div className="flex items-center justify-between border-b border-[#8d8270]/50 pb-2">
            <h3 className="font-display text-sm font-bold m-0">
              {i18n.text("INSCRIPTION ARCHIVE")} ({filteredItems.length})
            </h3>
          </div>

          <input
            type="search"
            placeholder={i18n.text("Filter inscriptions…")}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full p-2 text-xs rounded border border-[#8d8270] bg-[#faf5eb]"
          />

          <div className="space-y-2 max-h-[540px] overflow-y-auto pr-1">
            {filteredItems.map((item) => {
              const id = String(item.instanceId ?? item.definitionId ?? "");
              const isSelected = selectedItem === id;
              const equippedSlot = Object.keys(assignments).find(
                (k) => assignments[k] === id
              );

              return (
                <div
                  key={id}
                  onClick={() => {
                    audio.ui();
                    draft.selectItem(id);
                    refresh();
                  }}
                  className={`p-3 rounded border cursor-pointer transition-all ${
                    isSelected
                      ? "bg-[#eee4d1] border-[#b83e25] ring-2 ring-[#b83e25]/30"
                      : "bg-[#faf5eb] border-[#8d8270]/60 hover:border-[#20201c]"
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-display font-bold text-xs text-[#20201c]">
                      {String(item.name ?? id)}
                    </span>
                    <div className="flex items-center gap-1.5">
                      {equippedSlot && (
                        <span className="px-1.5 py-0.5 text-[9px] font-bold uppercase rounded bg-[#286853] text-[#faf5eb]">
                          {equippedSlot}
                        </span>
                      )}
                      <span className="px-1.5 py-0.5 text-[10px] font-mono font-bold rounded bg-[#20201c] text-[#faf5eb]">
                        {Number(item.cost ?? 0)} CP
                      </span>
                    </div>
                  </div>
                  <p className="text-xs text-[#5b574f] mt-1 mb-0">
                    {String(item.description ?? "")}
                  </p>
                </div>
              );
            })}
          </div>
        </div>

        {/* Column 2: 6 Equipment Slots & Capacity Meter */}
        <div className="ivory-panel rounded-md p-4 flex flex-col gap-4">
          <div className="flex items-center justify-between border-b border-[#8d8270]/50 pb-2">
            <h3 className="font-display text-sm font-bold m-0">
              {i18n.text("ACTIVE VESSEL SLOTS")}
            </h3>
            <span
              className={`px-2 py-0.5 rounded font-mono text-xs font-bold ${
                overBudget
                  ? "bg-[#a4322c] text-[#faf5eb]"
                  : "bg-[#20201c] text-[#faf5eb]"
              }`}
            >
              {usedCapacity} / {capacity} CP
            </span>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            {slots.map((slotId) => {
              const assignedId = assignments[slotId] ?? "";
              const assignedItem = items.find(
                (it) => String(it.instanceId ?? "") === assignedId
              );
              const isSlotSelected = selectedSlot === slotId;

              return (
                <div
                  key={slotId}
                  onClick={() => {
                    audio.ui();
                    draft.selectSlot(slotId);
                    if (draft.selectedItem) {
                      draft.stageSelected();
                    }
                    refresh();
                  }}
                  className={`p-3.5 rounded border-2 min-h-[104px] flex flex-col justify-between cursor-pointer transition-all ${
                    isSlotSelected
                      ? "border-[#b83e25] bg-[#eee4d1]"
                      : assignedItem
                      ? "border-[#20201c] bg-[#faf5eb]"
                      : "border-dashed border-[#8d8270] bg-[#f2ebdd]/50"
                  }`}
                >
                  <div className="flex items-center justify-between text-[10px] font-display font-bold uppercase tracking-wider text-[#5b574f]">
                    <span>{slotId}</span>
                    {assignedItem && (
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          draft.selectSlot(slotId);
                          draft.removeSelected();
                          refresh();
                        }}
                        className="text-[#a4322c] hover:underline"
                      >
                        ✕
                      </button>
                    )}
                  </div>

                  {assignedItem ? (
                    <div>
                      <div className="font-display font-bold text-xs text-[#20201c]">
                        {String(assignedItem.name ?? assignedId)}
                      </div>
                      <div className="text-[11px] text-[#5b574f] truncate">
                        {String(assignedItem.description ?? "")}
                      </div>
                    </div>
                  ) : (
                    <div className="text-xs italic text-[#5b574f]">
                      {selectedItem
                        ? i18n.text("Click to assign selected")
                        : i18n.text("Empty slot")}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>

        {/* Column 3: Character Attributes Table */}
        <div className="ivory-panel rounded-md p-4 flex flex-col gap-3">
          <div className="border-b border-[#8d8270]/50 pb-2">
            <div className="text-[10px] font-display font-semibold uppercase tracking-widest text-[#b83e25]">
              {String(character.title ?? "WAYFARER")}
            </div>
            <h3 className="font-display text-sm font-bold m-0">
              {String(character.name ?? i18n.text("ATTRIBUTES SUMMARY"))}
            </h3>
          </div>

          <div className="divide-y divide-[#8d8270]/30 text-xs">
            {attributes.map((stat) => {
              const id = String(stat.id ?? "");
              const label = String(stat.label ?? id);
              const val = Number(stat.value ?? stat.base ?? 0);
              const change = Number(stat.change ?? 0);
              const isPct = String(stat.format ?? "") === "percent";
              const fmt = (n: number) =>
                isPct ? `${i18n.number(n * 100)}%` : i18n.number(n);

              return (
                <div
                  key={id}
                  className="py-2 flex items-center justify-between gap-2"
                >
                  <span className="font-medium text-[#20201c]">{label}</span>
                  <div className="flex items-center gap-2 font-mono">
                    {Math.abs(change) > 1e-6 && (
                      <span
                        className={`text-[11px] font-bold ${
                          change > 0 ? "text-[#286853]" : "text-[#a4322c]"
                        }`}
                      >
                        ({change > 0 ? "+" : ""}
                        {fmt(change)})
                      </span>
                    )}
                    <span className="font-bold text-[#20201c]">{fmt(val)}</span>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </div>
  );
};
