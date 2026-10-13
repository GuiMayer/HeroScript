extends SceneTree
var failures := 0
class Portrait extends Control:
	func flash_hit() -> void: pass
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new()
	context.capture_motion()
	var feedback = preload("res://scripts/ui/components/combat_feedback.gd").new(context); root.add_child(feedback)
	var portrait := Portrait.new(); root.add_child(portrait)
	var presenter = preload("res://scripts/presentation/combat_presenter.gd").new({}, {}, [], context.translator)
	var applications: Array = []
	for index in 1000: applications.append({"effectType": "DAMAGE", "targetEntityId": "hero", "resourceId": "custom", "previousValue": index + 1, "currentValue": index})
	var frame := {"applications": applications}
	var before := JSON.stringify(frame)
	feedback.present(frame, 0, 1, presenter, {"hero": portrait})
	check(feedback.effects.get_child_count() <= 6 and JSON.stringify(frame) == before, "1000 facts retain exact data while decorative text stays bounded")
	feedback.dispose(); for _frame in 8: await process_frame
	check(feedback.effects.get_child_count() == 0, "settling releases every decorative node")
	feedback.queue_free(); portrait.queue_free()
	var playback = preload("res://scripts/presentation/playback.gd").new()
	var envelope = preload("res://scripts/presentation/presentation_envelope.gd").build({"sequence": 2, "state": {"resolution": {"frames": [{}]}}}, {"run": {"runId": "r"}}, {"run": {"runId": "r", "sequence": 2}}, [])
	envelope.mark_at("submitted", Time.get_ticks_msec() - 10); envelope.mark_at("received", Time.get_ticks_msec() - 5)
	playback.load_envelope(envelope); playback.activate()
	playback.executor = func(_frame, handle): handle.hit(); handle.finish()
	playback.advance()
	check(playback.measurements().size() == 1 and playback.measurements()[0].timings.has("first_impact"), "observational timings separate receipt, synchronization and impact")
	playback.release_facts()
	check(playback.envelope == null and playback.frames.is_empty(), "completed live presentation releases large receipt and snapshot references")
	playback.free(); await process_frame; root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
