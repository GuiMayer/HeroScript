extends Control
signal target_selected(actor_id: String)
signal target_previewed(actor_id: String)
signal preview_left
signal layout_requested
var actor_scroll: ScrollContainer
var portraits := {}
var targets := {}
var panels := {}
var allies: HBoxContainer
var opponents: HBoxContainer
func apply_model(actors: Array, presentation: Dictionary) -> void:
	if is_instance_valid(actor_scroll):
		_sync_actors(actors, presentation)
		return
	var stage := self
	stage.name = "Battlefield"
	stage.mouse_filter = Control.MOUSE_FILTER_IGNORE
	stage.clip_contents = true
	var backdrop := preload("res://scripts/ui/art_slot.gd").new()
	backdrop.setup("backgrounds", "combat")
	stage.add_child(backdrop)
	backdrop.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var scroller := ScrollContainer.new()
	scroller.name = "BattlefieldActors"
	actor_scroll = scroller
	scroller.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	scroller.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	var field := HBoxContainer.new()
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
	_sync_actors(actors, presentation)
	field.add_child(allies)
	var divider := CenterContainer.new()
	divider.custom_minimum_size.x = 88
	divider.add_child(AppTheme.title("◇", 26, AppTheme.GOLD))
	field.add_child(divider)
	field.add_child(opponents)
	scroller.add_child(field)
	stage.add_child(scroller)
	scroller.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

func _actor_card(actor: Dictionary, hostile: bool, intent: String, presentation: Dictionary) -> Control:
	var panel := preload("res://scripts/ui/actor_panel.gd").new()
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
	portraits[id] = panel.portrait_view
	targets[id] = panel.target_button
	return panel


func _sync_actors(actors: Array, presentation: Dictionary) -> void:
	var live: Array = []
	for item in actors:
		var actor: Dictionary = item.actor
		var id := str(actor.get("instanceId", ""))
		live.append(id)
		var parent := opponents if bool(item.hostile) else allies
		if panels.has(id):
			var panel: Control = panels[id]
			if panel.get_parent() != parent: panel.reparent(parent)
			panel.setup(actor, bool(item.hostile), presentation, str(item.intent), false, true)
		else:
			parent.add_child(_actor_card(actor, bool(item.hostile), str(item.intent), presentation))
	for id in panels.keys():
		if id not in live:
			var panel: Control = panels[id]
			panel.get_parent().remove_child(panel)
			panel.queue_free()
			panels.erase(id); portraits.erase(id); targets.erase(id)
