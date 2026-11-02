extends SceneTree
var failures := 0
class Router extends Node:
	var visual_context = preload("res://scripts/ui/visual_context.gd").new()
	func back_to_menu(): pass
func _init(): call_deferred("_run")
func check(ok: bool, message: String):
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok: failures += 1
func _run():
	var requests: int = root.get_node("GameServices").diagnostics().size()
	var owner := Router.new()
	var sandbox = load("res://scenes/ui/screens/sandbox.tscn").instantiate()
	var data := {"sandbox_modes":[{"id":"test", "name":"Test", "description":"Published mode"}],"default_scenario":{"actors":[]}}
	var original := data.duplicate(true)
	sandbox.setup(owner,data); root.add_child(sandbox)
	var mode: Button = sandbox.mode_buttons.test
	sandbox.setup(owner,data)
	check(sandbox.mode_buttons.test == mode and sandbox.mode_buttons.size() == 1, "sandbox reconciles mode controls without duplicates")
	mode.pressed.emit()
	check(sandbox.selected_mode == "test" and mode.button_pressed, "native mode selector uses existing selection behavior")
	check(data == original and sandbox.editor.text == JSON.stringify(data.default_scenario,"  "), "scenario display leaves source JSON immutable")
	check(not sandbox.scenario_picker.visible and sandbox.seed_input.max_value == 99999999, "scenario visibility and authored seed constraints remain intact")
	var codex = load("res://scenes/ui/screens/codex.tscn").instantiate()
	var probe := GDScript.new(); probe.source_code = 'extends "res://scripts/ui/codex_screen.gd"\nvar requested: Array = []\nfunc _load_kind(kind: String) -> void: requested.append(kind)\n'
	check(probe.reload() == OK, "codex test intercepts only external reads")
	codex.set_script(probe); codex.setup(owner); root.add_child(codex)
	await process_frame
	var tabs = codex.get_node("TabsScroll/Tabs")
	tabs.get_child(3).pressed.emit()
	check(tabs.get_child_count() == codex.KINDS.size() and codex.requested == ["cards","status-effects"], "codex template tabs request the published content kinds exactly once")
	codex.current_items = [{"definitionId":"entry","components":{"value":2}}]; codex._select_item(0)
	check(codex.detail.text.contains('"value": 2') and not codex.detail.editable, "codex native details remain read only")
	sandbox.queue_free(); codex.queue_free(); owner.free(); await process_frame
	check(root.get_node("GameServices").diagnostics().size() == requests, "tool preview and local selections issue no gameplay requests")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
