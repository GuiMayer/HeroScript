extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var clock := {"now": 1000}
	var trace = preload("res://scripts/presentation/interaction_trace.gd").new(func(): return clock.now)
	trace.begin(); clock.now += 120; trace.mark("sent"); clock.now += 300; trace.mark("receipt")
	trace.mark("first_motion"); clock.now += 10; trace.mark("first_motion")
	var result: Dictionary = trace.snapshot(); result.clear()
	check(trace.snapshot().first_motion == 420 and trace.snapshot().sent == 120, "diagnostics preserve first milestone and return detached values")
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 5: await process_frame
	lab.visual_context._options["animation_speed"] = 1.0
	lab.visual_context._options["reduced_motion"] = false
	var source: Array = lab.models.duplicate(true)
	for count in [2, 5, 10]:
		var models: Array = []
		for i in count:
			var model: Dictionary = source[i % source.size()].duplicate(true)
			model.cardInstanceId = "baseline:%s:%s" % [count, i]; models.append(model)
		for operation in ["use", "retained", "draw"]:
			lab.hand.sync_models(models, "uno-motion-baseline")
			for _i in 4: await process_frame
			var before := JSON.stringify(models)
			lab.motion_lab.play({"operation": operation, "count": 2, "cycleSeconds": .12}, models)
			var start := Time.get_ticks_msec()
			while lab.motion_lab.playback.has_blocking_work() and Time.get_ticks_msec() - start < 3000: await process_frame
			check(not lab.motion_lab.playback.has_blocking_work() and lab.motion_lab.overlay.get_child_count() == 0,
				"baseline %s/%s settles through shared executors" % [count, operation])
			check(JSON.stringify(models) == before, "presentation does not mutate input models")
			print("UNO_BASELINE=", JSON.stringify({"cards": count, "operation": operation, "cycle_seconds": .12, "elapsed_ms": Time.get_ticks_msec() - start, "viewport": root.size}))
	lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
