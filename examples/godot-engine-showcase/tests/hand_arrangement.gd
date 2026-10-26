extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var type = preload("res://scripts/ui/hand_arrangement.gd")
	var source := ["a", "b", "c", "d", "e"]; var copy := source.duplicate()
	check(type.insert(source, "b", 3) == ["a", "c", "d", "b", "e"] and source == copy, "insertion shifts intermediate instances without mutating canonical identities")
	check(type.insert(source, "e", -1) == ["e", "a", "b", "c", "d"] and type.insert(source, "a", 99) == ["b", "c", "d", "e", "a"], "insertion clamps both ends")
	var state = type.new(); state.sync(source); state.begin("b"); state.move(3)
	check(state.confirmed == source and state.canonical == source, "preview cannot change confirmed or canonical order")
	state.cancel(); check(state.confirmed == source and state.preview.is_empty(), "cancel discards preview")
	state.begin("b"); state.move(3); check(state.commit() and state.manual, "drop commits visual preference only")
	state.sync(["a", "b", "d", "e", "new1", "new2"])
	check(state.confirmed == ["a", "d", "b", "e", "new1", "new2"], "removed identities disappear and new identities append in canonical relative order")
	var projected: Array = state.project(["b", "a", "new3"]); projected.clear()
	check(state.confirmed.size() == 6 and state.canonical[1] == "b", "projection returns detached data")
	state.begin("a"); state.move(2); state.sync(["b", "e"])
	check(state.dragged.is_empty() and state.confirmed == ["b", "e"], "collection updates invalidate a stale gesture")
	check(type.insert(source, "unknown", 1) == source and type.reconcile([], source).is_empty(), "unknown and empty inputs cannot duplicate identities")
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
