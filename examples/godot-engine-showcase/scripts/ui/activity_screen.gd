extends VBoxContainer

var router
var presentation: Dictionary
var action_panel: VBoxContainer
var presenter
var choice_buttons: Array[Button] = []
var verifying := false

func setup(owner, data: Dictionary) -> void:
	router = owner
	presentation = data
	presenter = preload("res://scripts/presentation/activity_presenter.gd").new(GameSession.run, GameSession.activity_choices(), data, I18n)
	add_theme_constant_override("separation", 16)
	_build_header()
	var lifecycle := str(GameSession.run.get("lifecycle", "Active"))
	if lifecycle.to_lower() != "active":
		_build_ending(lifecycle)
		return
	var columns := HBoxContainer.new()
	columns.size_flags_vertical = Control.SIZE_EXPAND_FILL
	columns.add_theme_constant_override("separation", 22)
	add_child(columns)
	columns.add_child(_build_map())
	var workspace := VBoxContainer.new()
	workspace.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	workspace.add_theme_constant_override("separation", 14)
	workspace.add_child(_activity_summary(_current_node()))
	action_panel = VBoxContainer.new()
	action_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	action_panel.add_theme_constant_override("separation", 14)
	_build_actions()
	var scroll := ScrollContainer.new()
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.add_child(action_panel)
	workspace.add_child(scroll)
	columns.add_child(workspace)

func _build_header() -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 12)
	var title_box := VBoxContainer.new()
	title_box.add_child(AppTheme.title(I18n.text("THE EMBER PATH"), 30))
	title_box.tooltip_text = "Seed: %s · Sequence: %s" % [GameSession.run.get("seed", 0), GameSession.run.get("sequence", 0)]
	title_box.add_child(AppTheme.muted(I18n.text("Build your deck. Choose your path."), 14))
	row.add_child(title_box)
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(push)
	for resource_id in GameSession.run.get("resources", {}):
		var resource = GameSession.run.get("resources", {}).get(resource_id, {})
		var badge := Label.new()
		badge.text = "%s %s" % [I18n.number(float(resource.get("current", 0))), I18n.content_name(resource_id)]
		badge.add_theme_color_override("font_color", AppTheme.GOLD)
		badge.add_theme_font_size_override("font_size", 15)
		row.add_child(badge)
	if not GameSession.run.get("relics", []).is_empty():
		var relic_badge := Label.new()
		relic_badge.text = "⬢  %s" % GameSession.run.get("relics", []).size()
		relic_badge.add_theme_color_override("font_color", AppTheme.EMBER)
		row.add_child(relic_badge)
	if not GameSession.run.get("modifiers", []).is_empty():
		var modifier_badge := Label.new()
		modifier_badge.text = "△  %s" % GameSession.run.get("modifiers", []).size()
		modifier_badge.add_theme_color_override("font_color", AppTheme.TEAL)
		row.add_child(modifier_badge)
	row.add_child(_button(I18n.text("VERIFY REPLAY"), _verify, 185))
	row.add_child(_button(I18n.text("PAUSE"), router.toggle_pause, 100))
	add_child(row)

func _build_map() -> Control:
	var column := VBoxContainer.new()
	column.custom_minimum_size.x = 282
	column.add_child(AppTheme.title(I18n.text("JOURNEY"), 17, AppTheme.GOLD))
	column.add_child(AppTheme.muted(I18n.text("Highlighted stops are available."), 12))
	var route := preload("res://scripts/ui/route_view.gd").new()
	route.setup(presenter.route())
	route.travel_requested.connect(_execute)
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.add_child(route)
	column.add_child(scroll)
	return AppTheme.panel(column, Color("#151727d9"))

func _activity_summary(node: Dictionary) -> Control:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 20)
	var kind := str(node.get("activity", {}).get("type", "Unknown"))
	var art := preload("res://scripts/ui/art_slot.gd").new()
	art.setup("activities", kind)
	art.custom_minimum_size = Vector2(140, 150)
	row.add_child(art)
	var content := VBoxContainer.new()
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.alignment = BoxContainer.ALIGNMENT_CENTER
	content.add_theme_constant_override("separation", 12)
	content.add_child(AppTheme.muted(I18n.content_name(kind).to_upper(), 13))
	var title := AppTheme.title(presenter.node_name(str(node.get("nodeId", ""))), 30, AppTheme.GOLD)
	title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	content.add_child(title)
	var description: String = {
		"Encounter": I18n.text("Study the enemy's intent. Balance offense and defense."),
		"RelicReward": I18n.text("Claim a relic. Its effects will influence your upcoming battles."),
		"CardSelection": I18n.text("Choose a card for your deck. Offers are generated from your journey's seed."),
		"Shop": I18n.text("Spend your gold on supplies or refresh the merchant's stock."),
		"Preparation": I18n.text("Rest or prepare for your next battle."),
		"CardUpgrade": I18n.text("Upgrade a card to improve its base components.")
	}.get(kind, I18n.text("Choose your next action."))
	content.add_child(AppTheme.muted(description, 16))
	content.add_child(AppTheme.muted(I18n.text("Deck · %s cards") % GameSession.run.get("deck", {}).get("cardInstances", []).size(), 14))
	row.add_child(content)
	return AppTheme.panel(row)

