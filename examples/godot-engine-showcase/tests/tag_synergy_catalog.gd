extends SceneTree
var failures := 0
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
	root.get_node("GameAudio").shutdown()
	await create_timer(.12).timeout
	quit(0 if failures == 0 else 1)
