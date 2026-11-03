extends RefCounted
## Presentation geometry only. Readable minimums take priority over proportions.
static func calculate(bounds: Vector2, hand_minimum: Vector2, dock_height: float, profile: Resource) -> Dictionary:
	var width := floorf(minf(bounds.x, maxf(hand_minimum.x, minf(bounds.x * profile.hand_width_ratio, profile.hand_max_width))))
	var safe_height := maxf(0., bounds.y - hand_minimum.y - dock_height - profile.safe_margin)
	return {"hand_width": width, "hand_left": (bounds.x - width) * .5, "actor_height": safe_height,
		"dock_height": dock_height, "hand_top": maxf(0., bounds.y - dock_height - hand_minimum.y)}


static func stage_layout(bounds: Vector2, bottom_hud_height: float, hand_minimum_height: float, toolbar_height: float, turn_controls_height: float, profile: Resource) -> Dictionary:
	var hand_metrics := calculate(bounds, Vector2(0, hand_minimum_height), bottom_hud_height - hand_minimum_height, profile)
	var dock_height := toolbar_height + turn_controls_height + float(profile.floating_dock_gap)
	return {
		"hand_width": float(hand_metrics.get("hand_width", 0.0)),
		"bottom_hud_top": -bottom_hud_height,
		"dock_height": dock_height,
		"candidate_bottom": -dock_height,
		"candidate_top": -dock_height - float(profile.candidate_height),
		"candidate_right": float(profile.candidate_width),
		"floating_preview_right": float(profile.preview_width),
		"floating_preview_bottom": -dock_height,
		"floating_preview_top": -dock_height - float(profile.floating_preview_height),
		"battlefield_bottom": -bottom_hud_height - float(profile.safe_margin),
	}

