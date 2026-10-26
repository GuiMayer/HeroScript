extends RefCounted
## Normalized visual timing. No RNG, game facts, timers or scene ownership.
static func sample(name: String, value: float) -> float:
	var t := clampf(value, 0.0, 1.0)
	if t == 0.0 or t == 1.0: return t
	if name == "smooth": return t * t * (3.0 - 2.0 * t)
	if name != "swift": return t
	# CSS cubic-bezier(.22, 1, .36, 1): invert X before sampling Y.
	var low := 0.0; var high := 1.0
	for _i in 18:
		var u := (low + high) * .5
		var x := 3.0 * (1.0-u) * (1.0-u) * u * .22 + 3.0 * (1.0-u) * u * u * .36 + u*u*u
		if x < t: low = u
		else: high = u
	var u := (low + high) * .5
	return 1.0 - pow(1.0-u, 3.0)
static func settle(value: float, amplitude: float) -> float:
	var p := clampf(value, 0.0, 1.0)
	# Entire accommodation belongs to the last 18% of the existing cycle.
	if p <= .82 or p >= 1.0: return 1.0
	return 1.0 - clampf(amplitude, 0.0, .06) * sin((p-.82) / .18 * PI)
