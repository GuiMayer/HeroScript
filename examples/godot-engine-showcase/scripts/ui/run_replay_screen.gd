extends "res://scripts/ui/screen_view.gd"
## Adapter between read-only replay coordination and a passive view.
var router
var controller
var view
var replay_run_id := ""
var return_to_history := false
var _inspection_ticket
var _inspection_dialog: Window
var _visual_replay
var _visual_sequence := -1
var _visual_ticket
var _visual_epoch := 0
var _visual_mode := false
var entries: Array:
	get: return controller.entries if controller else []
var selected_index: int:
	get: return controller.selected_index if controller else -1
var playing: bool:
	get: return controller.playing if controller else false
var play_button: Button:
	get: return view.play_button
func setup(owner, run_id: String, from_history := false) -> void:
	router = owner; mount(owner.visual_context); replay_run_id = run_id; return_to_history = from_history
	controller = preload("res://scripts/ui/controllers/run_replay_controller.gd").new()
	controller.setup(GameSession, run_id, visual_context.translator)
	view = preload("res://scripts/ui/run_replay_view.gd").new()
	view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(view); view.setup(visual_context, from_history)
	view.intent_requested.connect(_intent); controller.changed.connect(_update)
	_update(); controller.start.call_deferred()
func _update() -> void:
	if _visual_replay != null and (selected_index < 0 or int(entries[selected_index].sequence) != _visual_sequence): _cancel_visual()
	if is_instance_valid(view) and is_inside_tree(): view.apply_model(controller.model())
func _intent(action: String, arguments: Array) -> void:
	GameAudio.ui()
	match action:
		"select": _stop_and_select(arguments[0])
		"restart": _restart()
		"previous": _previous()
		"next": _next()
		"more": _more()
		"inspect": _inspect()
		"watch": _watch()
		"toggle_play": _toggle_play()
		"verify": _verify()
		"back": _back()
func _select(index: int) -> void: await controller.select(index)
func _stop_and_select(index: int) -> void:
	controller.stop(); await controller.select(index)
	if _visual_mode and is_inside_tree(): await _watch()
func _restart() -> void: await _stop_and_select(0)
func _previous() -> void: await _stop_and_select(maxi(0, selected_index - 1))
func _next() -> void:
	controller.stop(); await controller.navigate(1)
	if _visual_mode and is_inside_tree(): await _watch()
func _more() -> void:
	await controller.load_more()
	if is_inside_tree() and selected_index < 0 and not entries.is_empty(): await controller.select(0)
func playback_delay() -> float: return (.18 if Preferences.reduced_motion else .7) / maxf(Preferences.animation_speed, .25)
func _playback_active(epoch: int) -> bool:
	return is_inside_tree() and controller != null and controller.valid() and playing and epoch == controller.play_epoch
func _toggle_play() -> void:
	if not is_inside_tree() or controller == null or not controller.valid(): return
	if playing:
		controller.stop()
		if _visual_replay != null: _visual_replay.set_paused(true)
		return
	if _visual_replay != null: _visual_replay.set_paused(false)
	controller.playing = true; _update()
	var epoch: int = controller.play_epoch
	while _playback_active(epoch):
		if _visual_replay != null and _visual_replay.blocking(): await _visual_replay.settled
		if not _playback_active(epoch): return
		if not await controller.navigate(1): break
		if not _playback_active(epoch): return
		if _visual_mode: await _watch()
		if not _playback_active(epoch): return
		await get_tree().create_timer(playback_delay()).timeout
		if not _playback_active(epoch): return
	if _playback_active(epoch): controller.stop()

func _cancel_visual() -> void:
	_visual_epoch += 1
	if _visual_ticket != null and not _visual_ticket.done: _visual_ticket.cancel()
	if is_instance_valid(_visual_replay): _visual_replay.dispose(); _visual_replay.queue_free()
	_visual_replay = null
	if is_instance_valid(view): view.visual_scroll.hide(); view.frame_text.show()
func _watch() -> void:
	if not controller.inspection_allowed or selected_index < 0: return
	_visual_mode = true
	_cancel_visual()
	var epoch := _visual_epoch; var sequence := int(entries[selected_index].sequence)
	_visual_ticket = GameSession.begin_replay_inspection(replay_run_id, sequence)
	var response: Dictionary = await _visual_ticket.result()
	if not is_inside_tree() or not controller.valid() or epoch != _visual_epoch or selected_index < 0 or int(entries[selected_index].sequence) != sequence: return
	if not response.get("ok", false):
		if not response.get("cancelled", false): router.show_error(I18n.error(response))
		return
	if str(response.data.get("runId", "")) != replay_run_id or int(response.data.get("sequence", -1)) != sequence: return
	if JSON.stringify(response.data).to_utf8_buffer().size() > controller.max_cache_bytes: router.show_toast(I18n.text("Visual replay payload is too large.")); return
	_visual_sequence = sequence
	_visual_replay = preload("res://scripts/ui/historical_beat_view.gd").new(visual_context.derive(visual_context.profile))
	view.visual_slot.add_child(_visual_replay); view.visual_scroll.show(); view.frame_text.hide()
	_visual_replay.load_commit(response.data, router.presentation)
func _verify() -> void:
	if controller.verifying: return
	controller.verifying = true; _update()
	router.show_toast(I18n.text("Verifying replay…"))
	var result: Dictionary = await GameSession.verify_run(replay_run_id)
	if not is_inside_tree(): return
	controller.verifying = false; _update()
	if result.ok:
		var valid := bool(result.data.get("isValid", result.data.get("valid", false)))
		router.show_toast(I18n.text("Deterministic replay verified.") if valid else I18n.text("Replay diverged."), not valid)
	else: router.show_error(I18n.error(result))
func _back() -> void:
	controller.stop()
	if return_to_history: router.show_history()
	else: router.open_game()
func _inspect() -> void:
	if not controller.inspection_allowed or selected_index < 0: return
	var sequence := int(entries[selected_index].sequence)
	if _inspection_ticket != null and not _inspection_ticket.done: _inspection_ticket.cancel()
	_inspection_ticket = GameSession.begin_replay_inspection(replay_run_id, sequence)
	var response: Dictionary = await _inspection_ticket.result()
	if not is_inside_tree() or not controller.valid() or selected_index < 0 or int(entries[selected_index].sequence) != sequence: return
	if not response.get("ok", false):
		if not response.get("cancelled", false): router.show_error(I18n.error(response))
		return
	if is_instance_valid(_inspection_dialog): _inspection_dialog.queue_free()
	_inspection_dialog = preload("res://scripts/ui/inspection_dialog.gd").new(visual_context)
	add_child(_inspection_dialog)
	_inspection_dialog.setup(I18n.text("Show engine details"), JSON.stringify(response.data, "  "))
	_inspection_dialog.popup_centered_clamped(Vector2i(1000, 650), .9)
func dispose() -> void:
	_cancel_visual()
	if controller: controller.dispose()
	if _inspection_ticket != null and not _inspection_ticket.done: _inspection_ticket.cancel()
	if is_instance_valid(_inspection_dialog): _inspection_dialog.queue_free()
