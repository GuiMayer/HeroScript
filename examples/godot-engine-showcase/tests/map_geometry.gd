extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var layout = load("res://scripts/presentation/map_layout.gd").new()
	var profile = load("res://scripts/ui/styles/map_layout_profile.gd").new()
	var nodes := [{"id": "start", "next": ["b", "a"]}, {"id": "a", "next": ["boss"]},
		{"id": "b", "next": ["boss"]}, {"id": "boss", "next": []}, {"id": "isolated", "next": []}]
	var result: Dictionary = layout.calculate(nodes, profile)
	check(result.rects.a.position.y == result.rects.b.position.y and result.rects.a.position.x != result.rects.b.position.x, "parallel routes occupy separate lanes")
	check(result.rects.boss.position.y > result.rects.a.position.y and result.rects.start.position.y < result.rects.a.position.y, "DAG layers follow published topology, not array order")
	check(result.rects.has("isolated") and result.edges.size() == 4, "disconnected nodes remain visible without invented edges")
	nodes.reverse()
	check(layout.calculate(nodes, profile) == result and layout.calculation_count == 1, "reordering equivalent source nodes preserves geometry and cache")
	nodes[0]["current"] = true; nodes[0]["travel"] = {"type": "ADVANCE_NODE"}
	check(layout.calculate(nodes, profile) == result and layout.calculation_count == 1, "availability and current stop do not relayout topology")
	result.rects.clear()
	check(layout.calculate(nodes, profile).rects.size() == 5, "geometry results are defensively copied")
	var cycle: Dictionary = layout.calculate([{"id": "a", "next": ["b"]}, {"id": "b", "next": ["a", "b", "missing"]}], profile)
	check(cycle.cyclic and cycle.rects.size() == 2 and cycle.edges.size() == 3, "cycles/self/return edges survive and dangling references are safely ignored")
	check(not cycle.rects.a.intersects(cycle.rects.b), "cyclic nodes never overlap")
	var graph: Array = []
	for i in 100: graph.append({"id": "n%03d" % i, "next": ["n%03d" % (i + 1)] if i < 99 else []})
	var large: Dictionary = layout.calculate(graph, profile)
	check(large.rects.size() == 100 and large.edges.size() == 99 and large.bounds.y > 6000, "large custom maps retain all readable nodes and directed edges")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
