extends VBoxContainer

var router
var presentation: Dictionary
var selected_target := ""
var selected_card := ""
var hand_row: HBoxContainer
var detail_label: RichTextLabel
var queue_label: Label
var next_frame_button: Button
var actor_portraits: Dictionary = {}
var target_buttons: Dictionary = {}
var card_buttons: Dictionary = {}
var action_buttons: Array[Button] = []
var choices: VBoxContainer
var hint_label: Label
var selection_label: Label
var frame_delay := 0.0
var submitting := false
var reconnect_button: Button

func setup(owner, data: Dictionary) -> void:
	router = owner
	presentation = data
	Playback.frame_presented.connect(_on_frame)
	add_theme_constant_override("separation", 8)
	_build_header()
	_build_battlefield()
	_build_hand()
	_build_footer()
	_update_controls()

func _exit_tree() -> void:
	if Playback.frame_presented.is_connected(_on_frame):
		Playback.frame_presented.disconnect(_on_frame)

func _build_header() -> void:
	var row := HBoxContainer.new()
	var phase: Dictionary = GameSession.combat.get("phase", {}) if GameSession.combat.get("phase", {}) is Dictionary else {}
	var activation: Dictionary = GameSession.combat.get("activation", {}) if GameSession.combat.get("activation", {}) is Dictionary else {}
	var label := AppTheme.title(I18n.text("COMBAT  %s") % str(GameSession.combat.get("runNodeId", "")).to_upper(), 25)
	row.add_child(label)
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(push)
	var turn := Label.new()
	turn.text = I18n.text("ROUND %s   •   %s   •   STEP %s") % [
		int(activation.get("round", GameSession.combat.get("currentTurn", 1))),
		str(phase.get("currentPhaseId", phase.get("phaseId", I18n.text("resolution")))).replace("_", " ").to_upper(),
		int(GameSession.combat.get("step", 0))]
	turn.add_theme_color_override("font_color", AppTheme.GOLD)
	row.add_child(turn)
	row.add_child(_button(I18n.text("TIMELINE  [%s]") % Preferences.action_label("open_timeline"), router.show_timeline, 165))
	row.add_child(_button(I18n.text("PAUSE"), router.toggle_pause, 100))
	add_child(row)

func _build_battlefield() -> void:
	var field := HBoxContainer.new()
	field.size_flags_vertical = Control.SIZE_EXPAND_FILL
	field.add_theme_constant_override("separation", 42)
	var player := GameSession.player_actor()
	field.add_child(_actor_card(player, false))
	var center := VBoxContainer.new()
	center.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	center.alignment = BoxContainer.ALIGNMENT_CENTER
	var sigil := Label.new()
	sigil.text = "✦\n╲  ◆  ╱\n✦"
	sigil.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	sigil.add_theme_font_size_override("font_size", 31)
	sigil.add_theme_color_override("font_color", Color("#77576d"))
	center.add_child(sigil)
	var stack_count: int = GameSession.combat.get("pendingActions", []).size()
	if stack_count > 0:
		center.add_child(AppTheme.muted(I18n.text("STACK: %s action(s)") % stack_count))
	field.add_child(center)
	var opponents := GameSession.opponents()
	var enemies := HBoxContainer.new()
	enemies.add_theme_constant_override("separation", 12)
	for opponent in opponents:
		enemies.add_child(_actor_card(opponent, true))
	field.add_child(enemies)
	add_child(field)

