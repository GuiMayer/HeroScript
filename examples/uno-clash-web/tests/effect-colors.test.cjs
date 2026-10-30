const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const modes = require('../game-modes.js');
const source = fs.readFileSync(path.join(__dirname, '..', 'app.js'), 'utf8');

for (const mode of Object.values(modes)) {
  test(`${mode.label}: card color, pending wild and choice follow each recorded frame`, () => {
    const context = vm.createContext({ state: {}, document: { body: { dataset: { activeColor: 'red' } } },
      el: { activeColorLabel: {} }, COLOR_LABELS: { blue: 'Azul', green: 'Verde' },
      modeConfig: () => mode, playerId: () => mode.playerId });
    const start = source.indexOf('  function presentEffectColor(');
    vm.runInContext(source.slice(start, source.indexOf('  async function animateOpponentEvents(', start)), context);
    const app = (color, value, owner = mode.playerId) => ({ TargetEntityId: owner, ResourceId: mode.resources.color + color, CurrentValue: value });
    context.presentEffectColor({ cardResolution: { applications: [app('red', 0), app('blue', 1)] } });
    assert.equal(context.document.body.dataset.activeColor, 'blue');
    assert.equal(context.el.activeColorLabel.textContent, 'Azul');
    context.presentEffectColor({ cardResolution: { applications: [app('blue', 0)] } });
    assert.equal(context.document.body.dataset.activeColor, 'neutral');
    context.presentEffectColor({ abilityResolution: { applications: [app('green', 1)] } });
    assert.equal(context.document.body.dataset.activeColor, 'green');
    context.presentEffectColor({ abilityResolution: { applications: [app('red', 1, 'another-actor')] } });
    assert.equal(context.document.body.dataset.activeColor, 'green');
  });
}
