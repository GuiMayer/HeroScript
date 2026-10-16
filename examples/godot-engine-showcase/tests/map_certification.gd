extends SceneTree
## Rendered/read-only certification of production MapView with real published topology.
var failures := 0
var output_dir := ""
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 12: await process_frame
func capture(label: String) -> void:
	if output_dir.is_empty() or DisplayServer.get_name() == "headless": return
	await RenderingServer.frame_post_draw
	check(root.get_texture().get_image().save_png(output_dir.path_join(label + ".png")) == OK, "capture " + label)
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
	if not output_dir.is_empty(): DirAccess.make_dir_recursive_absolute(output_dir)
	root.content_scale_size = Vector2i.ZERO
	var fixture: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://tests/fixtures/map_world_one.json"))
	var run := {"runId":"map-qa", "branchId":"qa", "currentNodeId":"r05_corrosion", "map":{"nodes":fixture.nodes}, "metadata":{"displayName":"Ascendant Matrix · World 1"}, "resources":{"gold":{"current":50}}}
	var current: Dictionary = fixture.nodes.filter(func(n): return n.nodeId == run.currentNodeId)[0]
	var choices: Array = current.nextNodeIds.map(func(id): return {"type":"ADVANCE_NODE", "subjectId":id, "payload":{"targetNodeId":id}})
	var original := JSON.stringify([run,choices]); var commands: Array = []
	for resolution in [Vector2i(1280,720),Vector2i(1920,1080),Vector2i(2560,1080),Vector2i(2560,1440)]:
		root.size = resolution
		for language in ["en","pt_BR"]:
			root.get_node("I18n").set_locale(language)
			for font_scale in [1.0,1.2]:
				var context = load("res://scripts/ui/visual_context.gd").new(root.get_node("I18n"), {"text_scale":font_scale,"high_contrast":font_scale>1,"reduced_motion":true},load("res://data/themes/ivory_run.tres"))
				var host := PanelContainer.new(); host.theme = context.theme; root.add_child(host); host.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
				var margin := MarginContainer.new()
				for side in ["left","top","right","bottom"]: margin.add_theme_constant_override("margin_" + side,24)
				host.add_child(margin)
				var view = load("res://scripts/ui/map_view.gd").new(); margin.add_child(view)
				view.intent_requested.connect(func(action,args): commands.append([action,args]))
				var presenter = load("res://scripts/presentation/activity_presenter.gd").new(run,choices,{},context.translator)
				view.apply_projection(run,choices,presenter,context); await settle()
				var label := "%sx%s-%s-%s" % [resolution.x,resolution.y,language,font_scale]
				var safe := Rect2(view._scroll.global_position,view._usable_size()).grow(1)
				var visible := true
				for id in view.navigation.decision_ids():
					if not safe.encloses(view.route.buttons[id].get_global_rect()): visible = false
				check(visible and not view._scroll.get_h_scroll_bar().visible, label + ": all six alternatives and current fit without horizontal pan")
				check(load("res://tests/layout_inspector.gd").vertical_text_issues(view).is_empty(), label + ": no vertical text")
				var button = view.route.buttons[choices[0].subjectId]
				var calculation_count: int = view.route._layout.calculation_count
				var camera_state: Vector2 = Vector2(view._scroll.scroll_horizontal,view._scroll.scroll_vertical)
				for _i in 20: view.apply_projection(run,choices,presenter,context)
				await settle()
				check(view.route.buttons[choices[0].subjectId] == button and view.route._layout.calculation_count == calculation_count and camera_state == Vector2(view._scroll.scroll_horizontal,view._scroll.scroll_vertical), label + ": twenty refreshes preserve nodes, layout and camera")
				var saved: Dictionary = view.capture_local_state()
				view._center_node("s01_arrival"); await settle(); view.restore_local_state(saved); await settle()
				check(camera_state.distance_to(Vector2(view._scroll.scroll_horizontal,view._scroll.scroll_vertical)) <= 2, label + ": semantic restoration returns to middle-act reading context")
				if resolution == Vector2i(1280,720) and language == "en" and font_scale == 1:
					root.size = Vector2i(1400,800); await settle()
					var resized: Dictionary = view.capture_local_state()
					check(str(saved.anchor) == str(resized.anchor) and saved.offset.distance_to(resized.offset) < 3, "resize preserves semantic anchor and offset")
					root.size = resolution; await settle()
				if language == "en" and font_scale == 1: await capture(label + "-decision")
				view._toggle_overview(); await settle()
				var overview_size: Vector2 = view.route.custom_minimum_size * view._zoom
				check(overview_size.y <= view._usable_size().y + 1 and button.mouse_filter == Control.MOUSE_FILTER_IGNORE, label + ": overview fits whole act and disables micro targets")
				if language == "en" and font_scale == 1: await capture(label + "-overview")
				host.queue_free(); await settle()
	check(commands.is_empty() and JSON.stringify([run,choices]) == original, "entire matrix emits zero commands and leaves snapshots unchanged")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
