extends RefCounted
## Card-centered transforms shared by transit visuals and their hand destinations.
static func read(card: Control) -> Dictionary:
	var transform := card.get_global_transform()
	return {"center": transform * (card.size * .5), "rotation": transform.get_rotation(), "scale": transform.get_scale(), "size": card.size}
static func apply(card: Control, center: Vector2, rotation: float, scale: Vector2) -> void:
	var basis := Transform2D(rotation, scale, 0.0, Vector2.ZERO)
	card.pivot_offset = card.size * .5
	basis.origin = center - basis.basis_xform(card.pivot_offset)
	var parent := null if card.is_set_as_top_level() else card.get_parent() as CanvasItem
	var local := parent.get_global_transform().affine_inverse() * basis if parent else basis
	card.rotation = local.get_rotation()
	card.scale = local.get_scale()
	card.position = local.origin - card.pivot_offset + local.basis_xform(card.pivot_offset)
static func blend(start: Dictionary, target: Dictionary, progress: float) -> Dictionary:
	return {"center": (start.center as Vector2).lerp(target.center, progress),
		"rotation": lerp_angle(float(start.rotation), float(target.rotation), progress),
		"scale": (start.scale as Vector2).lerp(target.scale, progress)}
static func write(card: Control, pose: Dictionary) -> void:
	apply(card, pose.center, pose.rotation, pose.scale)
static func arrival_center(start: Vector2, destination: Vector2, angle: float, progress: float) -> Vector2:
	var delta := destination - start
	var tangent := Vector2.RIGHT.rotated(angle)
	if delta.dot(tangent) < 0: tangent = -tangent
	var lift := minf(42.0, delta.length() * .12)
	var first := start + delta * .35 - Vector2(0, lift)
	var last := destination - tangent * minf(160.0, delta.length() * .25)
	return start.bezier_interpolate(first, last, destination, progress)
