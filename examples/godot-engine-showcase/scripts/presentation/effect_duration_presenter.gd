extends RefCounted
## Presentation only. Missing lifecycle metadata must never imply a gameplay rule.
static func describe(value, translator, definition: Dictionary = {}) -> String:
	if not (value is int or value is float) or not is_finite(float(value)):
		return translator.text("Duration determined when applied")
	if float(value) == -1:
		return translator.text("No time-based expiration")
	if float(value) < 0: return translator.text("Duration determined when applied")
	if is_zero_approx(float(value)): return translator.text("Expired")
	var boundary = definition.get("durationTickBoundary", definition.get("durationBoundary", ""))
	# Numeric enums have different meanings for statuses and modifiers. Do not guess.
	var labels := {
		"StartActivation": "Duration: %s · decreases at the start of the affected actor's activation",
		"EndActivation": "Duration: %s · decreases at the end of the affected actor's activation",
		"StartRound": "Duration: %s · decreases at the start of each round",
		"EndRound": "Duration: %s · decreases at the end of each round",
		"Command": "Duration: %s · decreases after each command",
		"Activation": "Duration: %s · decreases after each activation",
		"Round": "Duration: %s · decreases after each round",
		"Combat": "Duration: %s · decreases after each combat",
		"Node": "Duration: %s · decreases after each map stop",
		"Run": "Duration: %s · decreases after each run"}
	return translator.text(str(labels.get(str(boundary), "Duration: %s cycles"))) % translator.number(float(value))
