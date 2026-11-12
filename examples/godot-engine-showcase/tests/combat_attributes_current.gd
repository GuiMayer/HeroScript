extends SceneTree

func _initialize() -> void:
	call_deferred("run")

func run() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new(null, {"reduced_motion": true})
	var drawer = preload("res://scenes/ui/components/combat/combat_attributes_drawer.tscn").instantiate()
	root.add_child(drawer)
	drawer.mount(context)
	var model := {"run": {"playerEntityId": "hero"}, "actors": [{"instanceId": "hero", "definitionId": "first_hero", "resources": {"health": {"current": 20, "maximum": 30}}}], "attributes": {"actorId": "hero", "attributes": []}}
	drawer.present(model, context)
	var portrait = drawer.attributes_view.get_node("%Portrait")
	assert(portrait.model.contentId == "first_hero")
	assert(not drawer.title_label.text.contains("[I]"))
	assert(not drawer.toggle_button.tooltip_text.contains("[I]"))
	model.actors[0].definitionId = "second_hero"
	model.actors[0].resources.health.current = 11
	drawer.present(model, context)
	assert(portrait.model.contentId == "second_hero")
	assert(drawer.attributes_view._rows["resource:health"].equipped.text.begins_with("11"))
	print("PASS: portrait and resources follow the active character; shortcut hint hidden")
	drawer.queue_free()
	quit()
