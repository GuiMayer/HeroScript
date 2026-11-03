extends SceneTree
## Etapa 0 Baseline & Regression Verifier for Godot UI Decoupling & Editability.
## Captures and compares geometry across 1280x720, 1920x1080, 2560x1080 and scales 1.0, 1.6,
## plus representative motion poses and durations (draw, use, discard, exile, retained, hover, fan, map).

const Context = preload("res://scripts/ui/visual_context.gd")
const Fixtures = preload("res://scripts/ui/workbenches/screen_catalog_fixtures.gd")
const Pose = preload("res://scripts/ui/card_motion_pose.gd")
const Motion = preload("res://scripts/ui/card_zone_motion.gd")
const Registry = preload("res://scripts/ui/card_transit_registry.gd")
const Handle = preload("res://scripts/presentation/presentation_handle.gd")
const Policy = preload("res://scripts/presentation/motion_policy.gd")

const RESOLUTIONS := [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1080)]
const SCALES := [1.0, 1.6]
const POSITION_TOLERANCE := 1.0
const ANGLE_TOLERANCE := 0.005

var failures := 0
var output_dir := ""
var write_baseline_path := ""
var compare_baseline_path := ""

func _init() -> void:
	call_deferred("_run")

func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok:
		failures += 1

func settle(frames := 6) -> void:
	for _i in frames:
		await process_frame

static func rect_dict(control: Control) -> Dictionary:
	if control == null or not is_instance_valid(control):
		return {}
	return {
		"x": snappedf(control.position.x, 0.01),
		"y": snappedf(control.position.y, 0.01),
		"w": snappedf(control.size.x, 0.01),
		"h": snappedf(control.size.y, 0.01)
	}

static func pose_dict(pose: Dictionary) -> Dictionary:
	return {
		"cx": snappedf(pose.center.x, 0.01),
		"cy": snappedf(pose.center.y, 0.01),
		"rot": snappedf(pose.rotation, 0.0001),
		"sx": snappedf(pose.scale.x, 0.001),
		"sy": snappedf(pose.scale.y, 0.001)
	}

static func rects_match(a: Dictionary, b: Dictionary, tolerance := POSITION_TOLERANCE) -> bool:
	if a.is_empty() or b.is_empty():
		return a.is_empty() == b.is_empty()
	return absf(float(a.get("x", 0.0)) - float(b.get("x", 0.0))) <= tolerance \
		and absf(float(a.get("y", 0.0)) - float(b.get("y", 0.0))) <= tolerance \
		and absf(float(a.get("w", 0.0)) - float(b.get("w", 0.0))) <= tolerance \
		and absf(float(a.get("h", 0.0)) - float(b.get("h", 0.0))) <= tolerance

static func poses_match(a: Dictionary, b: Dictionary) -> bool:
	return absf(float(a.get("cx", 0.0)) - float(b.get("cx", 0.0))) <= POSITION_TOLERANCE \
		and absf(float(a.get("cy", 0.0)) - float(b.get("cy", 0.0))) <= POSITION_TOLERANCE \
		and absf(angle_difference(float(a.get("rot", 0.0)), float(b.get("rot", 0.0)))) <= ANGLE_TOLERANCE \
		and absf(float(a.get("sx", 1.0)) - float(b.get("sx", 1.0))) <= 0.01 \
		and absf(float(a.get("sy", 1.0)) - float(b.get("sy", 1.0))) <= 0.01

