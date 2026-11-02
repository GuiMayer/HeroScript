@tool
class_name BattlefieldView
extends Control
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType = ContextType.new()
signal target_selected(actor_id: String)
signal target_previewed(actor_id: String)
signal preview_left
signal layout_requested
var actor_scroll: ScrollContainer
var targets := {}
var panels := {}
var allies: HBoxContainer
var opponents: HBoxContainer
var _field: HBoxContainer
var _teams: Dictionary = {}
static func create() -> BattlefieldView:
	return (load("res://scenes/ui/components/battlefield.tscn") as PackedScene).instantiate()
func _bind_nodes() -> void:
	actor_scroll = get_node("BattlefieldActors"); _field = actor_scroll.get_node("Field")
	allies = _field.get_node("Allies"); opponents = _field.get_node("Opponents")
func _ready() -> void:
	_bind_nodes(); theme = visual_context.theme
func apply_model(actors: Array, presentation: Dictionary) -> void:
	_bind_nodes(); theme = visual_context.theme
	_field.get_node("Divider/Glyph").add_theme_color_override("font_color", visual_context.styles.GOLD)
	_sync_actors(actors, presentation)

func _actor_card(actor: Dictionary, hostile: bool, intent: String, presentation: Dictionary) -> Control:
	var panel := preload("res://scripts/ui/actor_panel.gd").create(visual_context)
	var id := str(actor.get("instanceId", ""))
	panel.name = "ActorView_" + id.validate_node_name()
	panel.setup(actor, hostile, presentation, intent, false, true)
	panel.minimum_size_changed.connect(func(): layout_requested.emit())
	panel.target_selected.connect(target_selected.emit)
	panel.target_button.mouse_entered.connect(func(): target_previewed.emit(id))
	panel.target_button.focus_entered.connect(func(): target_previewed.emit(id))
	panel.target_button.mouse_exited.connect(func():
		preview_left.emit())
	panels[id] = panel
	targets[id] = panel.target_button
	return panel


func _sync_actors(actors: Array, presentation: Dictionary) -> void:
	var live: Array = []
	for item in actors:
		var actor: Dictionary = item.actor
		var id := str(actor.get("instanceId", ""))
		live.append(id)
		var relation := str(item.get("relationship", actor.get("relationship", "Neutral")))
		var team_key := JSON.stringify([str(item.get("sideId", actor.get("sideId", ""))), relation])
		if not _teams.has(team_key):
			var existing: Array = _teams.values()
			var group: HBoxContainer
			if relation == "Ally" and allies not in existing: group = allies
			elif relation == "Enemy" and opponents not in existing: group = opponents
			else:
				group = preload("res://scenes/ui/components/battlefield_team.tscn").instantiate()
				_field.add_child(group)
			_teams[team_key] = group
		var parent: HBoxContainer = _teams[team_key]
		var hostile := relation == "Enemy"
		if panels.has(id):
			var panel: Control = panels[id]
			if panel.get_parent() != parent: panel.reparent(parent)
			panel.setup(actor, hostile, presentation, str(item.intent), false, true)
		else:
			parent.add_child(_actor_card(actor, hostile, str(item.intent), presentation))
	for id in panels.keys():
		if id not in live:
			var panel: Control = panels[id]
			panel.get_parent().remove_child(panel)
			panel.queue_free()
			panels.erase(id); targets.erase(id)
	for key in _teams.keys():
		var group: HBoxContainer = _teams[key]
		if group.get_child_count() == 0:
			_teams.erase(key)
			if group != allies and group != opponents:
				_field.remove_child(group); group.queue_free()
