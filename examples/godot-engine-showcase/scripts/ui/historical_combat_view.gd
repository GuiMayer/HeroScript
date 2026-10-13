extends VBoxContainer
## Read-only visual state after a saved command, not an intermediate reconstruction.
var visual_context
func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
var presenter
var actor_panels := {}
var _row: HBoxContainer
var _run: Dictionary
var _appearance: Dictionary
func apply_frame_snapshot(combat: Dictionary) -> void:
	presenter = preload("res://scripts/presentation/combat_presenter.gd").new(_run, combat, [], visual_context.translator)
	for actor in presenter.actors():
		var id := str(actor.get("instanceId", ""))
		var panel = actor_panels.get(id)
		if panel == null:
			panel = preload("res://scripts/ui/actor_panel.gd").new(visual_context); _row.add_child(panel); actor_panels[id] = panel
		panel.setup(actor, presenter.relationship(actor, str(combat.get("activeActorId", ""))) == "Enemy", _appearance, presenter.intent_for(id))
		panel.target_button.disabled = true; panel.target_button.focus_mode = Control.FOCUS_NONE

func display(state: Dictionary, appearance: Dictionary) -> void:
	for child in get_children():
		remove_child(child)
		child.queue_free()
	actor_panels.clear()
	var combat: Dictionary = state.get("combat", {})
	_run = state.get("run", {}).duplicate(true); _appearance = appearance.duplicate(true)
	presenter = preload("res://scripts/presentation/combat_presenter.gd").new(state.get("run", {}), combat, [], visual_context.translator)
	add_child(AppTheme.muted(visual_context.translator.text("Saved state after command #%s") % int(state.get("runSequence", 0))))
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	var row := HBoxContainer.new()
	_row = row
	var viewpoint := str(combat.get("activation", {}).get("activeActorId", ""))
	for actor in presenter.actors():
		var panel := preload("res://scripts/ui/actor_panel.gd").new(visual_context)
		panel.setup(actor, presenter.relationship(actor, viewpoint) == "Enemy", appearance, presenter.intent_for(str(actor.get("instanceId", ""))))
		panel.target_button.disabled = true
		panel.target_button.focus_mode = Control.FOCUS_NONE
		actor_panels[str(actor.get("instanceId", ""))] = panel
		row.add_child(panel)
	scroll.add_child(row)
	add_child(scroll)
	AppTheme.apply_view_preferences(self)