func _capture_screen_geometry(res: Vector2i, scale_val: float) -> Dictionary:
	root.size = res
	var context = Context.new(null, {"text_scale": scale_val, "high_contrast": false, "reduced_motion": true}, load("res://data/themes/ivory_run.tres"))
	var result := {}

	# 1. Menu
	var menu: Control = load("res://scenes/ui/screens/main_menu.tscn").instantiate()
	root.add_child(menu)
	menu.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	Fixtures.apply(menu, "menu", "populated", context)
	await settle(4)
	result["menu"] = {
		"root": rect_dict(menu),
		"body": rect_dict(menu.get_node_or_null("MenuScroll/Center/MenuBody")),
		"hero": rect_dict(menu.get_node_or_null("MenuScroll/Center/MenuBody/HeroColumn")),
		"nav": rect_dict(menu.get_node_or_null("MenuScroll/Center/MenuBody/NavigationColumn"))
	}
	await _maybe_screenshot("menu", res, scale_val)
	menu.queue_free()
	await settle(2)

	# 2. Settings
	var settings: Control = load("res://scenes/ui/screens/settings.tscn").instantiate()
	root.add_child(settings)
	settings.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	settings.mount(context)
	settings.theme = context.theme
	await settle(4)
	result["settings"] = {
		"root": rect_dict(settings),
		"header": rect_dict(settings.get_node_or_null("Header")),
		"categories": rect_dict(settings.get_node_or_null("SettingsBody/SettingsCategories")),
		"page": rect_dict(settings.get_node_or_null("SettingsBody/SettingsPage"))
	}
	await _maybe_screenshot("settings", res, scale_val)
	settings.queue_free()
	await settle(2)

	# 3. Map
	var map_view: Control = load("res://scenes/ui/components/map_view.tscn").instantiate()
	root.add_child(map_view)
	map_view.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	Fixtures.apply(map_view, "map", "populated", context)
	await settle(4)
	result["map"] = {
		"root": rect_dict(map_view),
		"scroll": rect_dict(map_view._scroll),
		"route": rect_dict(map_view.route),
		"inspector": rect_dict(map_view._inspector_panel),
		"toggle": rect_dict(map_view._inspector_toggle)
	}
	await _maybe_screenshot("map", res, scale_val)
	map_view.queue_free()
	await settle(2)

	# 4. Activity
	var journey: Control = load("res://scenes/ui/components/journey_view.tscn").instantiate()
	root.add_child(journey)
	journey.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	Fixtures.apply(journey, "activity", "populated", context)
	await settle(4)
	result["activity"] = {
		"root": rect_dict(journey),
		"header": rect_dict(journey._header),
		"summary": rect_dict(journey._summary),
		"actions": rect_dict(journey.action_panel)
	}
	await _maybe_screenshot("activity", res, scale_val)
	journey.queue_free()
	await settle(2)

	# 5. Dialogue
	var dialogue: Control = load("res://scenes/ui/dialogue_scene.tscn").instantiate()
	root.add_child(dialogue)
	dialogue.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	Fixtures.apply(dialogue, "dialogue", "populated", context)
	await settle(4)
	result["dialogue"] = {
		"root": rect_dict(dialogue),
		"panel": rect_dict(dialogue.panel)
	}
	await _maybe_screenshot("dialogue", res, scale_val)
	dialogue.queue_free()
	await settle(2)

	# 6. Loadout
	var loadout_lab: Control = load("res://scenes/unified_loadout_lab.tscn").instantiate()
	root.add_child(loadout_lab)
	loadout_lab.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	loadout_lab.shell.mount(context)
	await settle(4)
	result["loadout"] = {
		"root": rect_dict(loadout_lab),
		"shell": rect_dict(loadout_lab.shell),
		"attributes_panel": rect_dict(loadout_lab.shell.attributes_panel)
	}
	await _maybe_screenshot("loadout", res, scale_val)
	loadout_lab.queue_free()
	await settle(2)

	# 7. Combat (default 5 cards, 2 actors; plus hand variations 0, 1, 2, 5, 10 and 3 actors at scale 1.0)
	var combat: Control = load("res://scenes/ui/components/combat_view.tscn").instantiate()
	root.add_child(combat)
	combat.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	Fixtures.apply(combat, "combat", "populated", context)
	await settle(4)
	result["combat"] = {
		"root": rect_dict(combat),
		"header": rect_dict(combat.header_view),
		"battlefield": rect_dict(combat.battlefield_actors),
		"bottom_hud": rect_dict(combat.bottom_hud),
		"hand_section": rect_dict(combat.hand_section),
		"hand": rect_dict(combat.hand),
		"turn_view": rect_dict(combat.turn_view)
	}
	if is_equal_approx(scale_val, 1.0) and res == Vector2i(1920, 1080):
		var hand_counts := {}
		for count in [0, 1, 2, 5, 10]:
			var cards: Array = []
			for i in count:
				cards.append({"cardInstanceId": "hand-%d-%d" % [count, i], "definitionId": ["basic_attack", "defend", "fireball", "heal", "arcane_bolt"][i % 5]})
			var run := Fixtures.run_fixture()
			var actors := [
				{"instanceId": "hero", "definitionId": "spire_adept", "sideId": "player", "resources": {"health": {"current": 42, "maximum": 60}, "energy": {"current": 3}}, "statuses": []},
				{"instanceId": "enemy1", "definitionId": "spire_wisp", "sideId": "enemy", "resources": {"health": {"current": 24, "maximum": 30}}, "statuses": []},
				{"instanceId": "enemy2", "definitionId": "spire_sentinel", "sideId": "enemy", "resources": {"health": {"current": 36, "maximum": 40}}, "statuses": []}
			]
			var combat_data := {"combatId": "fixture", "activeActorId": "hero", "activation": {"round": 1, "activeActorId": "hero", "waitingForInput": true}, "relationships": {"sameSide": "Ally", "differentSides": "Enemy"}, "actors": actors}
			var zones := {"zones": [{"zoneId": "play", "allowsCardPlay": true, "contentsVisible": true, "cards": cards, "count": count}, {"zoneId": "draw", "count": 8}, {"zoneId": "discard", "count": 2}]}
			var data := Fixtures.appearance()
			var presenter = load("res://scripts/presentation/combat_presenter.gd").new(run, combat_data, [], context.translator, data, zones)
			var card_presenter = load("res://scripts/presentation/card_zone_presenter.gd").new(zones, context.translator)
			combat.animate_card_entry = false
			combat.apply_projection(data, presenter, card_presenter, {"run": run, "combat": combat_data, "actions": [], "zones": zones, "input_actor": "hero", "scope": "catalog-%d" % count, "setting": {}, "setting_id": "default"}, context)
			await settle(3)
			var slot_rects := {}
			for id in combat.hand.order:
				slot_rects[id] = pose_dict(combat.hand.destination_pose(id))
			hand_counts[str(count)] = {
				"order_count": combat.hand.order.size(),
				"actor_count": combat.target_buttons.size(),
				"poses": slot_rects
			}
		result["combat_Matrix"] = hand_counts
	await _maybe_screenshot("combat", res, scale_val)
	combat.queue_free()
	await settle(2)

	# 8. Replay
	var replay: Control = load("res://scenes/ui/components/run_replay_view.tscn").instantiate()
	root.add_child(replay)
	replay.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	Fixtures.apply(replay, "replay", "populated", context)
	await settle(4)
	result["replay"] = {
		"root": rect_dict(replay),
		"list": rect_dict(replay.list),
		"progress": rect_dict(replay.progress),
		"visual_scroll": rect_dict(replay.visual_scroll)
	}
	await _maybe_screenshot("replay", res, scale_val)
	replay.queue_free()
	await settle(2)

	return result