func _build_actions() -> void:
	var choices := GameSession.activity_choices()
	if choices.is_empty():
		action_panel.add_child(AppTheme.muted(I18n.text("Waiting for the next available action.")))
		return
	var primary := HFlowContainer.new()
	primary.add_theme_constant_override("h_separation", 14)
	primary.add_theme_constant_override("v_separation", 14)
	action_panel.add_child(primary)
	var secondary := VBoxContainer.new()
	secondary.add_theme_constant_override("separation", 8)
	for choice in choices:
		var type := str(choice.type)
		if type == "ADVANCE_NODE": continue # The route itself is the navigation control.
		var model: Dictionary = presenter.offer(choice)
		var label := _choice_label(choice, model)
		if choice.secondary or type in ["ABANDON_RUN", "RESOLVE_NODE", "RESOLVE_COMBAT"]:
			var button := _button(label, func(): _confirm_choice(choice, model, label))
			button.alignment = HORIZONTAL_ALIGNMENT_LEFT
			button.add_theme_font_size_override("font_size", 14)
			choice_buttons.append(button)
			secondary.add_child(button)
		elif str(model.category) == "cards":
			var card := CardView.new()
			model["availability"] = label
			if str(model.cost).is_empty(): model["cost"] = I18n.text("DECK CHOICE")
			card.configure(model)
			if primary.get_child_count() == 0: card.set_meta("initial_focus", true)
			card.pressed.connect(func(): _confirm_choice(choice, model, label))
			choice_buttons.append(card)
			primary.add_child(card)
		else:
			var column := VBoxContainer.new()
			column.custom_minimum_size.x = 236
			var art := preload("res://scripts/ui/art_slot.gd").new()
			art.setup("activities", str(_current_node().get("activity", {}).get("type", "")))
			art.custom_minimum_size.y = 100
			column.add_child(art)
			if not str(choice.get("subjectId", "")).is_empty():
				var title := AppTheme.title(str(model.name), 19)
				title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
				column.add_child(title)
			if not str(model.cost).is_empty(): column.add_child(AppTheme.title(str(model.cost), 17, AppTheme.GOLD))
			var button := _button(label, func(): _confirm_choice(choice, model, label))
			button.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
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
		"APPLY_PREPARATION_OPTION": "PREPARE  •  %s", "UPGRADE_CARD": "UPGRADE  •  %s"
	}
	var type := str(choice.type)
	if templates.has(type): return I18n.text(templates[type]) % str(model.name)
	if type == "REROLL_CARD_REWARD": return I18n.text("NEW OPTIONS")
	if type == "REROLL_SHOP": return I18n.text("REFRESH STOCK")
	return _friendly_command(type)

func _confirm_choice(choice: Dictionary, model: Dictionary, label: String) -> void:
	if GameSession.busy: return
	if str(choice.type) not in ["ABANDON_RUN", "BUY_SHOP_ITEM", "DECOMPOSE_CARD_REWARD", "UPGRADE_CARD", "REROLL_SHOP", "REROLL_CARD_REWARD"]:
		_execute(choice)
		return
	var dialog := ConfirmationDialog.new()
	dialog.title = I18n.text("Confirm choice")
	dialog.dialog_text = label + ("\n" + str(model.cost) if not str(model.cost).is_empty() else "")
	if str(choice.type) == "ABANDON_RUN": dialog.dialog_text += "\n" + I18n.text("This ends the current journey. Its history remains available.")
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
	var accepted := await GameSession.submit_activity(choice)
	if not is_inside_tree():
		return
	for button in choice_buttons: button.disabled = false
	if accepted:
		if str(choice.type) in ["PICK_CARD_REWARD", "BUY_SHOP_ITEM", "APPLY_PREPARATION_OPTION", "UPGRADE_CARD"]:
			GameAudio.reward()
		router.open_game()

func _verify() -> void:
	if verifying: return
	verifying = true
	router.show_toast(I18n.text("Verifying replay…"))
	var result := await GameSession.verify()
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
	content.add_child(AppTheme.muted(I18n.text("Your journey has been preserved. You can verify its replay or start a new ascent.")))
	content.add_child(_button(I18n.text("VERIFY REPLAY"), _verify, 420))
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
		"ABANDON_RUN": I18n.text("ABANDON RUN"),
		"START_ENCOUNTER": I18n.text("ENTER COMBAT"),
		"RESOLVE_COMBAT": I18n.text("CONFIRM RESULT"),
		"RESOLVE_NODE": I18n.text("COMPLETE STAGE"),
			"ACQUIRE_RELIC": I18n.text("CLAIM RELIC"),
			"CREATE_CARD_SELECTION": I18n.text("REVEAL REWARD"),
		"CREATE_SHOP": I18n.text("ENTER SHOP"),
		"CREATE_PREPARATION": I18n.text("LIGHT CAMPFIRE")
	}.get(type, type.replace("_", " "))

func _button(text: String, action: Callable, width := 0) -> Button:
	var value := AppTheme.button(text, width)
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
