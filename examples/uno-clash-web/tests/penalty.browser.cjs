const { runBrowserScenario } = require('./modes.browser.cjs');

// Run against the stack / stack-four JSON fixtures, with real cards and API effects.
async function scenario() {
  const report = document.querySelector('#ui-check');
  const params = new URLSearchParams(location.search), base = params.get('api');
  const four = params.get('testMode') === 'freeForAll';
  const player = four ? 'seat_0' : 'player', acceptId = four ? 't_accept' : 'clash_accept_draw2';
  const debtId = four ? 't_debt' : 'clash_draw2_pending', energyId = four ? 't_energy' : 'clash_energy';
  const expected = four ? 8 : 4, requests = [], splashes = [];
  const originalFetch = window.fetch.bind(window);
  window.fetch = async (url, options) => {
    const response = await originalFetch(url, options);
    if (String(url).endsWith('/commands') && options?.body) {
      requests.push({ request: JSON.parse(options.body), response: await response.clone().json() });
    }
    return response;
  };
  const splashNode = document.querySelector('#calloutSplashInner');
  const observer = new MutationObserver(() => splashes.push(splashNode.textContent));
  observer.observe(splashNode, { subtree: true, characterData: true, childList: true });
  const wait = async (predicate, label) => {
    for (let i = 0; i < 1000; i++) {
      if (await predicate()) return;
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    throw Error(label + ': ' + document.querySelector('#aiActionText').textContent);
  };
  const json = async route => (await originalFetch(base + route)).json();
  const activate = node => {
    const r = node.getBoundingClientRect(), options = { bubbles: true, button: 0, pointerId: 1, clientX: r.x + r.width / 2, clientY: r.y + r.height / 2 };
    node.dispatchEvent(new PointerEvent('pointerdown', options));
    window.dispatchEvent(new PointerEvent('pointerup', options));
  };
  const fx = setInterval(() => {
    for (const animation of document.querySelector('#fxLayer').getAnimations({ subtree: true })) {
      try { animation.finish(); } catch {}
    }
  }, 50);
  try {
    document.querySelector('#autoEndTurnToggle').checked = false;
    document.querySelector('#seedInput').value = '3';
    document.querySelector('#newRunBtn').click();
    document.querySelector(`[data-mode="${four ? 'freeForAll' : 'duel'}"]`).click();
    const hand = document.querySelector('#playerHandContainer'), pass = document.querySelector('#endTurnBtn'), pile = document.querySelector('#drawCardBtn');
    await wait(() => hand.children.length === 7 && document.body.dataset.gameState === 'playing', 'Setup');
    const card = [...hand.children].find(node => node.dataset.definitionId === (four ? 't_red_draw2' : 'clash_red_draw2'));
    if (!card) throw Error('Fixture has no red +2 card');
    activate(card);
    await wait(() => hand.children.length === 6 && !pass.disabled, 'Player +2 landing');
    pass.click();
    await wait(() => !pass.disabled && pile.disabled && requests.some(item => item.response.state?.resolution?.frames?.some(frame =>
      frame.payload?.cardResolution?.applications?.some(app => (app.ResourceId || app.resourceId) === debtId && (app.CurrentValue ?? app.currentValue) === expected))), 'Accumulated response');
    const runId = document.querySelector('#inspRunId').textContent, combatId = document.querySelector('#inspCombatId').textContent;
    const zones = await json(`/api/v1/runs/${runId}/card-zones`);
    const before = zones.zones.find(zone => zone.zoneId === 'hand' && zone.ownerId === player).count;
    const previousRequestCount = requests.length;
    pass.click();
    await wait(() => requests.slice(previousRequestCount).some(item => item.request.type === 'END_TURN'), 'Pass must also end activation');
    await wait(() => !pass.disabled || !pile.disabled || hand.querySelector('.is-legal'), 'Opponent resolution');
    const submitted = requests.slice(previousRequestCount);
    const accept = submitted.find(item => item.request.payload?.powerId === acceptId);
    if (!accept || accept.request.type !== 'EXECUTE_ACTION') throw Error('Pass did not submit legal acceptance');
    const ownFrames = accept.response.state.resolution.frames.filter(frame => {
      const cmd = frame.payload?.resolvedCommand || frame.payload?.command;
      return (cmd?.ActorId || cmd?.actorId) === player;
    });
    const applications = ownFrames.flatMap(frame => frame.payload.abilityResolution?.applications || []);
    const drawn = applications.flatMap(app => app.CardZoneSteps || app.cardZoneSteps || [])
      .filter(step => (step.TargetAddress || step.targetAddress || '').endsWith(`::${player}::hand`))
      .flatMap(step => step.InstanceIds || step.instanceIds || []);
    if (new Set(drawn).size !== expected) throw Error(`Expected ${expected} penalty cards, got ${drawn.length}`);
    if (!applications.some(app => (app.ResourceId || app.resourceId) === energyId && (app.CurrentValue ?? app.currentValue) === 0)) throw Error('Penalty did not consume the turn');
    const end = submitted.find(item => item.request.type === 'END_TURN');
    if (!end.response.state.resolution.frames.some(frame => {
      const cmd = frame.payload?.resolvedCommand || frame.payload?.command;
      return cmd && (cmd.ActorId || cmd.actorId) !== player && (cmd.ActionType || cmd.actionType) === 'PLAY_CARD';
    })) throw Error('Opponents did not take their turn after acceptance');
    const after = await json(`/api/v1/runs/${runId}/card-zones`);
    if (after.zones.find(zone => zone.zoneId === 'hand' && zone.ownerId === player).count !== before + expected) throw Error('Wrong physical hand count after accepting');
    if (after.zones.reduce((sum, zone) => sum + zone.count, 0) !== 108) throw Error('Shared pool lost cards');
    for (let value = 2; value <= expected; value += 2) {
      if (!splashes.includes(`💥 COMPRA +${value}!`)) throw Error(`Missing accumulated +${value} animation: ${splashes.join(', ')}`);
    }
    report.textContent = `MODES_UI_PASS ${four ? 'four-player' : 'duel'}: Pass bought ${expected}, ended activation, opponents acted; splashes follow recorded debt`;
  } catch (error) { report.textContent = 'MODES_UI_FAIL ' + error.message; }
  finally { clearInterval(fx); observer.disconnect(); }
}

runBrowserScenario(scenario, { testMode: process.argv[4] || 'duel' }).catch(error => { console.error(error); process.exitCode = 1; });
