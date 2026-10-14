extends RefCounted
## Optional local visual override. Only an explicit lab action writes it.
const PATH := "user://card_animation_profile.json"
const KEYS := {"burn": ["exile_burn"], "use": ["card_flight", "staged_discard"], "retained": ["card_flight", "retain"], "draw": ["draw"], "discard": ["discard"], "create": ["create"], "destroy": ["destroy"]}
static func read() -> Dictionary:
	if not FileAccess.file_exists(PATH): return {}
	var value = JSON.parse_string(FileAccess.get_file_as_string(PATH))
	return value if value is Dictionary else {}
static func apply(policy) -> void:
	var profile := read()
	for operation in KEYS:
		var entry = profile.get("use", profile.get("retained")) if operation in ["use", "retained"] else profile.get(operation)
		if not entry is Dictionary: continue
		var seconds = entry.get("seconds")
		if not (seconds is int or seconds is float) or not is_finite(float(seconds)) or seconds < 0 or seconds > 30: continue
		var keys: Array = KEYS[operation]
		for key in keys: policy._values[key] = float(seconds) / keys.size()
		if operation == "burn" and str(entry.get("direction", "center")) in ["center", "edges"]:
			var flows: Dictionary = policy._cues.get("card_flows", {}).duplicate(true)
			for flow in flows:
				if flows[flow].get("animation") == "burn": flows[flow]["direction"] = str(entry.get("direction", "center"))
			policy._cues["card_flows"] = flows
static func save(operation: String, seconds: float, direction: String) -> Error:
	if not KEYS.has(operation) or not is_finite(seconds) or seconds < 0 or seconds > 30: return ERR_INVALID_PARAMETER
	var profile := read()
	profile[operation] = {"seconds": seconds, "direction": direction}
	# Both are the same two-phase use family; the early accepted flight must
	# have the same duration before post-command inspection knows retention.
	if operation in ["use", "retained"]:
		profile["use"] = {"seconds": seconds}
		profile["retained"] = {"seconds": seconds}
	var file := FileAccess.open(PATH, FileAccess.WRITE)
	if file == null: return FileAccess.get_open_error()
	file.store_string(JSON.stringify(profile, "\t"))
	return OK
