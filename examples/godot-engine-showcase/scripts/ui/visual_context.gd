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
var scenery_profile: Resource = preload("res://scripts/ui/styles/scenery_visual_profile.gd").new()
const StyleFactory = preload("res://scripts/ui/styles/view_style_factory.gd")
var styles: StyleFactory = StyleFactory.new()
var _theme: Theme
var _theme_key := ""
var theme: Theme:
	get:
		var key := JSON.stringify([profile.presentation_key(), text_scale, high_contrast])
		if _theme == null or _theme_key != key:
			_sync_styles()
			_theme = styles.build(high_contrast)
			_theme_key = key
		return _theme
var _options: Dictionary = {}
var _revision_key: Array = []
var _resource_styles: Dictionary = {}
var setting_id := "":
	set(value):
		if value == setting_id: return
		setting_id = value
		changed.emit()
func set_resource_styles(styles: Dictionary) -> void:
	if styles == _resource_styles: return
	_resource_styles = styles.duplicate(true)
	changed.emit()
func resource_style(id: String) -> Dictionary:
	return resource_appearance(id, _resource_styles.get(id, {}))
func resource_appearance(id: String, appearance: Dictionary = {}) -> Dictionary:
	var result := appearance.duplicate(true)
	if profile.resource_color_overrides.has(id): result.color = profile.resource_color_overrides[id]
	return result
func presentation_key() -> String:
	return JSON.stringify([_options, _resource_styles, translator.locale, setting_id, profile.presentation_key()])
var text_scale: float:
	get: return float(_options.get("text_scale", 1.0))
var card_style: CardVisualStyle:
	get: return profile.card_style if profile.card_style else preload("res://data/default_card_visual_style.tres")
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
	_sync_styles()
	_revision_key = [_options.duplicate(true), translator.locale, profile.presentation_key()]
static func from_nodes(preferences, i18n):
	var options := {}
	for key in ["text_scale", "high_contrast", "reduced_motion", "animation_speed", "hand_layout_mode", "hand_drag_enabled", "hand_emphasis"]:
		options[key] = preferences.get(key)
	options["action_labels"] = {}
	for action in ["inspect_card", "end_turn", "confirm_action", "open_timeline", "pause_game"]: options.action_labels[action] = preferences.action_label(action)
	return load("res://scripts/ui/visual_context.gd").new(i18n, options, AppTheme.profile)
func update_from(preferences, i18n, theme_profile: Resource) -> void:
	var next := {}
	for key in ["text_scale", "high_contrast", "reduced_motion", "animation_speed", "hand_layout_mode", "hand_drag_enabled", "hand_emphasis"]:
		next[key] = preferences.get(key)
	next["action_labels"] = {}
	for action in ["inspect_card", "end_turn", "confirm_action", "open_timeline", "pause_game"]: next.action_labels[action] = preferences.action_label(action)
	var revision: Array = [next, i18n.locale, theme_profile.presentation_key()]
	if revision == _revision_key: return
	_revision_key = revision.duplicate(true)
	_options = next
	translator = i18n
	profile = theme_profile
	_sync_styles()
	changed.emit()

func _sync_styles() -> void:
	styles.profile = profile
	styles.configure_preferences(text_scale, high_contrast)
func action_label(action: String) -> String:
	return str(_options.get("action_labels", {}).get(action, action))
func derive(theme_profile: Resource):
	# Isolated presentation snapshot for overlays; never changes the gameplay context.
	var derived = get_script().new(translator, _options, theme_profile)
	derived.set_resource_styles(_resource_styles)
	derived.setting_id = setting_id
	derived.scenery_profile = scenery_profile
	return derived
func resolve_art(category: String, content_id: String) -> Dictionary:
	var manifest: String = scenery_profile.manifest_for(setting_id) if category in ["actors", "backgrounds"] else profile.manifest_for(setting_id)
	return preload("res://scripts/presentation/art_catalog.gd").resolve(category, content_id, manifest)

func set_scenery_profile(value: Resource) -> void:
	if scenery_profile == value: return
	scenery_profile = value
	changed.emit()
