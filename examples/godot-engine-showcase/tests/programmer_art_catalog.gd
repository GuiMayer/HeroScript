extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func run() -> void:
	var path := "res://data/labs/programmer_art_manifest.json"
	var catalog = preload("res://scripts/presentation/art_catalog.gd")
	var manifest: Dictionary = catalog.manifest(path)
	var original: Dictionary = catalog.manifest("res://data/ivory_run_art_manifest.json")
	for slot in manifest.slots:
		var texture: Texture2D = catalog.texture_for(manifest.slots[slot])
		check(texture != null, "catalogue imports " + str(slot))
		if str(manifest.slots[slot].path).contains("programmer-art/"):
			check(texture != null and texture.get_size() in [Vector2(320, 150), Vector2(128, 128)], "vector canvas contract: " + str(slot))
	for category in ["actors", "backgrounds", "portraits"]:
		check(manifest.categories[category] == original.categories[category], "stage five art mapping unchanged: " + category)
		for id in original.categories[category]:
			var slot: String = original.categories[category][id]
			check(manifest.slots[slot] == original.slots[slot], "existing scenery asset preserved: " + slot)
	var fixture: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_hand_lab.json"))
	for sample in fixture.cards.values():
		var id := str(sample.definitionId)
		check(manifest.categories.cards.has(id), "explicit art mapping for fixture " + id)
		var resolved: Dictionary = catalog.resolve("cards", id, path)
		check(catalog.texture_for(resolved).get_size() == Vector2(320, 150) and resolved.get("placeholder", false), "fixture keeps canvas and printed placeholder label: " + id)
	check(catalog.resolve("cards", "unrecognized_mod_card", path).path.ends_with("unknown-1.svg"), "unknown mod content has neutral fallback")
	check(catalog.resolve("relics", "unknown", path).path != catalog.resolve("inscriptions", "unknown", path).path and catalog.resolve("inscriptions", "unknown", path).path != catalog.resolve("modifier_items", "unknown", path).path, "separate object families remain recognizable")
	quit(0 if failures == 0 else 1)
