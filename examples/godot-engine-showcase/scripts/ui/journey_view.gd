extends "res://scripts/ui/screen_view.gd"
## Passive journey composition. Region updates never execute workflows.
signal intent_requested(action: String, arguments: Array)
var _run_snapshot: Dictionary
var _choices_snapshot: Array
var _zone_snapshot: Dictionary
var presentation: Dictionary
var presenter
const OptionsView = preload("res://scripts/ui/components/activity_options_view.gd")
var choice_buttons: Array[Button]:
	get: return action_panel.choice_buttons if is_instance_valid(action_panel) else []
var action_panel: OptionsView
var animate_card_entry := false
var render_epoch := 0
var _header: Control
var _columns: HBoxContainer
var _workspace: VBoxContainer
var _summary: Control
var _ending: Control
var _lifecycle := ""
var _region_keys: Dictionary = {}
func set_compartments_available(value: bool) -> void:
	var button := find_child("DeckCompartmentsButton", true, false)
	if button is Button: button.visible = value
func _scroll_value(name: String) -> int:
	var scroll := find_child(name, true, false)
	return scroll.scroll_vertical if scroll is ScrollContainer else 0


func _restore_scroll(ticket: int, route_value: int, action_value: int, route_horizontal := 0) -> void:
	if ticket != render_epoch or not is_inside_tree(): return
	var route := find_child("RouteScroll", true, false)
	var actions := find_child("ActionScroll", true, false)
	# Container bounds settle at the end of the frame; defer the values once more
	# so Godot does not clamp a valid previous position against an old zero range.
	if route is ScrollContainer:
		route.set_deferred("scroll_vertical", route_value)
		route.set_deferred("scroll_horizontal", route_horizontal)
	if actions is ScrollContainer: actions.set_deferred("scroll_vertical", action_value)


func _build_header() -> Control:
	var header := VBoxContainer.new()
	header.name = "ActivityHeader"
	header.add_theme_constant_override("separation", 8)
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 12)
	var title_box := VBoxContainer.new()
	title_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var metadata: Dictionary = _run_snapshot.get("metadata", {})
	var run_title := visual_context.translator.content_name(str(_run_snapshot.get("runDefinitionId", "")), str(metadata.get("displayName", visual_context.translator.text("THE EMBER PATH"))))
	var heading := visual_context.styles.title(run_title, 30)
	heading.name = "RunTitle"
	heading.tooltip_text = "Seed: %s · Sequence: %s" % [_run_snapshot.get("seed", 0), _run_snapshot.get("sequence", 0)]
	title_box.add_child(heading)
	title_box.add_child(visual_context.styles.muted(visual_context.translator.text("Build your deck. Choose your path."), 14, 240))
	row.add_child(title_box)
	row.add_child(_button(visual_context.translator.text("VERIFY REPLAY"), _verify, 185))
	row.add_child(_button(visual_context.translator.text("MAP"), _map, 100))
	row.add_child(_button(visual_context.translator.text("MODIFIERS"), func(): intent_requested.emit("modifier_items", []), 140))
	if _run_snapshot.get("loadout") is Dictionary: row.add_child(_button(visual_context.translator.text("INSCRIPTIONS"), func(): intent_requested.emit("inscriptions", []), 140))
	row.add_child(_button(visual_context.translator.text("PAUSE"), _pause, 100))
	var compartments := _button(visual_context.translator.text("DECK BUILD"), func(): intent_requested.emit("deck_compartments", []), 140)
	compartments.name = "DeckCompartmentsButton"; compartments.visible = false; row.add_child(compartments)
	header.add_child(row)
	var badges := HFlowContainer.new()
	badges.alignment = FlowContainer.ALIGNMENT_END
	badges.add_theme_constant_override("h_separation", 16)
	badges.add_theme_constant_override("v_separation", 6)
	var character: Dictionary = _run_snapshot.get("characterResources", {})
	for resource_id in character.get("resources", {}):
		var pool: Dictionary = character.resources[resource_id]
		var resource_badge := Label.new()
		resource_badge.text = "%s/%s %s" % [visual_context.translator.number(float(pool.get("current", 0))), visual_context.translator.number(float(pool.get("maximum", 0))), visual_context.translator.content_name(str(resource_id))]
		resource_badge.tooltip_text = visual_context.translator.text("Character resources") + " · " + str(character.get("ownerId", ""))
		resource_badge.add_theme_color_override("font_color", visual_context.styles.TEAL)
		badges.add_child(resource_badge)
	for resource_id in _run_snapshot.get("resources", {}):
		var resource = _run_snapshot.get("resources", {}).get(resource_id, {})
		var badge := Label.new()
		badge.text = "%s %s" % [visual_context.translator.number(float(resource.get("current", 0))), visual_context.translator.content_name(resource_id)]
		badge.add_theme_color_override("font_color", visual_context.styles.GOLD)
		badge.add_theme_font_size_override("font_size", 15)
		badges.add_child(badge)
	if not _run_snapshot.get("relics", []).is_empty():
		var relic_badge := Label.new()
		relic_badge.text = "⬢  %s" % _run_snapshot.get("relics", []).size()
		relic_badge.add_theme_color_override("font_color", visual_context.styles.EMBER)
		badges.add_child(relic_badge)
	if not _run_snapshot.get("modifiers", []).is_empty():
		var modifier_badge := Label.new()
		modifier_badge.text = "△  %s" % _run_snapshot.get("modifiers", []).size()
		modifier_badge.add_theme_color_override("font_color", visual_context.styles.TEAL)
		badges.add_child(modifier_badge)
	if badges.get_child_count() > 0: header.add_child(badges)
	else: badges.free()
	return header


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
	content.add_child(visual_context.styles.muted(visual_context.translator.content_name(kind).to_upper(), 13))
	var title := visual_context.styles.wrap(visual_context.styles.title(presenter.node_name(str(node.get("nodeId", ""))), 30, visual_context.styles.GOLD), 180)
	content.add_child(title)
	var description: String = {
		"Encounter": visual_context.translator.text("Study the enemy's intent. Balance offense and defense."),
		"RelicReward": visual_context.translator.text("Claim a relic. Its effects will influence your upcoming battles."),
		"CardSelection": visual_context.translator.text("Choose a card for your deck. Offers are generated from your journey's seed."),
		"Shop": visual_context.translator.text("Spend your gold on supplies or refresh the merchant's stock."),
		"Preparation": visual_context.translator.text("Rest or prepare for your next battle."),
		"CardUpgrade": visual_context.translator.text("Upgrade a card to improve its base components.")
	}.get(kind, visual_context.translator.text("Choose your next action."))
	content.add_child(visual_context.styles.muted(presenter.node_description(node, description), 16, 180))
	var zone_view = preload("res://scripts/presentation/card_zone_presenter.gd").new(_zone_snapshot, visual_context.translator)
	content.add_child(visual_context.styles.muted(visual_context.translator.text("Deck · %s cards") % zone_view.total_cards(), 14))
	row.add_child(content)
	return visual_context.styles.panel(row)


