extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1

func snapshot() -> Dictionary:
	return {"runId": "run", "runSequence": 2, "expectedStep": 0, "contentRevision": "revision", "actorId": "hero", "enabled": true, "canChange": true,
		"profile": {"compartments": [{"compartmentId": "utility", "nameKey": "Utility", "descriptionKey": "Gain block", "capacity": 1}]},
		"allocations": [], "cards": [{"cardInstanceId": "one", "definitionId": "card", "presentation": {"identity": {"definitionId": "card", "cardInstanceId": "one"}, "effects": [], "costs": [], "conditions": [], "tags": []}}]}

func _run() -> void:
	var workspace = load("res://scripts/application/deck_compartment_workspace.gd").new()
	var source := snapshot(); workspace.load_snapshot(source)
	check(not workspace.dirty(), "initial snapshot is confirmed, not a draft")
	check(workspace.stage("one", "utility") and workspace.dirty(), "allocation stages a local change")
	check(source.allocations.is_empty(), "draft never mutates source payload")
	check(not workspace.stage("foreign", "utility"), "unknown card cannot be staged")
	var old_ticket: int = workspace.revision; workspace.stage("one", "")
	check(not workspace.accept_preview(source, old_ticket), "late preview cannot replace a newer draft")
	check(workspace.accept_preview(source, workspace.revision), "current preview is accepted")
	check(not workspace.accept_preview({"runId": "other", "runSequence": 2}, workspace.revision), "foreign run preview is rejected")
	workspace.load_snapshot(source); workspace.clear()
	check(workspace.allocations().is_empty(), "clear uses an explicit empty final distribution")
	check(load("res://scripts/engine/deck_compartment_gateway.gd") != null, "REST adapter parses")
	check(load("res://scenes/deck_compartment_lab.tscn") != null, "isolated lab scene parses")
	var editor = load("res://scripts/ui/deck_compartment_editor.gd").new()
	editor.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	root.add_child(editor); await process_frame
	editor.workspace.load_snapshot(source); editor._render(); await process_frame
	check(editor._body.get_child_count() == 2, "neutral area and configured compartment render independently")
	check(editor.find_children("*", "Button", true, false).any(func(button): return button.get_script() == load("res://scripts/ui/card_view.gd")), "editor uses the same CardView as combat")
	check(editor._apply.disabled, "confirmed state cannot submit a redundant command")
	check(not editor._error({"data": null}).is_empty(), "null transport body cannot crash editor")
	check(load("res://scripts/ui/run_deck_compartments.gd") != null, "run editor reuses the laboratory editor without character picker")
	var choices = load("res://scripts/application/activity_choices.gd").build({}, [{"type": "APPLY_DECK_COMPARTMENTS", "validPayload": {"actorId": "hero"}}])
	check(choices.is_empty(), "generic activity auto-flow never invents a compartment command payload")
	editor.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
