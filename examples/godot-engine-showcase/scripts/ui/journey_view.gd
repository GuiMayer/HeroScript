@tool
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
var _action_scroll: ScrollContainer
var _lifecycle := ""
var _region_keys: Dictionary = {}
@export var initial_ending_header_gap := 4
func set_compartments_available(value: bool) -> void:
	var button := find_child("DeckCompartmentsButton", true, false)
	if button is Button: button.visible = value
func _scroll_value(name: String) -> int:
	if name == "ActionScroll" and is_instance_valid(_action_scroll): return _action_scroll.scroll_vertical
	var scroll := find_child(name, true, false)
	return scroll.scroll_vertical if scroll is ScrollContainer else 0


func _restore_scroll(ticket: int, route_value: int, action_value: int, route_horizontal := 0) -> void:
	if ticket != render_epoch or not is_inside_tree(): return
	var route := find_child("RouteScroll", true, false)
	var actions := _action_scroll if is_instance_valid(_action_scroll) else find_child("ActionScroll", true, false)
	# Container bounds settle at the end of the frame; defer the values once more
	# so Godot does not clamp a valid previous position against an old zero range.
	if route is ScrollContainer:
		route.set_deferred("scroll_vertical", route_value)
		route.set_deferred("scroll_horizontal", route_horizontal)
	if actions is ScrollContainer: actions.set_deferred("scroll_vertical", action_value)


var _bound := false
var _badges := {}
static func create():
	return (load("res://scenes/ui/components/journey_view.tscn") as PackedScene).instantiate()
func _ready() -> void:
	if visual_context == null:
		mount(ContextType.new()); theme = visual_context.theme
	_bind_nodes()
func _bind_nodes() -> void:
	if _bound: return
	_header = get_node("ActivityHeader"); _columns = get_node("ActivityColumns")
	_workspace = _columns.get_node("ActivityWorkspace"); _summary = _workspace.get_node("ActivitySummary")
	_workspace.get_node("EncounterAttempt").adjust_requested.connect(func(): intent_requested.emit("loadout",[]))
	_action_scroll = _workspace.get_node("ActionScroll")
	action_panel = _workspace.get_node("ActionScroll/ActionPanel"); _ending = get_node("ActivityEnding")
	action_panel.intent_requested.connect(func(action,arguments): intent_requested.emit(action,arguments))
	_header.get_node("Row/Verify").pressed.connect(_verify); _header.get_node("Row/Map").pressed.connect(_map)
	_header.get_node("Row/Loadout").pressed.connect(func(): intent_requested.emit("loadout",[]))
	_header.get_node("Row/Pause").pressed.connect(_pause)
	_ending.get_node("Panel/Content/EndingReplayButton").pressed.connect(func(): _replay(str(_run_snapshot.get("runId",""))))
	_ending.get_node("Panel/Content/Verify").pressed.connect(_verify)
	_ending.get_node("Panel/Content/NewJourney").pressed.connect(_new_journey)
	_ending.get_node("Panel/Content/MainMenu").pressed.connect(_main_menu); _bound = true
func _update_header() -> void:
	var metadata: Dictionary = _run_snapshot.get("metadata",{})
	_header.get_node("Row/Titles/RunTitle").text = visual_context.translator.content_name(str(_run_snapshot.get("runDefinitionId","")),str(metadata.get("displayName",visual_context.translator.text("THE EMBER PATH"))))
	_header.get_node("Row/Titles/Subtitle").text = visual_context.translator.text("Build your deck. Choose your path.")
	for entry in [["Verify","VERIFY REPLAY"],["Map","MAP"],["Loadout","LOADOUT"],["Pause","PAUSE"]]: _header.get_node("Row/"+entry[0]).text = visual_context.translator.text(entry[1])
	var live: Array = []; var index := 0
	var character: Dictionary = _run_snapshot.get("characterResources",{})
	for id in character.get("resources",{}):
		var pool: Dictionary = character.resources[id]
		var text := "%s/%s %s" % [visual_context.translator.number(float(pool.get("current",0))),visual_context.translator.number(float(pool.get("maximum",0))),visual_context.translator.content_name(str(id))]
		_badge("character_"+str(id),text,visual_context.styles.TEAL,index,live,visual_context.translator.text("Character resources")+" · "+str(character.get("ownerId",""))); index += 1
	for id in _run_snapshot.get("resources",{}):
		var pool: Dictionary = _run_snapshot.resources[id]
		_badge("run_"+str(id),"%s %s" % [visual_context.translator.number(float(pool.get("current",0))),visual_context.translator.content_name(str(id))],visual_context.styles.GOLD,index,live,"",15); index += 1
	for entry in [["relics","⬢",visual_context.styles.EMBER],["modifiers","△",visual_context.styles.TEAL]]:
		if not _run_snapshot.get(entry[0],[]).is_empty():
			_badge(entry[0],"%s  %s" % [entry[1],_run_snapshot[entry[0]].size()],entry[2],index,live); index += 1
	for id in _badges.keys():
		if id not in live:
			var old = _badges[id]; old.get_parent().remove_child(old); old.queue_free(); _badges.erase(id)
	_header.get_node("Badges").visible = not live.is_empty()
