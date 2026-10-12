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
func apply_model(actors: Array, presentation: Dictionary) -> void:
	theme = visual_context.theme
	if is_instance_valid(actor_scroll):
		_sync_actors(actors, presentation)
		return
	var stage := self
	stage.name = "Battlefield"
	stage.mouse_filter = Control.MOUSE_FILTER_IGNORE
	stage.clip_contents = true
	var scroller := ScrollContainer.new()
	scroller.name = "BattlefieldActors"
	actor_scroll = scroller
	scroller.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	scroller.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	var field := HBoxContainer.new()
	_field = field
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.size_flags_vertical = Control.SIZE_EXPAND_FILL
	field.alignment = BoxContainer.ALIGNMENT_CENTER
	field.add_theme_constant_override("separation", 22)
	allies = HBoxContainer.new()
	opponents = HBoxContainer.new()
	allies.alignment = BoxContainer.ALIGNMENT_CENTER
	opponents.alignment = BoxContainer.ALIGNMENT_CENTER
	for side in [allies, opponents]:
		side.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		side.size_flags_vertical = Control.SIZE_EXPAND_FILL
		side.add_theme_constant_override("separation", 18)
	field.add_child(allies)
	var divider := CenterContainer.new()
	divider.custom_minimum_size.x = 88
	divider.add_child(visual_context.styles.title("◇", 26, visual_context.styles.GOLD))
	field.add_child(divider)
	field.add_child(opponents)
	_sync_actors(actors, presentation)
	scroller.add_child(field)
	stage.add_child(scroller)
	scroller.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

func _actor_card(actor: Dictionary, hostile: bool, intent: String, presentation: Dictionary) -> Control:
	var panel := preload("res://scripts/ui/actor_panel.gd").new(visual_context)
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
				group = HBoxContainer.new()
				group.alignment = BoxContainer.ALIGNMENT_CENTER
				group.size_flags_vertical = Control.SIZE_EXPAND_FILL
				group.add_theme_constant_override("separation", 18)
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
