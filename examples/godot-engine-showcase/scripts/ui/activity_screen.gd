extends "res://scripts/ui/screen_view.gd"

var controller
var _owns_controller := true
var _intent_owner
var _run_snapshot: Dictionary
var _choices_snapshot: Array
var _zone_snapshot: Dictionary
var _projection_key: Array = []
var router
var presentation: Dictionary
var action_panel: VBoxContainer
var presenter
var choice_buttons: Array[Button] = []
var verifying := false
var auto_advancing := false
var animate_card_entry := true
var recovering_actions := false
var automatic_recovery_attempted := false
var render_epoch := 0

func setup(owner, data: Dictionary) -> void:
	router = owner
	mount(owner.visual_context)
	visual_context = owner.visual_context
	controller = preload("res://scripts/ui/controllers/activity_controller.gd").new(GameSession)
	_render(data, true)

func refresh_state(data: Dictionary) -> void:
	_render(data, false)

func _render(data: Dictionary, play_entry_animation: bool) -> void:
	visual_context.update_from(Preferences, I18n, AppTheme.profile)
	_run_snapshot = GameSession.run
	_choices_snapshot = GameSession.activity_choices()
	_zone_snapshot = GameSession.card_zones
	var key: Array = [_run_snapshot, _choices_snapshot, _zone_snapshot, data, I18n.locale, Preferences.text_scale, Preferences.high_contrast, AppTheme.profile.presentation_key()]
	if _projection_key == key and get_child_count() > 0: return
	_projection_key = key.duplicate(true)
	var previous_route_scroll := _scroll_value("RouteScroll")
	var previous_action_scroll := _scroll_value("ActionScroll")
	render_epoch += 1
	presentation = data.duplicate(true)
	animate_card_entry = play_entry_animation
	controller.bind(_run_snapshot, _choices_snapshot)
	presenter = preload("res://scripts/presentation/activity_presenter.gd").new(_run_snapshot, _choices_snapshot, data, I18n)
	if get_child_count() == 0:
		_build_contents()
	else:
		var desired = get_script().new()
		desired._owns_controller = false
		desired._intent_owner = self
		desired.controller = controller
		desired.router = router; desired.visual_context = visual_context; desired.presentation = presentation
		desired.presenter = presenter; desired._run_snapshot = _run_snapshot; desired._choices_snapshot = _choices_snapshot; desired._zone_snapshot = _zone_snapshot
		desired.animate_card_entry = false
		desired._build_contents()
		preload("res://scripts/ui/view_reconciler.gd").children(self, desired)
		desired.free()
		choice_buttons.clear()
		_collect_choices(self)
		action_panel = get_node_or_null("ActivityColumns/ActivityWorkspace/ActionScroll/ActionPanel")
	AppTheme.apply_view_preferences(self)
	_restore_scroll.call_deferred(render_epoch, previous_route_scroll, previous_action_scroll)
	_maybe_auto_advance.call_deferred(render_epoch)

func _build_contents() -> void:
	add_theme_constant_override("separation", 16)
	_build_header()
	var lifecycle := str(_run_snapshot.get("lifecycle", "Active"))
	if lifecycle.to_lower() != "active":
		_build_ending(lifecycle)
		return
	var columns := HBoxContainer.new()
	columns.name = "ActivityColumns"
	columns.size_flags_vertical = Control.SIZE_EXPAND_FILL
	columns.add_theme_constant_override("separation", 22)
	add_child(columns)
	columns.add_child(_build_map())
	var workspace := VBoxContainer.new()
	workspace.name = "ActivityWorkspace"
	workspace.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	workspace.add_theme_constant_override("separation", 14)
	if str(_current_node().get("activity", {}).get("type", "")) != "Dialogue":
		workspace.add_child(_activity_summary(_current_node()))
	action_panel = VBoxContainer.new()
	action_panel.name = "ActionPanel"
	action_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	action_panel.add_theme_constant_override("separation", 14)
	_build_actions()
	var scroll := ScrollContainer.new()
	scroll.name = "ActionScroll"
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.add_child(action_panel)
	workspace.add_child(scroll)
	columns.add_child(workspace)

func _collect_choices(node: Node) -> void:
	if node is Button and node.get_meta("activity_choice", false): choice_buttons.append(node)
	for child in node.get_children(): _collect_choices(child)

func _scroll_value(name: String) -> int:
	var scroll := find_child(name, true, false)
	return scroll.scroll_vertical if scroll is ScrollContainer else 0

