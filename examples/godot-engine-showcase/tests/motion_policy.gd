extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var Policy = preload("res://scripts/presentation/motion_policy.gd")
	var normal = Policy.capture("unknown", {"animation_speed": 1.})
	var fast = Policy.capture("unknown", {"animation_preset": "fast"})
	var instant = Policy.capture("unknown", {"animation_preset": "instant"})
	var reduced = Policy.capture("unknown", {"reduced_motion": true})
	check(normal.duration("card_flight") > 0 and fast.duration("card_flight") == normal.duration("card_flight") / 2., "fast preset changes presentation duration only")
	check(normal.duration("exile_burn", true) == .5 and normal.duration("card_flight", true) + normal.duration("staged_discard", true) == 1., "exile burns in half a second and normal card use spans one second")
	check(["draw", "discard", "create", "destroy", "card_entry", "zone_transfer", "impact", "float"].all(func(key): return normal.duration(key) == 1.), "remaining presentation animations use one-second durations")
	check(normal.duration("hover") < .2 and normal.duration("hand_transition") < .2, "input feedback remains responsive instead of inheriting cinematic timing")
	var zones = preload("res://scripts/ui/card_zone_motion.gd")
	check(zones.uses_burn([{"cardZoneSteps": [{"flowId": "card.played.to-exhaust", "instanceIds": ["played"]}]}], "played", normal), "burn consumes the published post-play flow rather than a card name")
	check(not zones.uses_burn([{"cardZoneSteps": [{"flowId": "activation.ethereal", "instanceIds": ["played"]}]}], "played", normal), "unplayed exile does not masquerade as post-use burning")
	check(instant.duration("card_flight", true) == 0 and instant.duration("impact") == 0, "instant mode removes all blocking motion duration")
	check(reduced.duration("card_flight", true) == 0 and reduced.duration("impact") > 0, "reduced movement retains reading feedback without spatial movement")
	check(Policy.capture("ascendant", {}).random_labels().has("critical"), "setting-specific published random outcomes have configured labels")
	check(Policy.capture("unknown", {}, "res://absent.json").duration("card_flight") > 0, "missing manifest falls back to bounded defaults")
	var copy: Dictionary = normal.cue("impact"); copy.sound = "altered"
	check(normal.cue("impact").sound == "impact", "captured cue policy is defensively copied")
	check(normal.curve("card_flight") == Tween.TRANS_CUBIC and normal.easing("card_flight") == Tween.EASE_OUT, "configured easing is captured with the command policy")
	check(normal.curve("unknown") == Tween.TRANS_LINEAR, "unknown motion has a neutral easing fallback")
	Policy.invalidate()
	var parses: int = Policy.document_parses
	for _index in 20: Policy.capture("ascendant", {})
	check(Policy.document_parses == parses + 1, "unchanged manifest is parsed once across captured presentations")
	root.get_node("GameAudio").shutdown(); quit(0 if failures == 0 else 1)
