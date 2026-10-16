extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var model = load("res://scripts/presentation/map_navigation_model.gd").new()
	var nodes := [{"id":"a", "current":true, "next":["b","c"]},
		{"id":"b", "travel":{"optionId":"opaque"}}, {"id":"c", "kind":"Unknown"}]
	var before := JSON.stringify(nodes)
	model.update(nodes); model.inspect("c"); model.hovered = "b"; model.focused = "a"
	check(model.selected == "c" and model.current == "a" and model.hovered == "b", "selection, hover, focus and authoritative position are independent")
	check(model.alternatives() == ["b"] and model.decision_ids().has("a"), "only published choices are alternatives, never graph edges")
	model.nodes.b.travel.clear()
	check(JSON.stringify(nodes) == before, "reading model owns copies and cannot mutate engine projections")
	model.update(nodes); nodes[1].erase("travel"); model.update(nodes)
	check(model.alternatives().is_empty() and model.selected == "c", "removed choices cannot survive refresh; inspection remains local")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
