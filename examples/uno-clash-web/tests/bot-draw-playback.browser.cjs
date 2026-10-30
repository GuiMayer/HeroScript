const { runBrowserScenario } = require('./modes.browser.cjs');

// The bot-draw4 JSON fixture deals two real +4s to the bot, with no ordinary red card.
async function scenario() {
  const report = document.querySelector('#ui-check'), hand = document.querySelector('#playerHandContainer');
  const pass = document.querySelector('#endTurnBtn'), banner = document.querySelector('#aiActionText');
  const observed = [], purchases = [];
  const observer = new MutationObserver(() => {
    observed.push({ text: banner.textContent, count: hand.children.length });
  });
  observer.observe(banner, { childList: true, subtree: true, characterData: true });
  const wait = async (predicate, label) => {
    for (let i = 0; i < 1000; i++) {
      if (await predicate()) return;
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    throw Error(label + ': ' + banner.textContent);
  };
  const fx = setInterval(() => {
    for (const animation of document.querySelector('#fxLayer').getAnimations({ subtree: true })) {
      try { animation.finish(); } catch {}
    }
    if (hand.children.length && purchases.at(-1) !== hand.children.length) purchases.push(hand.children.length);
  }, 30);
  try {
    document.querySelector('#autoEndTurnToggle').checked = false;
    document.querySelector('#newRunBtn').click();
    document.querySelector('[data-mode="duel"]').click();
    await wait(() => hand.children.length === 7 && document.body.dataset.gameState === 'playing', 'Setup');
    const card = [...hand.children].find(node => node.dataset.definitionId === 'clash_red_7');
    const rect = card.getBoundingClientRect(), pointer = { bubbles: true, button: 0, pointerId: 1, clientX: rect.x + rect.width / 2, clientY: rect.y + rect.height / 2 };
    card.dispatchEvent(new PointerEvent('pointerdown', pointer));
    window.dispatchEvent(new PointerEvent('pointerup', pointer));
    await wait(() => hand.children.length === 6 && !pass.disabled, 'Player landing');
    const originalNodes = [...hand.children];
    pass.click();
    await wait(() => observed.filter(item => item.text.includes('jogou Coringa +4')).length >= 2, 'Two +4 announcements');
    const announcements = observed.filter(item => item.text.includes('jogou Coringa +4'));
    if (announcements[0].count !== 6 || announcements[1].count !== 10) throw Error('First +4 purchase was delayed: ' + JSON.stringify(announcements));
    await wait(() => hand.children.length === 14 && !pass.disabled, 'Second purchase and return to player');
    if (originalNodes.some(node => ![...hand.children].includes(node))) throw Error('Existing cards were replaced');
    if (!purchases.includes(10) || !purchases.includes(14)) throw Error('Missing intermediate physical hand: ' + purchases);
    const base = new URLSearchParams(location.search).get('api'), runId = document.querySelector('#inspRunId').textContent;
    const zones = await (await fetch(base + `/api/v1/runs/${runId}/card-zones`)).json();
    const actual = zones.zones.find(zone => zone.zoneId === 'hand' && zone.ownerId === 'player');
    const ids = new Set(actual.cards.map(card => card.cardInstanceId));
    if (actual.count !== 14 || [...hand.children].some(node => !ids.has(node.dataset.cardInstanceId))) throw Error('Visual cards differ from real shared deck');
    report.textContent = 'MODES_UI_PASS consecutive bot +4s: hand 6 -> 10 before next +4 -> 14; original nodes and real identities preserved';
  } catch (error) { report.textContent = 'MODES_UI_FAIL ' + error.message; }
  finally { clearInterval(fx); observer.disconnect(); }
}

runBrowserScenario(scenario).catch(error => { console.error(error); process.exitCode = 1; });
