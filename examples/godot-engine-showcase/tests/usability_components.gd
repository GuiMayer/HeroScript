extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(value: bool, label: String) -> void:
	print(("[PASS] " if value else "[FAIL] ") + label)
	if not value: failures += 1
func run() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var original := JSON.stringify(root.get_node("GameSession").run)
	var toast = load("res://scenes/ui/overlays/toast.tscn").instantiate()
	root.add_child(toast)
	toast.present("A long notice ".repeat(30), true, context)
	check(toast.text.begins_with("Error:"), "Errors carry a textual cue, independent of color")
	check(toast.get_node("Message").autowrap_mode == TextServer.AUTOWRAP_WORD_SMART, "Long notices wrap")
	toast.free()
	var before := Button.new(); before.text = "Origin"
	root.add_child(before); before.grab_focus()
	var dialog = load("res://scenes/ui/overlays/confirmation_dialog.tscn").instantiate()
	root.add_child(dialog)
	dialog.about_to_popup.emit()
	var other := Button.new(); root.add_child(other); other.grab_focus()
	dialog.free()
	check(root.gui_get_focus_owner() == before, "Closing a modal restores the previous living focus owner")
	before.free(); other.free()
	var codex = load("res://scenes/ui/screens/codex.tscn").instantiate()
	root.add_child(codex); codex.mount(context)
	codex.current_items = [{"definitionId":"first"},{"definitionId":"second"}]
	codex.get_node("Search").text = "second"; codex._filter_items()
	check(codex.filtered_items.size() == 1 and codex.detail.text.contains("second"), "Filtered selection addresses its displayed definition")
	codex.get_node("Search").text = "absent"; codex._filter_items()
	check(codex.filtered_items.is_empty() and codex.detail.text.is_empty(), "Empty search clears stale detail")
	codex.free()
	var map = load("res://scenes/ui/components/map_view.tscn").instantiate()
	check(map.has_node("Header/CenterCurrent"), "Map provides an explicit center action")
	check(map.has_node("MapWorkspace/MapFloatingInspector/Margin/Stack/Actions"), "Travel actions remain outside scrolling details")
	map.free()
	var first := {"combatId":"archived", "actors":[{"instanceId":"hero", "definitionId":"spire_adept", "resources":{"custom":{"current":1,"maximum":10}}}]}
	var last := first.duplicate(true); last.actors[0].resources.custom.current = 2
	var commit := {"sequence":1,"combatResolution":{"combatId":"archived"},"stateAfter":{"encounters":[{"combat":last}]},"frames":[{"scope":"combat","kind":"effect","combatStateAfter":first},{"scope":"combat","kind":"effect","combatStateAfter":last}]}
	var recorded := JSON.stringify(commit)
	var history = load("res://scripts/ui/historical_beat_view.gd").create(context)
	root.add_child(history); history.load_commit(commit, {}, false)
	history.seek_cursor(1)
	check(history.history.presenter.actors()[0].resources.custom.current == 1, "Backward inspection renders the recorded earlier snapshot")
	check(JSON.stringify(commit) == recorded, "Seeking preserves the canonical commit")
	history.free()
	var view = load("res://scripts/ui/dialogue_panel.gd").create(context)
	root.add_child(view)
	var selected := []
	view.choice_requested.connect(func(command): selected.append(command))
	view.apply_model({"text":"Read", "options":[{"id":"choice", "text":"Continue", "cost":"", "available":true, "reason":"", "command":{"reference":"published"}}]})
	var event := InputEventKey.new(); event.keycode = KEY_1; event.pressed = true
	view._unhandled_key_input(event)
	check(selected.size() == 1 and selected[0].reference == "published", "Numeric choice emits the unchanged engine command")
	view.set_read_only(true); view._unhandled_key_input(event)
	check(selected.size() == 1, "Read-only dialogue rejects shortcuts")
	view.free()
	check(JSON.stringify(root.get_node("GameSession").run) == original, "Usability controls preserve authoritative session state")
	quit(1 if failures else 0)
