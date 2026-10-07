extends RefCounted
## Pure presentation geometry, not an engine calculation.
static func calculate(count: int, available: float, card: Vector2, style: Resource, mode: String) -> Dictionary:
	var angle := deg_to_rad(style.maximum_angle_degrees) if mode == "adaptive" else 0.0
	var inset: float = style.padding + card.y * absf(sin(angle))
	var usable := maxf(0.0, available - 2.0 * inset)
	var preferred: float = card.x + style.gap
	var step := preferred
	if count > 1 and mode == "adaptive":
		step = clampf((usable - card.x) / (count - 1), minf(card.x, style.minimum_step), preferred)
	var span := card.x + maxi(0, count - 1) * step
	var extent := maxf(available, span + 2.0 * inset)
	var compression := clampf(1.0 - step / preferred, 0.0, 1.0)
	var positions: Array[Vector2] = []
	var rotations: Array[float] = []
	var hit_rects: Array[Rect2] = []
	var x0 := (extent - span) * .5
	var top: float = style.padding + style.lift
	for i in count:
		var t := (2.0 * i / (count - 1) - 1.0) if count > 1 else 0.0
		positions.append(Vector2(x0 + i * step, top + style.arc_height * t * t * compression))
		rotations.append(t * angle * compression)
		var left := x0 + i * step if i == 0 else x0 + i * step + (card.x - step) * .5
		var right := x0 + i * step + card.x if i == count - 1 else x0 + (i + 1) * step + (card.x - step) * .5
		hit_rects.append(Rect2(left, style.padding, right - left, card.y + style.lift + style.arc_height))
	return {"positions": positions, "rotations": rotations, "hitRects": hit_rects,
		"extent": extent, "step": step, "overflow": extent > available + 1,
		"height": card.y + top + style.arc_height + card.x * absf(sin(angle)) * .5 + style.padding}
