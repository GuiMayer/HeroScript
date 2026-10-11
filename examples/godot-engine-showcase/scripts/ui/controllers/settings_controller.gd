extends RefCounted
## Owns preference mutations/persistence. Views expose only local intents.
const Schema = preload("res://scripts/ui/styles/settings_schema.gd")
var _preferences
var _translator
var _session
func _init(preferences, translator, session) -> void:
	_preferences = preferences; _translator = translator; _session = session
func read(key: String): return _preferences.get(key)
func set_option(key: String, value, persist := false) -> bool:
	var spec: Dictionary = Schema.option(key)
	if spec.is_empty(): return false
	if spec.has("values") and value not in spec["values"]: return false
	if spec.get("type") == "bool" and not value is bool: return false
	if spec.has("min"):
		if not (value is float or value is int) or not is_finite(float(value)): return false
		value = clampf(float(value), float(spec.min), float(spec.max))
	_preferences.set(key, value)
	if persist: commit()
	return true
func commit() -> void:
	_preferences.save()
	_translator.set_locale(str(_preferences.get("locale")))
func actions() -> Array: return _preferences.ACTIONS.duplicate()
func action_label(action: String) -> String: return _preferences.action_label(action)
func controller_label(action: String) -> String: return _preferences.controller_label(action)
func remap_event(action: String, event: InputEvent) -> String: return _preferences.remap_event(action, event)
func reset_bindings() -> void: _preferences.reset_bindings()
func resolution_options() -> Array[Vector2i]:
	var values: Array[Vector2i] = []
	values.assign(_preferences.resolution_options())
	return values
func set_resolution(value: Vector2i) -> void: _preferences.set_resolution(value)
func save_connection(value: String) -> String:
	if value.strip_edges().trim_suffix("/") == _preferences.api_url: return ""
	if _session.busy or _session.has_pending_command: return "Finish or recover the current operation before changing the connection."
	if not _preferences.set_api_url(value): return "Use an HTTP or HTTPS address."
	return ""
