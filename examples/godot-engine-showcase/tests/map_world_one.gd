extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var fixture: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://tests/fixtures/map_world_one.json"))
	var nodes: Array = fixture.nodes.map(func(n): return {"id":n.nodeId,"next":n.nextNodeIds})
	var layout = load("res://scripts/presentation/map_layout.gd").new()
	var profile = load("res://scripts/ui/styles/map_layout_profile.gd").new()
	var result: Dictionary = layout.calculate(nodes, profile)
	check(result.rects.size() == 50 and result.bounds.x <= 800, "real world-one topology keeps all 50 stops and fits six lanes in 800 units")
	var layers: Dictionary = {}
	for rect in result.rects.values(): layers[rect.position.y] = true
	check(layers.size() == 25, "all 25 authoritative stages remain individually inspectable")
	var original := result.duplicate(true); nodes.reverse()
	check(layout.calculate(nodes, profile) == original and layout.calculation_count == 1, "real-world geometry is stable under reordered snapshots")
	profile.compact_auxiliary = false
	check(layout.calculate(nodes, profile) == original, "auxiliary visual distinction never hides, groups or moves a transaction")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
