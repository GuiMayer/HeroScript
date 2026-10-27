(() => {
  const params = new URLSearchParams(window.location.search);
  const API_BASE = (params.get("api") || "http://localhost:5260").replace(/\/+$/, "");

  const COLOR_LABELS = {
    red: "Vermelho",
    blue: "Azul",
    green: "Verde",
    yellow: "Amarelo",
    wild: "Multicor"
  };

  function movedCardsToHands(payload) {
    const byActor = new Map();
    const applications = payload?.cardResolution?.applications || payload?.abilityResolution?.applications || [];
    for (const application of applications) {
      for (const step of application.cardZoneSteps || application.CardZoneSteps || []) {
        const match = (step.targetAddress || step.TargetAddress || "").match(/::([^:]+)::hand$/);
        if (!match) continue;
        if (!byActor.has(match[1])) byActor.set(match[1], new Set());
        for (const id of step.instanceIds || step.InstanceIds || []) byActor.get(match[1]).add(id);
      }
    }
    return [...byActor].map(([actorId, ids]) => ({ actorId, count: ids.size }));
  }

  function opponentEvents(response) {
    const events = [];
    for (const frame of response.state?.resolution?.frames || []) {
      const payload = frame.payload || {};
      const command = payload.resolvedCommand || payload.command;
      if ((command?.actorId || command?.ActorId) !== "dealer") continue;
      const definitionId = payload.cardResolution?.definitionId;
      const actionId = command.powerId || command.PowerId;
      if (definitionId) events.push({ kind: "card", definitionId, cardInstanceId: payload.consumedCardId || command.cardInstanceId || command.CardInstanceId, draws: movedCardsToHands(payload) });
      else if (actionId) events.push({ kind: "action", actionId, draws: movedCardsToHands(payload) });
      else if ((command.actionType || command.ActionType) === "END_TURN") events.push({ kind: "pass", draws: [] });
    }
    return events;
  }

  async function withBotActionPacing(present) {
    const startedAt = performance.now();
    await present();
    const remaining = 1000 - (performance.now() - startedAt);
    if (remaining > 0) await new Promise(resolve => setTimeout(resolve, remaining));
  }

  async function animateOpponentEvents(response) {
    for (const event of opponentEvents(response)) {
      await withBotActionPacing(async () => {
        const messages = [];
        const card = event.kind === "card" ? parseCardIdentity(event.definitionId) : null;
        if (card) messages.push(`🃏 O bot jogou ${card.label}.`);
        else if (event.kind === "pass") messages.push("O bot passou a vez.");
        else if (event.actionId.startsWith("uno_choose_")) {
          messages.push(`🌈 O bot escolheu ${COLOR_LABELS[event.actionId.replace("uno_choose_", "")]}.`);
        } else if (event.actionId === "uno_shout_uno") messages.push("📣 O bot declarou UNO!");
        else if (event.actionId === "uno_catch_no_uno") messages.push("🚨 O bot cobrou o UNO esquecido.");
        else if (event.actionId === "uno_draw_card" && !event.draws.some(draw => draw.count > 0)) {
          messages.push("O bot tentou comprar, mas não havia cartas disponíveis.");
        }
        for (const draw of event.draws) {
          if (draw.count) messages.push(`${draw.actorId === "player" ? "Você comprou" : "O bot comprou"} ${draw.count} carta(s) do monte.`);
        }
        if (messages.length) setBannerMessage(messages.join(" • "), true);

        if (card) {
          const from = el.aiHandFan?.lastElementChild?.getBoundingClientRect() || el.aiHandFan?.getBoundingClientRect();
          await animateCardFlight(from, el.discardStage, card, 380);
          state.botHandCount = Math.max(0, state.botHandCount - 1);
          state.botHandCards = state.botHandCards.filter(item => item.cardInstanceId !== event.cardInstanceId);
          renderBotHandFan();
          renderBotHandTelemetry();
          spawnShockwave(el.discardStage);
          if (card.isReverse && el.colorOrbitRing) {
            state.orbitReversed = !state.orbitReversed;
            el.colorOrbitRing.classList.toggle("reversed", state.orbitReversed);
          }
          if (card.specialSplash) showCalloutSplash(card.specialSplash);
        } else if (event.actionId === "uno_shout_uno") showCalloutSplash("📣 BOT: UNO!");
        for (const draw of event.draws) {
          if (!draw.count) continue;
          const player = draw.actorId === "player";
          if (event.actionId === "uno_accept_draw2") showCalloutSplash(`📥 ${player ? "VOCÊ" : "BOT"} COMPRA +${draw.count}!`);
          await animateCardFlight(el.drawPileAnchor?.getBoundingClientRect(), player ? el.playerHandContainer : el.aiHandFan, null, 360);
          if (!player) {
            state.botHandCount += draw.count;
            renderBotHandFan();
            renderBotHandTelemetry();
          }
        }
        appendLog("BOT", card ? event.definitionId : event.actionId || "END_TURN");
      });
    }
  }

  const state = {
    settingRevision: null,
    runId: null,
    combatId: null,
    runSequence: 0,
    combatStep: 0,
    busy: false,
    botHandCards: [],
    botHandCount: 0,
    legalByCardInstance: new Map(),
    legalByActionId: new Map(),
    autoTurnTimer: null,
    canEndTurn: false,
    autoEndTurnEligible: false,
    autoEndTurnDelay: 680,
    pendingTableFlight: null,
    tableFlightGeneration: 0,
    pendingDraw2: 0,
    opponentPendingDraw2: 0,
    lastTableCode: null,
    lastActiveColor: null,
    discardHistory: [],
    orbitReversed: false,
    splashTimer: null
  };

  // DOM Elements
  const el = {
    fxLayer: document.getElementById("fxLayer"),
    calloutSplash: document.getElementById("calloutSplash"),
    calloutSplashInner: document.getElementById("calloutSplashInner"),
    colorOrbitRing: document.getElementById("colorOrbitRing"),

    apiStatusDot: document.getElementById("apiStatusDot"),
    revisionBadge: document.getElementById("revisionBadge"),
    seedInput: document.getElementById("seedInput"),
    autoEndTurnToggle: document.getElementById("autoEndTurnToggle"),
    newRunBtn: document.getElementById("newRunBtn"),
    toggleInspectorBtn: document.getElementById("toggleInspectorBtn"),
    closeInspectorBtn: document.getElementById("closeInspectorBtn"),
    engineInspector: document.getElementById("engineInspector"),
    showBotCardsToggle: document.getElementById("showBotCardsToggle"),
    telemetryBotCards: document.getElementById("telemetryBotCards"),
    telemetryBotCardsStatus: document.getElementById("telemetryBotCardsStatus"),

    aiHandCountPill: document.getElementById("aiHandCountPill"),
    aiHandFan: document.getElementById("aiHandFan"),
    aiActionBanner: document.getElementById("aiActionBanner"),
    turnOwnerBadge: document.getElementById("turnOwnerBadge"),
    aiActionText: document.getElementById("aiActionText"),

    drawCardBtn: document.getElementById("drawCardBtn"),
    drawPileAnchor: document.getElementById("drawPileAnchor"),
    drawPileCount: document.getElementById("drawPileCount"),
    drawPileHint: document.getElementById("drawPileHint"),
    discardStage: document.getElementById("discardStage"),
    activeColorLabel: document.getElementById("activeColorLabel"),
    activeValueLabel: document.getElementById("activeValueLabel"),

    shoutUnoBtn: document.getElementById("shoutUnoBtn"),
    endTurnBtn: document.getElementById("endTurnBtn"),
    endTurnProgress: document.getElementById("endTurnProgress"),

    playerHandCountBadge: document.getElementById("playerHandCountBadge"),
    playableCountBadge: document.getElementById("playableCountBadge"),
    unoProtectedBadge: document.getElementById("unoProtectedBadge"),
    extraTurnBadge: document.getElementById("extraTurnBadge"),
    turnPromptText: document.getElementById("turnPromptText"),
    playerHandContainer: document.getElementById("playerHandContainer"),

    inspRunId: document.getElementById("inspRunId"),
    inspCombatId: document.getElementById("inspCombatId"),
    inspSeqStep: document.getElementById("inspSeqStep"),
    inspStateHash: document.getElementById("inspStateHash"),
    inspResourceGrid: document.getElementById("inspResourceGrid"),
    engineEventLog: document.getElementById("engineEventLog"),

    wildModal: document.getElementById("wildModal"),
    radialCenterHub: document.getElementById("radialCenterHub"),
    radialHubGlyph: document.getElementById("radialHubGlyph"),
    radialHubTitle: document.getElementById("radialHubTitle"),
    radialHubSub: document.getElementById("radialHubSub"),
    outcomeModal: document.getElementById("outcomeModal"),
    outcomeBadge: document.getElementById("outcomeBadge"),
    outcomeTitle: document.getElementById("outcomeTitle"),
    outcomeDescription: document.getElementById("outcomeDescription"),
    restartMatchBtn: document.getElementById("restartMatchBtn")
  };

  function uuid() {
    if (crypto && crypto.randomUUID) return crypto.randomUUID();
    return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, c => {
      const r = (Math.random() * 16) | 0;
      return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
    });
  }

  async function api(method, path, body = null) {
    const opts = {
      method,
      headers: { "Content-Type": "application/json" }
    };
    if (body !== null) {
      opts.body = JSON.stringify(body);
    }
    const res = await fetch(`${API_BASE}${path}`, opts);
    const text = await res.text();
    const data = text ? JSON.parse(text) : null;
    if (!res.ok) {
      const detail = (data && (data.detail || data.title)) || `HTTP ${res.status}`;
      throw new Error(detail);
    }
    return data;
  }

  function toCombatPayload(cmd) {
    const allowed = [
      "actorId",
      "actionType",
      "powerId",
      "targetId",
      "costOptionId",
      "cardInstanceId",
      "targetIds"
    ];
    const clean = {};
    for (const k of allowed) {
      if (cmd[k] !== undefined && cmd[k] !== null) {
        clean[k] = cmd[k];
      }
    }
    return clean;
  }

  function appendLog(title, detail) {
    const item = document.createElement("div");
    item.className = "log-entry";
    item.innerHTML = `<strong>${title}</strong><div>${detail}</div>`;
    el.engineEventLog.prepend(item);
    while (el.engineEventLog.children.length > 40) {
      el.engineEventLog.removeChild(el.engineEventLog.lastChild);
    }
  }

  function setBannerMessage(msg, flash = true) {
    el.aiActionText.textContent = msg;
    if (flash) {
      el.aiActionBanner.classList.remove("flash");
      void el.aiActionBanner.offsetWidth;
      el.aiActionBanner.classList.add("flash");
      setTimeout(() => el.aiActionBanner.classList.remove("flash"), 520);
    }
  }

  // =========================================================================
  // Visual FX Helpers (Flying Cards, Shockwaves & Callout Splashes)
  // =========================================================================
  function showCalloutSplash(text) {
    if (!el.calloutSplash || !el.calloutSplashInner) return;
    clearTimeout(state.splashTimer);
    el.calloutSplashInner.textContent = text;
    el.calloutSplash.classList.remove("hidden");
    el.calloutSplashInner.style.animation = "none";
    void el.calloutSplashInner.offsetWidth;
    el.calloutSplashInner.style.animation = "";
    state.splashTimer = setTimeout(() => {
      el.calloutSplash.classList.add("hidden");
    }, 920);
  }

  function spawnShockwave(targetEl) {
    if (!targetEl || !el.fxLayer) return;
    const rect = targetEl.getBoundingClientRect();
    const ring = document.createElement("div");
    ring.className = "shockwave-ring";
    ring.style.left = `${rect.left + rect.width / 2}px`;
    ring.style.top = `${rect.top + rect.height / 2}px`;
    el.fxLayer.appendChild(ring);
    setTimeout(() => ring.remove(), 680);
  }

  function clearTableFlight() {
    state.tableFlightGeneration++;
    if (state.pendingTableFlight) {
      state.pendingTableFlight.remove();
      state.pendingTableFlight = null;
    }
  }

  function animateCardFlight(fromRect, toEl, cardSpec = null, durationMs = 360) {
    return new Promise(resolve => {
      if (!fromRect || !toEl || !el.fxLayer) {
        resolve();
        return;
      }
      const landsOnTable = Boolean(cardSpec && toEl === el.discardStage);
      const generation = state.tableFlightGeneration;
      const tableCard = landsOnTable && toEl.querySelector(".table-card");
      const toRect = (tableCard || toEl).getBoundingClientRect();
      let flyEl;
      if (cardSpec) {
        flyEl = createCardElement({
          color: cardSpec.color,
          corner: cardSpec.corner,
          center: cardSpec.center,
          isTextGlyph: cardSpec.isTextGlyph,
          extraClass: "flying-card"
        });
      } else {
        flyEl = document.createElement("div");
        flyEl.className = "uno-card-back flying-card";
        flyEl.style.width = `${fromRect.width || 96}px`;
        flyEl.style.height = `${fromRect.height || 140}px`;
        flyEl.innerHTML = `<div class="back-oval"><span>UNO</span></div>`;
      }

      if (landsOnTable) {
        flyEl.style.width = `${toRect.width}px`;
        flyEl.style.height = `${toRect.height}px`;
      }
      el.fxLayer.appendChild(flyEl);
      const flightRect = flyEl.getBoundingClientRect();
      const width = flightRect.width || fromRect.width || 96;
      const height = flightRect.height || fromRect.height || 140;
      const startX = fromRect.left + fromRect.width / 2 - width / 2;
      const startY = fromRect.top + fromRect.height / 2 - height / 2;
      const endX = toRect.left + toRect.width / 2 - width / 2;
      const endY = toRect.top + toRect.height / 2 - height / 2;
      const endScaleX = landsOnTable ? toRect.width / width : 1.02;
      const endScaleY = landsOnTable ? toRect.height / height : 1.02;
      const startScaleX = landsOnTable ? fromRect.width / width : 0.95;
      const startScaleY = landsOnTable ? fromRect.height / height : 0.95;
      const midX = (startX + endX) / 2;
      const midY = Math.min(startY, endY) - 48;
      const finalTransform = `translate(${endX}px, ${endY}px) scale(${endScaleX}, ${endScaleY}) rotate(0deg)`;

      const finish = () => {
        if (landsOnTable && generation === state.tableFlightGeneration) {
          // Keep the landed card covering the old table until the new snapshot is rendered.
          if (state.pendingTableFlight) state.pendingTableFlight.remove();
          state.pendingTableFlight = flyEl;
        } else {
          flyEl.remove();
        }
        resolve();
      };
      if (flyEl.animate) {
        const anim = flyEl.animate(
          [
            { transform: `translate(${startX}px, ${startY}px) scale(${startScaleX}, ${startScaleY}) rotate(-8deg)`, opacity: 1 },
            { transform: `translate(${midX}px, ${midY}px) scale(1.12) rotate(6deg)`, opacity: 1, offset: 0.55 },
            { transform: finalTransform, opacity: 1 }
          ],
          {
            duration: durationMs,
            easing: "cubic-bezier(0.22, 1, 0.36, 1)",
            fill: "forwards"
          }
        );
        anim.onfinish = finish;
        anim.oncancel = () => { flyEl.remove(); resolve(); };
      } else {
        flyEl.style.transform = finalTransform;
        finish();
      }
    });
  }

  function startAutoTurnProgress(durationMs) {
    if (!el.endTurnProgress) return;
    el.endTurnProgress.style.transition = "none";
    el.endTurnProgress.style.width = "0%";
    void el.endTurnProgress.offsetWidth;
    el.endTurnProgress.style.transition = `width ${durationMs}ms linear`;
    el.endTurnProgress.style.width = "100%";
  }

  function resetAutoTurnProgress() {
    if (!el.endTurnProgress) return;
    el.endTurnProgress.style.transition = "none";
    el.endTurnProgress.style.width = "0%";
  }

  function cancelAutoTurn() {
    clearTimeout(state.autoTurnTimer);
    state.autoTurnTimer = null;
    resetAutoTurnProgress();
  }

  function updateAutoTurn() {
    cancelAutoTurn();
    if (!el.autoEndTurnToggle.checked || !state.canEndTurn || !state.autoEndTurnEligible) return;
    const combatId = state.combatId;
    const step = state.combatStep;
    startAutoTurnProgress(state.autoEndTurnDelay);
    state.autoTurnTimer = setTimeout(() => {
      state.autoTurnTimer = null;
      if (!state.busy && el.autoEndTurnToggle.checked && state.canEndTurn &&
          state.autoEndTurnEligible && state.combatId === combatId && state.combatStep === step) {
        handleEndTurn();
      }
    }, state.autoEndTurnDelay);
  }

  function suspendPlayerControls() {
    cancelAutoTurn();
    state.canEndTurn = false;
    state.autoEndTurnEligible = false;
    el.drawCardBtn.disabled = true;
    el.drawCardBtn.classList.remove("must-draw-pulse");
    el.endTurnBtn.disabled = true;
    el.endTurnBtn.classList.remove("ready-glow");
    el.shoutUnoBtn.disabled = true;
    el.shoutUnoBtn.classList.remove("urgent");
    if (el.drawPileHint) el.drawPileHint.textContent = "Aguarde a jogada terminar";
  }

  // Parse card definition ID (`uno_red_7`, `uno_blue_skip`, `uno_wild_draw4`) into visual presentation
  function parseCardIdentity(definitionId) {
    if (definitionId === "uno_wild") {
      return { color: "wild", corner: "★", center: "UNO", label: "Coringa", isTextGlyph: true, specialSplash: "🌈 CORINGA!" };
    }
    if (definitionId === "uno_wild_draw4") {
      return { color: "wild", corner: "+4", center: "+4", label: "Coringa +4", isTextGlyph: true, specialSplash: "🔥 CORINGA +4!" };
    }
    const parts = definitionId.replace(/^uno_/, "").split("_");
    const color = parts[0] || "red";
    const val = parts.slice(1).join("_");

    if (val === "skip") {
      return { color, corner: "⊘", center: "⊘", label: `Bloqueio ${COLOR_LABELS[color]}`, isTextGlyph: false, specialSplash: "⊘ BLOQUEIO!" };
    }
    if (val === "reverse") {
      return { color, corner: "⇄", center: "⇄", label: `Inverter ${COLOR_LABELS[color]}`, isTextGlyph: false, specialSplash: "⇄ INVERTER!", isReverse: true };
    }
    if (val === "draw2") {
      return { color, corner: "+2", center: "+2", label: `+2 ${COLOR_LABELS[color]}`, isTextGlyph: true, specialSplash: "💥 COMPRA +2!" };
    }
    return { color, corner: val, center: val, label: `${val} ${COLOR_LABELS[color]}`, isTextGlyph: false, specialSplash: null };
  }

  // Decode table_card_code (100..414) + active color resources
  function decodeTableCard(code, activeColorKey) {
    const valCode = code % 100;
    const color = activeColorKey || "red";
    if (valCode === 10) {
      return { color, corner: "⊘", center: "⊘", valueName: "Bloqueio (⊘)", isTextGlyph: false };
    }
    if (valCode === 11) {
      return { color, corner: "⇄", center: "⇄", valueName: "Inverter (⇄)", isTextGlyph: false };
    }
    if (valCode === 12) {
      return { color, corner: "+2", center: "+2", valueName: "Compra +2", isTextGlyph: true };
    }
    if (valCode === 13) {
      return { color, corner: "★", center: "★", valueName: "Coringa", isTextGlyph: false };
    }
    if (valCode === 14) {
      return { color, corner: "+4", center: "+4", valueName: "Coringa +4", isTextGlyph: true };
    }
    const numStr = String(valCode);
    return { color, corner: numStr, center: numStr, valueName: `Número ${numStr}`, isTextGlyph: false };
  }

  function createCardElement({ color, corner, center, isTextGlyph, extraClass = "" }) {
    const card = document.createElement("div");
    card.className = `uno-card ${extraClass}`.trim();
    card.dataset.color = color;
    card.innerHTML = `
      <span class="card-corner top-left">${corner}</span>
      <span class="card-center-glyph ${isTextGlyph ? "glyph-text" : ""}">${center}</span>
      <span class="card-corner bottom-right">${corner}</span>
    `;
    return card;
  }

  function renderBotHandFan() {
    const count = state.botHandCount;
    el.aiHandCountPill.textContent = `${count} ${count === 1 ? "carta (UNO!)" : "cartas"}`;
    const fanCount = Math.max(0, Math.min(count, 14));
    while (el.aiHandFan.children.length > fanCount) el.aiHandFan.lastElementChild.remove();
    while (el.aiHandFan.children.length < fanCount) {
      const mini = document.createElement("div");
      mini.className = "ai-mini-card";
      mini.innerHTML = `<div class="ai-mini-oval">UNO</div>`;
      el.aiHandFan.appendChild(mini);
    }
    const cardWidth = el.aiHandFan.firstElementChild?.offsetWidth || 0;
    const availableWidth = Math.max(0, el.aiHandFan.clientWidth - 32);
    const overlap = fanCount > 1
      ? Math.min(-12, (availableWidth - cardWidth * fanCount) / (fanCount - 1)) : 0;
    el.aiHandFan.style.setProperty("--bot-card-overlap", `${overlap}px`);
    [...el.aiHandFan.children].forEach((mini, i) => {
      const norm = fanCount > 1 ? (i - (fanCount - 1) / 2) / ((fanCount - 1) / 2) : 0;
      mini.style.transform = `translateY(${norm * norm * 8}px) rotate(${norm * Math.min(fanCount * 1.45, 11)}deg)`;
    });
  }

  function renderBotHandTelemetry() {
    const visible = el.showBotCardsToggle.checked;
    el.telemetryBotCards.classList.toggle("hidden", !visible);
    el.telemetryBotCards.replaceChildren();
    if (!visible) {
      el.telemetryBotCardsStatus.textContent = "Ative para inspecionar a mão do bot.";
      return;
    }
    if (!state.combatId) {
      el.telemetryBotCardsStatus.textContent = "Aguardando o início da partida.";
      return;
    }
    if (state.botHandCards.length !== state.botHandCount) {
      el.telemetryBotCardsStatus.textContent = "Cartas indisponíveis nesta revisão. Reabra pelo BAT e inicie uma nova partida.";
      return;
    }
    el.telemetryBotCardsStatus.textContent = `${state.botHandCount} ${state.botHandCount === 1 ? "carta na mão do bot" : "cartas na mão do bot"}`;
    for (const instance of state.botHandCards) {
      const parsed = parseCardIdentity(instance.definitionId);
      const item = document.createElement("figure");
      item.className = "telemetry-bot-card";
      item.setAttribute("role", "listitem");
      item.dataset.cardInstanceId = instance.cardInstanceId;
      const card = createCardElement(parsed);
      card.setAttribute("aria-label", parsed.label);
      const caption = document.createElement("figcaption");
      caption.textContent = parsed.label;
      item.append(card, caption);
      el.telemetryBotCards.appendChild(item);
    }
  }

  function renderPlayerHand(cards, wildToken) {
    const container = el.playerHandContainer;
    const context = `${state.runId}:${state.combatId}`;
    if (container.dataset.handContext !== context) {
      container.replaceChildren();
      container.dataset.handContext = context;
    }
    const previous = new Map([...container.children].map(card => [card.dataset.cardInstanceId, card]));
    const positions = new Map([...previous.values()].map(card => [card, card.getBoundingClientRect().left]));
    for (const card of previous.values()) {
      for (const animation of card.getAnimations?.() || []) {
        if (animation.id === "hand-reflow") animation.cancel();
      }
    }
    const wanted = new Set(cards.map(card => card.presentation.identity.cardInstanceId));
    for (const [id, card] of previous) if (!wanted.has(id)) card.remove();

    const total = cards.length;
    const overlap = total <= 5 ? -10 : Math.max(-52, Math.round(-12 - (total - 5) * 4.2));
    container.style.setProperty("--card-overlap", `${overlap}px`);
    let entranceIndex = 0;
    cards.forEach((evaluation, index) => {
      const { definitionId, cardInstanceId } = evaluation.presentation.identity;
      const parsed = parseCardIdentity(definitionId);
      let card = previous.get(cardInstanceId);
      if (card && card.dataset.definitionId !== definitionId) {
        card.remove();
        card = null;
      }
      if (!card) {
        card = createCardElement({ ...parsed, extraClass: "hand-card-entering" });
        card.dataset.cardInstanceId = cardInstanceId;
        card.dataset.definitionId = definitionId;
        card.style.animationDelay = `${entranceIndex++ * 25}ms`;
        card.addEventListener("animationend", () => card.classList.remove("hand-card-entering"), { once: true });
        // Bind once, including currently illegal cards; eligibility is checked at interaction time.
        attachCardDragAndClick(card, cardInstanceId, parsed);
      }
      const legal = state.legalByCardInstance.has(cardInstanceId) && wildToken === 0;
      card.classList.toggle("is-legal", legal);
      card.classList.toggle("is-illegal", !legal);
      card.setAttribute("aria-label", parsed.label);
      card.setAttribute("aria-disabled", String(!legal));
      const reasons = evaluation.evaluation?.failureReasons || [];
      card.title = !legal && reasons.length ? `${parsed.label}: ${reasons.join("; ")}` : parsed.label;
      card.style.opacity = "";
      const norm = total > 1 ? (index - (total - 1) / 2) / ((total - 1) / 2) : 0;
      card.style.setProperty("--arc-rot", `${(norm * Math.min(total * 1.45, 11)).toFixed(2)}deg`);
      card.style.setProperty("--arc-y", `${(norm * norm * Math.min(total * 1.8, 16)).toFixed(1)}px`);
      card.style.zIndex = String(index + 1);
      if (container.children[index] !== card) container.insertBefore(card, container.children[index] || null);
    });
    // Move surviving cards into their new fan positions without replaying their deal animation.
    for (const card of container.children) {
      if (!positions.has(card) || !card.animate) continue;
      const offset = positions.get(card) - card.getBoundingClientRect().left;
      if (Math.abs(offset) < 0.5) continue;
      const animation = card.animate([{ translate: `${offset}px 0px` }, { translate: "0px 0px" }], {
        duration: 220, easing: "cubic-bezier(0.22, 1, 0.36, 1)"
      });
      animation.id = "hand-reflow";
    }
  }

  async function initSettingAndStartRun() {
    try {
      suspendPlayerControls();
      clearTimeout(state.autoTurnTimer);
      resetAutoTurnProgress();
      state.busy = true;
      clearTableFlight();
      state.discardHistory = [];
      state.lastTableCode = null;
      el.outcomeModal.classList.add("hidden");
      el.wildModal.classList.add("hidden");

      const settingsResp = await api("GET", "/api/v1/content/settings");
      const unoSetting = (settingsResp.items || []).find(s => s.settingId === "uno-clash");
      if (!unoSetting) {
        throw new Error("Pacote 'uno-clash' não encontrado no HeroScript.");
      }

      state.settingRevision = unoSetting.currentRevision;
      el.apiStatusDot.className = "status-dot connected";
      el.revisionBadge.textContent = `rev:${unoSetting.currentRevision.slice(0, 8)}`;

      const seedVal = parseInt(el.seedInput.value, 10) || Math.floor(Math.random() * 900000 + 100000);
      el.seedInput.value = seedVal;

      const runResp = await api("POST", "/api/v1/runs", {
        commandId: uuid(),
        settingId: "uno-clash",
        contentRevision: state.settingRevision,
        runDefinitionId: "uno_clash_run",
        playerEntityId: "player",
        modeId: "uno_classic",
        seed: seedVal
      });

      state.runId = runResp.runId;
      appendLog("POST /api/v1/runs", `Run criada (seed <code>${seedVal}</code>)`);

      state.busy = false;
      await startCurrentNodeEncounter();
    } catch (err) {
      el.apiStatusDot.className = "status-dot error";
      el.revisionBadge.textContent = "offline";
      setBannerMessage(`Erro ao conectar ao HeroScript (${API_BASE}): ${err.message}`, true);
    } finally {
      state.busy = false;
    }
  }

  async function startCurrentNodeEncounter() {
    state.busy = true;
    suspendPlayerControls();
    clearTableFlight();
    try {
      const avail = await api("GET", `/api/v1/runs/${state.runId}/available-commands`);
      const startCmd = (avail.commands || []).find(c => c.type === "START_ENCOUNTER");
      if (!startCmd) {
        throw new Error("Comando START_ENCOUNTER não disponível para o nó atual.");
      }

      const encResp = await api("POST", `/api/v1/runs/${state.runId}/commands`, {
        commandId: uuid(),
        type: "START_ENCOUNTER",
        expectedSequence: startCmd.expectedSequence,
        expectedStep: startCmd.expectedStep,
        payload: startCmd.validPayload
      });

      state.combatId = encResp.state.activeEncounterId;
      state.runSequence = encResp.sequence;
      state.discardHistory = [];
      state.lastTableCode = null;

      const [currentRun, currentCombat, legal] = await Promise.all([
        api("GET", `/api/v1/runs/${state.runId}`),
        api("GET", `/api/v1/combats/${state.combatId}`),
        api("GET", `/api/v1/combats/${state.combatId}/legal-actions`)
      ]);
      const setup = legal.candidates.find(candidate => candidate.actionId === "uno_setup_table");
      if (!setup) throw new Error("Distribuição inicial de cartas indisponível. Inicie uma nova partida com as regras atuais.");
      await api("POST", `/api/v1/combats/${state.combatId}/commands`, {
        commandId: uuid(), type: "EXECUTE_ACTION", expectedSequence: currentRun.sequence,
        expectedStep: currentCombat.step, payload: toCombatPayload(setup.command)
      });

      appendLog("START_ENCOUNTER", "Partida iniciada contra o bot.");
      setBannerMessage("Mesa aberta! Combine a cor ou o número/símbolo da carta central.", true);

      await refreshArenaState();
    } finally {
      state.busy = false;
    }
  }

  async function refreshArenaState() {
    if (!state.runId || !state.combatId) return;
    cancelAutoTurn();

    const [runSnap, combatSnap, legalSnap, evalsSnap, zoneSnap] = await Promise.all([
      api("GET", `/api/v1/runs/${state.runId}`),
      api("GET", `/api/v1/combats/${state.combatId}`),
      api("GET", `/api/v1/combats/${state.combatId}/legal-actions`),
      api("GET", `/api/v1/combats/${state.combatId}/cards/evaluations?targetIds=dealer`),
      api("GET", `/api/v1/runs/${state.runId}/card-zones`)
    ]);

    state.runSequence = runSnap.sequence;
    state.combatStep = combatSnap.step;
    // Map legal candidates
    state.legalByCardInstance.clear();
    state.legalByActionId.clear();

    for (const cand of legalSnap.candidates || []) {
      if (cand.source === "Card" && cand.command && cand.command.cardInstanceId) {
        state.legalByCardInstance.set(cand.command.cardInstanceId, cand);
      } else if (cand.actionId) {
        state.legalByActionId.set(cand.actionId, cand);
      }
    }

    const playerActor = combatSnap.actors.find(a => a.instanceId === "player");
    const dealerActor = combatSnap.actors.find(a => a.instanceId === "dealer");
    const pRes = (playerActor && playerActor.resources) || {};
    const dRes = (dealerActor && dealerActor.resources) || {};

    const getRes = (bag, key, fallback = 0) => (bag[key] ? bag[key].current : fallback);

    const zoneCount = (zoneId, ownerId) => zoneSnap.zones.find(zone => zone.zoneId === zoneId && zone.ownerId === ownerId)?.count || 0;
    const playerHandCount = zoneCount("hand", "player");
    const aiHandCount = zoneCount("hand", "dealer");
    state.botHandCards = zoneSnap.zones.find(zone => zone.zoneId === "hand" && zone.ownerId === "dealer")?.cards || [];
    state.botHandCount = aiHandCount;
    renderBotHandTelemetry();
    const tableCode = getRes(pRes, "table_card_code", 100);
    const unoEnergy = getRes(pRes, "uno_energy", 0);
    const unoCalled = getRes(pRes, "uno_called", 0);
    const drawAvailable = getRes(pRes, "uno_draw_available", 1);
    const pendingDraw2 = getRes(pRes, "uno_draw2_pending", 0);
    state.pendingDraw2 = pendingDraw2;
    state.opponentPendingDraw2 = getRes(dRes, "uno_draw2_pending", 0);
    const wildToken = getRes(pRes, "wild_color_token", 0);

    // Determine active table color
    let activeColor = "red";
    if (getRes(pRes, "color_blue") > 0) activeColor = "blue";
    else if (getRes(pRes, "color_green") > 0) activeColor = "green";
    else if (getRes(pRes, "color_yellow") > 0) activeColor = "yellow";
    else if (getRes(pRes, "color_red") > 0) activeColor = "red";

    document.body.dataset.activeColor = activeColor;

    // Record table discard history when the table card changes
    const tableChanged = state.lastTableCode !== null && (state.lastTableCode !== tableCode || state.lastActiveColor !== activeColor);
    if (state.lastTableCode !== null && state.lastTableCode !== tableCode) {
      state.discardHistory.push({
        code: state.lastTableCode,
        color: state.lastActiveColor || activeColor
      });
      if (state.discardHistory.length > 3) {
        state.discardHistory.shift();
      }
    }
    state.lastTableCode = tableCode;
    state.lastActiveColor = activeColor;

    // Update the floating opponent hand.
    renderBotHandFan();

    // Render Center Discard Stage (under-stack history + active top card)
    const decodedTable = decodeTableCard(tableCode, activeColor);
    el.discardStage.innerHTML = "";
    const historyAngles = [-14, 11, -6];
    const historyOffsets = [
      [-6, 4],
      [6, -3],
      [-3, -4]
    ];
    state.discardHistory.forEach((hItem, idx) => {
      const hDecoded = decodeTableCard(hItem.code, hItem.color);
      const hEl = createCardElement({
        color: hDecoded.color,
        corner: hDecoded.corner,
        center: hDecoded.center,
        isTextGlyph: hDecoded.isTextGlyph,
        extraClass: "discard-history-card"
      });
      const angle = historyAngles[idx % historyAngles.length];
      const [ox, oy] = historyOffsets[idx % historyOffsets.length];
      hEl.style.transform = `translate(${ox}px, ${oy}px) rotate(${angle}deg)`;
      el.discardStage.appendChild(hEl);
    });

    const tableCardEl = createCardElement({
      color: decodedTable.color,
      corner: decodedTable.corner,
      center: decodedTable.center,
      isTextGlyph: decodedTable.isTextGlyph,
      extraClass: `table-card ${tableChanged && !state.pendingTableFlight ? "pop-land" : ""}`.trim()
    });
    el.discardStage.appendChild(tableCardEl);
    clearTableFlight();

    el.activeColorLabel.textContent = COLOR_LABELS[activeColor];
    el.activeValueLabel.textContent = decodedTable.valueName;

    // Actual physical cards left in the shared draw pile.
    const drawZoneCount = zoneCount("draw", "$run");
    el.drawPileCount.textContent = `${drawZoneCount} cartas`;

    // Draw button & Shout UNO button states
    const legalCardsCount = state.legalByCardInstance.size;
    const canDraw = state.legalByActionId.has(pendingDraw2 > 0 ? "uno_accept_draw2" : "uno_draw_card") && wildToken === 0;
    el.drawCardBtn.disabled = !canDraw;
    el.drawCardBtn.classList.toggle("must-draw-pulse", canDraw && legalCardsCount === 0 && unoEnergy > 0);
    if (el.drawPileHint) {
      el.drawPileHint.textContent =
        pendingDraw2 > 0 ? `+${pendingDraw2} acumulado: responda com +2 ou compre` :
        canDraw && legalCardsCount === 0 && unoEnergy > 0
          ? "⚡ Clique ou arraste para comprar!"
          : drawAvailable === 0 ? "Compra usada: jogue ou passe" : "Clique ou arraste na sua vez";
    }

    const canShoutUno = state.legalByActionId.has("uno_shout_uno") && unoCalled === 0;
    el.shoutUnoBtn.disabled = !canShoutUno;
    el.shoutUnoBtn.classList.toggle("urgent", canShoutUno && playerHandCount <= 2);

    // End Turn button
    const canEndTurn = legalSnap.candidates.some(c => c.command && c.command.actionType === "END_TURN");
    state.canEndTurn = canEndTurn;
    el.endTurnBtn.disabled = !canEndTurn;
    const blockedEnd = (legalSnap.blockedCommands || []).find(c => c.actionType === "END_TURN");
    const blockedMessages = { "uno.draw_required": "Sem carta jogável: compre 1 carta antes de passar.", "uno.color_required": "Escolha a cor do coringa." };
    el.endTurnBtn.title = blockedEnd ? (blockedMessages[blockedEnd.messageKey] || blockedEnd.reason || blockedEnd.code) : "";
    el.endTurnBtn.classList.toggle("ready-glow", unoEnergy === 0 && wildToken === 0);

    if (el.turnOwnerBadge) {
      el.turnOwnerBadge.textContent = unoEnergy > 0 ? "SUA VEZ" : "FIM DO TURNO";
    }

    renderPlayerHand(evalsSnap.cards || [], wildToken);

    // Update Telemetry Inspector
    el.inspRunId.textContent = state.runId;
    el.inspCombatId.textContent = state.combatId;
    el.inspSeqStep.textContent = `seq ${state.runSequence} / step ${state.combatStep}`;
    el.inspStateHash.textContent = combatSnap.stateHash || "-";

    const trackedRes = [
      ["player.hand", playerHandCount],
      ["dealer.hand", aiHandCount],
      ["table_card_code", tableCode],
      ["uno_energy", unoEnergy],
      ["uno_called", unoCalled],
      ["wild_color_token", wildToken],
      ["color_red", getRes(pRes, "color_red")],
      ["color_blue", getRes(pRes, "color_blue")],
      ["color_green", getRes(pRes, "color_green")],
      ["color_yellow", getRes(pRes, "color_yellow")]
    ];
    el.inspResourceGrid.innerHTML = trackedRes
      .map(([k, v]) => `<div class="res-chip"><span>${k}</span><strong>${v}</strong></div>`)
      .join("");

    // Check Combat Outcome (VICTORY / DEFEAT)
    if (combatSnap.status && combatSnap.status !== "ACTIVE" && combatSnap.status !== "InProgress") {
      resetAutoTurnProgress();
      showOutcomeModal(combatSnap.status);
      return;
    }

    // Check if Radial Wild Color Picker should open
    if (wildToken > 0) {
      state.canEndTurn = false;
      state.autoEndTurnEligible = false;
      cancelAutoTurn();
      resetRadialHubPreview();
      el.wildModal.classList.remove("hidden");
      return;
    } else {
      el.wildModal.classList.add("hidden");
    }

    // Buying keeps the play available. Only pass automatically if it cannot be used.
    state.autoEndTurnEligible = unoEnergy === 0 || (pendingDraw2 > 0 ? legalCardsCount === 0 : drawAvailable === 0 && legalCardsCount === 0);
    state.autoEndTurnDelay = playerHandCount === 1 && unoCalled === 0 && canShoutUno ? 1650 : 680;
    updateAutoTurn();
  }

  // =========================================================================
  // Fluid Pointer-Events Drag-and-Drop Engine (Hand Cards & Draw Pile)
  // =========================================================================
  function isPointOverDiscardDropZone(clientX, clientY, startY) {
    if (!el.discardStage) return false;
    const rect = el.discardStage.getBoundingClientRect();
    const cx = rect.left + rect.width / 2;
    const cy = rect.top + rect.height / 2;
    const dist = Math.hypot(clientX - cx, clientY - cy);
    // Generous magnetic radius around center table OR upward flick >= 85px from hand
    return dist <= Math.max(rect.width * 1.45, 165) || startY - clientY >= 85;
  }

  function attachCardDragAndClick(cardEl, instId, parsed) {
    let pointerId = null;
    let startX = 0;
    let startY = 0;
    let lastX = 0;
    let offsetX = 0;
    let offsetY = 0;
    let baseRect = null;
    let isDragging = false;
    let proxyEl = null;

    function cleanupDragVisuals() {
      document.body.classList.remove("is-dragging-card");
      cardEl.classList.remove("is-being-dragged");
      if (el.discardStage) {
        el.discardStage.classList.remove("drop-target-ready", "drop-target-hot");
      }
    }

    function onPointerMove(e) {
      if (e.pointerId !== pointerId) return;
      const dx = e.clientX - startX;
      const dy = e.clientY - startY;

      if (!isDragging) {
        if (Math.hypot(dx, dy) < 7) return;
        isDragging = true;
        document.body.classList.add("is-dragging-card");
        cardEl.classList.add("is-being-dragged");
        if (el.discardStage) {
          el.discardStage.classList.add("drop-target-ready");
        }

        proxyEl = createCardElement({
          color: parsed.color,
          corner: parsed.corner,
          center: parsed.center,
          isTextGlyph: parsed.isTextGlyph,
          extraClass: "dragging-card-proxy"
        });
        proxyEl.style.width = `${baseRect.width}px`;
        proxyEl.style.height = `${baseRect.height}px`;
        el.fxLayer.appendChild(proxyEl);
      }

      if (proxyEl) {
        const x = e.clientX - offsetX;
        const y = e.clientY - offsetY;
        const vx = Math.max(-20, Math.min(20, (e.clientX - lastX) * 1.25));
        lastX = e.clientX;
        const hot = isPointOverDiscardDropZone(e.clientX, e.clientY, startY);
        const scale = hot ? 1.14 : 1.07;
        proxyEl.style.transform = `translate3d(${x}px, ${y}px, 0) rotate(${vx.toFixed(1)}deg) scale(${scale})`;
        proxyEl.classList.toggle("over-drop-zone", hot);
        if (el.discardStage) {
          el.discardStage.classList.toggle("drop-target-hot", hot);
        }
      }
    }

    function onPointerUpOrCancel(e) {
      if (e.pointerId !== pointerId) return;
      window.removeEventListener("pointermove", onPointerMove);
      window.removeEventListener("pointerup", onPointerUpOrCancel);
      window.removeEventListener("pointercancel", onPointerUpOrCancel);

      const wasCancel = e.type === "pointercancel";
      pointerId = null;

      if (!isDragging) {
        if (!wasCancel) {
          handlePlayCard(instId, parsed, cardEl);
        }
        return;
      }

      const hot = !wasCancel && isPointOverDiscardDropZone(e.clientX, e.clientY, startY);
      if (hot && proxyEl) {
        const dropRect = proxyEl.getBoundingClientRect();
        proxyEl.remove();
        proxyEl = null;
        cleanupDragVisuals();
        handlePlayCard(instId, parsed, cardEl, dropRect);
      } else if (proxyEl) {
        const returnEl = proxyEl;
        proxyEl = null;
        if (el.discardStage) {
          el.discardStage.classList.remove("drop-target-ready", "drop-target-hot");
        }
        document.body.classList.remove("is-dragging-card");
        const targetRect = cardEl.getBoundingClientRect();
        if (returnEl.animate) {
          const anim = returnEl.animate(
            [
              { transform: returnEl.style.transform },
              { transform: `translate3d(${targetRect.left}px, ${targetRect.top}px, 0) rotate(0deg) scale(1)` }
            ],
            { duration: 210, easing: "cubic-bezier(0.22, 1, 0.36, 1)", fill: "forwards" }
          );
          anim.onfinish = () => {
            returnEl.remove();
            cardEl.classList.remove("is-being-dragged");
          };
        } else {
          returnEl.remove();
          cardEl.classList.remove("is-being-dragged");
        }
      } else {
        cleanupDragVisuals();
      }
    }

    cardEl.addEventListener("pointerdown", e => {
      if (state.busy || e.button !== 0 || !cardEl.classList.contains("is-legal")) return;
      e.preventDefault();
      pointerId = e.pointerId;
      startX = e.clientX;
      startY = e.clientY;
      lastX = e.clientX;
      baseRect = cardEl.getBoundingClientRect();
      offsetX = e.clientX - baseRect.left;
      offsetY = e.clientY - baseRect.top;
      isDragging = false;

      window.addEventListener("pointermove", onPointerMove);
      window.addEventListener("pointerup", onPointerUpOrCancel);
      window.addEventListener("pointercancel", onPointerUpOrCancel);
    });
  }

  async function handlePlayCard(cardInstanceId, parsedCard, sourceCardEl, customFromRect = null) {
    if (state.busy) return;
    const cand = state.legalByCardInstance.get(cardInstanceId);
    if (!cand) return;

    try {
      suspendPlayerControls();
      state.busy = true;
      setBannerMessage(`Você jogou ${parsedCard.label}!`, false);

      const sourceRect = customFromRect || (sourceCardEl ? sourceCardEl.getBoundingClientRect() : null);
      const flightMs = customFromRect ? 185 : 340;
      if (sourceCardEl) {
        sourceCardEl.style.opacity = "0.2";
      }

      const [resp] = await Promise.all([
        api("POST", `/api/v1/combats/${state.combatId}/commands`, {
          commandId: uuid(),
          type: "PLAY_CARD",
          expectedSequence: state.runSequence,
          expectedStep: state.combatStep,
          payload: toCombatPayload(cand.command)
        }),
        animateCardFlight(sourceRect, el.discardStage, parsedCard, flightMs)
      ]);

      spawnShockwave(el.discardStage);
      if (parsedCard.isReverse && el.colorOrbitRing) {
        state.orbitReversed = !state.orbitReversed;
        el.colorOrbitRing.classList.toggle("reversed", state.orbitReversed);
      }
      if (parsedCard.specialSplash) {
        showCalloutSplash(parsedCard.specialSplash);
      }

      appendLog("PLAY_CARD", `Jogou <code>${cand.cardDefinitionId}</code> (step ${resp.step})`);
      await refreshArenaState();
    } catch (err) {
      clearTableFlight();
      if (sourceCardEl) sourceCardEl.style.opacity = "";
      setBannerMessage(`Jogada recusada pela engine: ${err.message}`, true);
    } finally {
      state.busy = false;
    }
  }

  async function handleExecuteAction(actionId, logLabel, customFromRect = null) {
    if (state.busy) return;
    if (actionId === "uno_draw_card" && state.pendingDraw2 > 0) actionId = "uno_accept_draw2";
    const cand = state.legalByActionId.get(actionId);
    if (!cand) return;

    try {
      suspendPlayerControls();
      state.busy = true;

      const isDraw = actionId === "uno_draw_card" || actionId === "uno_accept_draw2";
      const drawRect = customFromRect || (isDraw && el.drawPileAnchor ? el.drawPileAnchor.getBoundingClientRect() : null);
      const flightMs = customFromRect ? 190 : 350;

      const resp = await api("POST", `/api/v1/combats/${state.combatId}/commands`, {
        commandId: uuid(), type: "EXECUTE_ACTION", expectedSequence: state.runSequence,
        expectedStep: state.combatStep, payload: toCombatPayload(cand.command)
      });
      const actualDrawCount = (resp.state?.resolution?.frames || [])
        .flatMap(frame => movedCardsToHands(frame.payload || {}))
        .filter(draw => draw.actorId === "player").reduce((total, draw) => total + draw.count, 0);
      if (isDraw && actualDrawCount > 0) await animateCardFlight(drawRect, el.playerHandContainer, null, flightMs);

      appendLog("EXECUTE_ACTION", `${logLabel} (<code>${actionId}</code>)`);
      if (actionId === "uno_shout_uno") {
        showCalloutSplash("📣 UNO!");
        setBannerMessage("📣 Você gritou UNO! Sua última carta está protegida!", true);
      } else if (actionId === "uno_accept_draw2") {
        showCalloutSplash(`📥 COMPRA +${actualDrawCount}!`);
        setBannerMessage(`Você comprou ${actualDrawCount} cartas e perdeu a vez.`, true);
      } else if (actionId === "uno_draw_card") {
        setBannerMessage(actualDrawCount ? "📥 Você comprou 1 carta. Jogue uma carta compatível ou passe a vez." : "O monte não tem cartas disponíveis. Passe a vez.", false);
      } else if (actionId.startsWith("uno_choose_")) {
        const cKey = actionId.replace("uno_choose_", "");
        spawnShockwave(el.discardStage);
        setBannerMessage(`🌈 Você escolheu a cor ${COLOR_LABELS[cKey]}!`, true);
      }

      await refreshArenaState();
    } catch (err) {
      setBannerMessage(`Ação recusada pela engine: ${err.message}`, true);
    } finally {
      state.busy = false;
    }
    if (actionId === "uno_accept_draw2" && state.pendingDraw2 === 0 && state.canEndTurn) {
      await handleEndTurn();
    }
  }

  async function handleEndTurn() {
    if (state.busy || !state.canEndTurn) return;
    if (state.pendingDraw2 > 0) {
      await handleExecuteAction("uno_accept_draw2", "Comprou o +2 acumulado");
      return;
    }
    try {
      suspendPlayerControls();
      state.busy = true;

      if (el.turnOwnerBadge) {
        el.turnOwnerBadge.textContent = "VEZ DO BOT";
      }

      const resp = await api("POST", `/api/v1/combats/${state.combatId}/commands`, {
        commandId: uuid(),
        type: "END_TURN",
        expectedSequence: state.runSequence,
        expectedStep: state.combatStep,
        payload: {
          actorId: "player",
          actionType: "END_TURN"
        }
      });

      // Real card identities and draw movements come from the resolved commands.
      await animateOpponentEvents(resp);

      await refreshArenaState();
    } catch (err) {
      clearTableFlight();
      setBannerMessage(`Erro ao passar turno: ${err.message}`, true);
    } finally {
      state.busy = false;
    }
  }

  function showOutcomeModal(status) {
    suspendPlayerControls();
    el.wildModal.classList.add("hidden");
    el.outcomeModal.classList.remove("hidden");

    const isVictory = status === "VICTORY";
    showCalloutSplash(isVictory ? "🏆 VITÓRIA!" : "💔 DERROTA");
    el.outcomeBadge.textContent = isVictory ? "VITÓRIA!" : "DERROTA";
    el.outcomeTitle.textContent = isVictory ? "Você bateu primeiro!" : "O bot bateu primeiro!";
    el.outcomeDescription.textContent = isVictory
      ? "Suas cartas acabaram! Inicie uma nova partida para jogar novamente."
      : "O bot zerou as cartas da mão antes de você. Tente novamente com uma nova partida!";
  }

  // =========================================================================
  // Draw Pile Drag-and-Drop + Click Support
  // =========================================================================
  function isPointOverHandDropZone(clientX, clientY, startY) {
    if (!el.playerHandContainer) return false;
    const handStage = el.playerHandContainer.parentElement || el.playerHandContainer;
    const rect = handStage.getBoundingClientRect();
    const insideHandStage =
      clientX >= rect.left - 40 &&
      clientX <= rect.right + 40 &&
      clientY >= rect.top - 55;
    return insideHandStage || clientY - startY >= 60;
  }

  function attachDrawPileDragAndClick() {
    let pointerId = null;
    let startX = 0;
    let startY = 0;
    let lastX = 0;
    let offsetX = 0;
    let offsetY = 0;
    let baseRect = null;
    let isDragging = false;
    let proxyEl = null;
    const handStage = el.playerHandContainer ? el.playerHandContainer.parentElement : null;

    function cleanupDrawDrag() {
      document.body.classList.remove("is-dragging-draw");
      if (handStage) {
        handStage.classList.remove("draw-drop-ready", "draw-drop-hot");
      }
    }

    function onPointerMove(e) {
      if (e.pointerId !== pointerId) return;
      const dx = e.clientX - startX;
      const dy = e.clientY - startY;

      if (!isDragging) {
        if (Math.hypot(dx, dy) < 7) return;
        isDragging = true;
        document.body.classList.add("is-dragging-draw");
        if (handStage) {
          handStage.classList.add("draw-drop-ready");
        }

        proxyEl = document.createElement("div");
        proxyEl.className = "uno-card-back dragging-card-proxy";
        proxyEl.style.width = `${baseRect.width}px`;
        proxyEl.style.height = `${baseRect.height}px`;
        proxyEl.innerHTML = `<div class="back-oval"><span>UNO</span></div>`;
        el.fxLayer.appendChild(proxyEl);
      }

      if (proxyEl) {
        const x = e.clientX - offsetX;
        const y = e.clientY - offsetY;
        const vx = Math.max(-18, Math.min(18, (e.clientX - lastX) * 1.2));
        lastX = e.clientX;
        const hot = isPointOverHandDropZone(e.clientX, e.clientY, startY);
        const scale = hot ? 1.1 : 1.04;
        proxyEl.style.transform = `translate3d(${x}px, ${y}px, 0) rotate(${vx.toFixed(1)}deg) scale(${scale})`;
        proxyEl.classList.toggle("over-drop-zone", hot);
        if (handStage) {
          handStage.classList.toggle("draw-drop-hot", hot);
        }
      }
    }

    function onPointerUpOrCancel(e) {
      if (e.pointerId !== pointerId) return;
      window.removeEventListener("pointermove", onPointerMove);
      window.removeEventListener("pointerup", onPointerUpOrCancel);
      window.removeEventListener("pointercancel", onPointerUpOrCancel);

      const wasCancel = e.type === "pointercancel";
      pointerId = null;

      if (!isDragging) {
        if (!wasCancel && !el.drawCardBtn.disabled) {
          handleExecuteAction("uno_draw_card", "Comprou 1 carta");
        }
        return;
      }

      const hot = !wasCancel && isPointOverHandDropZone(e.clientX, e.clientY, startY);
      if (hot && proxyEl && !el.drawCardBtn.disabled) {
        const dropRect = proxyEl.getBoundingClientRect();
        proxyEl.remove();
        proxyEl = null;
        cleanupDrawDrag();
        handleExecuteAction("uno_draw_card", "Comprou 1 carta", dropRect);
      } else if (proxyEl) {
        const returnEl = proxyEl;
        proxyEl = null;
        cleanupDrawDrag();
        const targetRect = el.drawCardBtn.getBoundingClientRect();
        if (returnEl.animate) {
          const anim = returnEl.animate(
            [
              { transform: returnEl.style.transform },
              { transform: `translate3d(${targetRect.left}px, ${targetRect.top}px, 0) rotate(0deg) scale(1)` }
            ],
            { duration: 200, easing: "cubic-bezier(0.22, 1, 0.36, 1)", fill: "forwards" }
          );
          anim.onfinish = () => returnEl.remove();
        } else {
          returnEl.remove();
        }
      } else {
        cleanupDrawDrag();
      }
    }

    el.drawCardBtn.addEventListener("pointerdown", e => {
      if (state.busy || el.drawCardBtn.disabled || e.button !== 0) return;
      e.preventDefault();
      pointerId = e.pointerId;
      startX = e.clientX;
      startY = e.clientY;
      lastX = e.clientX;
      baseRect = el.drawCardBtn.getBoundingClientRect();
      offsetX = e.clientX - baseRect.left;
      offsetY = e.clientY - baseRect.top;
      isDragging = false;

      window.addEventListener("pointermove", onPointerMove);
      window.addEventListener("pointerup", onPointerUpOrCancel);
      window.addEventListener("pointercancel", onPointerUpOrCancel);
    });
  }

  // =========================================================================
  // Radial Color Menu Hub Preview & Sector Selection
  // =========================================================================
  const RADIAL_GLYPHS = {
    red: "♦",
    blue: "♠",
    yellow: "★",
    green: "♣"
  };

  function resetRadialHubPreview() {
    if (!el.radialCenterHub) return;
    el.radialCenterHub.setAttribute("data-preview-color", "wild");
    if (el.radialHubGlyph) el.radialHubGlyph.textContent = "★";
    if (el.radialHubTitle) el.radialHubTitle.textContent = "Escolha a Cor";
    if (el.radialHubSub) el.radialHubSub.textContent = "4 Quadrantes";
    document.querySelectorAll(".radial-sector").forEach(s => s.classList.remove("is-hovered"));
  }

  function previewRadialSector(sectorBtn) {
    if (!sectorBtn || !el.radialCenterHub) return;
    const cKey = sectorBtn.dataset.colorKey || "wild";
    const cLabel = sectorBtn.dataset.colorLabel || "Escolha a Cor";
    el.radialCenterHub.setAttribute("data-preview-color", cKey);
    if (el.radialHubGlyph) el.radialHubGlyph.textContent = RADIAL_GLYPHS[cKey] || "★";
    if (el.radialHubTitle) el.radialHubTitle.textContent = cLabel;
    if (el.radialHubSub) el.radialHubSub.textContent = "Clique ou solte!";
    document.querySelectorAll(".radial-sector").forEach(s => {
      s.classList.toggle("is-hovered", s === sectorBtn);
    });
  }

  window.addEventListener("resize", () => { if (state.combatId) renderBotHandFan(); });

  // Wire UI Events
  attachDrawPileDragAndClick();
  el.shoutUnoBtn.addEventListener("click", () => handleExecuteAction("uno_shout_uno", "Gritou UNO!"));
  el.endTurnBtn.addEventListener("click", () => handleEndTurn());
  el.autoEndTurnToggle.addEventListener("change", () => updateAutoTurn());

  el.newRunBtn.addEventListener("click", () => {
    el.seedInput.value = Math.floor(Math.random() * 899999 + 100000);
    initSettingAndStartRun();
  });

  el.restartMatchBtn.addEventListener("click", () => {
    el.seedInput.value = Math.floor(Math.random() * 899999 + 100000);
    initSettingAndStartRun();
  });

  el.toggleInspectorBtn.addEventListener("click", () => {
    el.engineInspector.classList.toggle("hidden");
  });

  el.showBotCardsToggle.addEventListener("change", () => renderBotHandTelemetry());

  el.closeInspectorBtn.addEventListener("click", () => {
    el.engineInspector.classList.add("hidden");
  });

  let radialActionTriggeredAt = 0;
  document.querySelectorAll(".wild-color-choice").forEach(btn => {
    btn.addEventListener("pointerenter", () => previewRadialSector(btn));
    btn.addEventListener("pointerleave", () => resetRadialHubPreview());

    function triggerSectorChoice() {
      const now = Date.now();
      if (now - radialActionTriggeredAt < 350) return;
      radialActionTriggeredAt = now;
      const actionId = btn.dataset.action;
      handleExecuteAction(actionId, `Escolheu cor (${actionId})`);
    }

    btn.addEventListener("pointerup", () => triggerSectorChoice());
    btn.addEventListener("click", () => triggerSectorChoice());
  });

  // Launch automatically on page load
  initSettingAndStartRun();
})();
