extends SceneTree
var failures := 0
class FakePreferences extends RefCounted:
	var locale := "en"
	var text_scale := 1.0
	var high_contrast := false
	var hand_layout_mode := "adaptive"
	var saves := 0
	func save() -> void: saves += 1
class Translator extends RefCounted:
	var locale := "en"
	func set_locale(value: String) -> void: locale = value
class Session extends RefCounted:
	var busy := false
	var has_pending_command := false
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var prefs := FakePreferences.new()
	var translator := Translator.new()
	var controller = load("res://scripts/ui/controllers/settings_controller.gd").new(prefs, translator, Session.new())
	check(controller.set_option("text_scale", 9.0) and prefs.text_scale == 1.2 and prefs.saves == 0, "schema owns validation; view does not persist")
	check(not controller.set_option("hand_layout_mode", "invented") and prefs.hand_layout_mode == "adaptive", "unsupported option rejected")
	check(not controller.set_option("high_contrast", "yes") and not prefs.high_contrast, "wrong boolean type rejected")
	check(not controller.set_option("unknown", 1), "schema rejects unconfigured preference")
	controller.set_option("locale", "pt_BR", true)
	check(prefs.saves == 1 and translator.locale == "pt_BR", "coordinator persists and applies language")
	for path in ["res://scripts/ui/settings_screen.gd", "res://scripts/ui/controllers/settings_controller.gd"]:
		var source := FileAccess.get_file_as_string(path)
		if path.ends_with("settings_screen.gd"):
			check(not source.contains("Preferences.save") and not source.contains("Preferences.text_scale ="), "view has no preference mutations")
		else: check(not source.contains("Button.new") and not source.contains("Theme.new"), "settings coordinator has no node/style construction")
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
