extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var context = load("res://scripts/ui/visual_context.gd").new(null, {}, load("res://data/themes/ivory_run.tres"))
	context.set_scenery_profile(load("res://data/themes/ivory_scenery.tres"))
	check(context.profile.printed_surfaces and context.card_style.printed_face, "run reuses the approved printed card anatomy")
	check(context.styles.color("surface.canvas") == Color("#f2ebdd") and context.scenery_profile.ambient_style == "paper", "UI and independent scenery opt into paper explicitly")
	var actors := ["spire_adept", "spire_wisp", "spire_sentinel", "unknown-mod-actor"]
	for id in actors:
		var slot: Dictionary = context.resolve_art("actors", id)
		var texture = load("res://scripts/presentation/art_catalog.gd").texture_for(slot)
		check(texture != null and str(slot.path).begins_with("res://assets/ivory/"), "native printed actor/fallback art resolves: " + id)
	var prior: Resource = context.scenery_profile
	check(context.resource_appearance("energy", {"color": "#eeee00"}).color == "#7e4b15", "profile resource palette overrides presentation tone without changing resource semantics")
	check(context.resource_appearance("custom", {"color": "#123456", "display": "counter"}) == {"color": "#123456", "display": "counter"}, "unknown resources preserve authored presentation")
	context.profile = load("res://data/themes/preserved.tres")
	check(context.scenery_profile == prior, "changing UI does not implicitly change scene art")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
