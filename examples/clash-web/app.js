(() => {
  const params = new URLSearchParams(window.location.search);
  const API_BASE = (params.get("api") || "http://localhost:15260").replace(/\/+$/, "");

  const COLOR_LABELS = {
    red: "Vermelho",
    blue: "Azul",
    green: "Verde",
    yellow: "Amarelo",
    wild: "Multicor"
  };

  function modeConfig() {
    return state.mode || ClashModes.duel;
  }

  function playerId() { return modeConfig().playerId; }
  function actionId(role) { return modeConfig().actions[role]; }
  function fanFor(actorId) {
    return [el.aiHandFan, el.leftHandFan, el.rightHandFan].find(fan => fan?.dataset.actorId === actorId) || el.aiHandFan;
  }
  function actorLabel(actorId) {
    if (actorId === playerId()) return "Você";
    const seat = Object.entries(modeConfig().seats).find(([, id]) => id === actorId)?.[0];
    return { top: "Bot do topo", left: "Bot da esquerda", right: "Bot da direita" }[seat] || "Bot";
  }

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

  function pileSnapshot(payload) {
    let count = null, recycled = false;
    for (const application of payload?.cardResolution?.applications || payload?.abilityResolution?.applications || []) {
      for (const step of application.cardZoneSteps || application.CardZoneSteps || []) {
        const source = step.sourceAddress || step.SourceAddress || "";
        const target = step.targetAddress || step.TargetAddress || "";
        const sourceAfter = step.sourceOrderAfter || step.SourceOrderAfter;
        const targetAfter = step.targetOrderAfter || step.TargetOrderAfter;
        if (source.endsWith("::draw") && Array.isArray(sourceAfter)) count = sourceAfter.length;
        if (target.endsWith("::draw") && Array.isArray(targetAfter)) count = targetAfter.length;
        recycled ||= source.endsWith("::discard") && target.endsWith("::draw");
      }
    }
    return { count, recycled };
  }

  function presentPileSnapshot(snapshot) {
    if (snapshot.count !== null) renderDrawPile(snapshot.count);
    if (snapshot.recycled) {
      el.drawCardBtn.classList.remove("pile-recycled");
      void el.drawCardBtn.offsetWidth;
      el.drawCardBtn.classList.add("pile-recycled");
      setTimeout(() => el.drawCardBtn.classList.remove("pile-recycled"), 900);
      appendLog("RECICLAGEM", "O descarte voltou ao monte. A carta ativa permanece na mesa.");
    }
  }

  function cardEffectSplash(card, payload) {
    if (card.corner !== "+2") return card.specialSplash;
    const values = (payload?.cardResolution?.applications || [])
      .filter(application => (application.resourceId || application.ResourceId) === modeConfig().resources.debt)
      .map(application => application.currentValue ?? application.CurrentValue)
      .filter(value => Number.isFinite(value) && value > 0);
    return values.length ? `💥 COMPRA +${Math.max(...values)}!` : card.specialSplash;
  }

  function opponentEvents(response) {
    const events = [];
    for (const frame of response.state?.resolution?.frames || []) {
      const payload = frame.payload || {};
      const command = payload.resolvedCommand || payload.command;
      const actorId = command?.actorId || command?.ActorId;
      if (!actorId || actorId === playerId()) continue;
      const definitionId = payload.cardResolution?.definitionId;
      const actionId = command.powerId || command.PowerId;
      const pile = pileSnapshot(payload);
      if (definitionId) events.push({ kind: "card", actorId, definitionId, payload, cardInstanceId: payload.consumedCardId || command.cardInstanceId || command.CardInstanceId, draws: movedCardsToHands(payload), pile });
      else if (actionId) events.push({ kind: "action", actorId, actionId, payload, draws: movedCardsToHands(payload), pile });
      else if ((command.actionType || command.ActionType) === "END_TURN") events.push({ kind: "pass", actorId, draws: [] });
    }
    return events;
  }

  async function withBotActionPacing(present) {
    const startedAt = performance.now();
    await present();
    const remaining = 1000 - (performance.now() - startedAt);
    if (remaining > 0) await new Promise(resolve => setTimeout(resolve, remaining));
  }

  function playerHandSnapshot(payload, ownerId = playerId()) {
    let ids = null;
    for (const application of payload?.cardResolution?.applications || payload?.abilityResolution?.applications || []) {
      for (const step of application.cardZoneSteps || application.CardZoneSteps || []) {
        const address = `$run::${ownerId}::hand`;
        if ((step.sourceAddress || step.SourceAddress) === address) ids = step.sourceOrderAfter || step.SourceOrderAfter || ids;
        if ((step.targetAddress || step.TargetAddress) === address) ids = step.targetOrderAfter || step.TargetOrderAfter || ids;
      }
    }
    return ids;
  }

  async function preparePlayerHandPlayback(response) {
    const frames = response.state?.resolution?.frames || [];
    const known = new Map((state.playerCards || []).map(card => [card.presentation.identity.cardInstanceId, card]));
    const snapshots = frames.map(frame => playerHandSnapshot(frame.payload));
    if (snapshots.some(ids => ids?.some(id => !known.has(id)))) {
      const evaluations = await api("GET", `/api/v1/combats/${state.combatId}/cards/evaluations?actorId=${encodeURIComponent(playerId())}`);
      for (const card of evaluations.cards || []) known.set(card.presentation.identity.cardInstanceId, card);
    }
    return payload => {
      let ids = playerHandSnapshot(payload);
      const command = payload?.resolvedCommand || payload?.command;
      const consumed = payload?.consumedCardId;
      if (!ids && consumed && (command?.actorId || command?.ActorId) === playerId()) {
        ids = (state.playerCards || []).map(card => card.presentation.identity.cardInstanceId).filter(id => id !== consumed);
      }
      if (!ids || ids.some(id => !known.has(id))) return;
      // Replay the recorded hand order at this frame, never the final batch ahead of time.
      state.playerCards = ids.map(id => known.get(id));
      renderPlayerHand(state.playerCards, 0);
    };
  }

  async function prepareBotHandPlayback(response) {
    const frames = response.state?.resolution?.frames || [];
    const known = new Map(Object.values(state.botHands).flatMap(hand => hand.cards).map(card => [card.cardInstanceId, card]));
    for (const frame of frames) {
      const payload = frame.payload || {}, command = payload.resolvedCommand || payload.command;
      const id = payload.consumedCardId || command?.cardInstanceId || command?.CardInstanceId;
      if (id && payload.cardResolution?.definitionId) known.set(id, { cardInstanceId: id, definitionId: payload.cardResolution.definitionId });
    }
    const orders = frames.flatMap(frame => Object.keys(state.botHands).map(owner => playerHandSnapshot(frame.payload, owner)));
    if (orders.some(ids => ids?.some(id => !known.has(id)))) {
      const zones = await api("GET", `/api/v1/runs/${state.runId}/card-zones`);
      for (const zone of zones.zones || []) for (const card of zone.cards || []) known.set(card.cardInstanceId, card);
    }
    return (owner, payload) => {
      const ids = playerHandSnapshot(payload, owner), hand = state.botHands[owner];
      if (!ids || !hand) return false;
      hand.count = ids.length;
      hand.cards = ids.map(id => known.get(id)).filter(Boolean);
      return true;
    };
  }

  function presentEffectColor(payload, overrideColor = null) {
    let color = overrideColor;
    if (!color) {
      const prefix = (typeof modeConfig === "function" && modeConfig()?.resources?.color) || "color_";
      const pid = typeof playerId === "function" ? playerId() : null;
      const changes = (payload?.cardResolution?.applications || payload?.abilityResolution?.applications || [])
        .filter(application => (!pid || (application.targetEntityId || application.TargetEntityId) === pid) &&
          (application.resourceId || application.ResourceId)?.startsWith(prefix));
      if (!changes.length) return;
      const selected = changes.find(application => (application.currentValue ?? application.CurrentValue) > 0);
      color = selected ? (selected.resourceId || selected.ResourceId).slice(prefix.length) : "neutral";
    }
    if (typeof document !== "undefined" && document.body) {
      document.body.dataset.activeColor = color;
    }
    if (typeof state !== "undefined") {
      state.lastActiveColor = color;
    }
    if (typeof el !== "undefined" && el.activeColorLabel) {
      el.activeColorLabel.textContent = (typeof COLOR_LABELS !== "undefined" && COLOR_LABELS[color]) || "Sem cor";
    }
    if (color !== "neutral" && color !== "wild") {
      const applyColor = (cardEl) => {
        if (!cardEl) return;
        if (!cardEl.dataset) cardEl.dataset = {};
        if (cardEl.dataset.isWild === "true" || cardEl.dataset.color === "wild" || cardEl.classList?.contains?.("flying-card")) {
          cardEl.dataset.color = color;
          if (typeof cardEl.animate === "function" && cardEl.classList?.contains?.("flying-card")) {
            const sx = Number(cardEl.dataset.scaleX) || 1;
            const sy = Number(cardEl.dataset.scaleY) || 1;
            const x = Number(cardEl.dataset.endX) || 0;
            const y = Number(cardEl.dataset.endY) || 0;
            cardEl.animate([
              { transform: `translate(${x}px, ${y}px) scale(${sx * 1.26}, ${sy * 1.26}) rotate(-6deg)` },
              { transform: `translate(${x}px, ${y}px) scale(${sx * 0.96}, ${sy * 0.96}) rotate(2deg)`, offset: 0.65 },
              { transform: `translate(${x}px, ${y}px) scale(${sx}, ${sy}) rotate(0deg)` }
            ], {
              duration: 380,
              easing: "cubic-bezier(0.22, 1, 0.36, 1)",
              fill: "forwards"
            });
          } else if (cardEl.classList) {
            cardEl.classList.remove("pop-land");
            if (cardEl.offsetWidth) void cardEl.offsetWidth;
            cardEl.classList.add("pop-land");
          }
        }
      };
      if (typeof state !== "undefined" && state.pendingTableFlight) {
        applyColor(state.pendingTableFlight);
      }
      const currentTable = typeof el !== "undefined" && el.discardStage?.querySelector?.(".table-card");
      if (currentTable) {
        applyColor(currentTable);
      }
    }
  }

  async function animateOpponentEvents(response) {
    const [presentPlayerHand, presentBotHand] = await Promise.all([
      preparePlayerHandPlayback(response), prepareBotHandPlayback(response)
    ]);
    for (const frame of response.state?.resolution?.frames || []) {
      const command = frame.payload?.resolvedCommand || frame.payload?.command;
      if ((command?.actorId || command?.ActorId) === playerId()) presentPlayerHand(frame.payload);
    }
    for (const event of opponentEvents(response)) {
      await withBotActionPacing(async () => {
        const messages = [];
        const label = actorLabel(event.actorId);
        const fan = fanFor(event.actorId);
        const card = event.kind === "card" ? parseCardIdentity(event.definitionId) : null;
        if (card) messages.push(`🃏 ${label} jogou ${card.label}.`);
        else if (event.kind === "pass") messages.push(`${label} passou a vez.`);
        else if (event.actionId?.startsWith(actionId("choose"))) {
          messages.push(`🌈 O bot escolheu ${COLOR_LABELS[event.actionId.replace(actionId("choose"), "")]}.`);
        } else if (event.actionId === actionId("call")) messages.push("📣 O bot declarou CLASH!");
        else if (event.actionId === actionId("catch")) messages.push("🚨 O bot cobrou o CLASH esquecido.");
        else if (event.actionId === actionId("draw") && !event.draws.some(draw => draw.count > 0)) {
          messages.push("O bot tentou comprar, mas não havia cartas disponíveis.");
        }
        for (const draw of event.draws) {
          if (draw.count) messages.push(`${actorLabel(draw.actorId)} comprou ${draw.count} carta(s) do monte.`);
        }
        if (messages.length) setBannerMessage(messages.join(" • "), true);

        if (card) {
          const sourceCard = fan?.lastElementChild;
          const from = sourceCard?.getBoundingClientRect() || fan?.getBoundingClientRect();
          const lateral = fan?.dataset?.seat === "left" || fan?.dataset?.seat === "right";
          await animateCardFlight(from, el.discardStage, { ...card,
            sourceWidth: lateral ? from?.width : sourceCard?.offsetWidth,
            sourceHeight: lateral ? from?.height : sourceCard?.offsetHeight,
            sourceRotation: lateral ? 0 : -8
          }, 380);
          const hand = state.botHands[event.actorId];
          if (hand) {
            hand.count = Math.max(0, hand.count - 1);
            hand.cards = hand.cards.filter(item => item.cardInstanceId !== event.cardInstanceId);
          }
          renderBotHandFan();
          renderBotHandTelemetry();
          spawnShockwave(el.discardStage);
          presentEffectColor(event.payload);
          if (card.isReverse && el.colorOrbitRing) {
            state.orbitReversed = !state.orbitReversed;
            el.colorOrbitRing.classList.toggle("reversed", state.orbitReversed);
          }
          const splash = cardEffectSplash(card, event.payload);
          if (splash) showCalloutSplash(splash);
        } else if (Boolean(event.actionId && ((typeof actionId === "function" && event.actionId.startsWith(actionId("choose"))) || /(?:^|_)choose_([a-z]+)$/.test(event.actionId)))) {
          const choosePrefix = typeof actionId === "function" ? actionId("choose") : "clash_choose_";
          const chosenColor = event.actionId.startsWith(choosePrefix) ? event.actionId.replace(choosePrefix, "") : (event.actionId.match(/(?:^|_)choose_([a-z]+)$/)?.[1] || "");
          presentEffectColor(event.payload, chosenColor);
          spawnShockwave(el.discardStage);
        } else {
          presentEffectColor(event.payload);
          if (event.actionId === actionId("call")) showCalloutSplash("📣 BOT: CLASH!");
        }
        for (const draw of event.draws) {
          if (!draw.count) continue;
          const player = draw.actorId === playerId();
          if (event.actionId === actionId("accept")) showCalloutSplash(`📥 ${player ? "VOCÊ" : "BOT"} COMPRA +${draw.count}!`);
          await animateCardFlight(el.drawPileAnchor?.getBoundingClientRect(), player ? el.playerHandContainer : fanFor(draw.actorId), null, 360);
          if (player) {
            presentPlayerHand(event.payload);
          } else {
            if (!presentBotHand(draw.actorId, event.payload) && state.botHands[draw.actorId]) state.botHands[draw.actorId].count += draw.count;
            renderBotHandFan();
            renderBotHandTelemetry();
          }
        }
        if (event.pile) presentPileSnapshot(event.pile);
        appendLog("BOT", card ? event.definitionId : event.actionId || "END_TURN");
      });
    }
  }

  const state = {
    mode: null,
    botHands: {},
    endTurnCandidate: null,
    matchStatus: null,
    settingRevision: null,
    runId: null,
    combatId: null,
    runSequence: 0,
    combatStep: 0,
    busy: false,
    legalByCardInstance: new Map(),
    legalByActionId: new Map(),
    autoTurnTimer: null,
    canEndTurn: false,
    autoEndTurnEligible: false,
    autoEndTurnDelay: 680,
    pendingTableFlight: null,
    tableFlightGeneration: 0,
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
    centerNewRunBtn: document.getElementById("centerNewRunBtn"),
    toggleInspectorBtn: document.getElementById("toggleInspectorBtn"),
    closeInspectorBtn: document.getElementById("closeInspectorBtn"),
    engineInspector: document.getElementById("engineInspector"),
    showBotCardsToggle: document.getElementById("showBotCardsToggle"),
    telemetryBotCards: document.getElementById("telemetryBotCards"),
    telemetryBotCardsStatus: document.getElementById("telemetryBotCardsStatus"),

    aiHandCountPill: document.getElementById("aiHandCountPill"),
    aiHandFan: document.getElementById("aiHandFan"),
    leftHandFan: document.getElementById("leftHandFan"),
    rightHandFan: document.getElementById("rightHandFan"),
    modeModal: document.getElementById("modeModal"),
    closeModeBtn: document.getElementById("closeModeBtn"),
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

    shoutClashBtn: document.getElementById("shoutClashBtn"),
    endTurnBtn: document.getElementById("endTurnBtn"),
    endTurnProgress: document.getElementById("endTurnProgress"),

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
    const heading = document.createElement("strong");
    heading.textContent = title;
    const body = document.createElement("div");
    body.textContent = detail;
    item.append(heading, body);
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
        if (!flyEl.dataset) flyEl.dataset = {};
        if (cardSpec.isWild || cardSpec.color === "wild") {
          flyEl.dataset.isWild = "true";
        }
      } else {
        flyEl = document.createElement("div");
        flyEl.className = "clash-card-back flying-card";
        flyEl.style.width = `${fromRect.width || 96}px`;
        flyEl.style.height = `${fromRect.height || 140}px`;
        flyEl.innerHTML = '<div class="back-oval"><span>CLASH</span></div>';
        if (!flyEl.dataset) flyEl.dataset = {};
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
      const startScaleX = landsOnTable ? (cardSpec.sourceWidth || fromRect.width) / width : 0.95;
      const startScaleY = landsOnTable ? (cardSpec.sourceHeight || fromRect.height) / height : 0.95;
      const midX = (startX + endX) / 2;
      const midY = Math.min(startY, endY) - 48;
      const finalTransform = `translate(${endX}px, ${endY}px) scale(${endScaleX}, ${endScaleY}) rotate(0deg)`;
      if (!flyEl.dataset) flyEl.dataset = {};
      flyEl.dataset.endX = endX;
      flyEl.dataset.endY = endY;
      flyEl.dataset.scaleX = endScaleX;
      flyEl.dataset.scaleY = endScaleY;

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
            { transform: `translate(${startX}px, ${startY}px) scale(${startScaleX}, ${startScaleY}) rotate(${cardSpec?.sourceRotation ?? -8}deg)`, opacity: 1 },
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
    el.shoutClashBtn.disabled = true;
    el.shoutClashBtn.classList.remove("urgent");
    if (el.drawPileHint) el.drawPileHint.textContent = "Aguarde a jogada terminar";
  }

  // Parse card definition ID (`clash_red_7`, `clash_blue_skip`, `clash_wild_draw4`) into visual presentation
  function parseCardIdentity(definitionId) {
    definitionId = definitionId.replace(/^t_/, "clash_").replace("clash_wild_wild", "clash_wild");
    if (definitionId === "clash_wild") {
      return { color: "wild", corner: "★", center: "★", label: "Coringa", isWild: true, isSpecial: true, isTextGlyph: false };
    }
    if (definitionId === "clash_wild_draw4") {
      return { color: "wild", corner: "+4", center: "+4", label: "Coringa +4", isWild: true, isSpecial: true, isTextGlyph: true };
    }
    const parts = definitionId.replace(/^clash_/, "").split("_");
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

  function createCardElement({ color, corner, center, isTextGlyph, extraClass = "" }) {
    const card = document.createElement("div");
    card.className = ("clash-card " + extraClass).trim();
    card.dataset.color = color;
    card.innerHTML = `
      <span class="card-corner top-left">${corner}</span>
      <span class="card-center-glyph ${isTextGlyph ? "glyph-text" : ""}">${center}</span>
      <span class="card-corner bottom-right">${corner}</span>
    `;
    return card;
  }

  function renderBotHandFan() {
    for (const [actorId, hand] of Object.entries(state.botHands)) {
      const fan = fanFor(actorId);
      const count = Math.max(0, Math.min(hand.count, 20));
      while (fan.children.length > count) fan.lastElementChild.remove();
      while (fan.children.length < count) {
        const card = document.createElement("div");
        card.className = "ai-mini-card";
        card.innerHTML = '<div class="ai-mini-oval">CLASH</div>';
        fan.appendChild(card);
      }
      const width = fan.firstElementChild?.offsetWidth || 0;
      const available = Math.max(0, fan.clientWidth - 32);
      fan.style.setProperty("--bot-card-overlap", `${count > 1 ? Math.min(-12, (available - width * count) / (count - 1)) : 0}px`);
      [...fan.children].forEach((card, index) => {
        const norm = count > 1 ? (index - (count - 1) / 2) / ((count - 1) / 2) : 0;
        const lateral = fan.dataset.seat === "left" || fan.dataset.seat === "right";
        const facing = fan.dataset.seat === "left" ? 1 : -1;
        card.style.transform = lateral
          ? `translateZ(${norm * facing * 10}px) rotateY(${norm * facing * 4}deg)`
          : `translateY(${norm * norm * 8}px) rotate(${norm * Math.min(count * 1.45, 11)}deg)`;
      });
      fan.setAttribute("aria-label", `${actorLabel(actorId)}: ${hand.count} cartas`);
    }
  }

  function renderBotHandTelemetry() {
    const visible = el.showBotCardsToggle.checked;
    el.telemetryBotCards.classList.toggle("hidden", !visible);
    el.telemetryBotCards.replaceChildren();
    el.telemetryBotCardsStatus.textContent = !state.combatId ? "Aguardando o início da partida."
      : visible ? "Mãos publicadas pela API" : "Ative para inspecionar as mãos dos bots.";
    if (!visible || !state.combatId) return;
    for (const [actorId, hand] of Object.entries(state.botHands)) {
      if (hand.cards.length !== hand.count) {
        const notice = document.createElement("p");
        notice.textContent = `${actorLabel(actorId)}: ${hand.count} cartas ocultas pela configuração.`;
        el.telemetryBotCards.appendChild(notice);
        continue;
      }
      for (const instance of hand.cards) {
        const parsed = parseCardIdentity(instance.definitionId);
        const item = document.createElement("figure");
        item.className = "telemetry-bot-card";
        item.setAttribute("role", "listitem");
        item.dataset.cardInstanceId = instance.cardInstanceId;
        const card = createCardElement(parsed);
        const caption = document.createElement("figcaption");
        caption.textContent = `${actorLabel(actorId)} · ${parsed.label}`;
        item.append(card, caption);
        el.telemetryBotCards.appendChild(item);
      }
    }
  }

  function renderDrawPile(count, recyclable = 0) {
    const layers = [...el.drawCardBtn.querySelectorAll(".clash-card-back")];
    const visible = count === null ? layers.length : count > 0
      ? Math.max(1, Math.ceil(count / (state.deckCapacity || count) * layers.length)) : 0;
    layers.forEach((layer, index) => layer.classList.toggle("pile-layer-hidden", index < layers.length - visible));
    el.drawCardBtn.classList.toggle("pile-empty", count === 0);
    el.drawCardBtn.dataset.count = count === null ? "" : String(count);
    el.drawCardBtn.setAttribute("aria-label", count === null ? "Baralho de compras" : `${count} cartas no monte${recyclable ? `, ${recyclable} no descarte` : ""}`);
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
    const defaultOverlap = total <= 5 ? -10 : Math.max(-52, Math.round(-12 - (total - 5) * 4.2));
    const cardWidth = container.firstElementChild?.offsetWidth || el.drawCardBtn?.offsetWidth || 0;
    const availableWidth = Math.max(0, (container.parentElement?.clientWidth || 0) - 48);
    const overlap = total > 1 && cardWidth && availableWidth
      ? Math.min(defaultOverlap, (availableWidth - total * cardWidth) / (total - 1)) : defaultOverlap;
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
      const legal = state.legalByCardInstance.has(cardInstanceId);
      card.classList.toggle("is-legal", legal);
      card.classList.toggle("is-illegal", !legal);
      card.setAttribute("aria-label", parsed.label);
      card.setAttribute("aria-disabled", String(!legal));
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

  async function initSettingAndStartRun(modeKey) {
    state.mode = ClashModes[modeKey];
    if (!state.mode) return;
    try {
      suspendPlayerControls();
      clearTimeout(state.autoTurnTimer);
      resetAutoTurnProgress();
      state.busy = true;
      state.runId = null;
      state.combatId = null;
      state.endTurnCandidate = null;
      state.legalByCardInstance.clear();
      state.legalByActionId.clear();
      el.discardStage.replaceChildren();
      renderDrawPile(null);
      clearTableFlight();
      state.discardHistory = [];
      state.lastTableCode = null;
      state.orbitReversed = false;
      el.colorOrbitRing.classList.remove("reversed");
      state.botHands = {};
      state.matchStatus = null;
      el.playerHandContainer.replaceChildren();
      for (const fan of [el.aiHandFan, el.leftHandFan, el.rightHandFan]) fan.replaceChildren();
      for (const [seat, actorId] of Object.entries(modeConfig().seats)) {
        const fan = { top: el.aiHandFan, left: el.leftHandFan, right: el.rightHandFan }[seat];
        fan.dataset.actorId = actorId;
        state.botHands[actorId] = { count: 0, cards: [] };
      }
      document.body.dataset.gameMode = modeKey;
      document.body.dataset.gameState = "starting";
      document.querySelectorAll(".side-opponent").forEach(node => node.classList.toggle("hidden", modeKey !== "freeForAll"));
      el.outcomeModal.classList.add("hidden");
      el.wildModal.classList.add("hidden");

      const settingsResp = await api("GET", "/api/v1/content/settings");
      const clashSetting = (settingsResp.items || []).find(s => s.settingId === modeConfig().settingId);
      if (!clashSetting) {
        throw new Error(`Modo ${modeConfig().label} indisponível. Reabra o jogo pelo BAT para carregar os dois modos.`);
      }

      state.settingRevision = clashSetting.currentRevision;
      el.apiStatusDot.className = "status-dot connected";
      el.revisionBadge.textContent = `rev:${clashSetting.currentRevision.slice(0, 8)}`;

      const seedVal = parseInt(el.seedInput.value, 10) || Math.floor(Math.random() * 900000 + 100000);
      el.seedInput.value = seedVal;

      const runResp = await api("POST", "/api/v1/runs", {
        commandId: uuid(),
        settingId: clashSetting.settingId,
        contentRevision: state.settingRevision,
        runDefinitionId: clashSetting.launch.runDefinitionId,
        playerEntityId: clashSetting.launch.playerEntityId,
        modeId: clashSetting.launch.modeId,
        seed: seedVal
      });

      state.runId = runResp.runId;
      appendLog("POST /api/v1/runs", `Run criada (seed ${seedVal})`);

      state.busy = false;
      await startCurrentNodeEncounter();
    } catch (err) {
      document.body.dataset.gameState = "idle";
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
      const setup = actionId("setup") ? legal.candidates.find(candidate => candidate.actionId === actionId("setup")) : null;
      if (setup) await api("POST", `/api/v1/combats/${state.combatId}/commands`, {
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
      api("GET", `/api/v1/combats/${state.combatId}/cards/evaluations?actorId=${encodeURIComponent(playerId())}`),
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

    const playerActor = combatSnap.actors.find(a => a.instanceId === playerId());
    const pRes = (playerActor && playerActor.resources) || {};

    const getRes = (bag, key, fallback = 0) => (bag[key] ? bag[key].current : fallback);

    const zoneCount = (zoneId, ownerId) => zoneSnap.zones.find(zone => zone.zoneId === zoneId && zone.ownerId === ownerId)?.count || 0;
    state.deckCapacity = zoneSnap.zones.reduce((total, zone) => total + zone.count, 0);
    const playerHandCount = zoneCount("hand", playerId());
    for (const [actorId, hand] of Object.entries(state.botHands)) {
      const zone = zoneSnap.zones.find(zone => zone.zoneId === "hand" && zone.ownerId === actorId);
      hand.count = zone?.count || 0;
      hand.cards = zone?.cards || [];
    }
    renderBotHandTelemetry();
    const resources = modeConfig().resources;
    const clashEnergy = getRes(pRes, resources.energy);
    const wildToken = getRes(pRes, resources.choice);
    const clashCalled = getRes(pRes, resources.called);
    let activeColor = Object.keys(COLOR_LABELS).find(color => getRes(pRes, resources.color + color) > 0) || "neutral";
    document.body.dataset.activeColor = activeColor;
    document.body.dataset.gameState = "playing";
    if (combatSnap.activationSequenceStrategy === "Circular") {
      state.orbitReversed = combatSnap.activation?.circular?.direction === "Reverse";
      el.colorOrbitRing.classList.toggle("reversed", state.orbitReversed);
    }
    state.discardHistory = (zoneSnap.zones.find(zone => zone.zoneId === "discard")?.cards || []).slice(-3);
    const tableInstance = zoneSnap.zones.find(zone => zone.zoneId === "table")?.cards?.at(-1);
    const tableChanged = state.lastTableCode !== null && state.lastTableCode !== tableInstance?.cardInstanceId;
    state.lastTableCode = tableInstance?.cardInstanceId || null;
    state.lastActiveColor = activeColor;
    const tableCode = tableInstance?.definitionId || "";
    // Update the floating opponent hand.
    renderBotHandFan();

    // Render Center Discard Stage (under-stack history + active top card)
    const decodedTable = tableInstance ? parseCardIdentity(tableInstance.definitionId) : null;
    el.discardStage.innerHTML = "";
    const historyAngles = [-14, 11, -6];
    const historyOffsets = [
      [-6, 4],
      [6, -3],
      [-3, -4]
    ];
    state.discardHistory.forEach((hItem, idx) => {
      const hDecoded = parseCardIdentity(hItem.definitionId);
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
      color: ((decodedTable?.color === "wild" || decodedTable?.isWild) && (typeof activeColor !== "undefined" && activeColor && activeColor !== "neutral" && activeColor !== "wild"))
        ? activeColor
        : decodedTable?.color,
      corner: decodedTable?.corner,
      center: decodedTable?.center,
      isTextGlyph: decodedTable?.isTextGlyph,
      extraClass: `table-card ${tableChanged && !state.pendingTableFlight ? "pop-land" : ""}`.trim()
    });
    if (decodedTable?.color === "wild" || decodedTable?.isWild) tableCardEl.dataset.isWild = "true";
    // An empty table (e.g. before setup) renders no top card instead of crashing.
    if (decodedTable) el.discardStage.appendChild(tableCardEl);
    clearTableFlight();

    el.activeColorLabel.textContent = COLOR_LABELS[activeColor] || "Sem cor";
    el.activeValueLabel.textContent = decodedTable ? decodedTable.label : "Mesa vazia";

    // Actual physical cards left in the shared draw pile.
    const drawZoneCount = zoneCount("draw", "$run");
    el.drawPileCount.textContent = `${drawZoneCount} cartas`;

    renderDrawPile(drawZoneCount, zoneCount("discard", "$run"));
    const legalCardsCount = state.legalByCardInstance.size;
    // Bind the server's penalty action to Pass; keep the pile available when no card response exists.
    const acceptPenalty = state.legalByActionId.get(actionId("accept"));
    const purchase = acceptPenalty && legalCardsCount > 0 ? null :
      acceptPenalty || state.legalByActionId.get(actionId("draw"));
    el.drawCardBtn.disabled = !purchase;
    el.drawCardBtn.classList.toggle("must-draw-pulse", Boolean(purchase) && legalCardsCount === 0);
    el.shoutClashBtn.disabled = !state.legalByActionId.has(actionId("call"));
    el.shoutClashBtn.classList.toggle("urgent", !el.shoutClashBtn.disabled);
    state.endTurnCandidate = acceptPenalty || legalSnap.candidates.find(candidate => candidate.command?.actionType === "END_TURN") || null;
    state.canEndTurn = Boolean(state.endTurnCandidate);
    el.endTurnBtn.disabled = !state.canEndTurn;
    const blockedEnd = (legalSnap.blockedCommands || []).find(command => command.actionType === "END_TURN");
    el.endTurnBtn.title = acceptPenalty ? "Aceitar a penalidade e passar a vez" :
      blockedEnd ? (blockedEnd.reason || blockedEnd.messageKey || blockedEnd.code) : "";
    el.turnOwnerBadge.textContent = legalSnap.candidates.length ? "SUA VEZ" : "AGUARDE";
    state.matchStatus = combatSnap.status;
    state.playerCards = evalsSnap.cards || [];
    renderPlayerHand(evalsSnap.cards || [], wildToken);

    // Update Telemetry Inspector
    el.inspRunId.textContent = state.runId;
    el.inspCombatId.textContent = state.combatId;
    el.inspSeqStep.textContent = `seq ${state.runSequence} / step ${state.combatStep}`;
    el.inspStateHash.textContent = combatSnap.stateHash || "-";

    const trackedRes = [
      [`${playerId()}.hand`, playerHandCount],
      ...Object.entries(state.botHands).map(([id, hand]) => [`${id}.hand`, hand.count]),
      ["table_card", tableCode || "-"],
      [resources.energy, clashEnergy],
      [resources.called, clashCalled],
      [resources.choice, wildToken],
      ...["red", "blue", "green", "yellow"].map(color => [resources.color + color, getRes(pRes, resources.color + color)])
    ];
    el.inspResourceGrid.replaceChildren(...trackedRes.map(([k, v]) => {
      const chip = document.createElement("div");
      chip.className = "res-chip";
      const key = document.createElement("span");
      key.textContent = k;
      const value = document.createElement("strong");
      value.textContent = String(v);
      chip.append(key, value);
      return chip;
    }));

    // Check Combat Outcome (VICTORY / DEFEAT)
    if (combatSnap.status && combatSnap.status !== "ACTIVE" && combatSnap.status !== "InProgress") {
      resetAutoTurnProgress();
      showOutcomeModal(combatSnap.status, combatSnap.participantVictory);
      return;
    }

    // Check if Radial Wild Color Picker should open
    if (Object.keys(COLOR_LABELS).some(color => state.legalByActionId.has(actionId("choose") + color))) {
      state.canEndTurn = false;
      state.autoEndTurnEligible = false;
      cancelAutoTurn();
      resetRadialHubPreview();
      el.wildModal.classList.remove("hidden");
      return;
    } else {
      el.wildModal.classList.add("hidden");
    }

    // Optional automatic input schedules an already-legal server candidate.
    state.autoEndTurnEligible = state.canEndTurn && legalCardsCount === 0 && !purchase;
    state.autoEndTurnDelay = state.legalByActionId.has(actionId("call")) ? 1650 : 680;
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
    // Magnetic radius around the center table only; dragging upward elsewhere returns the card.
    return dist <= Math.max(rect.width * 1.45, 165);
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

      const playerFrame = (resp.state?.resolution?.frames || []).find(frame =>
        frame.payload?.cardResolution &&
        (frame.payload.resolvedCommand?.ActorId || frame.payload.resolvedCommand?.actorId || frame.payload.command?.ActorId || frame.payload.command?.actorId) === playerId());
      presentEffectColor(playerFrame?.payload);
      spawnShockwave(el.discardStage);
      if (parsedCard.isReverse && el.colorOrbitRing) {
        state.orbitReversed = !state.orbitReversed;
        el.colorOrbitRing.classList.toggle("reversed", state.orbitReversed);
      }
      if (parsedCard.specialSplash) {
        showCalloutSplash(cardEffectSplash(parsedCard, playerFrame?.payload));
      }

      appendLog("PLAY_CARD", `Jogou ${cand.cardDefinitionId} (step ${resp.step})`);
      for (const frame of resp.state?.resolution?.frames || []) {
        const payload = frame.payload || {};
        const actor = payload.resolvedCommand?.ActorId || payload.resolvedCommand?.actorId || payload.command?.ActorId || payload.command?.actorId;
        if (actor === playerId()) {
          presentPileSnapshot(pileSnapshot(payload));
          presentEffectColor(payload);
        }
      }
      await presentAcceptedResult(resp);
    } catch (err) {
      clearTableFlight();
      if (err.acceptedByServer) {
        await recoverAfterPresentationError(err);
      } else {
        if (sourceCardEl) sourceCardEl.style.opacity = "";
        setBannerMessage(`Jogada recusada pela engine: ${err.message}`, true);
      }
    } finally {
      state.busy = false;
    }
  }

  async function handleExecuteAction(selectedActionId, logLabel, customFromRect = null) {
    if (state.busy) return;
    const cand = state.legalByActionId.get(selectedActionId);
    if (!cand) return;

    try {
      suspendPlayerControls();
      state.busy = true;

      const isDraw = selectedActionId === actionId("draw") || selectedActionId === actionId("accept");
      const drawRect = customFromRect || (isDraw && el.drawPileAnchor ? el.drawPileAnchor.getBoundingClientRect() : null);
      const flightMs = customFromRect ? 190 : 350;

      const resp = await api("POST", `/api/v1/combats/${state.combatId}/commands`, {
        commandId: uuid(), type: "EXECUTE_ACTION", expectedSequence: state.runSequence,
        expectedStep: state.combatStep, payload: toCombatPayload(cand.command)
      });
      const actualDrawCount = (resp.state?.resolution?.frames || [])
        .flatMap(frame => movedCardsToHands(frame.payload || {}))
        .filter(draw => draw.actorId === playerId()).reduce((total, draw) => total + draw.count, 0);
      if (isDraw && actualDrawCount > 0) await animateCardFlight(drawRect, el.playerHandContainer, null, flightMs);
      for (const frame of resp.state?.resolution?.frames || []) {
        const payload = frame.payload || {};
        const actor = payload.resolvedCommand?.ActorId || payload.resolvedCommand?.actorId || payload.command?.ActorId || payload.command?.actorId;
        if (actor === playerId()) {
          presentPileSnapshot(pileSnapshot(payload));
          presentEffectColor(payload);
        }
      }

      appendLog("EXECUTE_ACTION", `${logLabel} (${selectedActionId})`);
      if (selectedActionId === actionId("call")) {
        showCalloutSplash("📣 CLASH!");
        setBannerMessage("📣 Você gritou CLASH! Sua última carta está protegida!", true);
      } else if (selectedActionId === actionId("accept")) {
        showCalloutSplash(`📥 COMPRA +${actualDrawCount}!`);
        setBannerMessage(`Você comprou ${actualDrawCount} cartas e perdeu a vez.`, true);
      } else if (selectedActionId === actionId("draw")) {
        setBannerMessage(actualDrawCount ? "📥 Você comprou 1 carta. Jogue uma carta compatível ou passe a vez." : "O monte não tem cartas disponíveis. Passe a vez.", false);
      } else if (selectedActionId.startsWith(actionId("choose"))) {
        const cKey = selectedActionId.replace(actionId("choose"), "");
        spawnShockwave(el.discardStage);
        setBannerMessage(`🌈 Você escolheu a cor ${COLOR_LABELS[cKey]}!`, true);
      }

      await presentAcceptedResult(resp);
    } catch (err) {
      if (err.acceptedByServer) await recoverAfterPresentationError(err);
      else setBannerMessage(`Ação recusada pela engine: ${err.message}`, true);
    } finally {
      state.busy = false;
    }
  }

  async function handleEndTurn() {
    if (state.busy || !state.canEndTurn) return;
    if (state.endTurnCandidate?.actionId === actionId("accept")) {
      await handleExecuteAction(actionId("accept"), "Aceitou a penalidade");
      // Finish the user's Pass request using the newly authorized server command.
      if (state.canEndTurn && state.endTurnCandidate?.command?.actionType === "END_TURN") {
        return handleEndTurn();
      }
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
        payload: toCombatPayload(state.endTurnCandidate.command)
      });

      // Real card identities and draw movements come from the resolved commands.
      await presentAcceptedResult(resp);
    } catch (err) {
      clearTableFlight();
      if (err.acceptedByServer) await recoverAfterPresentationError(err);
      else setBannerMessage(`Erro ao passar turno: ${err.message}`, true);
    } finally {
      state.busy = false;
    }
  }

  // The command was already applied by the server; failures from here on are presentation/sync errors.
  async function presentAcceptedResult(resp) {
    try {
      await animateOpponentEvents(resp);
      await refreshArenaState();
    } catch (err) {
      err.acceptedByServer = true;
      throw err;
    }
  }

  async function recoverAfterPresentationError(err) {
    setBannerMessage(`A jogada foi aplicada, mas a mesa não pôde ser atualizada (${err.message}). Sincronizando...`, true);
    try {
      await refreshArenaState();
    } catch (retryErr) {
      setBannerMessage(`A jogada foi aplicada, mas a conexão com o HeroScript falhou: ${retryErr.message}`, true);
    }
  }

  function showOutcomeModal(status, victory = null) {
    if (typeof document !== "undefined" && document.body) document.body.dataset.gameState = "ended";
    suspendPlayerControls();
    el.wildModal.classList.add("hidden");
    el.outcomeModal.classList.remove("hidden");

    const isVictory = status === "VICTORY";
    showCalloutSplash(isVictory ? "🏆 VITÓRIA!" : "💔 DERROTA");
    el.outcomeBadge.textContent = isVictory ? "VITÓRIA!" : "DERROTA";
    el.outcomeTitle.textContent = isVictory ? "Você bateu primeiro!" : `${actorLabel(victory?.actorIds?.[0])} venceu!`;
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
        proxyEl.className = "clash-card-back dragging-card-proxy";
        proxyEl.style.width = `${baseRect.width}px`;
        proxyEl.style.height = `${baseRect.height}px`;
        proxyEl.innerHTML = '<div class="back-oval"><span>CLASH</span></div>';
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
          handleExecuteAction((state.legalByActionId.has(actionId("accept")) ? actionId("accept") : actionId("draw")), "Comprou cartas");
        }
        return;
      }

      const hot = !wasCancel && isPointOverHandDropZone(e.clientX, e.clientY, startY);
      if (hot && proxyEl && !el.drawCardBtn.disabled) {
        const dropRect = proxyEl.getBoundingClientRect();
        proxyEl.remove();
        proxyEl = null;
        cleanupDrawDrag();
        handleExecuteAction((state.legalByActionId.has(actionId("accept")) ? actionId("accept") : actionId("draw")), "Comprou cartas", dropRect);
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

  let resizeFrame = 0;
  window.addEventListener("resize", () => {
    cancelAnimationFrame(resizeFrame);
    resizeFrame = requestAnimationFrame(() => {
      if (!state.combatId) return;
      renderBotHandFan();
      if (!state.busy) renderPlayerHand(state.playerCards || [], 0);
    });
  });

  // Wire UI Events
  attachDrawPileDragAndClick();
  el.shoutClashBtn.addEventListener("click", () => handleExecuteAction(actionId("call"), "Gritou CLASH!"));
  el.endTurnBtn.addEventListener("click", () => handleEndTurn());
  el.autoEndTurnToggle.addEventListener("change", () => updateAutoTurn());

  function openModePicker() {
    if (state.busy) return;
    cancelAutoTurn();
    state.modeTrigger = document.activeElement;
    el.modeModal.classList.remove("hidden");
    el.modeModal.querySelector(".mode-choice").focus();
  }
  function closeModePicker() {
    el.modeModal.classList.add("hidden");
    (state.modeTrigger || el.newRunBtn).focus();
    if (state.combatId) updateAutoTurn();
  }
  el.newRunBtn.addEventListener("click", openModePicker);
  if (el.centerNewRunBtn) el.centerNewRunBtn.addEventListener("click", openModePicker);
  el.restartMatchBtn.addEventListener("click", openModePicker);
  el.closeModeBtn.addEventListener("click", closeModePicker);
  el.modeModal.addEventListener("click", event => { if (event.target === el.modeModal) closeModePicker(); });
  document.addEventListener("keydown", event => {
    if (event.key === "Escape" && !el.modeModal.classList.contains("hidden")) closeModePicker();
    if (event.key === "Tab" && !el.modeModal.classList.contains("hidden")) {
      const buttons = [...el.modeModal.querySelectorAll("button")];
      const index = buttons.indexOf(document.activeElement);
      event.preventDefault();
      buttons[(index + (event.shiftKey ? -1 : 1) + buttons.length) % buttons.length].focus();
    }
  });
  document.querySelectorAll(".mode-choice").forEach(button => button.addEventListener("click", () => {
    if (state.busy) return;
    el.modeModal.classList.add("hidden");
    el.seedInput.value = Math.floor(Math.random() * 899999 + 100000);
    initSettingAndStartRun(button.dataset.mode);
  }));

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
      const choice = actionId("choose") + btn.dataset.colorKey;
      handleExecuteAction(choice, `Escolheu cor (${btn.dataset.colorLabel})`);
    }

    btn.addEventListener("pointerup", () => triggerSectorChoice());
    btn.addEventListener("click", () => triggerSectorChoice());
  });

  // No run is created until a mode is chosen.
  suspendPlayerControls();
  renderDrawPile(null);
  el.revisionBadge.textContent = "pronto";
  renderBotHandTelemetry();
})();
