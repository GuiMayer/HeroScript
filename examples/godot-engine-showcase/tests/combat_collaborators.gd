extends SceneTree
const Inspection = preload("res://scripts/ui/controllers/combat_inspection_coordinator.gd")
const Driver = preload("res://scripts/ui/controllers/presentation_director.gd")
var failures := 0
var results: Array = []
class Probe extends RefCounted:
	signal release
	var calls := 0
	var held := false
	func inspect_hand(targets: Array) -> Dictionary:
		calls += 1
		if held: await release
		return {"ok": true, "data": {"cards": [{"targets": targets}]}}
class Options extends RefCounted:
	var auto_animations := true
	var animation_speed := 1.0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func request(coordinator, revision: Array, targets: Array) -> void:
	results.append(await coordinator.inspect(revision, targets))
func _run() -> void:
	var probe := Probe.new()
	var coordinator = Inspection.new(probe)
	var a: Dictionary = await coordinator.inspect(["combat", 1, "revision"], ["enemy-a"])
	a.data.cards.clear()
	var b: Dictionary = await coordinator.inspect(["combat", 1, "revision"], ["enemy-a"])
	check(probe.calls == 1 and b.data.cards.size() == 1, "inspection cache is defensively copied")
	await coordinator.inspect(["combat", 1, "revision"], ["enemy-b"])
	await coordinator.inspect(["combat", 1, "new-revision"], ["enemy-b"])
	check(probe.calls == 3, "actual targets and content revision participate in inspection identity")
	probe.held = true
	request(coordinator, ["concurrent"], [])
	request(coordinator, ["concurrent"], [])
	check(probe.calls == 4, "identical concurrent requests share one read")
	probe.release.emit()
	check(results.size() == 2 and results.all(func(result): return result.ok), "all coalesced callers receive the result")
	results.clear()
	request(coordinator, ["obsolete"], [])
	request(coordinator, ["obsolete"], [])
	coordinator.dispose()
	check(results.size() == 1 and results[0].get("canceled", false), "dispose immediately releases coalesced waiters")
	probe.release.emit()
	check(results.size() == 2 and results.all(func(result): return result.get("canceled", false)), "late response cannot publish after disposal")
	var cursor = load("res://scripts/presentation/playback.gd").new()
	var options := Options.new()
	var pause := {"value": false}
	var driver = Driver.new(cursor, options, func(): return pause.value)
	cursor.load_receipt({"state": {"resolution": {"frames": [{}, {}, {}]}}})
	driver._process(.01)
	check(cursor.index == 1, "timing driver advances the existing presentation cursor")
	driver._process(.1)
	check(cursor.index == 1, "presentation interval persists without a screen render dependency")
	pause.value = true
	driver._process(10.)
	check(not driver.advance() and cursor.index == 1, "pause blocks both automatic and manual advancement")
	pause.value = false
	options.auto_animations = false
	driver._process(10.)
	check(cursor.index == 1 and driver.advance() and cursor.index == 2, "manual mode uses the same cursor and no second queue")
	options.auto_animations = true
	cursor.load_receipt({"state": {"resolution": {"frames": [{}]}}})
	driver._process(.01)
	check(cursor.index == 1, "a new receipt resets timing independently of UI refreshes")
	driver.dispose()
	cursor.load_receipt({"state": {"resolution": {"frames": [{}]}}})
	check(not driver.advance() and cursor.index == 0, "disposed presentation driver cannot advance")
	driver.free(); cursor.free()
	root.get_node("GameAudio").shutdown()
	await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