func _badge(id: String,text: String,color: Color,index: int,live: Array,hint := "",font_size := 0) -> void:
	var label = _badges.get(id)
	if not is_instance_valid(label):
		label = preload("res://scenes/ui/components/activity_badge.tscn").instantiate()
		label.name = "Badge_"+id.validate_node_name(); _header.get_node("Badges").add_child(label); _badges[id] = label
	label.text = text; label.tooltip_text = hint; label.add_theme_color_override("font_color",color)
	if font_size > 0: label.add_theme_font_size_override("font_size",font_size)
	_header.get_node("Badges").move_child(label,index); live.append(id)
func _update_summary(node: Dictionary) -> void:
	var kind := str(node.get("activity",{}).get("type","Unknown"))
	var art = _summary.get_node("Row/Art"); art.rebind_context(visual_context); art.setup("activities",kind)
	var content = _summary.get_node("Row/Content")
	content.get_node("Kind").text = visual_context.translator.content_name(kind).to_upper()
	content.get_node("Title").text = presenter.node_name(str(node.get("nodeId","")))
	content.get_node("Title").add_theme_color_override("font_color",visual_context.styles.GOLD)
	var description: String = {
		"Encounter": visual_context.translator.text("Study the enemy's intent. Balance offense and defense."),
		"RelicReward": visual_context.translator.text("Claim a relic. Its effects will influence your upcoming battles."),
		"CardSelection": visual_context.translator.text("Choose a card for your deck. Offers are generated from your journey's seed."),
		"Shop": visual_context.translator.text("Spend your gold on supplies or refresh the merchant's stock."),
		"Preparation": visual_context.translator.text("Rest or prepare for your next battle."),
		"CardUpgrade": visual_context.translator.text("Upgrade a card to improve its base components.")
	}.get(kind, visual_context.translator.text("Choose your next action."))
	content.get_node("Description").text = presenter.node_description(node,description)
	var zones = preload("res://scripts/presentation/card_zone_presenter.gd").new(_zone_snapshot,visual_context.translator)
	content.get_node("DeckCount").text = visual_context.translator.text("Deck · %s cards") % zones.total_cards()
func _update_ending(lifecycle: String) -> void:
	var content = _ending.get_node("Panel/Content"); var won := lifecycle == "completed"
	content.get_node("Title").text = visual_context.translator.text("ARCHIVE COMPLETE" if won else "THE FLAME HAS FADED")
	content.get_node("Title").add_theme_color_override("font_color",visual_context.styles.GOLD if won else visual_context.styles.BLOOD)
	content.get_node("Description").text = visual_context.translator.text("Your journey has been preserved. Watch its immutable recording, verify its integrity or start a new ascent.")
	for entry in [["EndingReplayButton","WATCH REPLAY"],["Verify","VERIFY INTEGRITY"],["NewJourney","NEW JOURNEY"],["MainMenu","MAIN MENU"]]: content.get_node(entry[0]).text = visual_context.translator.text(entry[1])

func _current_node() -> Dictionary:
	return presenter.current_node()


func _card_name(id: String) -> String:
	return str(presentation.get("cards", {}).get(id, {}).get("name", id.replace("_", " ").capitalize()))


func _node_name(id: String) -> String:
	return str(presentation.get("nodes", {}).get(id, {}).get("name", id.replace("_", " ").capitalize()))


func apply_projection(run: Dictionary, choices: Array, zones: Dictionary, data: Dictionary, projection, context, can_adjust_build := false) -> void:
	var first_projection := _region_keys.is_empty()
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
	_bind_nodes()
	if _region_keys.get("header") != header_key:
		_update_header(); _region_keys.header = header_key.duplicate(true)
	_header.get_node("Row/Titles/RunTitle").tooltip_text = "Seed: %s · Sequence: %s" % [run.get("seed",0),run.get("sequence",0)]
	var lifecycle := str(run.get("lifecycle","Active")).to_lower()
	if first_projection and lifecycle != "active": add_theme_constant_override("separation",initial_ending_header_gap)
	_columns.visible = lifecycle == "active"; _ending.visible = lifecycle != "active"
	if lifecycle != "active":
		_update_ending(lifecycle); visual_context.styles.apply_view_preferences(self); return
	_workspace.get_node("EncounterAttempt").apply_projection(run, can_adjust_build, visual_context)
	var summary_key: Array = [_current_node(),_zone_snapshot,visual_key]
	_summary.visible = str(_current_node().get("activity",{}).get("type","")) != "Dialogue"
	if _summary.visible and _region_keys.get("summary") != summary_key:
		_update_summary(_current_node()); _region_keys.summary = summary_key.duplicate(true)
	action_panel.animate_card_entry = animate_card_entry
	action_panel.apply_projection(_run_snapshot,choices,presenter,visual_context)
	visual_context.styles.apply_view_preferences(self)
	_restore_scroll.call_deferred(render_epoch,0,action_scroll)

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


func choice_label(choice: Dictionary, model: Dictionary) -> String: return action_panel.choice_label(choice, model)
func _choice_label(choice: Dictionary, model: Dictionary) -> String:
	return action_panel.choice_label(choice, model)


func _map() -> void: intent_requested.emit("map", [])
