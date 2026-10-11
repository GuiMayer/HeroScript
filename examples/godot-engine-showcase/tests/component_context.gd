extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var Context = load("res://scripts/ui/visual_context.gd")
	var context = Context.new(null, {"text_scale": 1.2, "reduced_motion": true})
	var card = load("res://scripts/ui/card_view.gd").new(context)
	card.configure({"cardInstanceId": "a", "name": "Standalone", "summary": "Generic effect"})
	check(card.custom_minimum_size.y > 328, "isolated card receives explicit scale without preferences")
	card.free()
	var resource = load("res://scripts/ui/resource_view.gd").new(context)
	resource.setup("unknown_resource", {"current": -7}, {})
	check(not resource.bar.visible and resource.amount.text == "-7", "unbounded unknown resource remains textual and exact")
	resource.setup("unknown_resource", {"current": -7, "minimum": -10, "maximum": -1}, {})
	check(resource.bar.visible and resource.bar.min_value == -10 and resource.bar.max_value == -1, "negative published bounds are not replaced by a hardcoded maximum")
	resource.setup("unknown_resource", {"current": 30, "minimum": 0, "maximum": 10}, {})
	check(not resource.bar.visible and resource.amount.text == "30/10", "out-of-range values are not silently clamped in display")
	resource.free()
	var Catalog = load("res://scripts/presentation/art_catalog.gd")
	var base: Dictionary = Catalog.resolve("cards", "basic_attack")
	var custom: Dictionary = Catalog.resolve("cards", "basic_attack", "res://data/labs/art_manifest_context_test.json")
	check(base.get("path") != custom.get("path"), "same content ID uses explicit manifest-specific resolution")
	check(Catalog.resolve("cards", "basic_attack").get("path") == base.get("path"), "alternate manifest never pollutes default cache")
	for file in ["card_view", "card_hand_view", "resource_view", "card_details_panel", "card_effect_text", "art_slot", "actor_panel"]:
		var code := FileAccess.get_file_as_string("res://scripts/ui/" + file + ".gd")
		check(not code.contains("Preferences.") and not code.contains("I18n.") and not code.contains("GameSession."), "explicit component dependencies: " + file)
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
