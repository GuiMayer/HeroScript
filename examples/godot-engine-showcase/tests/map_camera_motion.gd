extends SceneTree
const Context = preload("res://scripts/ui/visual_context.gd")
const Presenter = preload("res://scripts/presentation/activity_presenter.gd")
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for frame in 8: await process_frame
func offset(view) -> Vector2: return Vector2(view._scroll.scroll_horizontal, view._scroll.scroll_vertical)
func apply(view, state: Dictionary, context, choices: Array = []) -> void:
	view.apply_projection(state, choices, Presenter.new(state, choices, {}, context.translator), context)
func run() -> void:
	var fixture: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://tests/fixtures/map_world_one.json"))
	for dimensions in [Vector2i(1280, 800), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = dimensions
		var context = Context.new(null, {}, load("res://data/themes/ivory_run.tres"))
		var holder := VBoxContainer.new(); holder.position = Vector2(24, 24); holder.size = Vector2(dimensions) - Vector2(48, 48); root.add_child(holder)
		var view = load("res://scripts/ui/map_view.gd").new(); view.size_flags_vertical = Control.SIZE_EXPAND_FILL
		var state := {"runId": "map-motion", "currentNodeId": "s01_arrival", "map": {"nodes": fixture.nodes.duplicate(true), "resolvedNodeIds": []}}
		apply(view, state, context); holder.add_child(view); await settle()
		check(view._camera_tween == null, "initial framing is immediate: " + str(dimensions))
		var origin := offset(view)
		state.currentNodeId = "r01"; apply(view, state, context); await settle()
		check(view._camera_tween != null and view._camera_tween.is_valid() and offset(view) != view._camera_target.round(), "node progress starts interpolation rather than teleporting")
		var tween: Tween = view._camera_tween
		tween.pause(); tween.custom_step(.14)
		var middle := offset(view); var target: Vector2 = view._camera_target
		check(middle.y > origin.y and middle.y < target.y, "camera visits an intermediate position toward the next node")
		apply(view, state, context, [{"type": "RESOLVE_NODE", "subjectId": "", "payload": {}}]); await settle()
		check(view._camera_tween == tween and offset(view) == middle, "ordinary refresh neither restarts nor snaps an active interpolation")
		tween.custom_step(1)
		check(offset(view).distance_to(target) < 1, "interpolation ends at the exact clamped center")
		context._options.reduced_motion = true
		state.currentNodeId = "s02_gate"; apply(view, state, context); await settle()
		check(view._camera_tween == null and offset(view).distance_to(view._camera_target) < 1, "reduced movement centers immediately")
		context._options.reduced_motion = false; context._options.animation_preset = "instant"
		state.currentNodeId = "r02"; apply(view, state, context); await settle()
		check(view._camera_tween == null, "instant preset creates no camera animation")
		context._options.animation_preset = "normal"
		state.currentNodeId = "s03_forge"; apply(view, state, context); await settle()
		tween = view._camera_tween; tween.pause(); tween.custom_step(.04)
		middle = offset(view)
		state.currentNodeId = "relic_keen"; apply(view, state, context); await settle()
		check(not tween.is_valid() and view._camera_tween != tween, "a new node replaces the old interpolation instead of stacking pans")
		var reference := offset(view)
		paused = true; await create_timer(.04).timeout
		check(offset(view) == reference, "pause freezes map interpolation")
		paused = false
		# Real pointer input must take ownership from the automatic camera.
		var point: Vector2 = view._scroll.get_global_rect().get_center() + Vector2(200, 0)
		var down := InputEventMouseButton.new(); down.button_index = MOUSE_BUTTON_LEFT; down.pressed = true; down.position = point; down.global_position = point
		view._input(down)
		check(view._camera_tween == null and view.camera.pressed, "pointer drag interrupts interpolation immediately")
		view.cancel_gesture()
		# Returning from an activity rebuilds MapView: the stored previous current
		# node still supplies an origin, even though the old view was disposed.
		var saved := {"current": "s01_arrival", "selected": "s01_arrival"}
		holder.remove_child(view); view.queue_free()
		view = load("res://scripts/ui/map_view.gd").new(); view.size_flags_vertical = Control.SIZE_EXPAND_FILL
		apply(view, state, context)
		view.restore_local_state(saved)
		check(view._camera_restore_from == "s01_arrival", "detached map reserves the previous stop before its first visible framing")
		holder.add_child(view)
		for frame in 12:
			if view._camera_tween != null and view._camera_tween.is_valid(): break
			await process_frame
		check(view._camera_tween != null and view._camera_tween.is_valid(), "return from an activity interpolates from the saved previous stop")
		if view._camera_tween != null: view._camera_tween.pause()
		var first: Button = view.route.buttons.s01_arrival
		var previous_offset: Vector2 = view.camera.offset_for(first.position + first.size * .5 + view.route.position, view._scroll.size, view._canvas.custom_minimum_size)
		check(offset(view).distance_to(previous_offset) < offset(view).distance_to(view._camera_target), "restored transition begins near the previous stop, not the scroll origin")
		tween = view._camera_tween
		view._reveal_node(state.currentNodeId); await settle()
		check(view._camera_tween == tween, "initial current-node focus cannot fight the restored camera transition")
		context._options.reduced_motion = true; apply(view, state, context)
		check(view._camera_tween == null and offset(view).distance_to(view._camera_target) < 1, "enabling reduced movement settles a running interpolation")
		check(state.map.resolvedNodeIds.is_empty() and state.currentNodeId == "relic_keen", "camera does not complete or advance gameplay nodes")
		context._options.reduced_motion = false
		state.currentNodeId = "s04_watch"; apply(view, state, context); await settle()
		tween = view._camera_tween
		holder.queue_free(); await settle()
		check(tween == null or not tween.is_valid(), "disposing the map cancels its camera motion")
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
