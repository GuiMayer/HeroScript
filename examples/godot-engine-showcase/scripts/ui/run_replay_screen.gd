extends VBoxContainer
## Read-only playback over persisted run commits. No gameplay command is submitted here.

var router
var replay_run_id := ""
var return_to_history := false
var entries: Array = []
var selected_index := -1
var list: ItemList
var heading: Label
var progress: ProgressBar
var state_text: RichTextLabel
var frame_text: RichTextLabel
var play_button: Button
var previous_button: Button
var next_button: Button
var verify_button: Button
var commit_cache := {}
var request_epoch := 0
var playing := false

func setup(owner, run_id: String, from_history := false) -> void:
	router = owner
	replay_run_id = run_id
	return_to_history = from_history
	add_theme_constant_override("separation", 10)
	var header := HBoxContainer.new()
	header.add_child(AppTheme.title(I18n.text("JOURNEY REPLAY"), 30))
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(push)
	header.add_child(_button(I18n.text("BACK TO HISTORY") if return_to_history else I18n.text("BACK TO RESULT"), _back))
	add_child(header)
	add_child(AppTheme.muted(I18n.text("This is the immutable recording produced by the engine. Playback only moves a local viewing cursor.")))
	var body := HSplitContainer.new()
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(body)
	var left := VBoxContainer.new()
	left.custom_minimum_size.x = 390
	left.add_child(AppTheme.muted(I18n.text("COMMAND TIMELINE"), 13))
	list = ItemList.new()
	list.name = "ReplayCommandList"
	list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	list.item_selected.connect(func(index): _stop_and_select(index))
	left.add_child(list)
	body.add_child(AppTheme.panel(left))
	var right := VBoxContainer.new()
	right.add_theme_constant_override("separation", 8)
	right.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	heading = AppTheme.title(I18n.text("Loading replay…"), 24, AppTheme.GOLD)
	right.add_child(heading)
	progress = ProgressBar.new()
	progress.name = "ReplayProgress"
	progress.show_percentage = false
	right.add_child(progress)
	state_text = RichTextLabel.new()
	state_text.bbcode_enabled = true
	state_text.fit_content = true
	state_text.custom_minimum_size.y = 170
	right.add_child(state_text)
	frame_text = RichTextLabel.new()
	frame_text.bbcode_enabled = true
	frame_text.size_flags_vertical = Control.SIZE_EXPAND_FILL
	frame_text.custom_minimum_size.y = 160
	right.add_child(frame_text)
	var controls := HFlowContainer.new()
	controls.add_child(_button(I18n.text("RESTART"), _restart))
	previous_button = _button(I18n.text("PREVIOUS"), _previous)
	controls.add_child(previous_button)
	play_button = _button(I18n.text("PLAY"), _toggle_play)
	play_button.name = "ReplayPlayButton"
	controls.add_child(play_button)
	next_button = _button(I18n.text("NEXT"), _next)
	controls.add_child(next_button)
	verify_button = _button(I18n.text("VERIFY INTEGRITY"), _verify)
	controls.add_child(verify_button)
	right.add_child(controls)
	body.add_child(AppTheme.panel(right))
	_set_controls(false)
	call_deferred("_load")

func _load() -> void:
	var cursor := 0
	for _page in 20:
		var response: Dictionary = await GameSession.replay_timeline(replay_run_id, cursor, 200)
		if not is_inside_tree(): return
		if not response.ok:
			heading.text = I18n.text("Replay unavailable")
			state_text.text = I18n.error(response)
			return
		var page: Array = response.data.get("items", [])
		for entry in page: entries.append(entry.duplicate(true))
		var next_cursor := int(response.data.get("nextCursor", cursor))
		if page.is_empty() or next_cursor <= cursor: break
		cursor = next_cursor
	list.clear()
	for entry in entries:
		list.add_item("#%03d  •  %s\n%s %s" % [int(entry.get("sequence", 0)), _command_name(str(entry.get("commandType", ""))), I18n.text("Step"), int(entry.get("step", 0))])
	progress.max_value = maxi(entries.size() - 1, 1)
	if entries.is_empty():
		heading.text = I18n.text("Replay unavailable")
		state_text.text = I18n.text("This journey has no recorded commands.")
		return
	_set_controls(true)
	await _select(0)

func _select(index: int) -> void:
	if index < 0 or index >= entries.size(): return
	request_epoch += 1
	var ticket := request_epoch
	selected_index = index
	list.select(index)
	list.ensure_current_is_visible()
	progress.value = index
	var entry: Dictionary = entries[index]
	heading.text = I18n.text("Command %s of %s") % [index + 1, entries.size()]
	state_text.text = I18n.text("Loading saved state…")
	_render_frames(entry)
	_update_controls()
	var sequence := int(entry.get("sequence", 0))
	var commit: Dictionary = commit_cache.get(sequence, {})
	if commit.is_empty():
		var response: Dictionary = await GameSession.replay_commit(replay_run_id, sequence)
		if not is_inside_tree() or ticket != request_epoch: return
		if not response.ok:
			state_text.text = I18n.error(response)
			return
		commit = response.data.duplicate(true)
		commit_cache[sequence] = commit
	_render_state(entry, commit)