func _build_ending(lifecycle: String) -> Control:
	var center := CenterContainer.new()
	center.size_flags_vertical = Control.SIZE_EXPAND_FILL
	var content := VBoxContainer.new()
	content.custom_minimum_size.x = 560
	var won := lifecycle.to_lower() == "completed"
	content.add_child(visual_context.styles.title(visual_context.translator.text("ARCHIVE COMPLETE") if won else visual_context.translator.text("THE FLAME HAS FADED"), 40,
		visual_context.styles.GOLD if won else visual_context.styles.BLOOD))
	content.add_child(visual_context.styles.muted(visual_context.translator.text("Your journey has been preserved. Watch its immutable recording, verify its integrity or start a new ascent.")))
	var replay_button := _button(visual_context.translator.text("WATCH REPLAY"), _replay.bind(str(_run_snapshot.get("runId", ""))), 420)
	replay_button.name = "EndingReplayButton"
	content.add_child(replay_button)
	content.add_child(_button(visual_context.translator.text("VERIFY INTEGRITY"), _verify, 420))
	content.add_child(_button(visual_context.translator.text("NEW JOURNEY"), _new_journey, 420))
	content.add_child(_button(visual_context.translator.text("MAIN MENU"), _main_menu, 420))
	center.add_child(visual_context.styles.panel(content))
	return center


func _current_node() -> Dictionary:
	return presenter.current_node()


func _card_name(id: String) -> String:
	return str(presentation.get("cards", {}).get(id, {}).get("name", id.replace("_", " ").capitalize()))


func _node_name(id: String) -> String:
	return str(presentation.get("nodes", {}).get(id, {}).get("name", id.replace("_", " ").capitalize()))


func _button(text: String, action: Callable, width := 0) -> Button:
	var value := visual_context.styles.button(text, width)
	value.name = "ActivityAction_" + str(action.get_method()).validate_node_name()
	value.pressed.connect(action)
	return value



