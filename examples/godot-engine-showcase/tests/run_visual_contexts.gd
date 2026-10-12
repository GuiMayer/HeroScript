extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func _run() -> void:
	var Context = load("res://scripts/ui/visual_context.gd")
	var profile_a: Resource = load("res://data/themes/preserved.tres").duplicate(true)
	var profile_b: Resource = profile_a.duplicate(true)
	profile_a.colors = {"text.primary": "#aa1111", "surface.panel": "#112244"}
	profile_b.colors = {"text.primary": "#117733", "surface.panel": "#ffeeaa"}
	var a = Context.new(null, {"text_scale": 1.}, profile_a)
	var b = Context.new(null, {"text_scale": 1.2}, profile_b)
	var host := Control.new(); root.add_child(host)
	var actor := {"instanceId": "neutral", "definitionId": "spire_wisp", "relationship": "Neutral", "resources": {}, "statuses": []}
	var panel_a = load("res://scripts/ui/actor_panel.gd").new(a)
	var panel_b = load("res://scripts/ui/actor_panel.gd").new(b)
	panel_a.setup(actor, false, {}, ""); panel_b.setup(actor, false, {}, "")
	host.add_child(panel_a); host.add_child(panel_b)
	AppTheme.apply_view_preferences(host)
	var labels_a: Array = panel_a.find_children("*", "Label", true, false)
	var labels_b: Array = panel_b.find_children("*", "Label", true, false)
	var title_a: Label = labels_a.filter(func(label): return label.text == "Spire Wisp")[0]
	var title_b: Label = labels_b.filter(func(label): return label.text == "Spire Wisp")[0]
	check(title_a.get_theme_color("font_color") == profile_a.color("text.primary") and title_b.get_theme_color("font_color") == profile_b.color("text.primary"), "two simultaneous actor contexts retain independent palettes under global styling")
	check(a.theme != b.theme and a.theme == a.theme, "native themes are cached per context, never shared between profiles")
	check(labels_a.any(func(label): return label.text == "NEUTRAL") and not labels_a.any(func(label): return label.text == "YOUR SIDE"), "neutral actor has explicit affiliation rather than player-side label")
	var before: Color = a.scenery_profile.color("scenery.base")
	profile_a.colors["scenery.base"] = "#ff0000"
	check(a.scenery_profile.color("scenery.base") == before, "UI profile does not recolor the independent scenery profile")
	var raw := {"actors": [{"instanceId": "hero", "sideId": "player"}, {"instanceId": "guest", "sideId": "neutral"}], "relationships": {"sameSide": "Ally", "differentSides": "Neutral"}}
	var presenter = load("res://scripts/presentation/combat_presenter.gd").new({}, raw, [], a.translator)
	var actors: Array = presenter.combat_model("hero").read("actors")
	check(actors[1].relationship == "Neutral" and actors[1].sideId == "neutral" and actors[0].actor.isInputActor, "combat projection preserves full relationship, team and controlled actor")
	check(not raw.actors[0].has("relationship"), "presentation enrichment never changes incoming combat snapshot")
	var mappings := {"defaults": {"backgroundId": "combat"}, "settings": {"custom": {"nodes": {"camp": {"backgroundId": "forest"}}}}}
	var scenery = load("res://scripts/presentation/scenery_presenter.gd").build({"currentNodeId": "camp"}, {}, "custom", mappings)
	check(scenery.read("backgroundId") == "forest" and scenery.read("nodeId") == "camp", "setting/node mappings select scenery without game rules")
	var snapshot: Dictionary = scenery.to_dictionary(); snapshot.backgroundId = "mutated"
	check(scenery.read("backgroundId") == "forest", "scenery model is defensively copied")
	host.queue_free()
	for _frame in 8: await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
