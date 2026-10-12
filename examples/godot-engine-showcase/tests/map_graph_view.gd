extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func node(id: String, next: Array, available := false) -> Dictionary:
	return {"id": id, "name": "A readable stop " + id, "kind": "Encounter", "current": false,
		"resolved": false, "risk": "High" if id == "b" else "Normal", "next": next,
		"travel": {"type": "ADVANCE_NODE", "payload": {"targetNodeId": id}} if available else {}}
func _run() -> void:
	var scroll := ScrollContainer.new()
	scroll.position = Vector2(24, 24); scroll.size = Vector2(360, 520)
	scroll.follow_focus = true; root.add_child(scroll)
	var route = load("res://scripts/ui/route_view.gd").new(); scroll.add_child(route)
	var nodes := [node("start", ["a", "b"]), node("a", ["boss"]), node("b", ["boss"], true), node("boss", [])]
	route.setup(nodes); await settle()
	for resolution in [Vector2i(1280, 720), Vector2i(2560, 1080)]:
		root.size = resolution; await settle()
		check(scroll.get_h_scroll_bar().visible and route.buttons.b.size.x >= 230, "wide graph scrolls without shrinking nodes at %s" % resolution)
		route.buttons.b.grab_focus(); await settle()
		check(scroll.scroll_horizontal > 0 and scroll.get_global_rect().intersects(route.buttons.b.get_global_rect()), "focus reveals an offscreen published choice at %s" % resolution)
	check(route.buttons.a.disabled and not route.buttons.b.disabled and route.buttons.b.text.contains("High risk"), "only published choices enable travel; risk is explicit text")
	var positions: Dictionary = route.markers.duplicate(true)
	var retained: Button = route.buttons.b
	nodes[0].current = true; nodes.reverse(); route.setup(nodes); await settle()
	check(route.markers == positions and route.buttons.b == retained and retained.has_focus(), "state change and array reorder preserve map geometry, identity and focus")
	nodes.append(node("return", ["b", "return"], true)); route.setup(nodes); await settle()
	check(route.buttons.has("return") and route._geometry.edges.any(func(edge): return edge.from == "return" and edge.to == "return"), "hotreloaded self/return edges retain their nodes and drawn paths")
	check(load("res://tests/layout_inspector.gd").vertical_text_issues(scroll).is_empty(), "graph text remains horizontal and readable")
	scroll.queue_free(); await settle()
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
