extends RefCounted
## Shared card staging and active-cycle budgeting; independent of engine facts.
const Burn = preload("res://scripts/ui/card_burn_motion.gd")
const Zones = preload("res://scripts/ui/card_zone_motion.gd")

static func stage_center(hand) -> Vector2:
	return hand.scroll.get_global_rect().get_center() + Vector2(0, -160)

static func fit_cycle(frames: Array, policy, seconds: float, snapshots: Array, anchor: Callable, role: Callable, burn_enabled: bool) -> void:
	if seconds <= 0:
		policy.instant = true
		for frame in frames:
			if frame.get("presentationOnly") == "card_burn": frame.durationSeconds = 0.0
		return
	var span := 0.0
	var staged := {}
	for frame in frames:
		if frame.get("presentationOnly") == "card_burn":
			if burn_enabled: span += float(frame.durationSeconds)
		elif frame.get("presentationOnly") == "card_departure":
			staged[str(frame.cardInstanceId)] = true
			span += policy.duration("card_flight", true)
			if frame.retained and not frame.continued: span += policy.duration("retain", true)
		else:
			var beat_span := 0.0
			for item in Zones.schedule(Zones.resolve(frame.get("cardZoneSteps", []), snapshots, anchor, policy, role), policy, func(id): return staged.erase(id)):
				beat_span = maxf(beat_span, float(item.delay) + float(item.duration))
			span += beat_span
	if span <= 0: return
	var factor := seconds / span
	for key in ["card_flight", "retain", "draw", "discard", "staged_discard", "create", "destroy", "exile_burn", "stagger", "batch_span", "zone_transfer"]:
		policy._values[key] = float(policy._values[key]) * factor
	for frame in frames:
		if frame.get("presentationOnly") == "card_burn": frame.durationSeconds = float(frame.durationSeconds) * factor
