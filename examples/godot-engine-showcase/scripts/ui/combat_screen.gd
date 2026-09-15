extends VBoxContainer

const Presenter = preload("res://scripts/presentation/combat_presenter.gd")
const CardZonePresenter = preload("res://scripts/presentation/card_zone_presenter.gd")
var presenter
var zone_presenter
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
	zone_presenter = CardZonePresenter.new(GameSession.card_zones, I18n)
	presenter = Presenter.new(GameSession.run, GameSession.combat, GameSession.legal_actions,
		I18n, presentation, GameSession.card_zones)
	Playback.frame_presented.connect(_on_frame)
	add_theme_constant_override("separation", 8)
	_build_header()
	_build_battlefield()
	_build_hand()
	_build_footer()
	_update_controls()
	call_deferred("_load_inspection")

func _exit_tree() -> void:
	if Playback.frame_presented.is_connected(_on_frame):
		Playback.frame_presented.disconnect(_on_frame)

func _build_header() -> void:
	var row := HBoxContainer.new()
	var activation: Dictionary = GameSession.combat.get("activation", {}) if GameSession.combat.get("activation", {}) is Dictionary else {}
	var title := AppTheme.title(I18n.text("COMBAT"), 24)
	title.tooltip_text = str(GameSession.combat.get("runNodeId", "")) + " · " + str(GameSession.combat.get("stateHash", ""))
	row.add_child(title)
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(push)
	var turn := AppTheme.title(I18n.text("ROUND %s") % int(activation.get("round", GameSession.combat.get("currentTurn", 1))), 18, AppTheme.GOLD)
	row.add_child(turn)
	row.add_child(_button(I18n.text("TIMELINE  [%s]") % Preferences.action_label("open_timeline"), router.show_timeline, 165))
	row.add_child(_button(I18n.text("PAUSE"), router.toggle_pause, 100))
	add_child(row)

func _build_battlefield() -> void:
	var stage := PanelContainer.new()
	stage.size_flags_vertical = Control.SIZE_EXPAND_FILL
	stage.custom_minimum_size.y = 348
	stage.add_theme_stylebox_override("panel", AppTheme.box(Color("#121423"), 12))
	stage.clip_contents = true
	var backdrop := preload("res://scripts/ui/art_slot.gd").new()
	backdrop.setup("backgrounds", "combat")
	stage.add_child(backdrop)
	var scroller := ScrollContainer.new()
	scroller.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	scroller.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	var field := HBoxContainer.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.size_flags_vertical = Control.SIZE_EXPAND_FILL
	field.alignment = BoxContainer.ALIGNMENT_CENTER
	field.add_theme_constant_override("separation", 22)
	var allies := HBoxContainer.new()
	var opponents := HBoxContainer.new()
	allies.alignment = BoxContainer.ALIGNMENT_CENTER
	opponents.alignment = BoxContainer.ALIGNMENT_CENTER
	for actor in presenter.actors():
		var hostile: bool = presenter.relationship(actor, GameSession.input_actor_id()) == "Enemy"
		(opponents if hostile else allies).add_child(_actor_card(actor, hostile))
	field.add_child(allies)
	var divider := CenterContainer.new()
	divider.custom_minimum_size.x = 88
	divider.add_child(AppTheme.title("◇", 26, AppTheme.GOLD))
	field.add_child(divider)
	field.add_child(opponents)
	scroller.add_child(field)
	stage.add_child(scroller)
	add_child(stage)

func _actor_card(actor: Dictionary, hostile: bool) -> Control:
	var panel := preload("res://scripts/ui/actor_panel.gd").new()
	var id := str(actor.get("instanceId", ""))
	panel.setup(actor, hostile, presentation, presenter._intent_for(id))
	panel.target_selected.connect(_select_target)
	actor_portraits[id] = panel.portrait_view
	target_buttons[id] = panel.target_button
	return panel

func _build_hand() -> void:
	var section := VBoxContainer.new()
	section.add_theme_constant_override("separation", 4)
	var heading := HBoxContainer.new()
	heading.add_child(AppTheme.title(zone_presenter.playable_label(), 17, AppTheme.GOLD))
	hint_label = AppTheme.muted(I18n.text("Select a card, then choose a highlighted target."), 13)
	hint_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hint_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	heading.add_child(hint_label)
	for zone in zone_presenter.auxiliary_zones():
		var zone_id := str(zone.get("zoneId", ""))
		var label: String = zone_presenter.zone_label(zone)
		var pile := _button("%s · %s" % [label, int(zone.get("count", 0))],
			func(): _inspect_pile(label, zone_id))
		pile.add_theme_font_size_override("font_size", 13)
		pile.custom_minimum_size.y = 32
		heading.add_child(pile)
	section.add_child(heading)
	hand_row = HBoxContainer.new()
	hand_row.alignment = BoxContainer.ALIGNMENT_CENTER
	hand_row.add_theme_constant_override("separation", 12)
	hand_row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var playable_cards: Array = zone_presenter.playable_cards()
	for card_view in playable_cards:
		var card := _card_instance(str(card_view.get("cardInstanceId", "")))
		if not card.is_empty(): hand_row.add_child(_card_button(card))
	if playable_cards.is_empty(): hand_row.add_child(AppTheme.muted(I18n.text("Your hand is empty.")))
	var margin := MarginContainer.new()
	margin.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for side in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + side, 8)
	margin.add_child(hand_row)
	var scroller := ScrollContainer.new()
	scroller.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	scroller.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroller.custom_minimum_size.y = 284
	scroller.add_child(margin)
	section.add_child(scroller)
	add_child(section)