func _actor_card(actor: Dictionary, hostile: bool) -> Control:
	var content := VBoxContainer.new()
	content.custom_minimum_size = Vector2(300 if not hostile else 280, 285)
	content.add_theme_constant_override("separation", 7)
	var name := Label.new()
	name.text = I18n.content_name(str(actor.get("definitionId", "")), str(actor.get("name", ""))).to_upper()
	name.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name.add_theme_font_size_override("font_size", 19)
	name.add_theme_color_override("font_color", AppTheme.BLOOD if hostile else AppTheme.TEAL)
	content.add_child(name)
	var portrait := ActorPortrait.new()
	portrait.custom_minimum_size = Vector2(260, 112)
	portrait.configure(hostile, str(actor.get("definitionId", "")).contains("sentinel"))
	actor_portraits[str(actor.get("instanceId", ""))] = portrait
	content.add_child(portrait)
	var resources: Dictionary = actor.get("resources", {})
	var ordered: Array = presentation.get("resource_order", []).duplicate()
	for id in resources:
		if id not in ordered:
			ordered.append(id)
	for resource_id in ordered:
		if resources.has(resource_id):
			content.add_child(_resource(resource_id, resources[resource_id]))
	var statuses: Array = actor.get("statuses", [])
	if not statuses.is_empty():
		var status_text := statuses.map(func(status): return "%s ×%s" % [
			I18n.content_name(str(status.get("statusId", "status"))), status.get("stacks", 1)] )
		var status := AppTheme.muted("  •  ".join(status_text), 13)
		status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		status.add_theme_color_override("font_color", AppTheme.EMBER)
		content.add_child(status)
	if hostile:
		var intent := _intent_for(str(actor.get("instanceId", "")))
		var intent_label := Label.new()
		intent_label.text = I18n.text("INTENT  •  %s") % intent
		intent_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		intent_label.add_theme_color_override("font_color", AppTheme.GOLD)
		content.add_child(intent_label)
	var actor_id := str(actor.get("instanceId", ""))
	var select := _button(I18n.text("TARGET"), func(): _select_target(actor_id), 0)
	select.toggle_mode = true
	target_buttons[actor_id] = select
	content.add_child(select)
	return AppTheme.panel(content, Color("#261923e8") if hostile else Color("#15252ce8"))

func _resource(id: String, state: Dictionary) -> Control:
	var row := HBoxContainer.new()
	var label := Label.new()
	label.text = I18n.content_name(id).to_upper()
	label.custom_minimum_size.x = 78
	label.add_theme_font_size_override("font_size", 12)
	row.add_child(label)
	var bar := ProgressBar.new()
	bar.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bar.custom_minimum_size.y = 22
	bar.min_value = float(state.get("minimum", 0))
	bar.max_value = maxf(float(state.get("maximum", 1)), 1.0)
	bar.value = float(state.get("current", 0))
	bar.show_percentage = false
	var tone: Color = {"health": AppTheme.BLOOD, "block": Color("#7e9fbd"), "energy": AppTheme.GOLD, "mana": Color("#8c75dc")}.get(id, AppTheme.TEAL)
	bar.add_theme_stylebox_override("fill", AppTheme.box(tone, 5))
	row.add_child(bar)
	var amount := Label.new()
	amount.text = "%s/%s" % [I18n.number(float(state.get("current", 0))), I18n.number(float(state.get("maximum", 0)))]
	amount.custom_minimum_size.x = 65
	amount.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	row.add_child(amount)
	return row

func _build_hand() -> void:
	var section := VBoxContainer.new()
	section.add_theme_constant_override("separation", 8)
	var heading := HBoxContainer.new()
	heading.add_child(AppTheme.title(I18n.text("HAND"), 18, AppTheme.GOLD))
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	heading.add_child(push)
	hint_label = AppTheme.muted(I18n.text("Select a card, then choose a highlighted target."))
	hint_label.custom_minimum_size.x = 500
	hint_label.autowrap_mode = TextServer.AUTOWRAP_OFF
	hint_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	heading.add_child(hint_label)
	section.add_child(heading)
	hand_row = HBoxContainer.new()
	hand_row.alignment = BoxContainer.ALIGNMENT_CENTER
	hand_row.add_theme_constant_override("separation", 10)
	var hand_ids: Array = GameSession.run.get("deck", {}).get("handInstanceIds", [])
	for id in hand_ids:
		var card := _card_instance(str(id))
		if not card.is_empty():
			hand_row.add_child(_card_button(card))
	if hand_ids.is_empty():
		hand_row.add_child(AppTheme.muted(I18n.text("Your hand is empty.")))
	var scroller := ScrollContainer.new()
	scroller.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	scroller.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroller.custom_minimum_size.y = 170
	hand_row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroller.add_child(hand_row)
	section.add_child(scroller)
	var counts: Dictionary = GameSession.run.get("deck", {}).get("counts", {})
	section.add_child(AppTheme.muted(I18n.text("Draw: %s  •  Discard: %s  •  Exile: %s") % [
		int(counts.get("drawPile", 0)), int(counts.get("discardPile", 0)), int(counts.get("exhaustPile", 0))], 13))
	add_child(AppTheme.panel(section, Color("#151625e8")))

