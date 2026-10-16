extends SceneTree
var failures := 0
var replay_script: GDScript
class Cursor extends RefCounted:
	signal navigation_ready
	var playing := false
	var play_epoch := 0
	var entries: Array = []
	var selected_index := -1
	var alive := true
	var hold_navigation := false
	var remaining := 1
	var calls := 0
	func valid() -> bool: return alive
	func model() -> Dictionary: return {}
	func navigate(_direction: int) -> bool:
		calls += 1
		if hold_navigation: await navigation_ready
		if remaining <= 0: return false
		remaining -= 1; return true
	func stop() -> void: playing = false; play_epoch += 1
	func dispose() -> void: alive = false; stop()
class Visual extends Node:
	signal settled
	func blocking() -> bool: return true
	func set_paused(_value: bool) -> void: pass
	func dispose() -> void: pass
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func make_screen():
	var screen = replay_script.new(); screen.controller = Cursor.new()
	screen._visual_mode = true; root.add_child(screen); return screen
func _run() -> void:
	# Load after autoload initialization; only waiting points/delay are controlled.
	# The production playback loop, disposal and validity checks are not replaced.
	replay_script = GDScript.new()
	replay_script.source_code = """extends "res://scripts/ui/run_replay_screen.gd"
signal release_watch
var watch_calls := 0
func _watch() -> void:
	watch_calls += 1
	await release_watch
func playback_delay() -> float: return .02
"""
	check(replay_script.reload() == OK, "probe inherits production replay adapter")
	var screen = make_screen(); var cursor = screen.controller
	screen._toggle_play()
	check(screen.watch_calls == 1, "visual inspection is really pending before closure")
	root.remove_child(screen); screen.release_watch.emit(); await process_frame
	check(not cursor.playing and cursor.calls == 1, "closing during visual inspection cannot schedule a timer or advance again")
	screen._toggle_play()
	check(cursor.calls == 1, "detached or disposed playback cannot restart")
	screen.free()

	screen = make_screen(); cursor = screen.controller; cursor.hold_navigation = true
	screen._toggle_play(); root.remove_child(screen); cursor.navigation_ready.emit(); await process_frame
	check(screen.watch_calls == 0 and cursor.calls == 1, "closing during navigation cannot start a visual read")
	screen.free()

	screen = make_screen(); cursor = screen.controller
	cursor.entries = [{"sequence":1}]; cursor.selected_index = 0; screen._visual_sequence = 1
	var visual := Visual.new(); screen.add_child(visual); screen._visual_replay = visual
	screen._toggle_play(); root.remove_child(screen); visual.settled.emit()
	check(cursor.calls == 0, "closing during an animation settlement cannot navigate")
	await process_frame; screen.free()

	screen = make_screen(); cursor = screen.controller
	screen._toggle_play(); cursor.stop(); screen.release_watch.emit()
	await create_timer(.05).timeout
	check(cursor.calls == 1 and not cursor.playing, "stop during inspection prevents further playback")
	screen.queue_free(); await process_frame

	screen = make_screen(); cursor = screen.controller; screen._visual_mode = false
	screen._toggle_play(); root.remove_child(screen); await create_timer(.05).timeout
	check(cursor.calls == 1 and not cursor.playing, "closing while delay timer runs never resumes navigation")
	screen.free()

	screen = make_screen(); cursor = screen.controller
	screen._toggle_play(); cursor.stop()
	cursor.remaining = 2; screen._visual_mode = false; screen._toggle_play()
	var epoch: int = cursor.play_epoch
	screen.release_watch.emit()
	check(cursor.playing and cursor.play_epoch == epoch and cursor.calls == 2, "late old generation neither stops nor advances the restarted playback")
	await create_timer(.1).timeout
	check(not cursor.playing and cursor.calls == 4, "current generation finishes normally after stale continuation is ignored")
	screen.queue_free(); await process_frame

	screen = make_screen(); cursor = screen.controller
	screen._toggle_play(); cursor.alive = false; screen.release_watch.emit(); await create_timer(.05).timeout
	check(cursor.calls == 1, "invalidated replay context cannot schedule another iteration")
	screen.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
