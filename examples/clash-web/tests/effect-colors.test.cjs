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
  test(`${mode.label}: wild card on table and pending flight update color and bounce when chosen`, () => {
    let flightAnimated = false;
    const pendingCard = {
      dataset: { isWild: 'true', color: 'wild', endX: '100', endY: '200', scaleX: '1', scaleY: '1' },
      classList: {
        contains: cls => cls === 'flying-card',
        remove() {},
        add() {}
      },
      animate(keyframes, options) {
        flightAnimated = true;
        assert.equal(options.duration, 380);
        return {};
      }
    };
    const classes = new Set(['table-card']);
    const tableCard = {
      dataset: { isWild: 'true', color: 'wild' },
      classList: {
        contains: cls => classes.has(cls),
        remove: cls => classes.delete(cls),
        add: cls => classes.add(cls)
      },
      offsetWidth: 100
    };
    const context = vm.createContext({
      state: { pendingTableFlight: pendingCard },
      document: { body: { dataset: { activeColor: 'neutral' } } },
      el: {
        activeColorLabel: {},
        discardStage: { querySelector: () => tableCard }
      },
      COLOR_LABELS: { blue: 'Azul', green: 'Verde', red: 'Vermelho', yellow: 'Amarelo' },
      modeConfig: () => mode,
      playerId: () => mode.playerId
    });
    const start = source.indexOf('  function presentEffectColor(');
    vm.runInContext(source.slice(start, source.indexOf('  async function animateOpponentEvents(', start)), context);

    context.presentEffectColor(null, 'blue');

    assert.equal(context.document.body.dataset.activeColor, 'blue');
    assert.equal(context.el.activeColorLabel.textContent, 'Azul');
    assert.equal(pendingCard.dataset.color, 'blue');
    assert.equal(flightAnimated, true);
    assert.equal(tableCard.dataset.color, 'blue');
    assert.equal(classes.has('pop-land'), true);
  });
}