func _card_button(card: Dictionary) -> Button:
	var id := str(card.get("definitionId", ""))
	var info: Dictionary = presentation.get("cards", {}).get(id, {})
	var value := CardView.new()
	value.custom_minimum_size = Vector2(190, 156)
	value.add_theme_font_size_override("font_size", 15)
	value.text = "%s%s\n\n%s" % [
		I18n.content_name(id, str(info.get("name", id))).to_upper(),
		" +" if not card.get("upgrades", []).is_empty() else "",
		str(info.get("text", I18n.text("Data-driven component.")))]
	value.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	var tone := str(info.get("tone", "skill"))
	var color := AppTheme.BLOOD if tone == "attack" else (Color("#8c75dc") if tone == "power" else AppTheme.TEAL)
	value.add_theme_stylebox_override("normal", AppTheme.box(color.darkened(.62), 10, color.darkened(.12), 2))
	value.add_theme_stylebox_override("hover", AppTheme.box(color.darkened(.42), 10, AppTheme.GOLD, 3))
	value.add_theme_stylebox_override("focus", AppTheme.box(Color.TRANSPARENT, 10, AppTheme.GOLD, 3))
	var instance_id := str(card.get("cardInstanceId", ""))
	card_buttons[instance_id] = value
	var candidate := _candidate_for_card(instance_id)
	if not candidate.is_empty():
		value.text += "\n\n" + I18n.text("Cost: %s") % _cost_text(candidate)
	else:
		value.text += "\n\n" + I18n.text("UNAVAILABLE")
	value.tooltip_text = _preview(candidate) if not candidate.is_empty() else I18n.text("No legal actions for this card in the current state.")
	value.pressed.connect(func(): _choose_card(instance_id))
	value.mouse_entered.connect(func(): _show_preview(instance_id))
	value.focus_entered.connect(func(): _show_preview(instance_id))
	return value

func _build_footer() -> void:
	detail_label = RichTextLabel.new()
	detail_label.bbcode_enabled = false
	detail_label.custom_minimum_size = Vector2(0, 36)
	detail_label.text = I18n.text("Select a card, then choose a highlighted target.")
	detail_label.tooltip_text = str(GameSession.combat.get("stateHash", ""))
	add_child(detail_label)
	choices = VBoxContainer.new()
	add_child(choices)
	var row := HBoxContainer.new()
	selection_label = Label.new()
	selection_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(selection_label)
	queue_label = Label.new()
	queue_label.add_theme_color_override("font_color", AppTheme.TEAL)
	row.add_child(queue_label)
	next_frame_button = _button(I18n.text("NEXT ANIMATION  [%s]") % Preferences.action_label("confirm_action"), _next_frame, 180)
	row.add_child(next_frame_button)
	var cancel := _button(I18n.text("CANCEL SELECTION"), _cancel_selection, 160)
	action_buttons.append(cancel)
	row.add_child(cancel)
	var pass_candidate := _system_candidate(["PASS", "PASS_PRIORITY"])
	if not pass_candidate.is_empty():
		var pass_button := _button(I18n.text("PASS PRIORITY"), func(): _execute_system(pass_candidate), 180)
		action_buttons.append(pass_button)
		row.add_child(pass_button)
	var end := _button(I18n.text("END TURN  [%s]") % Preferences.action_label("end_turn"), _end_turn, 200)
	end.set_meta("requires_end", true)
	action_buttons.append(end)
	row.add_child(end)
	reconnect_button = _button(I18n.text("RECONNECT"), _reconnect, 140)
	row.add_child(reconnect_button)
	add_child(row)

func _unhandled_input(event: InputEvent) -> void:
	if get_tree().paused or not is_visible_in_tree():
		return
	if event.is_action_pressed("end_turn") and not event.is_echo():
		_end_turn()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("confirm_action") and not event.is_echo():
		_next_frame()
		get_viewport().set_input_as_handled()
	elif event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT:
		_cancel_selection()

func _select_target(actor_id: String) -> void:
	if _locked() or selected_card.is_empty():
		return
	selected_target = actor_id
	var matches: Array = _candidates(selected_card).filter(func(candidate): return actor_id in _targets(candidate))
	_update_controls()
	_offer_candidates(matches)

func _play_candidate(candidate: Dictionary) -> void:
	if _locked() or candidate.is_empty():
		return
	submitting = true
	_update_controls()
	var id := str(candidate.command.get("cardInstanceId", ""))
	var success := await GameSession.submit_candidate(candidate)
	if success:
		GameAudio.card()
		var target_ids := _targets(candidate)
		var destination := size * .5 + global_position
		if not target_ids.is_empty() and actor_portraits.has(target_ids[0]):
			var portrait: Control = actor_portraits[target_ids[0]]
			destination = portrait.global_position + portrait.size * .5
		if card_buttons.has(id):
			await card_buttons[id].fly_to(destination, router)
		router.open_game()
	else:
		submitting = false
		_update_controls()