func apply_projection(run: Dictionary, choices: Array, zones: Dictionary, data: Dictionary, projection, context) -> void:
	mount(context)
	theme = context.theme
	_run_snapshot = run.duplicate(true)
	_choices_snapshot = choices.duplicate(true)
	_zone_snapshot = zones.duplicate(true)
	presentation = data.duplicate(true)
	presenter = projection
	render_epoch += 1
	var action_scroll := _scroll_value("ActionScroll")
	var visual_key := visual_context.presentation_key()
	var display_run := _run_snapshot.duplicate(true)
	display_run.erase("sequence")
	var header_key: Array = [display_run, visual_key]
	if _region_keys.get("header") != header_key:
		var desired := _build_header()
		if is_instance_valid(_header):
			preload("res://scripts/ui/view_reconciler.gd").patch_contents(_header, desired); desired.free()
		else:
			_header = desired; add_child(_header)
		_region_keys.header = header_key.duplicate(true)
	var title := _header.find_child("RunTitle", true, false)
	if title: title.tooltip_text = "Seed: %s · Sequence: %s" % [run.get("seed", 0), run.get("sequence", 0)]
	var lifecycle := str(run.get("lifecycle", "Active")).to_lower()
	if _lifecycle != lifecycle:
		for node in [_columns, _ending]:
			if is_instance_valid(node): remove_child(node); node.queue_free()
		_columns = null; _ending = null; _summary = null
		_region_keys.erase("actions"); _region_keys.erase("summary")
		_lifecycle = lifecycle
	if lifecycle != "active":
		var ending_key: Array = [lifecycle, visual_key, run.get("runId", "")]
		if _region_keys.get("ending") != ending_key:
			var ending := _build_ending(lifecycle)
			if is_instance_valid(_ending):
				preload("res://scripts/ui/view_reconciler.gd").patch_contents(_ending, ending); ending.free()
			else: _ending = ending; add_child(_ending)
			_region_keys.ending = ending_key.duplicate(true)
		visual_context.styles.apply_view_preferences(self)
		return
	if not is_instance_valid(_columns):
		_build_structure()
	var summary_key: Array = [_current_node(), _zone_snapshot, visual_key]
	if _region_keys.get("summary") != summary_key:
		var dialogue := str(_current_node().get("activity", {}).get("type", "")) == "Dialogue"
		if dialogue:
			if is_instance_valid(_summary): _workspace.remove_child(_summary); _summary.queue_free(); _summary = null
		else:
			var summary := _activity_summary(_current_node())
			if is_instance_valid(_summary):
				preload("res://scripts/ui/view_reconciler.gd").patch_contents(_summary, summary); summary.free()
			else:
				_summary = summary; _workspace.add_child(summary); _workspace.move_child(summary, 0)
		_region_keys.summary = summary_key.duplicate(true)
	action_panel.apply_projection(_run_snapshot, choices, presenter, visual_context)
	visual_context.styles.apply_view_preferences(self)
	_restore_scroll.call_deferred(render_epoch, 0, action_scroll)

func _build_structure() -> void:
	add_theme_constant_override("separation", 16)
	_columns = HBoxContainer.new(); _columns.name = "ActivityColumns"
	_columns.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_columns.add_theme_constant_override("separation", 22)
	add_child(_columns)
	_workspace = VBoxContainer.new(); _workspace.name = "ActivityWorkspace"
	_workspace.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_workspace.add_theme_constant_override("separation", 14)
	var scroll := ScrollContainer.new(); scroll.name = "ActionScroll"
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	action_panel = OptionsView.new(); action_panel.name = "ActionPanel"
	action_panel.intent_requested.connect(func(action, arguments): intent_requested.emit(action, arguments))
	action_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(action_panel); _workspace.add_child(scroll); _columns.add_child(_workspace)

func set_choices_disabled(disabled: bool) -> void:
	for button in choice_buttons:
		if is_instance_valid(button): button.disabled = disabled or bool(button.get_meta("engine_disabled", false))
func _execute(choice: Dictionary) -> void:
	intent_requested.emit("execute", [presenter.option_reference(choice)])
func _confirm_ref(reference_id: String) -> void: intent_requested.emit("confirm", [reference_id])
func _verify() -> void: intent_requested.emit("verify", [])
func _recover_actions() -> void: intent_requested.emit("recover", [])
func _pause() -> void: intent_requested.emit("pause", [])
func _replay(run_id: String) -> void: intent_requested.emit("replay", [run_id])
func _new_journey() -> void: intent_requested.emit("new_journey", [])
func _main_menu() -> void: intent_requested.emit("main_menu", [])


func _choice_label(choice: Dictionary, model: Dictionary) -> String:
	return action_panel._choice_label(choice, model)


func _map() -> void: intent_requested.emit("map", [])
