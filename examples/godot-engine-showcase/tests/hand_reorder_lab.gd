extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab)
	for _i in 8: await process_frame
	lab.visual_context._options.reduced_motion = false; lab.motion_lab.preset = "normal"
	var original := JSON.stringify(lab.models)
	var outcomes: Array = []
	lab.motion_lab.finished.connect(func(_models, metrics): outcomes.append(metrics))
	for scenario_id in ["arrange_first_last", "arrange_last_first", "arrange_cancel"]:
		var before: Array = lab.hand.order.duplicate()
		lab.animation_picker.select_animation(scenario_id); lab.animation_cycle.value = .12
		lab.play_selected_animation()
		var start := Time.get_ticks_msec()
		while lab.motion_lab._running and Time.get_ticks_msec()-start < 3000: await process_frame
		check(not lab.motion_lab._running and lab.hand._ghost == null and lab.hand._transit.is_empty(), scenario_id + " uses production gesture and settles all owners")
		check(not outcomes.is_empty() and outcomes[-1].result == "completed" and Time.get_ticks_msec()-start < 400, "settlement uses actual current motion, without fallback or watchdog")
		if scenario_id == "arrange_first_last": check(lab.hand.order[-1] == before[0], "lab insertion moves the first visible instance to last")
		if scenario_id == "arrange_last_first": check(lab.hand.order[0] == before[-1], "lab insertion moves last to first")
		if scenario_id == "arrange_cancel": check(lab.hand.order == before, "lab cancellation preserves confirmed arrangement")
		check(JSON.stringify(lab.models) == original, "lab keeps its canonical fixture models unchanged")
	lab.motion_lab.preset = "instant"
	lab.play_selected_animation()
	for _i in 3: await process_frame
	check(not lab.motion_lab._running and lab.hand._ghost == null, "instant local gesture has no empty wait")
	lab.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
