extends Node

signal locale_changed
const FALLBACK_LOCALE := "en"
const LOCALES := ["en", "pt_BR"]
var locale := FALLBACK_LOCALE
var catalogs: Dictionary = {}
var content_names: Dictionary = {}

func _ready() -> void:
	for code in LOCALES:
		var messages := _read_catalog("res://data/locales/%s.json" % code)
		catalogs[code] = messages
		var translation := Translation.new()
		translation.locale = code
		for key in messages:
			translation.add_message(key, messages[key])
		TranslationServer.add_translation(translation)
	content_names = _read_catalog("res://data/locales/content.json")
	set_locale(Preferences.locale)

func _read_catalog(path: String) -> Dictionary:
	if not FileAccess.file_exists(path):
		return {}
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	return parsed if parsed is Dictionary else {}

func set_locale(code: String) -> void:
	locale = code if code in LOCALES else FALLBACK_LOCALE
	TranslationServer.set_locale(locale)
	locale_changed.emit()

func text(key: String) -> String:
	var translated := str(catalogs.get(locale, {}).get(key, ""))
	if not translated.is_empty():
		return translated
	var english := str(catalogs.get(FALLBACK_LOCALE, {}).get(key, ""))
	return english if not english.is_empty() else key

func number(value: float) -> String:
	var formatted := str(int(value)) if is_equal_approx(value, roundf(value)) else str(snappedf(value, .01))
	return formatted.replace(".", ",") if locale == "pt_BR" else formatted

func content_name(id: String, fallback := "") -> String:
	var names: Dictionary = content_names.get(id, {})
	var translated := str(names.get(locale, ""))
	if not translated.is_empty():
		return translated
	var english := str(names.get(FALLBACK_LOCALE, ""))
	if not english.is_empty():
		return english
	return fallback if not fallback.is_empty() else id.replace("_", " ").capitalize()

func presentation(source: Dictionary) -> Dictionary:
	var result := source.duplicate(true)
	for kind in ["cards", "nodes"]:
		for id in result.get(kind, {}):
			for field in ["name", "text", "hint"]:
				if result[kind][id].has(field):
					result[kind][id][field] = text(str(result[kind][id][field]))
	for mode in result.get("sandbox_modes", []):
		mode.name = text(mode.name)
		mode.description = text(mode.description)
	return result

func error(result: Dictionary) -> String:
	var key := str(result.get("errorKey", ""))
	if key.is_empty():
		return str(result.get("error", "Unknown engine error."))
	var arguments: Array = result.get("errorArgs", [])
	return text(key) % arguments if not arguments.is_empty() else text(key)
