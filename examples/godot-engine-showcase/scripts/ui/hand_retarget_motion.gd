extends RefCounted
## Finite Hermite retargeting: keep current pose and a bounded incoming velocity.
static func append(tween: Tween, target: Control, property: String, destination: Variant, seconds: float) -> void:
	var start: Variant = target.get(property)
	var delta: Variant = destination - start
	var key := "hand_velocity_" + property
	var velocity: Variant = target.get_meta(key, delta * 0.0)
	if delta is Vector2:
		velocity = velocity.limit_length(delta.length() * 2.0 / seconds)
		if velocity.dot(delta) < 0: velocity = Vector2.ZERO
	else:
		velocity = clampf(float(velocity), -absf(float(delta)) * 2.0 / seconds, absf(float(delta)) * 2.0 / seconds)
		if float(velocity) * float(delta) < 0: velocity = 0.0
	var tangent: Variant = velocity * seconds
	tween.tween_method(func(t: float):
		var t2 := t*t; var t3 := t2*t
		target.set(property, start + delta * (-2.0*t3+3.0*t2) + tangent * (t3-2.0*t2+t))
		target.set_meta(key, (delta * (-6.0*t2+6.0*t) + tangent * (3.0*t2-4.0*t+1.0)) / seconds),
		0.0, 1.0, seconds).set_trans(Tween.TRANS_LINEAR)
static func reset(target: Control) -> void:
	for property in ["position", "rotation"]:
		var key: String = "hand_velocity_" + property
		if target.has_meta(key): target.remove_meta(key)