func _restore_scroll(ticket: int, route_value: int, action_value: int) -> void:
	if ticket != render_epoch or not is_inside_tree(): return
	var route := find_child("RouteScroll", true, false)
	var actions := find_child("ActionScroll", true, false)
	# Container bounds settle at the end of the frame; defer the values once more
	# so Godot does not clamp a valid previous position against an old zero range.
	if route is ScrollContainer: route.set_deferred("scroll_vertical", route_value)
	if actions is ScrollContainer: actions.set_deferred("scroll_vertical", action_value)

func _build_header() -> void:
	var header := VBoxContainer.new()
	header.name = "ActivityHeader"
	header.add_theme_constant_override("separation", 8)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 12)
	var title_box := VBoxContainer.new()
	title_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var metadata: Dictionary = _run_snapshot.get("metadata", {})
	var run_title := I18n.content_name(str(_run_snapshot.get("runDefinitionId", "")), str(metadata.get("displayName", I18n.text("THE EMBER PATH"))))
	title_box.add_child(AppTheme.title(run_title, 30))
	title_box.tooltip_text = "Seed: %s · Sequence: %s" % [_run_snapshot.get("seed", 0), _run_snapshot.get("sequence", 0)]
	title_box.add_child(AppTheme.muted(I18n.text("Build your deck. Choose your path."), 14, 240))
	row.add_child(title_box)
	row.add_child(_button(I18n.text("VERIFY REPLAY"), _verify, 185))
	row.add_child(_button(I18n.text("PAUSE"), router.toggle_pause, 100))
	header.add_child(row)
	var badges := HFlowContainer.new()
	badges.alignment = FlowContainer.ALIGNMENT_END
	badges.add_theme_constant_override("h_separation", 16)
	badges.add_theme_constant_override("v_separation", 6)
	var character: Dictionary = _run_snapshot.get("characterResources", {})
	for resource_id in character.get("resources", {}):
		var pool: Dictionary = character.resources[resource_id]
		var resource_badge := Label.new()
		resource_badge.text = "%s/%s %s" % [I18n.number(float(pool.get("current", 0))), I18n.number(float(pool.get("maximum", 0))), I18n.content_name(str(resource_id))]
		resource_badge.tooltip_text = I18n.text("Character resources") + " · " + str(character.get("ownerId", ""))
		resource_badge.add_theme_color_override("font_color", AppTheme.TEAL)
		badges.add_child(resource_badge)
	for resource_id in _run_snapshot.get("resources", {}):
		var resource = _run_snapshot.get("resources", {}).get(resource_id, {})
		var badge := Label.new()
		badge.text = "%s %s" % [I18n.number(float(resource.get("current", 0))), I18n.content_name(resource_id)]
		badge.add_theme_color_override("font_color", AppTheme.GOLD)
		badge.add_theme_font_size_override("font_size", 15)
		badges.add_child(badge)
	if not _run_snapshot.get("relics", []).is_empty():
		var relic_badge := Label.new()
		relic_badge.text = "⬢  %s" % _run_snapshot.get("relics", []).size()
		relic_badge.add_theme_color_override("font_color", AppTheme.EMBER)
		badges.add_child(relic_badge)
	if not _run_snapshot.get("modifiers", []).is_empty():
		var modifier_badge := Label.new()
		modifier_badge.text = "△  %s" % _run_snapshot.get("modifiers", []).size()
		modifier_badge.add_theme_color_override("font_color", AppTheme.TEAL)
		badges.add_child(modifier_badge)
	if badges.get_child_count() > 0: header.add_child(badges)
	else: badges.free()
	add_child(header)

func _build_map() -> Control:
	var column := VBoxContainer.new()
	column.custom_minimum_size.x = 282
	column.add_child(AppTheme.title(I18n.text("JOURNEY"), 17, AppTheme.GOLD))
	column.add_child(AppTheme.muted(I18n.text("Highlighted stops are available."), 12))
	var route := preload("res://scripts/ui/route_view.gd").new()
	route.setup(presenter.route())
	route.travel_requested.connect(_execute if _intent_owner == null else _intent_owner._execute)
	var scroll := ScrollContainer.new()
	scroll.name = "RouteScroll"
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.add_child(route)
	column.add_child(scroll)
	return AppTheme.panel(column, AppTheme.color("surface.journey"))

