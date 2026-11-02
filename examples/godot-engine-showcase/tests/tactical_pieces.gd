extends SceneTree
const Catalog = preload("res://scripts/presentation/art_catalog.gd")
const Context = preload("res://scripts/ui/visual_context.gd")
const Slot = preload("res://scripts/ui/art_slot.gd")
var failures := 0
var output := ""
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for frame in 8: await process_frame
func run() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="): output = arg.trim_prefix("--lab-output=")
	var session = root.get_node("GameSession")
	var original := JSON.stringify([session.run, session.combat])
	var hero_paths: Array = []
	for name in ["hero-wanderer", "hero-regent", "hero-shard", "enemy-striker", "enemy-bulwark", "enemy-channeler", "enemy-swarm"]:
		var path: String = "res://assets/tactical-pieces/" + name + ".svg"
		var texture := load(path) as Texture2D
		check(texture != null and texture.get_size() == Vector2(240, 320), "native SVG imports at consistent undistorted dimensions: " + name)
		if name.begins_with("hero-"): hero_paths.append(path)
	for manifest in ["res://data/art_manifest.json", "res://data/ivory_run_art_manifest.json", "res://data/programmer_art_manifest.json"]:
		var data := Catalog.manifest(manifest)
		var preserved := JSON.stringify(data)
		var chosen := str(Catalog.resolve("actors", "w1_operator", manifest).path)
		check(chosen in hero_paths, "hero comes from the three approved variants: " + manifest)
		for id in ["w1_operator", "ascendant_operator", "core_adept", "spire_adept", "player_warrior"]:
			check(Catalog.resolve("actors", id, manifest).path == chosen, "hero aliases share a stable session appearance: " + id)
		Catalog.invalidate(manifest)
		check(Catalog.resolve("actors", "w1_operator", manifest).path == chosen, "catalog reload does not reroll the hero")
		for id in data.categories.actors:
			var slot := Catalog.resolve("actors", id, manifest)
			check(Catalog.texture_for(slot) != null and slot.path.begins_with("res://assets/tactical-pieces/"), "explicit actor mapping resolves: " + id)
		check(Catalog.resolve("actors", "unmapped-mod-enemy", manifest).path.ends_with("enemy-striker.svg"), "unknown actors retain a visible tactical fallback")
		check(JSON.stringify(Catalog.manifest(manifest)) == preserved, "variant resolution never mutates manifest data")
	# This RNG is isolated from both authoritative engine randomness and Godot's global RNG.
	Catalog._variant_choices.clear(); Catalog._cosmetic_rng = null
	seed(7123); var expected := randi()
	seed(7123); Catalog.resolve("actors", "w1_operator"); var actual := randi()
	check(actual == expected, "cosmetic resolution leaves global RNG untouched")
	Catalog._cosmetic_rng = RandomNumberGenerator.new(); Catalog._cosmetic_rng.seed = 41
	var seen := {}
	for trial in 40:
		Catalog._variant_choices.clear()
		seen[Catalog.resolve("actors", "w1_operator").path] = true
	check(seen.size() == 3, "all three heroes are reachable through cosmetic randomization")
	var context = Context.new(null, {}, load("res://data/themes/ivory_run.tres"))
	context.set_scenery_profile(load("res://data/themes/ivory_scenery.tres"))
	var stage := HBoxContainer.new(); stage.position = Vector2(40, 100); stage.size = Vector2(1400, 400)
	root.add_child(stage)
	var actor := {"instanceId": "hero", "definitionId": "w1_operator", "resources": {}, "statuses": []}
	var snapshot := JSON.stringify(actor)
	var actors: Array = [{"actor": actor}]
	var scenery = load("res://scripts/ui/components/combat_scenery_view.gd").create()
	root.add_child(scenery); scenery.apply_model(actors, "combat", context)
	var art = scenery.portraits.hero; var texture = art.texture
	scenery.apply_model(actors, "combat", context)
	check(scenery.portraits.hero == art and art.texture == texture, "combat reconciliation retains both art node and cosmetic choice")
	var portrait = Slot.new(context); portrait.setup("actors", "w1_operator"); stage.add_child(portrait)
	check(portrait.texture == texture, "actor panels and combat scenery share the same hero appearance")
	portrait.custom_minimum_size = Vector2(180, 320)
	for id in ["w1_scout", "w1_sentinel", "w1_binder", "w1_swarm"]:
		var slot = Slot.new(context); slot.setup("actors", id); slot.custom_minimum_size = Vector2(180, 320); stage.add_child(slot)
	await settle()
	if not output.is_empty() and DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
		root.get_texture().get_image().save_png(output.path_join("tactical-pieces.png"))
	check(JSON.stringify(actor) == snapshot and JSON.stringify([session.run, session.combat]) == original, "cosmetic integration leaves gameplay snapshots untouched")
	scenery.queue_free(); stage.queue_free(); await settle()
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
