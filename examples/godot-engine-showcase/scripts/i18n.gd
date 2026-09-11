extends Node

signal locale_changed
const LOCALES := ["pt_BR", "en"]
var locale := "pt_BR"
var catalogs: Dictionary = {}
var content_names: Dictionary = {}

func _ready() -> void:
	for code in LOCALES:
		var messages: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/locales/%s.json" % code))
		catalogs[code] = messages
		var translation := Translation.new()
		translation.locale = code
		for key in messages:
			translation.add_message(key, messages[key])
		TranslationServer.add_translation(translation)
	content_names = JSON.parse_string(FileAccess.get_file_as_string("res://data/locales/content.json"))
	set_locale(Preferences.locale)

func set_locale(code: String) -> void:
	locale = code if code in LOCALES else "pt_BR"
	TranslationServer.set_locale(locale)
	locale_changed.emit()

func text(key: String) -> String:
	return str(TranslationServer.translate(key))

func number(value: float) -> String:
	var formatted := str(int(value)) if is_equal_approx(value, roundf(value)) else str(snappedf(value, .01))
	return formatted.replace(".", ",") if locale == "pt_BR" else formatted

func content_name(id: String, fallback := "") -> String:
	var names: Dictionary = content_names.get(id, {})
	return str(names.get(locale, fallback if not fallback.is_empty() else id.replace("_", " ").capitalize()))

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