func _activity_summary(node: Dictionary) -> Control:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 20)
	var kind := str(node.get("activity", {}).get("type", "Unknown"))
	var art := preload("res://scripts/ui/art_slot.gd").new(visual_context)
	art.setup("activities", kind)
	art.custom_minimum_size = Vector2(140, 150)
	row.add_child(art)
	var content := VBoxContainer.new()
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.alignment = BoxContainer.ALIGNMENT_CENTER
	content.add_theme_constant_override("separation", 12)
	content.add_child(AppTheme.muted(I18n.content_name(kind).to_upper(), 13))
	var title := AppTheme.wrap(AppTheme.title(presenter.node_name(str(node.get("nodeId", ""))), 30, AppTheme.GOLD), 180)
	content.add_child(title)
	var description: String = {
		"Encounter": I18n.text("Study the enemy's intent. Balance offense and defense."),
		"RelicReward": I18n.text("Claim a relic. Its effects will influence your upcoming battles."),
		"CardSelection": I18n.text("Choose a card for your deck. Offers are generated from your journey's seed."),
		"Shop": I18n.text("Spend your gold on supplies or refresh the merchant's stock."),
		"Preparation": I18n.text("Rest or prepare for your next battle."),
		"CardUpgrade": I18n.text("Upgrade a card to improve its base components.")
	}.get(kind, I18n.text("Choose your next action."))
	content.add_child(AppTheme.muted(presenter.node_description(node, description), 16, 180))
	var zone_view = preload("res://scripts/presentation/card_zone_presenter.gd").new(_zone_snapshot, I18n)
	content.add_child(AppTheme.muted(I18n.text("Deck · %s cards") % zone_view.total_cards(), 14))
	row.add_child(content)
	return AppTheme.panel(row)

func _build_actions() -> void:
	var coordinator = self if _intent_owner == null else _intent_owner
	var choices := _choices_snapshot
	var dialogue: Dictionary = preload("res://scripts/presentation/dialogue_presenter.gd").build(_run_snapshot, choices, I18n)
	if not dialogue.is_empty():
		var panel := preload("res://scripts/ui/dialogue_panel.gd").new()
		panel.setup(dialogue)
		panel.choice_requested.connect(coordinator._execute)
		choice_buttons.append_array(panel.choice_buttons)
		action_panel.add_child(panel)
	if choices.is_empty():
		action_panel.add_child(AppTheme.muted(I18n.text("Waiting for the next available action.")))
		var retry := _button(I18n.text("REFRESH ACTIONS"), _recover_actions)
		retry.name = "RefreshActionsButton"
		action_panel.add_child(retry)
		choice_buttons.append(retry)
		retry.set_meta("activity_choice", true)
		if not controller.automatic_recovery_attempted:
			controller.automatic_recovery_attempted = true
			coordinator._recover_actions.call_deferred()
		return
	var primary := HFlowContainer.new()
	primary.add_theme_constant_override("h_separation", 14)
	primary.add_theme_constant_override("v_separation", 14)
	action_panel.add_child(primary)
	var secondary := VBoxContainer.new()
	secondary.add_theme_constant_override("separation", 8)
	for choice in choices:
		var type := str(choice.type)
		if type == "CHOOSE_DIALOGUE_OPTION": continue
		if type == "ABANDON_RUN": continue # Run exit belongs to the pause menu.
		if type == "ADVANCE_NODE": continue # The route itself is the navigation control.
		var reference_id: String = controller.option_reference(choice)
		var model: Dictionary = presenter.offer(choice)
		var label := _choice_label(choice, model)
		if choice.secondary or type in ["RESOLVE_NODE", "RESOLVE_COMBAT"]:
			var display_label := label + (" · " + str(model.cost) if not str(model.cost).is_empty() else "")
			var button := _button(display_label, func(): coordinator._confirm_ref(reference_id))
			button.alignment = HORIZONTAL_ALIGNMENT_LEFT
			button.add_theme_font_size_override("font_size", 14)
			button.name = "Choice_" + reference_id
			button.set_meta("activity_choice", true)
			choice_buttons.append(button)
			secondary.add_child(button)
		elif str(model.category) == "cards":
			var card := CardView.new(visual_context)
			card.animate_entry = animate_card_entry
			model["availability"] = label
			if str(model.cost).is_empty(): model["cost"] = I18n.text("DECK CHOICE")
			card.configure(model)
			if primary.get_child_count() == 0: card.set_meta("initial_focus", true)
			card.pressed.connect(func(): coordinator._confirm_ref(reference_id))
			card.name = "Choice_" + reference_id
			card.set_meta("activity_choice", true)
			choice_buttons.append(card)
			primary.add_child(card)
		else:
			var column := VBoxContainer.new()
			column.name = "Offer_" + reference_id
			column.custom_minimum_size.x = 236
			var art := preload("res://scripts/ui/art_slot.gd").new(visual_context)
			art.setup("activities", str(_current_node().get("activity", {}).get("type", "")))
			art.custom_minimum_size.y = 100
			column.add_child(art)
			if not str(choice.get("subjectId", "")).is_empty():
				var title := AppTheme.wrap(AppTheme.title(str(model.name), 19), 180)
				column.add_child(title)
			if not str(model.cost).is_empty(): column.add_child(AppTheme.title(str(model.cost), 17, AppTheme.GOLD))
			var button := _button(label, func(): coordinator._confirm_ref(reference_id))
			button.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
			button.name = "Choice_" + reference_id
			button.set_meta("activity_choice", true)
			choice_buttons.append(button)
			column.add_child(button)
			primary.add_child(AppTheme.panel(column))
	if secondary.get_child_count() > 0:
		action_panel.add_child(AppTheme.muted(I18n.text("OTHER OPTIONS"), 13))
		action_panel.add_child(secondary)
	else: secondary.free()

