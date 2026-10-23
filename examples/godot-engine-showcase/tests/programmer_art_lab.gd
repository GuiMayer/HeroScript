extends SceneTree
## Geometry baseline for a cosmetic-only revamp. Uses the production hand/cards.
var failures := 0
var output := ""
var capture := false
var records: Dictionary = {}
const BASELINE := "res://data/labs/programmer_art_baseline.json"
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for frame in 12: await process_frame
func pair(value: Vector2) -> Array:
	return [snappedf(value.x, .01), snappedf(value.y, .01)]
func geometry(node: Node, path := "root", rows: Array = []) -> Array:
	if node is Control:
		var transform: Transform2D = node.get_global_transform()
		rows.append([path, node.get_class(), pair(node.size), pair(node.get_combined_minimum_size()), pair(transform.origin), pair(transform.x), pair(transform.y), node.visible, node.mouse_filter, node.focus_mode])
	var index := 0
	for child in node.get_children():
		geometry(child, path + "/" + str(index), rows)
		index += 1
	return rows
func motion_sources() -> Dictionary:
	var result := {}
	for path in ["res://scripts/ui/card_hand_view.gd", "res://scripts/presentation/card_choreography.gd", "res://scripts/presentation/card_animation_profile.gd", "res://scripts/presentation/motion_policy.gd", "res://data/motion_manifest.json", "res://data/labs/card_animation_catalog.json", "res://scripts/labs/card_motion_lab.gd", "res://shaders/card_burn.gdshader"]:
		if FileAccess.file_exists(path): result[path] = FileAccess.get_sha256(path)
	return result
func run() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="): output = arg.trim_prefix("--lab-output=")
		if arg == "--capture-phase=baseline": capture = true
	var original_size := root.size
	var original_scale := root.content_scale_size
	var session = root.get_node("GameSession")
	var services = root.get_node("GameServices")
	var before := JSON.stringify([session.run, session.combat, services.diagnostics()])
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate()
	root.add_child(lab)
	# Neutralize entrance only for reproducible measurement; production stays intact.
	root.get_node("Preferences").reduced_motion = true
	var expected: Dictionary = {} if capture else JSON.parse_string(FileAccess.get_file_as_string(BASELINE))
	for dimensions in [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = dimensions
		root.content_scale_size = dimensions
		for language in ["en", "pt_BR"]:
			lab.set_language(language)
			for scale in [1.0, 1.2]:
				lab.set_text_scale(scale)
				lab.set_preset(0)
				for amount in [0, 2, 5]:
					lab.set_count(amount)
					await settle()
					var key := "%sx%s/%s/%.1f/%s" % [dimensions.x, dimensions.y, language, scale, amount]
					var rows := geometry(lab)
					records[key] = {"digest": JSON.stringify(rows).sha256_text(), "controls": rows.size(), "hand_position": pair(lab.hand.global_position), "hand_size": pair(lab.hand.size)}
					check(capture or records[key].digest == expected.get("cases", {}).get(key, {}).get("digest"), "geometry unchanged: " + key)
					if not output.is_empty() and DisplayServer.get_name() != "headless" and language == "en" and scale == 1.0 and amount == 5:
						await RenderingServer.frame_post_draw
						root.get_texture().get_image().save_png(output.path_join("lab-%sx%s.png" % [dimensions.x, dimensions.y]))
	root.size = Vector2i(1920, 1080)
	root.content_scale_size = Vector2i(1920, 1080)
	lab.set_language("en"); lab.set_text_scale(1.0)
	for preset in range(lab.catalog.presets.size()):
		lab.set_preset(preset)
		await settle()
		var key := "preset/" + str(preset)
		var rows := geometry(lab)
		records[key] = {"digest": JSON.stringify(rows).sha256_text(), "controls": rows.size()}
		check(capture or records[key].digest == expected.get("cases", {}).get(key, {}).get("digest"), "preset geometry unchanged: " + str(preset))
		if not output.is_empty() and DisplayServer.get_name() != "headless":
			await RenderingServer.frame_post_draw
			root.get_texture().get_image().save_png(output.path_join("preset-%s.png" % preset))
	check(capture or motion_sources() == expected.get("motion_sources"), "motion sources and configuration unchanged")
	check(before == JSON.stringify([session.run, session.combat, services.diagnostics()]), "no session mutation or REST requests")
	if not output.is_empty():
		var file := FileAccess.open(output.path_join("geometry.json"), FileAccess.WRITE)
		file.store_string(JSON.stringify({"cases": records, "motion_sources": motion_sources()}, "\t"))
	lab.queue_free()
	await settle()
	root.size = original_size
	root.content_scale_size = original_scale
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