func _card_button(card: Dictionary) -> Button:
	var instance_id := str(card.get("cardInstanceId", ""))
	var candidate := _candidate_for_card(instance_id)
	var value := CardView.new()
	value.toggle_mode = true
	value.configure(presenter.card_view_model(instance_id))
	if not candidate.is_empty() and not card_buttons.values().any(func(card_view): return card_view.has_meta("initial_focus")):
		value.set_meta("initial_focus", true)
	card_buttons[instance_id] = value
	value.tooltip_text = _preview(candidate) if not candidate.is_empty() else presenter.unavailable_reason(instance_id)
	value.pressed.connect(func(): _choose_card(instance_id))
	value.mouse_entered.connect(func(): _show_preview(instance_id))
	value.focus_entered.connect(func(): _show_preview(instance_id))
	return value

func _build_footer() -> void:
	detail_label = RichTextLabel.new()
	detail_label.bbcode_enabled = false
	detail_label.custom_minimum_size = Vector2(0, 42)
	detail_label.add_theme_font_size_override("normal_font_size", 14)
	detail_label.text = I18n.text("Select a card, then choose a highlighted target.")
	add_child(detail_label)
	choices = VBoxContainer.new()
	# Alternate costs/multi-target candidates stay explicit and can scroll independently.
	var alternatives := ScrollContainer.new()
	alternatives.custom_minimum_size.y = 0
	alternatives.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	choices.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	alternatives.add_child(choices)
	add_child(alternatives)
	choices.child_order_changed.connect(func(): alternatives.custom_minimum_size.y = 52 if choices.get_child_count() > 0 else 0)
	var row := HBoxContainer.new()
	selection_label = AppTheme.muted("", 13)
	selection_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	selection_label.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	row.add_child(selection_label)
	queue_label = AppTheme.muted("", 13)
	queue_label.add_theme_color_override("font_color", AppTheme.TEAL)
	row.add_child(queue_label)
	next_frame_button = _button(I18n.text("NEXT ANIMATION  [%s]") % Preferences.action_label("confirm_action"), _next_frame, 180)
	row.add_child(next_frame_button)
	var cancel := _button(I18n.text("CANCEL SELECTION"), _cancel_selection, 145)
	cancel.set_meta("selection_only", true)
	action_buttons.append(cancel)
	row.add_child(cancel)
	var inspect := _button(I18n.text("INSPECT CARD"), _inspect_card, 130)
	inspect.set_meta("selection_only", true)
	action_buttons.append(inspect)
	row.add_child(inspect)
	var pass_candidate := _system_candidate(["PASS", "PASS_PRIORITY"])
	if not pass_candidate.is_empty():
		var pass_label := "PASS PRIORITY" if str(pass_candidate.command.get("actionType", "")) == "PASS_PRIORITY" else "PASS"
		var pass_button := _button(I18n.text(pass_label), func(): _execute_system(pass_candidate), 150)
		action_buttons.append(pass_button)
		row.add_child(pass_button)
	var end := _button(I18n.text("END TURN  [%s]") % Preferences.action_label("end_turn"), _end_turn, 195)
	end.add_theme_stylebox_override("normal", AppTheme.box(Color("#705033"), 10, AppTheme.GOLD, 2))
	end.set_meta("requires_end", true)
	action_buttons.append(end)
	row.add_child(end)
	reconnect_button = _button(I18n.text("RECONNECT"), _reconnect, 130)
	row.add_child(reconnect_button)
	add_child(row)

func _input(event: InputEvent) -> void:
	# Consume cancel before the router handles pause; the first Escape cancels targeting.
	if not get_tree().paused and not selected_card.is_empty() and event.is_action_pressed("ui_cancel"):
		_cancel_selection()
		get_viewport().set_input_as_handled()