func _end_turn() -> void:
	if _locked():
		return
	var candidate := _system_candidate(["END_TURN"])
	if candidate.is_empty():
		return
	submitting = true
	_update_controls()
	if await GameSession.submit_candidate(candidate):
		router.open_game()
	else:
		submitting = false
		_update_controls()

func _execute_system(candidate: Dictionary) -> void:
	if _locked():
		return
	submitting = true
	_update_controls()
	if await GameSession.submit_candidate(candidate):
		router.open_game()
	else:
		submitting = false
		_update_controls()

func _next_frame() -> void:
	if get_tree().paused:
		return
	Playback.advance()
	_update_controls()

func _on_frame(frame: Dictionary, index: int, total: int) -> void:
	detail_label.text = "%s %s/%s  •  %s" % [I18n.text("Animations"), index + 1, total, I18n.content_name(str(frame.get("transitionType", "")))]
	for application in frame.get("applications", []):
		var target := str(application.get("targetEntityId", ""))
		if actor_portraits.has(target):
			if not Preferences.reduced_motion:
				actor_portraits[target].flash_hit()
			_float_application(application, actor_portraits[target])
	GameAudio.hit()

func _queue_text() -> String:
	var remaining := Playback.total - Playback.index
	return I18n.text("%s animation(s) remaining") % maxi(remaining, 0)

func _candidate_for_card(instance_id: String, target_id := "") -> Dictionary:
	for candidate in GameSession.legal_actions:
		var command: Dictionary = candidate.get("command", {})
		if str(command.get("cardInstanceId", "")) != instance_id:
			continue
		var targets: Array = command.get("targetIds", [])
		if target_id.is_empty() or target_id in targets or str(command.get("targetId", "")) == target_id:
			return candidate
	return {}

func _system_candidate(action_types: Array) -> Dictionary:
	for candidate in GameSession.legal_actions:
		if str(candidate.get("source", "")) == "System" and \
			str(candidate.get("command", {}).get("actionType", "")) in action_types:
			return candidate
	return {}

func _preview(candidate: Dictionary) -> String:
	if candidate.is_empty():
		return I18n.text("No legal actions for this card in the current state.")
	var lines: Array[String] = [I18n.text("Cost: %s") % _cost_text(candidate)]
	if bool(candidate.get("outcomeUncertain", false)):
		lines.append(I18n.text("The outcome depends on the next responses."))
	for application in candidate.get("applications", []):
		var resource = application.get("resourceId")
		var previous = application.get("previousValue")
		var current = application.get("currentValue")
		if resource != null and previous != null and current != null:
			var delta := float(current) - float(previous)
			if not is_zero_approx(delta):
				lines.append("%s: %s%s %s" % [_actor_name(str(application.get("targetEntityId", ""))),
					"+" if delta > 0 else "", I18n.number(delta), I18n.content_name(str(resource))])
		elif application.get("statusId") != null:
			lines.append("%s: %s" % [_actor_name(str(application.get("targetEntityId", ""))),
				I18n.content_name(str(application.statusId))])
	return "  •  ".join(lines)

func _card_instance(instance_id: String) -> Dictionary:
	for card in GameSession.run.get("deck", {}).get("cardInstances", []):
		if str(card.get("cardInstanceId", "")) == instance_id:
			return card
	return {}

func _intent_for(actor_id: String) -> String:
	for intent in GameSession.combat.get("activation", {}).get("intents", []):
		if str(intent.get("actorId", "")) == actor_id:
			return I18n.content_name(str(intent.get("actionId", intent.get("actionType", ""))))
	return I18n.text("watching")

func _button(text: String, action: Callable, width := 0) -> Button:
	var value := AppTheme.button(text, width)
	value.custom_minimum_size.y = 44
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
func _process(delta: float) -> void:
	if get_tree().paused:
		return
	frame_delay -= delta
	if Preferences.auto_animations and frame_delay <= 0.0 and _has_frames():
		_next_frame()
		frame_delay = .32 / maxf(Preferences.animation_speed, .25)

func _has_frames() -> bool:
	return Playback.has_frames()

func _locked() -> bool:
	return submitting or GameSession.busy or not GameSession.synchronized or _has_frames() or router.get_tree().paused

func _choose_card(id: String) -> void:
	if _locked():
		return
	var matches := _candidates(id)
	if selected_card == id and matches.size() == 1:
		_play_candidate(matches[0])
		return
	selected_card = id
	selected_target = ""
	GameAudio.ui()
	_clear_choices()
	_show_preview(id)
	_update_controls()
	if matches.all(func(candidate): return _targets(candidate).is_empty()):
		_offer_candidates(matches)

