extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, message: String) -> void:
    print(("[PASS] " if ok else "[FAIL] ") + message)
    if not ok: failures += 1
func run() -> void:
    var profile = load("res://data/themes/ivory_run.tres")
    var policy = load("res://data/themes/ivory_run_card_style.tres")
    var context = load("res://scripts/ui/visual_context.gd").new()
    check(context.profile == profile and context.card_style == policy, "Standalone presentation defaults to the current combat profile")
    var card = load("res://scenes/ui/components/card_view.tscn").instantiate()
    card.animate_entry = false
    root.add_child(card)
    for frame in 3: await process_frame
    check(card.model == card.preview_data and card.visual_style == policy, "Standalone scene populates its current card preview without an API")
    check(card.custom_minimum_size == policy.effective_size(1.0), "Standalone card uses combat dimensions")
    check(card.get_node("%Cost").get_child(0).get_scene_file_path() == "res://scenes/ui/components/card_cost_symbol.tscn", "Default costs use the current symbol component")
    check(card.get_node("%PrintedKeyline").visible and not card.has_node("CardFaceMargin/Column/Art/ArtOverlayName"), "Card has only the current face anatomy")
    var fallback_context = load("res://scripts/ui/visual_context.gd").new(null, {}, load("res://data/themes/preserved.tres"))
    check(fallback_context.card_style == policy, "Profiles without a card policy fall back to the current style")
    check(not policy.get_property_list().any(func(property): return property.name == "printed_face"), "Removed legacy face cannot be enabled through a resource")
    check(not FileAccess.file_exists("res://data/default_card_visual_style.tres"), "Legacy card style asset is removed")
    card.free()
    quit(1 if failures else 0)
