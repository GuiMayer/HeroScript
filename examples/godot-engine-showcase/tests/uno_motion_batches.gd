extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var motion = preload("res://scripts/ui/card_zone_motion.gd")
	var policy = preload("res://scripts/presentation/motion_policy.gd").capture("default", {})
	policy._values.stagger = .025; policy._values.batch_span = .1
	for count in [1, 2, 5, 10]:
		var transfers: Array = []
		for i in count: transfers.append({"id": str(i), "operation": "draw"})
		var before := JSON.stringify(transfers)
		var schedule: Array = motion.schedule(transfers, policy)
		check(schedule.all(func(item): return is_equal_approx(item.delay + item.duration, policy.duration("draw", true))) and JSON.stringify(transfers) == before,
			"staggered %s-card wave fits the original cycle without mutating facts" % count)
		if count > 1: check(schedule[1].delay > 0 and schedule[-1].delay <= .1, "stagger is visible and bounded")
	var chain: Array = motion.schedule([{"id": "one", "operation": "draw"}, {"id": "one", "operation": "discard"}], policy)
	check(chain[1].delay == chain[0].duration and chain[1].duration == policy.duration("discard", true), "same-card causal passages retain their complete separate durations")
	policy._values.stagger = 0; policy._values.batch_span = 0
	var parallel: Array = motion.schedule([{"id": "a", "operation": "draw"}, {"id": "b", "operation": "draw"}], policy)
	check(parallel.all(func(item): return item.delay == 0 and item.duration == policy.duration("draw", true)), "explicit zero stagger preserves simultaneous playback")
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