func _choice_label(choice: Dictionary, model: Dictionary) -> String:
	var templates := {
		"ADVANCE_NODE": "TRAVEL TO %s", "PICK_CARD_REWARD": "CHOOSE  •  %s",
		"DECOMPOSE_CARD_REWARD": "DISMANTLE  •  %s", "BUY_SHOP_ITEM": "BUY  •  %s",
		"APPLY_PREPARATION_OPTION": "PREPARE  •  %s", "UPGRADE_CARD": "UPGRADE  •  %s",
		"REMOVE_CARD_TRANSFORMATION": "REMOVE  •  %s", "REPLACE_CARD_TRANSFORMATION": "REPLACE  •  %s"
	}
	var type := str(choice.type)
	if templates.has(type): return I18n.text(templates[type]) % str(model.name)
	if type == "REROLL_CARD_REWARD": return I18n.text("NEW OPTIONS")
	if type == "REROLL_SHOP": return I18n.text("REFRESH STOCK")
	if type == "START_DIALOGUE": return I18n.text("TALK")
	return _friendly_command(type)

func _confirm_choice(choice: Dictionary, model: Dictionary, label: String) -> void:
	if GameSession.busy: return
	var transformation := str(choice.type) in ["UPGRADE_CARD", "REMOVE_CARD_TRANSFORMATION", "REPLACE_CARD_TRANSFORMATION"]
	if not transformation and str(choice.type) not in ["BUY_SHOP_ITEM", "DECOMPOSE_CARD_REWARD", "REROLL_SHOP", "REROLL_CARD_REWARD"]:
		_execute(choice)
		return
	var dialog := ConfirmationDialog.new()
	dialog.title = I18n.text("Confirm choice")
	dialog.dialog_text = label + ("\n" + str(model.cost) if not str(model.cost).is_empty() else "")
	if transformation:
		var assessment: Dictionary = await controller.preview_transformation(choice)
		if not is_inside_tree():
			dialog.free()
			return
		if assessment.is_empty():
			dialog.free()
			router.show_toast(I18n.text("Detailed inspection is unavailable."))
			return
		dialog.dialog_text += "\n\n" + presenter.transformation_text(assessment)
		dialog.get_ok_button().disabled = not bool(assessment.get("isCompatible", false))
	dialog.ok_button_text = I18n.text("CONFIRM")
	dialog.cancel_button_text = I18n.text("CANCEL")
	dialog.confirmed.connect(func(): _execute(choice))
	dialog.visibility_changed.connect(func(): if not dialog.visible: dialog.queue_free())
	add_child(dialog)
	dialog.popup_centered(Vector2i(440, 180))

func _execute(choice: Dictionary) -> void:
	if GameSession.busy: return
	for button in choice_buttons: button.disabled = true
	router.show_toast(I18n.text("PROCESSING…"))
	var accepted: bool = await controller.execute(choice)
	if not is_inside_tree():
		return
	for button in choice_buttons: button.disabled = bool(button.get_meta("engine_disabled", false))
	if accepted:
		if str(choice.type) in ["PICK_CARD_REWARD", "BUY_SHOP_ITEM", "APPLY_PREPARATION_OPTION", "UPGRADE_CARD"]:
			GameAudio.reward()
		router.open_game()

