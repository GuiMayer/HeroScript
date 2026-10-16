extends SceneTree
var failures := 0
class Router extends RefCounted:
	var visual_context
	func back_to_menu() -> void: pass
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var catalog: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/tag_synergy_scenarios.json"))
	var presentation: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/world_one_presentation.json"))
	var names: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/locales/content.json"))
	var art: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/ivory_run_art_manifest.json"))
	var seen: Dictionary = {}
	check(catalog.presets.size() == 5, "five editable sandbox presets")
	for preset in catalog.presets:
		for card in preset.scenario.startingCards:
			var id := str(card.definitionId)
			if not presentation.cards.has(id) or not presentation.cards[id].has("name"): continue
			check(names.has(id) and names[id].has("en") and names[id].has("pt_BR"), "localized name " + id)
			check(art.categories.cards.has(id) and art.slots.has(art.categories.cards[id]), "existing art slot " + id)
			seen[id] = true
	var Screen = load("res://scripts/ui/sandbox_screen.gd")
	check(Screen != null, "rules laboratory script compiles")
	check(seen.size() >= 24, "presets expose expansion and baseline cards together")
	var session = root.get_node("GameSession")
	var previous: String = session.selected_setting_id
	session.selected_setting_id = "ascendant"
	var router := Router.new()
	router.visual_context = load("res://scripts/ui/visual_context.gd").new(root.get_node("I18n"), {}, load("res://data/themes/ivory_run.tres"))
	var screen = Screen.new()
	root.add_child(screen)
	screen.setup(router, {"sandbox_modes": [], "default_scenario": {}})
	check(screen.scenario_picker.item_count == 6, "preset selector includes custom plus five scenes")
	screen._select_scenario(1)
	check(screen.selected_mode == catalog.mode_id and screen.mode_buttons[catalog.mode_id].button_pressed, "preset selects canonical world-one mode")
	var first: Dictionary = JSON.parse_string(screen.editor.text)
	check(first == catalog.presets[0].scenario, "editor receives the scenario, not visual gameplay rules")
	first.startingCards.clear()
	check(not screen.scenario_presets[0].scenario.startingCards.is_empty(), "editing a scenario never mutates the preset catalog")
	screen.queue_free()
	session.selected_setting_id = previous
	root.get_node("GameAudio").shutdown()
	await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)