func _maybe_screenshot(name: String, res: Vector2i, scale_val: float) -> void:
	if output_dir.is_empty() or DisplayServer.get_name() == "headless":
		return
	await RenderingServer.frame_post_draw
	var filename := "%s-%dx%d-s%s.png" % [name, res.x, res.y, str(scale_val).replace(".", "_")]
	root.get_texture().get_image().save_png(output_dir.path_join(filename))

func _capture_motion_baseline() -> Dictionary:
	root.size = Vector2i(1920, 1080)
	var policy = Policy.capture("default", {"animation_speed": 1.0, "reduced_motion": false})
	var reduced_policy = Policy.capture("default", {"animation_speed": 1.0, "reduced_motion": true})
	var durations := {
		"draw": snappedf(policy.duration("draw", true), 0.0001),
		"use": snappedf(policy.duration("card_commitment", true), 0.0001),
		"discard": snappedf(policy.duration("discard", true), 0.0001),
		"staged_discard": snappedf(policy.duration("staged_discard", true), 0.0001),
		"exile": snappedf(policy.duration("exhaust", true), 0.0001),
		"retained": snappedf(policy.duration("retained", true), 0.0001),
		"hover": snappedf(policy.duration("hover", false), 0.0001),
		"fan_retarget": snappedf(policy.duration("hand_retarget", true), 0.0001),
		"reduced_draw": snappedf(reduced_policy.duration("draw", true), 0.0001),
		"reduced_discard": snappedf(reduced_policy.duration("discard", true), 0.0001)
	}

	var context = Context.new(null, {"reduced_motion": false, "animation_speed": 1.0}, load("res://data/themes/ivory_run.tres"))
	context.motion = policy
	var holder := Control.new()
	holder.size = Vector2(1400, 520)
	root.add_child(holder)
	var hand = load("res://scripts/ui/card_hand_view.gd").create(context)
	hand.size = Vector2(1400, 520)
	holder.add_child(hand)
	var overlay := Control.new()
	overlay.size = Vector2(1920, 1080)
	root.add_child(overlay)

	var cards: Array = []
	for i in 5:
		cards.append({"cardInstanceId": "motion-%d" % i, "definitionId": "basic_attack", "name": "Strike", "effects": ["Test"], "costs": [{"resourceId": "energy", "amount": 1}]})
	hand.sync_models(cards.slice(0, 3), "motion-baseline")
	await settle(4)

	# Fan & hover pose
	var rest_pose := pose_dict(Pose.read(hand.card_for("motion-1")))
	hand.interaction.pointer_active = true
	hand.interaction.hovered = "motion-1"
	hand._apply_emphasis()
	for state in hand._emphasis.values():
		if state.tween != null and state.tween.is_valid():
			state.tween.pause()
			state.tween.custom_step(1.0)
	var hover_pose := pose_dict(Pose.read(hand.card_for("motion-1")))
	hand.interaction.hovered = ""
	hand._apply_emphasis()
	for state in hand._emphasis.values():
		if state.tween != null and state.tween.is_valid():
			state.tween.pause()
			state.tween.custom_step(1.0)

	# Draw representative mid-flight pose (t = 0.5)
	hand.set_read_only(true)
	hand.begin_presentation(cards)
	var owner = Registry.new(hand, overlay, context)
	var draw_steps := [{"sourceAddress": "pile", "targetAddress": "hand", "instanceIds": ["motion-3", "motion-4"], "targetOrderAfter": cards.map(func(c): return c.cardInstanceId)}]
	var snapshots := [{"zones": [{"contentsVisible": true, "cards": cards}]}]
	var anchor := func(address, id): return hand.destination_center(id) if address == "hand" else Vector2(90, 590)
	var role := func(address): return "hand" if address == "hand" else "pile"
	var draw_handle = Handle.new()
	Motion.execute(draw_steps, snapshots, {}, anchor, overlay, context, draw_handle, role, owner)
	var draw_cycle: float = policy.duration("draw", true)
	for tween in draw_handle._tweens:
		tween.pause()
		tween.custom_step(draw_cycle * 0.5)
	var draw_mid_pose := pose_dict(Pose.read(owner._records["motion-3"]))
	draw_handle.finish("cancelled")
	await settle(2)
	check(overlay.get_child_count() == 0, "cancelled draw cleans up overlay ghosts")
	hand.end_presentation()
	owner.dispose()
	holder.queue_free()
	overlay.queue_free()
	await settle(2)

	# Map camera transition representative midpoint
	var fixture: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://tests/fixtures/map_world_one.json"))
	var map_holder := VBoxContainer.new()
	map_holder.position = Vector2(24, 24)
	map_holder.size = Vector2(1872, 1032)
	root.add_child(map_holder)
	var map_view = load("res://scripts/ui/map_view.gd").create()
	map_view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	var map_state := {"runId": "map-baseline", "currentNodeId": "s01_arrival", "map": {"nodes": fixture.nodes.duplicate(true), "resolvedNodeIds": []}}
	var map_presenter = load("res://scripts/presentation/activity_presenter.gd").new(map_state, [], {}, context.translator)
	map_view.apply_projection(map_state, [], map_presenter, context)
	map_holder.add_child(map_view)
	await settle(6)
	var map_origin := Vector2(map_view._scroll.scroll_horizontal, map_view._scroll.scroll_vertical)
	map_state.currentNodeId = "r01"
	map_view.apply_projection(map_state, [], load("res://scripts/presentation/activity_presenter.gd").new(map_state, [], {}, context.translator), context)
	for _f in 8:
		if map_view._camera_tween != null:
			break
		await process_frame
	var map_mid := map_origin
	var map_target: Vector2 = map_view._camera_target
	if map_view._camera_tween != null and map_view._camera_tween.is_valid():
		map_view._camera_tween.pause()
		map_view._camera_tween.custom_step(0.14)
		map_mid = Vector2(map_view._scroll.scroll_horizontal, map_view._scroll.scroll_vertical)
		map_view._camera_tween.custom_step(1.0)
	map_holder.queue_free()
	await settle(2)

	return {
		"durations": durations,
		"rest_pose": rest_pose,
		"hover_pose": hover_pose,
		"draw_mid_pose": draw_mid_pose,
		"map_transition": {
			"origin_y": snappedf(map_origin.y, 0.1),
			"mid_y": snappedf(map_mid.y, 0.1),
			"target_y": snappedf(map_target.y, 0.1)
		}
	}

