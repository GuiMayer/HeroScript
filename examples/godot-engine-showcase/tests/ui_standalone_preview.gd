extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run() -> void:
	root.size = Vector2i(1920, 1080)
	var before: Dictionary = root.get_node("GameSession").run.duplicate(true)
	for path in ["components/card_hand", "components/combat_view", "components/combat_mvp_view", "components/map_view", "screens/combat_screen", "screens/combat_mvp", "screens/map_screen"]:
		var scene = load("res://scenes/ui/" + path + ".tscn").instantiate()
		root.add_child(scene)
		current_scene = scene
		for _frame in 18: await process_frame
		if path.ends_with("card_hand"): check(scene.order.size() == 5, path + " renders five local cards")
		elif "combat" in path:
			var view = scene.get_node("CombatView") if path.begins_with("screens/") else scene
			check(view.hand.order.size() == 5 and view.target_buttons.size() == 2, path + " renders local actors and hand without a run")
		else:
			var view = scene.get_node("MapView") if path.begins_with("screens/") else scene
			check(view.route.buttons.size() == 4, path + " renders the campaign graph without a run")
		current_scene = null
		scene.queue_free()
		await process_frame
	check(root.get_node("GameSession").run == before, "previews do not overwrite the real session")
	quit(1 if failures else 0)