func _unhandled_input(event: InputEvent) -> void:
	if get_tree().paused or not is_visible_in_tree():
		return
	if event.is_action_pressed("end_turn") and not event.is_echo():
		_end_turn()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("confirm_action") and not event.is_echo():
		_next_frame()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("ui_cancel") or (event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT):
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
	if not is_inside_tree():
		return
	if success:
		GameAudio.card()
		var target_ids := _targets(candidate)
		var destination := size * .5 + global_position
		if not target_ids.is_empty() and actor_portraits.has(target_ids[0]):
			var portrait: Control = actor_portraits[target_ids[0]]
			destination = portrait.global_position + portrait.size * .5
		if card_buttons.has(id):
			await card_buttons[id].fly_to(destination, router)
		if is_inside_tree():
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
	var accepted := await GameSession.submit_candidate(candidate)
	if not is_inside_tree():
		return
	if accepted:
		router.open_game()
	else:
		submitting = false
		_update_controls()

func _execute_system(candidate: Dictionary) -> void:
	if _locked():
		return
	submitting = true
	_update_controls()
	var accepted := await GameSession.submit_candidate(candidate)
	if not is_inside_tree():
		return
	if accepted:
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
		detail_label.text += "\n" + presenter.application_text(application, true)
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
	return presenter._candidate_for_card(instance_id, target_id)

func _system_candidate(action_types: Array) -> Dictionary:
	return presenter._system_candidate(action_types)

func _preview(candidate: Dictionary) -> String:
	return presenter._preview(candidate)

func _card_instance(instance_id: String) -> Dictionary:
	return presenter._card_instance(instance_id)

func _intent_for(actor_id: String) -> String:
	return presenter._intent_for(actor_id)

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
	return presenter.input_state(submitting or GameSession.busy, GameSession.synchronized,
		_has_frames(), router.get_tree().paused, selected_card) not in [Presenter.InputState.READY, Presenter.InputState.SELECTING]

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
	return presenter._candidates(id)

func _targets(candidate: Dictionary) -> Array:
	return presenter._targets(candidate)

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
		detail_label.text = presenter.unavailable_reason(id)

func _update_controls() -> void:
	preload("res://scripts/ui/focus_navigation.gd").wire.call_deferred(self)
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
		card.disabled = locked
		card.modulate = Color.WHITE if not _candidates(id).is_empty() else Color("#a99cab")
		card.select_card(id == selected_card)
	for button in action_buttons:
		if button.has_meta("selection_only"): button.visible = not selected_card.is_empty()
		button.disabled = locked or (button.has_meta("requires_end") and _system_candidate(["END_TURN"]).is_empty())
	for id in target_buttons:
		var allowed := not selected_card.is_empty() and _candidates(selected_card).any(func(candidate): return id in _targets(candidate))
		target_buttons[id].disabled = locked or not allowed
		target_buttons[id].button_pressed = selected_target == id
		target_buttons[id].modulate = AppTheme.GOLD if allowed else Color.WHITE

func _actor_name(id: String) -> String:
	return presenter._actor_name(id)

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
	var accepted := await GameSession.refresh()
	if not is_inside_tree():
		return
	if accepted:
		router.open_game()

func _cost_text(candidate: Dictionary) -> String:
	return presenter._cost_text(candidate)

func _load_inspection() -> void:
	var representative_targets: Array = []
	for candidate in GameSession.legal_actions:
		var targets: Array = presenter._targets(candidate)
		if not targets.is_empty():
			representative_targets = [targets[0]]
			break
	var response := await GameSession.inspect_hand(representative_targets)
	if not is_inside_tree() or not response.ok:
		return
	presenter.accept_evaluations(response.data.get("cards", []))
	for id in card_buttons:
		var card: CardView = card_buttons[id]
		card.configure(presenter.card_view_model(id))
		card.disabled = _locked()
		card.select_card(id == selected_card)
		card.tooltip_text = presenter.inspection_text(id)
	if not selected_card.is_empty(): _show_preview(selected_card)

func _inspect_card() -> void:
	if selected_card.is_empty():
		return
	var dialog := preload("res://scripts/ui/inspection_dialog.gd").new()
	dialog.setup(I18n.text("Card inspection"), presenter.inspection_text(selected_card), presenter.inspection_data(selected_card))
	add_child(dialog)
	dialog.popup_centered()

func _inspect_pile(label: String, zone: String) -> void:
	var lines: Array[String] = []
	for card in presenter.pile_cards(zone):
		lines.append(I18n.content_name(str(card.get("definitionId", ""))) + (" +" if not card.get("upgrades", []).is_empty() else ""))
	var dialog := preload("res://scripts/ui/inspection_dialog.gd").new()
	dialog.setup(I18n.text(label), "\n".join(lines) if not lines.is_empty() else I18n.text("Empty"))
	add_child(dialog)
	dialog.popup_centered()
