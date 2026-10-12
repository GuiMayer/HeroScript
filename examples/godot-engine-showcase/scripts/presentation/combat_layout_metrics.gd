extends RefCounted
## Presentation geometry only. Readable minimums take priority over proportions.
static func calculate(bounds: Vector2, hand_minimum: Vector2, dock_height: float, profile: Resource) -> Dictionary:
	var width := floorf(minf(bounds.x, maxf(hand_minimum.x, minf(bounds.x * profile.hand_width_ratio, profile.hand_max_width))))
	var safe_height := maxf(0., bounds.y - hand_minimum.y - dock_height - profile.safe_margin)
	return {"hand_width": width, "hand_left": (bounds.x - width) * .5, "actor_height": safe_height,
		"dock_height": dock_height, "hand_top": maxf(0., bounds.y - dock_height - hand_minimum.y)}
