extends VBoxContainer
## Read-only visual state after a saved command, not an intermediate reconstruction.
var presenter
var actor_panels := {}

func display(state: Dictionary, appearance: Dictionary) -> void:
	for child in get_children():
		remove_child(child)
		child.queue_free()
	actor_panels.clear()
	var combat: Dictionary = state.get("combat", {})
	presenter = preload("res://scripts/presentation/combat_presenter.gd").new(state.get("run", {}), combat, [], I18n)
	add_child(AppTheme.muted(I18n.text("Saved state after command #%s") % int(state.get("runSequence", 0))))
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	var row := HBoxContainer.new()
	var viewpoint := str(combat.get("activation", {}).get("activeActorId", ""))
	for actor in presenter.actors():
		var panel := preload("res://scripts/ui/actor_panel.gd").new()
		panel.setup(actor, presenter.relationship(actor, viewpoint) == "Enemy", appearance, presenter._intent_for(str(actor.get("instanceId", ""))))
		panel.target_button.hide()
		actor_panels[str(actor.get("instanceId", ""))] = panel
		row.add_child(panel)
	scroll.add_child(row)
	add_child(scroll)
	AppTheme.apply_view_preferences(self)
