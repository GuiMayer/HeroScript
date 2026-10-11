extends RefCounted
## Coordinates engine-published options. No nodes, styling, transport or combat math.
signal changed
const State = preload("res://scripts/ui/controllers/combat_ui_state.gd")
var state = State.new()
var presenter
var _session
var _playback
var _paused: Callable
var _alive := true
var _epoch := 0
func _init(session, playback, paused: Callable) -> void:
	_session = session
	_playback = playback
	_paused = paused
func bind(projection, scope: String, card_ids: Array) -> void:
	presenter = projection
	if state.scope != scope:
		state.clear()
		_epoch += 1
	state.scope = scope
	if state.selected_card not in card_ids: state.clear()
func locked() -> bool:
	return not _alive or State.phase(state.submitting or _session.busy, _session.synchronized,
		_playback.has_frames(), _paused.call(), state.selected_card) not in [State.InputState.READY, State.InputState.SELECTING]
func select_card(id: String) -> bool:
	if locked(): return false
	state.selected_card = id
	state.selected_target = ""
	changed.emit()
	return true
func target_options(actor_id: String) -> Array:
	if locked() or state.selected_card.is_empty(): return []
	var matches: Array = presenter.candidates(state.selected_card).filter(func(option): return actor_id in presenter.targets(option))
	if matches.is_empty(): return []
	state.selected_target = actor_id
	changed.emit()
	return matches
func cancel() -> bool:
	if state.submitting or _session.busy: return false
	state.clear()
	changed.emit()
	return true
func submit(candidate: Dictionary) -> bool:
	if locked() or candidate.is_empty(): return false
	# Reject a stale/unpublished local option; never manufacture or default a payment.
	var id := str(candidate.get("command", {}).get("cardInstanceId", ""))
	var options: Array = presenter.candidates(id)
	if candidate not in options: return false
	var ticket := _epoch
	state.submitting = true
	changed.emit()
	var accepted: bool = await _session.submit_candidate(candidate.duplicate(true))
	if not _alive or ticket != _epoch: return false
	state.submitting = false
	if accepted: state.clear()
	changed.emit()
	return accepted
func execute_zone_flow(payload: Dictionary) -> bool:
	if locked(): return false
	var ticket := _epoch
	state.submitting = true
	changed.emit()
	var accepted: bool = await _session.execute_run_command("INVOKE_CARD_ZONE_FLOW", payload.duplicate(true), str(payload.get("flowId", "")))
	if not _alive or ticket != _epoch: return false
	state.submitting = false
	if accepted: state.clear()
	changed.emit()
	return accepted
func reconnect() -> bool: return await _session.refresh()
func inspect_hand(targets: Array) -> Dictionary: return await _session.inspect_hand(targets)
func dispose() -> void:
	_alive = false
	_epoch += 1


func submit_option(reference_id: String, card_id: String) -> bool:
	for option in presenter.action_options(card_id):
		if option.reference == reference_id: return await submit(option.read("candidate"))
	return false