func _compare_geometry(expected: Dictionary, actual: Dictionary) -> void:
	for res_key in expected.keys():
		check(actual.has(res_key), "geometry baseline contains resolution/scale key: " + res_key)
		if not actual.has(res_key):
			continue
		var exp_screens: Dictionary = expected[res_key]
		var act_screens: Dictionary = actual[res_key]
		for screen_key in exp_screens.keys():
			if screen_key == "combat_Matrix":
				var exp_m: Dictionary = exp_screens[screen_key]
				var act_m: Dictionary = act_screens.get(screen_key, {})
				for count_key in exp_m.keys():
					var exp_c: Dictionary = exp_m[count_key]
					var act_c: Dictionary = act_m.get(count_key, {})
					check(int(exp_c.get("order_count", -1)) == int(act_c.get("order_count", -2)) and int(exp_c.get("actor_count", -1)) == int(act_c.get("actor_count", -2)),
						"combat matrix counts preserved for hand=" + count_key)
					var exp_poses: Dictionary = exp_c.get("poses", {})
					var act_poses: Dictionary = act_c.get("poses", {})
					var all_poses_ok := true
					for card_id in exp_poses.keys():
						if not act_poses.has(card_id) or not poses_match(exp_poses[card_id], act_poses[card_id]):
							all_poses_ok = false
					check(all_poses_ok, "combat matrix fan poses preserved for hand=" + count_key)
				continue
			var exp_rects: Dictionary = exp_screens[screen_key]
			var act_rects: Dictionary = act_screens.get(screen_key, {})
			var screen_ok := true
			for node_key in exp_rects.keys():
				if not act_rects.has(node_key) or not rects_match(exp_rects[node_key], act_rects[node_key]):
					screen_ok = false
					print("  MISMATCH ", res_key, "/", screen_key, "/", node_key, " expected=", exp_rects[node_key], " actual=", act_rects.get(node_key))
			check(screen_ok, "geometry preserved: %s / %s" % [res_key, screen_key])

