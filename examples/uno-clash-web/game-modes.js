// Presentation bindings only. Legality, targets, order, effects and outcomes come from the API.
const ClashModes = Object.freeze({
  duel: {
    label: 'Duelo', settingId: 'clash', playerId: 'player',
    seats: { top: 'dealer' },
    actions: { setup: 'clash_setup_table', draw: 'clash_draw_card', accept: 'clash_accept_draw2',
      call: 'clash_shout_clash', catch: 'clash_catch_missed_clash', choose: 'clash_choose_' },
    resources: { energy: 'clash_energy', choice: 'wild_color_token', called: 'clash_called', color: 'color_', debt: 'clash_draw2_pending' }
  },
  freeForAll: {
    label: 'Todos contra Todos', settingId: 'circular-table', playerId: 'seat_0',
    seats: { right: 'seat_1', top: 'seat_2', left: 'seat_3' },
    actions: { draw: 't_draw', accept: 't_accept', call: 't_call', catch: 't_catch', choose: 't_choose_' },
    resources: { energy: 't_energy', choice: 't_choice', called: 't_called', color: 't_color_', debt: 't_debt' }
  }
});
const UnoModes = ClashModes;
if (typeof module !== 'undefined') module.exports = ClashModes;
if (typeof window !== 'undefined') {
  window.ClashModes = ClashModes;
  window.UnoModes = ClashModes;
}
