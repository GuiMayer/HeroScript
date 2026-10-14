extends SceneTree
## Temporal regressions. --animation-baseline records the pre-refactor defects.
const PlaybackType = preload("res://scripts/presentation/playback.gd")
var failures := 0
var baseline := "--animation-baseline" in OS.get_cmdline_user_args()
class Portrait extends Control:
	var flashes := 0
	func flash_hit() -> void: flashes += 1
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String, known := false) -> void:
	if baseline and known and not ok: print("[KNOWN] ", label); return
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var original := [{"frameId": "immutable", "effectSteps": [{"identity": {"procId": "A"}, "condensation": {}}],
		"applications": [{"id": "A1", "identity": {"procId": "A"}}, {"id": "B", "identity": {"procId": "B"}}, {"id": "A2", "identity": {"procId": "A"}}]}]
	var before := JSON.stringify(original)
	var order: Array = []
	for frame in PlaybackType.presentation_frames(original):
		for application in frame.applications: order.append(application.id)
	check(order == ["A1", "B", "A2"], "condensation grouping preserves interleaved canonical order", true)
	check(JSON.stringify(original) == before, "planning leaves source facts immutable")
	var cursor = PlaybackType.new(); root.add_child(cursor)
	var dispatched: Array = []
	cursor.frame_presented.connect(func(frame, _i, _n): dispatched.append(frame))
	# A real visual executor may claim the handle; an absent executor must not.
	cursor.executor = func(_frame, handle): handle.claimed = true
	cursor.load_receipt({"state": {"resolution": {"frames": [{"applications": []}]}}})
	cursor.advance()
	check(cursor.has_method("has_blocking_work") and cursor.has_blocking_work(), "last claimed visual remains blocking until completion", true)
	check(dispatched.size() == 1, "one explicit step dispatches exactly one frame")
	cursor.complete_active()
	check(not cursor.has_blocking_work(), "visual completion releases the last blocking handle")
	cursor.executor = Callable()
	cursor.load_receipt({"state": {"resolution": {"frames": [{"applications": []}]}}}); cursor.advance()
	check(not cursor.has_blocking_work(), "facts without an executor create no artificial wait")
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var feedback = preload("res://scripts/ui/components/combat_feedback.gd").new(context); root.add_child(feedback)
	var portrait := Portrait.new(); root.add_child(portrait)
	var presenter = preload("res://scripts/presentation/combat_presenter.gd").new({}, {}, [], context.translator)
	feedback.present({"applications": [{"effectType": "MODIFY_RESOURCE", "targetEntityId": "hero", "resourceId": "arbitrary_resource", "previousValue": 3, "currentValue": 2,
		"provenance": {"componentId": "payment", "sourceId": "card"}}], "paymentComponentIds": ["payment"]}, 0, 1, presenter, {"hero": portrait})
	check(portrait.flashes == 0, "resource payment does not produce a hit reaction", true)
	feedback.dispose()
	feedback.present({"applications": [{"effectType": "DAMAGE", "targetEntityId": "hero", "resourceId": "mana", "previousValue": 6, "currentValue": 3},
		{"effectType": "DAMAGE", "targetEntityId": "hero", "resourceId": "mana", "previousValue": 3, "currentValue": 1}]}, 0, 1, presenter, {"hero": portrait})
	var labels: Array = feedback.effects.get_children()
	check(labels.size() == 2 and labels[0].position != labels[1].position, "floating impacts occupy distinct lanes", true)
	feedback.dispose(); feedback.queue_free(); portrait.queue_free(); cursor.queue_free()
	for _i in 8: await process_frame
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
