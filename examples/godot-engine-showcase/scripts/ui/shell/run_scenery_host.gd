@tool
extends Control
## Shell-owned ambient scenery. UI theme/palette cannot recolor it.
var background: EmberBackground
var _combat_scenery: Control
func acquire_combat() -> Control:
	if is_instance_valid(_combat_scenery): release_combat(_combat_scenery)
	_combat_scenery = preload("res://scripts/ui/components/combat_scenery_view.gd").new()
	_combat_scenery.name = "CombatScenery"
	add_child(_combat_scenery)
	return _combat_scenery

func release_combat(node: Control) -> void:
	if node != _combat_scenery or not is_instance_valid(node): return
	_combat_scenery = null
	remove_child(node)
	node.queue_free()
func _ready() -> void:
	if background == null and (Engine.is_editor_hint() or get_parent() == get_tree().root):
		var context = preload("res://scripts/ui/visual_context.gd").new()
		context.set_scenery_profile(preload("res://data/themes/ivory_scenery.tres"))
		mount(context); set_active(true)
func mount(context) -> void:
	background = get_node("%GameplayBackground")
	background.visual_context = context
	background.queue_redraw()

func set_active(value: bool) -> void:
	visible = value
	background.visible = value
	background.set_process(value and background.visual_context.scenery_profile.ambient_style != "paper")
	if value: background.queue_redraw()
