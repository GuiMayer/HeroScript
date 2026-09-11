extends VBoxContainer

var router
var presentation: Dictionary
var action_panel: VBoxContainer

func setup(owner, data: Dictionary) -> void:
	router = owner
	presentation = data
	add_theme_constant_override("separation", 16)
	_build_header()
	_build_map()
	var lifecycle := str(GameSession.run.get("lifecycle", "Active"))
	if lifecycle.to_lower() != "active":
		_build_ending(lifecycle)
		return
	var current := _current_node()
	var columns := HBoxContainer.new()
	columns.size_flags_vertical = Control.SIZE_EXPAND_FILL
	columns.add_theme_constant_override("separation", 18)
	add_child(columns)
	columns.add_child(_activity_summary(current))
	action_panel = VBoxContainer.new()
	action_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	action_panel.add_theme_constant_override("separation", 10)
	action_panel.add_child(AppTheme.title(I18n.text("AVAILABLE ACTIONS"), 22, AppTheme.TEAL))
	_build_actions()
	columns.add_child(AppTheme.panel(action_panel))

func _build_header() -> void:
	var row := HBoxContainer.new()
	var title_box := VBoxContainer.new()
	title_box.add_child(AppTheme.title(I18n.text("THE EMBER PATH"), 30))
	title_box.add_child(AppTheme.muted(I18n.text("Run %s  •  seed %s  •  sequence %s  •  step %s") % [
		str(GameSession.run.get("runId", "")).left(8),
		GameSession.run.get("seed", 0), GameSession.run.get("sequence", 0), GameSession.run.get("step", 0)]))
	row.add_child(title_box)
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(push)
	for resource_id in ["gold", "power_points"]:
		var resource = GameSession.run.get("resources", {}).get(resource_id, {})
		var badge := Label.new()
		badge.text = "%s  %s" % ["◆" if resource_id == "gold" else "✦", int(resource.get("current", 0))]
		badge.add_theme_color_override("font_color", AppTheme.GOLD if resource_id == "gold" else AppTheme.TEAL)
		badge.add_theme_font_size_override("font_size", 19)
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

func _build_map() -> void:
	var line := HBoxContainer.new()
	line.alignment = BoxContainer.ALIGNMENT_CENTER
	line.add_theme_constant_override("separation", 7)
	var map: Dictionary = GameSession.run.get("map", {})
	var current_id := str(GameSession.run.get("currentNodeId", ""))
	for node in map.get("nodes", []):
		var id := str(node.get("nodeId", ""))
		var info: Dictionary = presentation.get("nodes", {}).get(id, {})
		var marker := VBoxContainer.new()
		marker.custom_minimum_size.x = 145
		var icon := Label.new()
		icon.text = str(info.get("icon", "•"))
		icon.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		icon.add_theme_font_size_override("font_size", 25)
		var resolved := bool(node.get("resolved", false))
		icon.add_theme_color_override("font_color", AppTheme.TEAL if resolved else (AppTheme.EMBER if id == current_id else AppTheme.MUTED))
		marker.add_child(icon)
		var label := AppTheme.muted(str(info.get("name", id)), 12)
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		label.add_theme_color_override("font_color", AppTheme.INK if id == current_id else AppTheme.MUTED)
		marker.add_child(label)
		line.add_child(marker)
		if node != map.get("nodes", [])[-1]:
			var connector := Label.new()
			connector.text = "—"
			connector.add_theme_color_override("font_color", Color("#55566d"))
			line.add_child(connector)
	add_child(AppTheme.panel(line, Color("#151727d9")))

func _activity_summary(node: Dictionary) -> Control:
	var content := VBoxContainer.new()
	content.custom_minimum_size = Vector2(390, 0)
	content.add_theme_constant_override("separation", 13)
	var id := str(node.get("nodeId", ""))
	var info: Dictionary = presentation.get("nodes", {}).get(id, {})
	var icon := Label.new()
	icon.text = str(info.get("icon", "◈"))
	icon.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	icon.add_theme_font_size_override("font_size", 76)
	icon.add_theme_color_override("font_color", AppTheme.EMBER)
	content.add_child(icon)
	var title := AppTheme.title(str(info.get("name", id)), 27, AppTheme.GOLD)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	content.add_child(title)
	var activity: Dictionary = node.get("activity", {})
	var kind := str(activity.get("type", "Unknown"))
	var description: String = {
		"Encounter": I18n.text("Enter the encounter. Its opponents, deck and rules are defined by the setting."),
		"RelicReward": I18n.text("Claim a relic. Its effects will influence your upcoming battles."),
		"CardSelection": I18n.text("Choose a card for your deck. Offers are generated from your journey's seed."),
		"Shop": I18n.text("Spend your gold on supplies or refresh the merchant's stock."),
		"Preparation": I18n.text("Rest or prepare for your next battle."),
		"CardUpgrade": I18n.text("Upgrade a card to improve its base components.")
	}.get(kind, I18n.text("Choose your next action."))
	content.add_child(AppTheme.muted(description, 16))
	var deck: Dictionary = GameSession.run.get("deck", {})
	content.add_child(AppTheme.muted(I18n.text("Deck: %s cards  •  hand: %s  •  discard: %s") % [
		deck.get("cardInstances", []).size(), deck.get("counts", {}).get("hand", 0),
		deck.get("counts", {}).get("discardPile", 0)], 14))
	return AppTheme.panel(content)

func _build_actions() -> void:
	var choices := GameSession.activity_choices()
	if choices.is_empty():
		action_panel.add_child(AppTheme.muted(I18n.text("Waiting for the next available action.")))
		return
	for choice in choices:
		var type := str(choice.type)
		var label := _friendly_command(type)
		var templates := {
			"ADVANCE_NODE": "TRAVEL TO %s", "PICK_CARD_REWARD": "CHOOSE  •  %s",
			"DECOMPOSE_CARD_REWARD": "DISMANTLE  •  %s", "BUY_SHOP_ITEM": "BUY  •  %s",
			"APPLY_PREPARATION_OPTION": "PREPARE  •  %s", "UPGRADE_CARD": "UPGRADE  •  %s"
		}
		if templates.has(type):
			var name := I18n.content_name(str(choice.subjectId))
			if choice.subjectType == "node":
				name = _node_name(str(choice.subjectId))
			elif choice.subjectType == "card":
				name = _card_name(str(choice.subjectId))
			label = I18n.text(templates[type]) % name
			if choice.has("variantId"):
				label += " · " + I18n.content_name(str(choice.variantId))
		elif type == "REROLL_CARD_REWARD":
			label = I18n.text("NEW OPTIONS")
		elif type == "REROLL_SHOP":
			label = I18n.text("REFRESH STOCK")
		var button := _button(label, func(): _execute(choice), 0)
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		if choice.secondary:
			button.modulate = Color("#c9bdca")
		action_panel.add_child(button)

func _execute(choice: Dictionary) -> void:
	router.show_toast(I18n.text("Processing %s…") % str(choice.type))
	if await GameSession.submit_activity(choice):
		if str(choice.type) in ["PICK_CARD_REWARD", "BUY_SHOP_ITEM", "APPLY_PREPARATION_OPTION", "UPGRADE_CARD"]:
			GameAudio.reward()
		router.open_game()

func _verify() -> void:
	var result := await GameSession.verify()
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
	var id := str(GameSession.run.get("currentNodeId", ""))
	for node in GameSession.run.get("map", {}).get("nodes", []):
		if str(node.get("nodeId", "")) == id:
			return node
	return {}

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
	var value := AppTheme.button(text, width)
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
