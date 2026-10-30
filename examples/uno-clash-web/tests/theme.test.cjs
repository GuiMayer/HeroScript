const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'theme.js'), 'utf8');
function load(saved, blocked = false) {
  const root = { dataset: {} }, attributes = {};
  const button = { setAttribute: (key, value) => attributes[key] = value, addEventListener: (name, fn) => button[name] = fn };
  const label = {}, icon = {}, values = new Map([['clash.theme', saved]]);
  let loaded;
  const context = vm.createContext({
    document: { documentElement: root, addEventListener: (name, fn) => loaded = fn,
      getElementById: id => ({ themeToggle: button, themeToggleLabel: label, themeToggleIcon: icon })[id] },
    localStorage: { getItem: key => { if (blocked) throw Error('Storage blocked'); return values.get(key); },
      setItem: (key, value) => { if (blocked) throw Error('Storage blocked'); values.set(key, value); } }
  });
  vm.runInContext(source, context);
  return { root, button, label, icon, attributes, values, ready: () => loaded() };
}
test('saved dark theme is applied before the document finishes loading', () => {
  const h = load('dark'); assert.equal(h.root.dataset.theme, 'dark'); h.ready();
  assert.equal(h.label.textContent, 'Modo claro'); assert.equal(h.attributes['aria-pressed'], 'true');
});
test('toggle changes the palette immediately and restores its saved preference on reload', () => {
  const h = load(); h.ready(); assert.equal(h.root.dataset.theme, 'light');
  h.button.click(); assert.equal(h.root.dataset.theme, 'dark'); assert.equal(h.values.get('clash.theme'), 'dark');
  const reloaded = load(h.values.get('clash.theme')); assert.equal(reloaded.root.dataset.theme, 'dark');
  h.button.click(); assert.equal(h.root.dataset.theme, 'light'); assert.equal(h.label.textContent, 'Modo escuro');
});
test('unavailable browser storage does not prevent switching themes', () => {
  const h = load(null, true); h.ready(); h.button.click(); assert.equal(h.root.dataset.theme, 'dark');
  h.button.click(); assert.equal(h.root.dataset.theme, 'light');
});