func _cancel_selection() -> void:
	if submitting or GameSession.busy:
		return
	selected_card = ""
	selected_target = ""
	_clear_choices()
	_update_controls()

func _candidates(id: String) -> Array:
	return GameSession.legal_actions.filter(func(candidate): return str(candidate.get("command", {}).get("cardInstanceId", "")) == id)

func _targets(candidate: Dictionary) -> Array:
	var command: Dictionary = candidate.get("command", {})
	var targets: Array = command.get("targetIds", [])
	if targets.is_empty() and command.get("targetId") != null:
		targets = [command.targetId]
	return targets

func _clear_choices() -> void:
	for child in choices.get_children():
		choices.remove_child(child)
		child.queue_free()

func _offer_candidates(candidates: Array) -> void:
	_clear_choices()
	if candidates.size() == 1:
		_play_candidate(candidates[0])
		return
	# Keep alternative costs and multi-target combinations explicit.
	for candidate in candidates:
		var label := "%s  •  %s  •  %s" % [
			I18n.text("PLAY"), _cost_text(candidate), _preview(candidate)]
		choices.add_child(_button(label, func(): _play_candidate(candidate)))

func _show_preview(id: String) -> void:
	if not is_instance_valid(detail_label) or _has_frames():
		return
	var matches := _candidates(id)
	detail_label.text = _preview(matches[0]) if matches.size() == 1 else I18n.text("Choose a highlighted target.")
	if matches.is_empty():
		detail_label.text = I18n.text("No legal actions for this card in the current state.")

func _update_controls() -> void:
	if not is_instance_valid(queue_label):
		return
	var locked := _locked()
	reconnect_button.visible = not GameSession.synchronized
	queue_label.text = _queue_text() if _has_frames() else ""
	next_frame_button.visible = _has_frames()
	selection_label.text = I18n.text("PROCESSING…") if GameSession.busy or submitting else (
		I18n.text("Choose a highlighted target.") if not selected_card.is_empty() else "")
	for id in card_buttons:
		var card: CardView = card_buttons[id]
		card.disabled = locked or _candidates(id).is_empty()
		card.select_card(id == selected_card)
	for button in action_buttons:
		button.disabled = locked or (button.has_meta("requires_end") and _system_candidate(["END_TURN"]).is_empty())
	for id in target_buttons:
		var allowed := not selected_card.is_empty() and _candidates(selected_card).any(func(candidate): return id in _targets(candidate))
		target_buttons[id].disabled = locked or not allowed
		target_buttons[id].button_pressed = selected_target == id
		target_buttons[id].modulate = AppTheme.GOLD if allowed else Color.WHITE

func _actor_name(id: String) -> String:
	for actor in GameSession.combat.get("actors", []):
		if str(actor.get("instanceId", "")) == id:
			return I18n.content_name(str(actor.get("definitionId", "")), str(actor.get("name", id)))
	return id

func _float_application(application: Dictionary, portrait: Control) -> void:
	var previous = application.get("previousValue")
	var current = application.get("currentValue")
	if previous == null or current == null:
		return
	var delta := float(current) - float(previous)
	if is_zero_approx(delta):
		return
	var label := Label.new()
	label.text = "%s%s %s" % ["+" if delta > 0 else "", I18n.number(delta),
		I18n.content_name(str(application.get("resourceId", "")))]
	label.add_theme_font_size_override("font_size", 22)
	label.add_theme_color_override("font_color", AppTheme.TEAL if delta > 0 else AppTheme.GOLD)
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.process_mode = Node.PROCESS_MODE_PAUSABLE
	router.add_child(label)
	label.global_position = portrait.global_position + Vector2(20, 45)
	var tween := label.create_tween().set_parallel()
	var duration := .6 / maxf(Preferences.animation_speed, .25)
	if not Preferences.reduced_motion:
		tween.tween_property(label, "position:y", label.position.y - 36, duration)
	tween.tween_property(label, "modulate:a", 0.0, duration)
	tween.chain().tween_callback(label.queue_free)

func _reconnect() -> void:
	if await GameSession.refresh():
		router.open_game()

func _cost_text(candidate: Dictionary) -> String:
	var parts: Array[String] = []
	for cost in candidate.get("costs", []):
		parts.append("%s %s" % [I18n.number(float(cost.get("amount", 0))), I18n.content_name(str(cost.get("resourceId", "")))])
	return " + ".join(parts) if not parts.is_empty() else "0"
