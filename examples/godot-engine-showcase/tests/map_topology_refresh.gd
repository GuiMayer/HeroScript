extends SceneTree
const Context = preload("res://scripts/ui/visual_context.gd")
const Presenter = preload("res://scripts/presentation/activity_presenter.gd")
var failures := 0
var output := ""
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for frame in 12: await process_frame
func centered(view) -> bool:
	var current: Button = view.route.buttons[view.navigation.current]
	return current.get_global_rect().get_center().distance_to(view._scroll.get_global_rect().get_center()) < 2
func run() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--lab-output="): output = arg.trim_prefix("--lab-output=")
	var fixture: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://tests/fixtures/map_world_one.json"))
	var context = Context.new(null, {}, load("res://data/themes/ivory_run.tres"))
	for dimensions in [Vector2i(1280, 800), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = dimensions
		var holder := VBoxContainer.new(); holder.position = Vector2(24, 24); holder.size = Vector2(dimensions) - Vector2(48, 48); root.add_child(holder)
		var view = load("res://scripts/ui/map_view.gd").new(); view.size_flags_vertical = Control.SIZE_EXPAND_FILL
		var state := {"runId": "map-refresh", "currentNodeId": "s01_arrival", "map": {"nodes": [fixture.nodes[0].duplicate(true)], "resolvedNodeIds": []}}
		view.apply_projection(state, [], Presenter.new(state, [], {}, context.translator), context)
		holder.add_child(view); await settle()
		var node: Button = view.route.buttons.s01_arrival
		check(centered(view), "compact initial map centers current node: " + str(dimensions))
		state.map.nodes = fixture.nodes.duplicate(true)
		var preserved := JSON.stringify(state)
		var intents: Array = []; view.intent_requested.connect(func(action, args): intents.append([action, args]))
		view.apply_projection(state, [], Presenter.new(state, [], {}, context.translator), context)
		await settle()
		check(view.route.buttons.size() == 50 and view.route.buttons.s01_arrival == node, "expanded topology retains current node and mounts all stops")
		check(centered(view), "same current node recenters after compact-to-full map expansion: " + str(dimensions))
		var stable := node.get_global_rect()
		await settle()
		check(node.get_global_rect() == stable, "camera and current node do not drift after layout settles")
		# An ordinary choice/resource/selection refresh must not reset a deliberate pan.
		view._scroll.scroll_vertical += 110; view._scroll.scroll_horizontal += 45
		var pan := Vector2(view._scroll.scroll_horizontal, view._scroll.scroll_vertical)
		view._inspect("r01")
		var choice := {"type": "RESOLVE_COMBAT", "subjectId": "", "payload": {"combatId": "won"}}
		view.apply_projection(state, [choice], Presenter.new(state, [choice], {}, context.translator), context); await settle()
		check(Vector2(view._scroll.scroll_horizontal, view._scroll.scroll_vertical) == pan, "choice refresh preserves intentional pan when geometry is unchanged")
		view._set_inspector_open(true); await settle()
		check(Vector2(view._scroll.scroll_horizontal, view._scroll.scroll_vertical) == pan, "floating inspector never reframes map")
		view.route.layout_profile.lane_gap += 40
		view._model_key = [] # Refresh the supplied layout profile, without changing engine data.
		view.apply_projection(state, [choice], Presenter.new(state, [choice], {}, context.translator), context); await settle()
		check(centered(view), "geometry changes with identical node IDs also reframe current node")
		view._scroll.scroll_vertical += 80
		state.map.nodes = [fixture.nodes[0].duplicate(true)]
		view.apply_projection(state, [], Presenter.new(state, [], {}, context.translator), context); await settle()
		check(centered(view), "shrinking topology clamps and centers without stale offsets")
		check(intents.is_empty() and state.currentNodeId == "s01_arrival", "framing never submits gameplay actions or advances current node")
		state.map.nodes = fixture.nodes.duplicate(true)
		check(JSON.stringify(state) == preserved, "presentation never mutates published topology")
		if not output.is_empty() and DisplayServer.get_name() != "headless":
			view.apply_projection(state, [], Presenter.new(state, [], {}, context.translator), context); await settle()
			await RenderingServer.frame_post_draw
			root.get_texture().get_image().save_png(output.path_join("map-refresh-%sx%s.png" % [dimensions.x, dimensions.y]))
		holder.queue_free(); await settle()
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
