const { runBrowserScenario } = require('./modes.browser.cjs');

async function scenario() {
  const report = document.querySelector('#ui-check'), hand = document.querySelector('#playerHandContainer');
  const pass = document.querySelector('#endTurnBtn'), banner = document.querySelector('#aiActionText');
  const params = new URLSearchParams(location.search), draw2 = params.get('effect') === 'draw2';
  const observed = [], splashes = [];
  const observer = new MutationObserver(() => observed.push({ text: banner.textContent,
    hand: hand.children.length, color: document.body.dataset.activeColor,
    reversed: document.querySelector('#colorOrbitRing').classList.contains('reversed'),
    left: document.querySelector('#leftHandFan').children.length,
    leftKnown: [...document.querySelectorAll('.telemetry-bot-card figcaption')].filter(node => node.textContent.startsWith('Bot da esquerda')).length,
    pile: Number(document.querySelector('#drawCardBtn').dataset.count) }));
  observer.observe(banner, { childList: true, subtree: true, characterData: true });
  const splash = document.querySelector('#calloutSplashInner');
  const splashObserver = new MutationObserver(() => splashes.push(splash.textContent));
  splashObserver.observe(splash, { childList: true, subtree: true, characterData: true });
  const wait = async (predicate, label) => {
    for (let i = 0; i < 1000; i++) {
      if (await predicate()) return;
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    throw Error(label + ': ' + JSON.stringify(observed));
  };
  const fx = setInterval(() => {
    for (const animation of document.querySelector('#fxLayer').getAnimations({ subtree: true })) {
      try { animation.finish(); } catch {}
    }
  }, 30);
  try {
    document.querySelector('#autoEndTurnToggle').checked = false;
    document.querySelector('#newRunBtn').click();
    document.querySelector('[data-mode="freeForAll"]').click();
    await wait(() => hand.children.length === 7 && document.body.dataset.gameState === 'playing', 'Setup');
    document.querySelector('#showBotCardsToggle').checked = true;
    document.querySelector('#showBotCardsToggle').dispatchEvent(new Event('change'));
    const card = [...hand.children].find(node => node.dataset.definitionId === 't_red_7');
    const rect = card.getBoundingClientRect(), pointer = { bubbles: true, button: 0, pointerId: 1, clientX: rect.x + rect.width / 2, clientY: rect.y + rect.height / 2 };
    card.dispatchEvent(new PointerEvent('pointerdown', pointer));
    window.dispatchEvent(new PointerEvent('pointerup', pointer));
    await wait(() => hand.children.length === 6 && !pass.disabled, 'Player landing');
    const oldNodes = [...hand.children];
    pass.click();
    await wait(() => observed.some(item => item.text.includes(draw2 ? 'comprou 2 carta(s)' : 'jogou Inverter')), 'All effects reached');
    await wait(() => !pass.disabled || !document.querySelector('#drawCardBtn').disabled, 'Return to player');
    const choice = observed.find(item => item.text.includes('escolheu Azul'));
    if (!choice || choice.hand !== 10 || choice.color !== 'blue') throw Error('+4 / chosen color delayed: ' + JSON.stringify(choice));
    const next = observed.find(item => item.text.includes('Bot da direita jogou') && item.color === 'blue');
    if (!next || next.hand !== 10 || next.pile !== 75) throw Error('Player purchase / draw pile not ready before next opponent: ' + JSON.stringify(next));
    const firstTop = observed.findIndex(item => item.text.includes('Bot do topo jogou'));
    const leftFour = observed.findIndex(item => item.text.includes('Bot da esquerda jogou Coringa +4'));
    if (firstTop <= leftFour || !splashes.includes('⊘ BLOQUEIO!')) throw Error('Skip did not follow the API seat sequence');
    if (draw2) {
      const accepted = observed.findIndex(item => item.text.includes('Bot da esquerda comprou 2 carta(s)'));
      if (accepted < 0 || !observed.slice(accepted + 1).some(item => item.left === 8 && item.leftKnown === 8 && item.pile === 73)) throw Error('+2 purchase not shown before next action');
      if (!splashes.includes('💥 COMPRA +2!') || !splashes.includes('📥 BOT COMPRA +2!')) throw Error('Missing +2 effect feedback');
    } else {
      const reverse = observed.findIndex(item => item.text.includes('jogou Inverter'));
      if (reverse < 0 || !observed.slice(reverse + 1).some(item => item.reversed)) throw Error('Reverse only appeared at final refresh');
      if (!splashes.includes('⇄ INVERTER!')) throw Error('Reverse splash missing');
    }
    if (oldNodes.some(node => ![...hand.children].includes(node))) throw Error('Original player cards were replaced');
    const base = params.get('api'), runId = document.querySelector('#inspRunId').textContent;
    const zones = await (await fetch(base + `/api/v1/runs/${runId}/card-zones`)).json();
    if (zones.zones.reduce((sum, zone) => sum + zone.count, 0) !== 108) throw Error('Shared pool changed');
    for (const [owner, selector] of [['seat_0', '#playerHandContainer'], ['seat_1', '#rightHandFan'], ['seat_2', '#aiHandFan'], ['seat_3', '#leftHandFan']]) {
      if (document.querySelector(selector).children.length !== zones.zones.find(zone => zone.zoneId === 'hand' && zone.ownerId === owner).count) throw Error('Visual hand differs from API: ' + owner);
    }
    report.textContent = `MODES_UI_PASS four players: immediate +4, color, draw pile, skip and ${draw2 ? '+2 acceptance' : 'reverse'}; physical hands match API`;
  } catch (error) { report.textContent = 'MODES_UI_FAIL ' + error.message; }
  finally { clearInterval(fx); observer.disconnect(); splashObserver.disconnect(); }
}

runBrowserScenario(scenario, { effect: process.argv[4] || 'reverse' }).catch(error => { console.error(error); process.exitCode = 1; });
