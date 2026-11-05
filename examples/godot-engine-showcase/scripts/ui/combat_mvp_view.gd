extends "res://scripts/ui/combat_view.gd"
## Layout-only MVP variant. Rule authority remains in the shared adapter/engine.
func _build_header() -> void:
	super._build_header()
	var title: Label = header_view.get_node("HeroName")
	header_view.move_child(title, 0)
	title.text = presenter.actor_name(_input_actor)
	header_view.get_node("TimelineButton").hide()
	# TODO(v2): add the log control after reviewing the published log projection.

func _build_hand() -> void:
	super._build_hand()
	var energy: Label = hand_section.get_node("Energy")
	hand_section.move_child(energy, 0)
	energy.text = ""
	for actor in _combat_snapshot.get("actors", []):
		if str(actor.get("instanceId", "")) != _input_actor: continue
		var resources: Dictionary = actor.get("resources", {})
		if resources.get("energy") is Dictionary:
			var value: Dictionary = resources.energy
			energy.text = "%s: %s / %s" % [visual_context.translator.content_name("energy"), visual_context.translator.number(value.get("current", 0)), visual_context.translator.number(value.get("maximum", value.get("current", 0)))]
	energy.visible = not energy.text.is_empty()

func _build_footer() -> void:
	super._build_footer()
	var gap: Control = turn_view.get_node("EndTurnGap")
	turn_view.move_child(gap, turn_view.get_child_count() - 2)

func _layout_gameplay() -> void:
	super._layout_gameplay()
	if not is_instance_valid(bottom_hud) or not is_instance_valid(turn_view): return
	var balance: Control = bottom_hud.get_node("Balance")
	bottom_hud.move_child(balance, 0)
	balance.custom_minimum_size.x = turn_view.get_combined_minimum_size().x
	# Matching edge reservations center the hand in the battlefield, while the
	# right controls stay attached to the edge. Other aspect ratios are provisional.
	bottom_hud.add_theme_constant_override("separation", 16)
