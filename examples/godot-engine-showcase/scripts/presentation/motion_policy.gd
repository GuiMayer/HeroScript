extends RefCounted
## Visual JSON captured at a presentation boundary, independent of rule content.
const PATH := "res://data/motion_manifest.json"
const DEFAULTS := {"prepare": .075, "retain": .09, "draw": .18, "discard": .15, "create": .14, "destroy": .12, "stagger": .025, "batch_span": .15, "card_flight": .16, "card_entry": .18, "impact": .085, "float": .45, "zone_transfer": .18, "hand_transition": .11, "hover": .085, "watchdog": 12., "max_floats": 24, "max_transfers": 12}
const CURVES := {"linear": Tween.TRANS_LINEAR, "cubic": Tween.TRANS_CUBIC, "sine": Tween.TRANS_SINE, "quad": Tween.TRANS_QUAD}
const EASES := {"in": Tween.EASE_IN, "out": Tween.EASE_OUT, "in_out": Tween.EASE_IN_OUT}
const DEFAULT_EASINGS := {"card_flight": {"curve": "cubic", "ease": "in"}, "zone_transfer": {"curve": "cubic", "ease": "in_out"}, "hover": {"curve": "cubic", "ease": "out"}, "card_entry": {"curve": "cubic", "ease": "out"}}
var _easings: Dictionary = {}
var _values: Dictionary = {}
var _cues: Dictionary = {}
var _random: Dictionary = {}
var speed := 1.0
var instant := false
var reduced := false
static var _documents: Dictionary = {}
static var document_parses := 0
static func invalidate() -> void: _documents.clear()
static func _document(path: String) -> Dictionary:
	# Source comparison also catches same-second/same-size development edits.
	# Only this command boundary touches disk; hot paths consume captured policy.
	var source := FileAccess.get_file_as_string(path) if FileAccess.file_exists(path) else ""
	if _documents.has(path) and _documents[path].source == source: return _documents[path].document
	var parsed = JSON.parse_string(source) if not source.is_empty() else null
	document_parses += 1
	var document: Dictionary = parsed if parsed is Dictionary and int(parsed.get("version", 0)) == 1 else {}
	if _documents.size() >= 8: _documents.erase(_documents.keys()[0])
	_documents[path] = {"source": source, "document": document}
	return document
static func capture(setting: String, options: Dictionary, path := PATH):
	var policy = load("res://scripts/presentation/motion_policy.gd").new()
	var document: Dictionary = _document(path)
	policy._values = DEFAULTS.duplicate()
	var settings: Dictionary = as_dictionary(document.get("settings", {}))
	var override: Dictionary = as_dictionary(settings.get(setting, {}))
	for layer in [document.get("durations", {}), override.get("durations", {})]:
		if not layer is Dictionary: continue
		for key in layer:
			if DEFAULTS.has(key) and (layer[key] is float or layer[key] is int) and is_finite(float(layer[key])) and float(layer[key]) >= 0 and float(layer[key]) <= 64: policy._values[key] = layer[key]
	policy._cues = as_dictionary(document.get("cues", {})).duplicate(true)
	policy._cues.merge(as_dictionary(override.get("cues", {})), true)
	policy._random = as_dictionary(override.get("random_inputs", {})).duplicate(true)
	policy._easings = DEFAULT_EASINGS.duplicate(true)
	for layer in [document.get("easings", {}), override.get("easings", {})]:
		if not layer is Dictionary: continue
		for key in layer:
			var curve: Dictionary = as_dictionary(layer[key])
			if CURVES.has(str(curve.get("curve", ""))) and EASES.has(str(curve.get("ease", ""))): policy._easings[key] = curve.duplicate()
	policy.instant = str(options.get("animation_preset", "normal")) == "instant"
	policy.speed = 2.0 if options.get("animation_preset") == "fast" else clampf(float(options.get("animation_speed", 1)), .25, 2.)
	policy.reduced = bool(options.get("reduced_motion", false))
	return policy
func duration(key: String, movement := false) -> float:
	return 0.0 if instant or (movement and reduced) else float(_values.get(key, DEFAULTS.get(key, .12))) / speed
func limit(key: String) -> int: return maxi(1, int(_values.get(key, DEFAULTS.get(key, 16))))
static func as_dictionary(value) -> Dictionary: return value if value is Dictionary else {}
func cue(kind: String) -> Dictionary: return as_dictionary(_cues.get(kind, {})).duplicate(true)
func random_labels() -> Dictionary: return _random.duplicate(true)
func curve(key: String) -> int: return CURVES.get(str(_easings.get(key, {}).get("curve", "linear")), Tween.TRANS_LINEAR)
func easing(key: String) -> int: return EASES.get(str(_easings.get(key, {}).get("ease", "in_out")), Tween.EASE_IN_OUT)
