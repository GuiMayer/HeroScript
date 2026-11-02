@tool
extends VBoxContainer
## Read-only visual state after a saved command, not an intermediate reconstruction.
var visual_context
static func create(context = null):
	var view = load("res://scenes/ui/components/historical_combat.tscn").instantiate()
	view.visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
	return view
func _ready() -> void:
	if visual_context == null: visual_context = preload("res://scripts/ui/visual_context.gd").new()
	theme = visual_context.theme
var presenter
var actor_panels := {}
var _row: HBoxContainer
var _run: Dictionary
var _appearance: Dictionary
func apply_frame_snapshot(combat: Dictionary) -> void:
	combat = preload("res://scripts/contracts/snapshot_projection.gd").combat(combat)
	presenter = preload("res://scripts/presentation/combat_presenter.gd").new(_run, combat, [], visual_context.translator)
	var visible_ids := {}
	for actor in presenter.actors():
		var id := str(actor.get("instanceId", ""))
		visible_ids[id] = true
		var panel = actor_panels.get(id)
		if panel == null:
			panel = preload("res://scripts/ui/actor_panel.gd").create(visual_context); _row.add_child(panel); actor_panels[id] = panel
		panel.setup(actor, presenter.relationship(actor, str(combat.get("activeActorId", ""))) == "Enemy", _appearance, presenter.intent_for(id))
		panel.target_button.disabled = true; panel.target_button.focus_mode = Control.FOCUS_NONE
	for id in actor_panels.keys():
		if visible_ids.has(id): continue
		var removed = actor_panels[id]
		_row.remove_child(removed); removed.queue_free(); actor_panels.erase(id)

func display(state: Dictionary, appearance: Dictionary) -> void:
	_row = get_node("Scroll/Actors")
	for child in _row.get_children():
		_row.remove_child(child)
		child.queue_free()
	actor_panels.clear()
	var combat: Dictionary = state.get("combat", {})
	_run = state.get("run", {}).duplicate(true); _appearance = appearance.duplicate(true)
	presenter = preload("res://scripts/presentation/combat_presenter.gd").new(state.get("run", {}), combat, [], visual_context.translator)
	theme = visual_context.theme
	get_node("Heading").text = visual_context.translator.text("Saved state after command #%s") % int(state.get("runSequence", 0))
	var viewpoint := str(combat.get("activation", {}).get("activeActorId", ""))
	for actor in presenter.actors():
		var panel := preload("res://scripts/ui/actor_panel.gd").create(visual_context)
		panel.setup(actor, presenter.relationship(actor, viewpoint) == "Enemy", appearance, presenter.intent_for(str(actor.get("instanceId", ""))))
		panel.target_button.disabled = true
		panel.target_button.focus_mode = Control.FOCUS_NONE
		actor_panels[str(actor.get("instanceId", ""))] = panel
		_row.add_child(panel)
	AppTheme.apply_view_preferences(self)
