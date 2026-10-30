const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { pathToFileURL } = require('node:url');

// Uses the actual frontend and API; no game decisions or simulated server responses.
async function scenario() {
  const report = document.querySelector('#ui-check');
  document.querySelector('#autoEndTurnToggle').checked = false;
  const wait = async (predicate, label) => {
    for (let i = 0; i < 1000; i++) {
      if (predicate()) return;
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    throw Error(label + ': ' + document.querySelector('#aiActionText').textContent);
  };
  const hand = () => document.querySelector('#playerHandContainer');
  const pass = () => document.querySelector('#endTurnBtn');
  const activate = card => {
    const r = card.getBoundingClientRect();
    const options = { bubbles: true, button: 0, pointerId: 1, clientX: r.x + r.width / 2, clientY: r.y + r.height / 2 };
    card.dispatchEvent(new PointerEvent('pointerdown', options));
    window.dispatchEvent(new PointerEvent('pointerup', options));
  };
  const fx = setInterval(() => {
    for (const animation of document.querySelector('#fxLayer').getAnimations({ subtree: true })) {
      try { animation.finish(); } catch {}
    }
  }, 50);
  try {
    if (hand().children.length || document.querySelector('.table-card') || document.querySelectorAll('.ai-mini-card').length) throw Error('Initial table not empty');
    const idleOrbit = getComputedStyle(document.querySelector('#colorOrbitRing'));
    if (idleOrbit.animationName !== 'orbitSpin' || idleOrbit.animationDuration !== '120s') throw Error('Idle ring does not spin slowly');
    if (document.querySelectorAll('#drawCardBtn .clash-card-back:not(.pile-layer-hidden)').length !== 4) throw Error('Initial deck absent');
    document.querySelector('#newRunBtn').click();
    if (document.querySelector('#modeModal').classList.contains('hidden')) throw Error('Radial menu missing');
    window.modeMenuReady = true;
    await wait(() => window.continueModeTest, 'Menu screenshot checkpoint');
    document.querySelector('#closeModeBtn').click();
    if (hand().children.length) throw Error('Cancel started a game');
    document.querySelector('#newRunBtn').click();
    document.querySelector('[data-mode="freeForAll"]').click();
    await wait(() => hand().children.length === 7 && document.body.dataset.gameState === 'playing', 'Four-player setup');
    await new Promise(resolve => setTimeout(resolve, 400));
    for (const id of ['aiHandFan', 'leftHandFan', 'rightHandFan']) {
      if (document.getElementById(id).children.length !== 7) throw Error('Missing hand: ' + id);
    }
    for (const [id, facing] of [['leftHandFan', 1], ['rightHandFan', -1]]) {
      const fan = document.getElementById(id), card = fan.firstElementChild.getBoundingClientRect();
      const transform = new DOMMatrix(getComputedStyle(fan.parentElement).transform);
      if (card.height < card.width * 0.6) throw Error('Side card is lying sideways: ' + id);
      if (transform.m31 * facing <= 0) throw Error('Side hand does not face inward upright: ' + id);
    }
    const left = document.querySelector('#leftHandFan');
    const outside = left.firstElementChild.getBoundingClientRect(), inside = left.lastElementChild.getBoundingClientRect();
    const run = inside.x + inside.width / 2 - outside.x - outside.width / 2;
    if (outside.bottom - inside.bottom < run * 0.08) throw Error('Left lower edge does not recede inward');
    if (document.querySelector('#drawCardBtn').dataset.count !== '79') throw Error('Wrong shared draw count');
    if (document.documentElement.scrollWidth > innerWidth) throw Error('Four-player horizontal overflow');
    const theme = document.querySelector('#themeToggle');
    const handNodes = [...hand().children], tableNode = document.querySelector('.table-card');
    const runBeforeTheme = document.querySelector('#inspRunId').textContent;
    const buttonBounds = theme.getBoundingClientRect();
    theme.click();
    if (document.documentElement.dataset.theme !== 'dark' || theme.getAttribute('aria-pressed') !== 'true') throw Error('Dark theme did not activate');
    if (getComputedStyle(document.body).backgroundColor === 'rgb(255, 255, 255)') throw Error('Dark theme background stayed white');
    if ([...hand().children].some((node, index) => node !== handNodes[index]) || document.querySelector('.table-card') !== tableNode) throw Error('Theme rebuilt cards');
    if (document.querySelector('#inspRunId').textContent !== runBeforeTheme) throw Error('Theme restarted game');
    window.darkThemeReady = true;
    await wait(() => window.continueThemeTest, 'Dark theme screenshot checkpoint');
    theme.click();
    if (document.documentElement.dataset.theme !== 'light' || getComputedStyle(document.body).backgroundColor !== 'rgb(255, 255, 255)') throw Error('Light theme did not restore white');
    if (Math.abs(theme.getBoundingClientRect().width - buttonBounds.width) > 0.5) throw Error('Theme button changed width');
    const legal = hand().querySelector('.is-legal');
    if (legal) {
      activate(legal);
      await wait(() => hand().children.length === 6, 'Play in four-player mode');
      await wait(() => !pass().disabled || !document.querySelector('#wildModal').classList.contains('hidden'), 'Next input after landing');
      if (!document.querySelector('#wildModal').classList.contains('hidden')) document.querySelector('[data-color-key="red"]').click();
      await wait(() => !pass().disabled, 'Pass after play');
    } else {
      activate(document.querySelector('#drawCardBtn'));
      await wait(() => hand().children.length === 8, 'Draw in four-player mode');
    }
    if (!pass().disabled) {
      pass().click();
      await wait(() => !pass().disabled || hand().querySelector('.is-legal') || !document.querySelector('#drawCardBtn').disabled, 'Bots return input');
    }
    await new Promise(resolve => setTimeout(resolve, 400));
    const runId = document.querySelector('#inspRunId').textContent;
    const base = new URLSearchParams(location.search).get('api');
    const zones = await (await fetch(base + '/api/v1/runs/' + runId + '/card-zones')).json();
    const combatId = document.querySelector('#inspCombatId').textContent;
    const combat = await (await fetch(base + '/api/v1/combats/' + combatId)).json();
    const rotation = getComputedStyle(document.querySelector('#colorOrbitRing')).animationDirection;
    if (rotation !== (combat.activation.circular.direction === 'Reverse' ? 'reverse' : 'normal')) throw Error('Orbit direction differs from engine direction');
    if (zones.zones.reduce((total, zone) => total + zone.count, 0) !== 108) throw Error('Lost cards');
    for (const fan of document.querySelectorAll('[data-seat]')) {
      const zone = zones.zones.find(zone => zone.zoneId === 'hand' && zone.ownerId === fan.dataset.actorId);
      if (fan.children.length !== Math.min(zone.count, 20)) throw Error('Visual hand differs from API');
    }
    window.fourPlayerReady = true;
    await wait(() => window.continueBrowserTest, 'Screenshot checkpoint');
    document.querySelector('#newRunBtn').click();
    document.querySelector('[data-mode="duel"]').click();
    await wait(() => hand().children.length === 7 && document.body.dataset.gameMode === 'duel' && document.body.dataset.gameState === 'playing', 'Duel setup');
    if (document.querySelector('#drawCardBtn').dataset.count !== '93') throw Error('Wrong duel draw count');
    if (document.documentElement.scrollWidth > innerWidth) throw Error('Duel horizontal overflow');
    if ([...document.querySelectorAll('.side-opponent')].some(node => !node.classList.contains('hidden'))) throw Error('Side hands remain in duel');
    const outcome = document.querySelector('#outcomeModal'), picker = document.querySelector('#modeModal');
    outcome.classList.remove('hidden');
    document.querySelector('#restartMatchBtn').click();
    if (picker.classList.contains('hidden') || Number(getComputedStyle(picker).zIndex) <= Number(getComputedStyle(outcome).zIndex)) throw Error('Mode menu covered by match result');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    if (!picker.classList.contains('hidden')) throw Error('Mode menu did not close with Escape');
    outcome.classList.add('hidden');
    report.textContent = 'MODES_UI_PASS';
  } catch (error) { report.textContent = 'MODES_UI_FAIL ' + error.message; }
  finally { clearInterval(fx); }
}

async function main(browserScenario = scenario, extraQuery = {}) {
  const repo = path.resolve(__dirname, '..');
  const output = path.join(repo, '.runtime/clash-modes-ui');
  fs.mkdirSync(output, { recursive: true });
  const frontend = path.resolve(__dirname, '..');
  const html = fs.readFileSync(path.join(frontend, 'index.html'), 'utf8')
    .replace('<head>', '<head><base href="' + pathToFileURL(frontend).href + '/">')
    .replace('</body>', '<pre id="ui-check" style="position:fixed;bottom:0;background:black;color:white;z-index:999">Checking...</pre><script>(' + browserScenario.toString() + ')()</script></body>');
  fs.writeFileSync(path.join(output, 'game.html'), html);
  const profile = fs.mkdtempSync(path.join(output, 'profile-'));
  const chrome = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', ['--headless', '--no-first-run', '--disable-extensions', '--hide-scrollbars', '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'], { windowsHide: true, stdio: 'ignore' });
  let ws;
  try {
    let tabs;
    for (let i = 0; i < 100; i++) {
      try {
        const port = Number(fs.readFileSync(path.join(profile, 'DevToolsActivePort'), 'utf8').split('\n')[0]);
        tabs = await (await fetch('http://127.0.0.1:' + port + '/json')).json();
        if (tabs.some(tab => tab.type === 'page')) break;
      } catch {}
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    ws = new WebSocket(tabs.find(tab => tab.type === 'page').webSocketDebuggerUrl);
    await new Promise(resolve => ws.addEventListener('open', resolve, { once: true }));
    let id = 0; const pending = new Map();
    ws.addEventListener('message', event => {
      const data = JSON.parse(event.data);
      if (data.id) { const item = pending.get(data.id); pending.delete(data.id); data.error ? item.reject(Error(data.error.message)) : item.resolve(data.result); }
    });
    const call = (method, params = {}) => new Promise((resolve, reject) => { const next = ++id; pending.set(next, { resolve, reject }); ws.send(JSON.stringify({ id: next, method, params })); });
    const width = Number(process.argv[3] || 1440), height = width < 760 ? 844 : 1000;
    await call('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: width < 760 });
    await call('Page.navigate', { url: pathToFileURL(path.join(output, 'game.html')).href + '?' + new URLSearchParams({ api: process.argv[2] || 'http://localhost:15260', ...extraQuery }) });
    let saved = false, menuSaved = false, darkSaved = false;
    for (let i = 0; i < 1800; i++) {
      const result = await call('Runtime.evaluate', { expression: 'JSON.stringify({text:document.querySelector("#ui-check")?.textContent, ready:window.fourPlayerReady, menu:window.modeMenuReady, dark:window.darkThemeReady})', returnByValue: true });
      const state = JSON.parse(result.result.value || '{}');
      if (state.dark && !darkSaved) {
        const shot = await call('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
        fs.writeFileSync(path.join(output, 'dark-' + width + '.png'), Buffer.from(shot.data, 'base64'));
        await call('Runtime.evaluate', { expression: 'window.continueThemeTest=true' }); darkSaved = true;
      }
      if (state.menu && !menuSaved) {
        const shot = await call('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
        fs.writeFileSync(path.join(output, 'menu-' + width + '.png'), Buffer.from(shot.data, 'base64'));
        await call('Runtime.evaluate', { expression: 'window.continueModeTest=true' }); menuSaved = true;
      }
      if (state.ready && !saved) {
        const shot = await call('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
        fs.writeFileSync(path.join(output, 'four-' + width + '.png'), Buffer.from(shot.data, 'base64'));
        await call('Runtime.evaluate', { expression: 'window.continueBrowserTest=true' }); saved = true;
      }
      if (state.text?.startsWith('MODES_UI_')) { console.log(state.text); if (!state.text.startsWith('MODES_UI_PASS')) process.exitCode = 1; break; }
      if (i === 1799) throw Error('Browser test timeout');
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    await call('Browser.close');
  } finally { ws?.close(); chrome.kill(); }
}
module.exports = { runBrowserScenario: main };
if (require.main === module) main().catch(error => { console.error(error); process.exitCode = 1; });