func _render_state(entry: Dictionary, commit: Dictionary) -> void:
	var state: Dictionary = commit.get("stateAfter", {})
	var lifecycle := str(state.get("lifecycle", "—"))
	var node := I18n.content_name(str(state.get("currentNodeId", "")), I18n.text("Unknown stop"))
	var resources: Array[String] = []
	for id in state.get("resourceState", {}).get("resources", {}):
		var pool: Dictionary = state.resourceState.resources[id]
		resources.append("%s %s/%s" % [I18n.content_name(str(id)), I18n.number(float(pool.get("current", 0))), I18n.number(float(pool.get("maximum", 0)))])
	var deck: Dictionary = state.get("deck", {})
	var cards = deck.get("cardInstances", {})
	var card_count: int = cards.size() if cards is Dictionary or cards is Array else 0
	state_text.text = "[font_size=22][color=#f5ecd8]%s[/color][/font_size]\n%s\n%s\n%s\n[color=#a9a0a8]%s[/color]" % [
		_command_name(str(entry.get("commandType", ""))),
		I18n.text("State after command: %s") % lifecycle,
		I18n.text("Stop: %s  •  Deck: %s cards  •  Relics: %s") % [node, card_count, state.get("relics", []).size()],
		I18n.text("Resources: %s") % (", ".join(resources) if not resources.is_empty() else "—"),
		I18n.text("State hash: %s") % str(entry.get("stateHash", ""))]

func _render_frames(entry: Dictionary) -> void:
	var frames: Array = entry.get("frames", [])
	if frames.is_empty():
		frame_text.text = I18n.text("No internal frames were recorded for this command.")
		return
	var lines: Array[String] = ["[color=#53c9b5]%s[/color]" % (I18n.text("%s internal frame(s)") % frames.size())]
	for frame in frames:
		var scope := str(frame.get("scope", "run"))
		var kind := str(frame.get("kind", "transition")).replace("_", " ").replace(".", " › ").capitalize()
		var context: Array[String] = []
		if frame.get("actorId") != null: context.append(str(frame.actorId))
		if frame.get("phaseId") != null: context.append(str(frame.phaseId))
		lines.append("• %s  ·  %s%s" % [scope.to_upper(), kind, "  ·  " + " / ".join(context) if not context.is_empty() else ""])
	frame_text.text = "\n".join(lines)

func _stop_and_select(index: int) -> void:
	_stop()
	await _select(index)

func _restart() -> void:
	_stop()
	await _select(0)

func _previous() -> void:
	_stop()
	await _select(maxi(0, selected_index - 1))

func _next() -> void:
	_stop()
	await _select(mini(entries.size() - 1, selected_index + 1))

func _toggle_play() -> void:
	if playing:
		_stop()
		return
	playing = true
	play_button.text = I18n.text("PAUSE REPLAY")
	request_epoch += 1
	var playback_ticket := request_epoch
	while playing and is_inside_tree() and selected_index < entries.size() - 1:
		await _select(selected_index + 1)
		playback_ticket = request_epoch
		await get_tree().create_timer(.18 if Preferences.reduced_motion else .7).timeout
		if not is_inside_tree() or playback_ticket != request_epoch: return
	_stop()

func _stop() -> void:
	playing = false
	request_epoch += 1
	if is_instance_valid(play_button): play_button.text = I18n.text("PLAY")

func _verify() -> void:
	if verify_button.disabled: return
	verify_button.disabled = true
	router.show_toast(I18n.text("Verifying replay…"))
	var result: Dictionary = await GameSession.verify_run(replay_run_id)
	if not is_inside_tree(): return
	verify_button.disabled = false
	if result.ok:
		var valid := bool(result.data.get("isValid", result.data.get("valid", false)))
		router.show_toast(I18n.text("Deterministic replay verified.") if valid else I18n.text("Replay diverged."), not valid)
	else:
		router.show_error(I18n.error(result))

func _back() -> void:
	_stop()
	if return_to_history: router.show_history()
	else: router.open_game()

func _set_controls(enabled: bool) -> void:
	previous_button.disabled = not enabled
	play_button.disabled = not enabled
	next_button.disabled = not enabled
	verify_button.disabled = not enabled

func _update_controls() -> void:
	previous_button.disabled = selected_index <= 0
	next_button.disabled = selected_index < 0 or selected_index >= entries.size() - 1

func _command_name(type: String) -> String:
	return I18n.text(type.replace("_", " ").capitalize())

func _button(text: String, action: Callable) -> Button:
	var value := AppTheme.button(text)
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