func _compare_motion(expected: Dictionary, actual: Dictionary) -> void:
	var exp_dur: Dictionary = expected.get("durations", {})
	var act_dur: Dictionary = actual.get("durations", {})
	var dur_ok := true
	for k in exp_dur.keys():
		if not is_equal_approx(float(exp_dur[k]), float(act_dur.get(k, -1.0))):
			dur_ok = false
	check(dur_ok, "motion durations preserved across draw, use, discard, exile, retained, hover, fan and reduced_motion")
	check(poses_match(expected.get("rest_pose", {}), actual.get("rest_pose", {})), "hand rest pose preserved")
	check(poses_match(expected.get("hover_pose", {}), actual.get("hover_pose", {})), "hand hover pose preserved")
	check(poses_match(expected.get("draw_mid_pose", {}), actual.get("draw_mid_pose", {})), "in-flight draw pose at t=0.5 preserved")
	var exp_map: Dictionary = expected.get("map_transition", {})
	var act_map: Dictionary = actual.get("map_transition", {})
	check(absf(float(exp_map.get("mid_y", 0.0)) - float(act_map.get("mid_y", 0.0))) <= 2.0 \
		and absf(float(exp_map.get("target_y", 0.0)) - float(act_map.get("target_y", 0.0))) <= 1.0,
		"map transition midpoint and target preserved")