func _recover_actions() -> void:
	if recovering_actions or GameSession.busy or not is_inside_tree():
		return
	recovering_actions = true
	for button in choice_buttons:
		button.disabled = true
	var accepted: bool = await controller.recover()
	recovering_actions = false
	if not is_inside_tree():
		return
	if accepted:
		router.open_game()
	else:
		for button in choice_buttons:
			if is_instance_valid(button): button.disabled = false

func _maybe_auto_advance(ticket := -1) -> void:
	if (ticket >= 0 and ticket != render_epoch) or auto_advancing or not is_inside_tree() or \
		router.current_screen != "activity" or GameSession.busy or get_tree().paused:
		return
	var choice: Dictionary = controller.forced_advance()
	if choice.is_empty(): return
	auto_advancing = true
	for button in choice_buttons: button.disabled = true
	router.show_toast(I18n.text("Moving to the next stop…"))
	var accepted: bool = await controller.execute(choice)
	if not is_inside_tree(): return
	auto_advancing = false
	if accepted: router.open_game()
	elif ticket < 0 or ticket == render_epoch:
		for button in choice_buttons: button.disabled = false

func _verify() -> void:
	if verifying: return
	verifying = true
	router.show_toast(I18n.text("Verifying replay…"))
	var result: Dictionary = await controller.verify()
	verifying = false
	if not is_inside_tree():
		return
	if result.ok:
		var valid := bool(result.data.get("isValid", result.data.get("valid", false)))
		router.show_toast(I18n.text("Deterministic replay verified.") if valid else I18n.text("Replay diverged."), not valid)
	else:
		router.show_error(I18n.error(result))

func _build_ending(lifecycle: String) -> void:
	var center := CenterContainer.new()
	center.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(center)
	var content := VBoxContainer.new()
	content.custom_minimum_size.x = 560
	var won := lifecycle.to_lower() == "completed"
	content.add_child(AppTheme.title(I18n.text("ARCHIVE COMPLETE") if won else I18n.text("THE FLAME HAS FADED"), 40,
		AppTheme.GOLD if won else AppTheme.BLOOD))
	content.add_child(AppTheme.muted(I18n.text("Your journey has been preserved. Watch its immutable recording, verify its integrity or start a new ascent.")))
	var replay_button := _button(I18n.text("WATCH REPLAY"), router.show_run_replay.bind(str(_run_snapshot.get("runId", ""))), 420)
	replay_button.name = "EndingReplayButton"
	content.add_child(replay_button)
	content.add_child(_button(I18n.text("VERIFY INTEGRITY"), _verify, 420))
	content.add_child(_button(I18n.text("NEW JOURNEY"), router._new_campaign, 420))
	content.add_child(_button(I18n.text("MAIN MENU"), router.back_to_menu, 420))
	center.add_child(AppTheme.panel(content))

func _current_node() -> Dictionary:
	return presenter.current_node()

func _card_name(id: String) -> String:
	return str(presentation.get("cards", {}).get(id, {}).get("name", id.replace("_", " ").capitalize()))

func _node_name(id: String) -> String:
	return str(presentation.get("nodes", {}).get(id, {}).get("name", id.replace("_", " ").capitalize()))

func _friendly_command(type: String) -> String:
	return {
		"START_ENCOUNTER": I18n.text("ENTER COMBAT"),
		"RESOLVE_COMBAT": I18n.text("CONFIRM RESULT"),
		"RESOLVE_NODE": I18n.text("COMPLETE STAGE"),
			"ACQUIRE_RELIC": I18n.text("CLAIM RELIC"),
			"CREATE_CARD_SELECTION": I18n.text("REVEAL REWARD"),
		"CREATE_SHOP": I18n.text("ENTER SHOP"),
		"CREATE_PREPARATION": I18n.text("LIGHT CAMPFIRE")
	}.get(type, type.replace("_", " "))

func _button(text: String, action: Callable, width := 0) -> Button:
	if _intent_owner != null and action.get_object() == self:
		action = Callable(_intent_owner, action.get_method()).bindv(action.get_bound_arguments())
	var value := AppTheme.button(text, width)
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value


func _confirm_ref(reference_id: String) -> void:
	var choice: Dictionary = controller.choice_for(reference_id)
	if choice.is_empty(): return
	var model: Dictionary = presenter.offer(choice)
	_confirm_choice(choice, model, _choice_label(choice, model))
func _exit_tree() -> void:
	if _owns_controller and controller: controller.dispose()
