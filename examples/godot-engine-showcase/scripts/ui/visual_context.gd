extends RefCounted
## Explicit, presentation-only projection. English defaults allow standalone components.
signal changed
class English extends RefCounted:
	var locale := "en"
	func text(key: String) -> String: return key
	func content_name(id: String, fallback := "") -> String: return fallback if not fallback.is_empty() else id.replace("_", " ").capitalize()
	func number(value: float) -> String: return str(roundi(value)) if is_equal_approx(value, roundi(value)) else String.num(value, 3)
var translator = English.new()
var profile: Resource = preload("res://data/themes/preserved.tres")
var _options: Dictionary = {}
var _revision_key: Array = []
var text_scale: float:
	get: return float(_options.get("text_scale", 1.0))
var high_contrast: bool:
	get: return bool(_options.get("high_contrast", false))
var reduced_motion: bool:
	get: return bool(_options.get("reduced_motion", false))
var animation_speed: float:
	get: return maxf(float(_options.get("animation_speed", 1.0)), .25)
var hand_layout_mode: String:
	get: return str(_options.get("hand_layout_mode", "adaptive"))
var hand_drag_enabled: bool:
	get: return bool(_options.get("hand_drag_enabled", false))
var hand_emphasis: float:
	get: return float(_options.get("hand_emphasis", 1.0))
func _init(i18n = null, options: Dictionary = {}, theme_profile: Resource = null) -> void:
	if i18n != null: translator = i18n
	_options = options.duplicate(true)
	if theme_profile != null: profile = theme_profile
	_revision_key = [_options.duplicate(true), translator.locale, profile.presentation_key()]
static func from_nodes(preferences, i18n):
	var options := {}
	for key in ["text_scale", "high_contrast", "reduced_motion", "animation_speed", "hand_layout_mode", "hand_drag_enabled", "hand_emphasis"]:
		options[key] = preferences.get(key)
	options["action_labels"] = {}
	for action in ["inspect_card", "hand_overview"]: options.action_labels[action] = preferences.action_label(action)
	return load("res://scripts/ui/visual_context.gd").new(i18n, options, AppTheme.profile)
func update_from(preferences, i18n, theme_profile: Resource) -> void:
	var next := {}
	for key in ["text_scale", "high_contrast", "reduced_motion", "animation_speed", "hand_layout_mode", "hand_drag_enabled", "hand_emphasis"]:
		next[key] = preferences.get(key)
	next["action_labels"] = {}
	for action in ["inspect_card", "hand_overview"]: next.action_labels[action] = preferences.action_label(action)
	var revision: Array = [next, i18n.locale, theme_profile.presentation_key()]
	if revision == _revision_key: return
	_revision_key = revision.duplicate(true)
	_options = next
	translator = i18n
	profile = theme_profile
	changed.emit()
func action_label(action: String) -> String:
	return str(_options.get("action_labels", {}).get(action, action))
func resolve_art(category: String, content_id: String) -> Dictionary:
	return preload("res://scripts/presentation/art_catalog.gd").resolve(category, content_id, profile.art_manifest)