func _run() -> void:
	root.gui_embed_subwindows = true
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="):
			output_dir = arg.trim_prefix("--lab-output=")
		elif arg.begins_with("--write-baseline="):
			write_baseline_path = arg.trim_prefix("--write-baseline=")
		elif arg.begins_with("--compare-baseline="):
			compare_baseline_path = arg.trim_prefix("--compare-baseline=")

	var prefs = root.get_node("Preferences")
	var saved_before := FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""
	var session = root.get_node("GameSession")
	var session_before := JSON.stringify([session.run, session.combat])
	var http_before: int = root.get_node("GameServices").diagnostics().size()
	root.get_node("I18n").set_locale("en")

	var geometry := {}
	for res in RESOLUTIONS:
		for scale_val in SCALES:
			var key := "%dx%d@%.1f" % [res.x, res.y, scale_val]
			geometry[key] = await _capture_screen_geometry(res, scale_val)

	var motion := await _capture_motion_baseline()

	var payload := {
		"engine_version": Engine.get_version_info().string,
		"locale": "en",
		"resolutions": ["1280x720", "1920x1080", "2560x1080"],
		"scales": [1.0, 1.6],
		"tolerances": {"position_px": POSITION_TOLERANCE, "angle_rad": ANGLE_TOLERANCE},
		"geometry": geometry,
		"motion": motion
	}

	if not write_baseline_path.is_empty():
		var file := FileAccess.open(write_baseline_path, FileAccess.WRITE)
		check(file != null, "baseline file writable: " + write_baseline_path)
		if file != null:
			file.store_string(JSON.stringify(payload, "\t") + "\n")
			file.close()

	var baseline_to_check := compare_baseline_path
	if baseline_to_check.is_empty() and FileAccess.file_exists("res://data/baselines/ui_decoupling_baseline.json"):
		baseline_to_check = "res://data/baselines/ui_decoupling_baseline.json"
	if not baseline_to_check.is_empty() and FileAccess.file_exists(baseline_to_check):
		var expected = JSON.parse_string(FileAccess.get_file_as_string(baseline_to_check))
		check(expected is Dictionary, "loaded baseline JSON from " + baseline_to_check)
		if expected is Dictionary:
			_compare_geometry(expected.get("geometry", {}), geometry)
			_compare_motion(expected.get("motion", {}), motion)
	else:
		check(geometry.size() == 6 and motion.has("durations"), "captured 6 resolution/scale combinations and motion baseline")

	check(JSON.stringify([session.run, session.combat]) == session_before, "baseline capture preserves live session state")
	check(root.get_node("GameServices").diagnostics().size() == http_before, "baseline capture issues zero HTTP requests")
	check((FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else "") == saved_before, "baseline capture writes no player preferences")

	root.get_node("GameAudio").shutdown()
	await create_timer(0.05).timeout
	quit(0 if failures == 0 else 1)
