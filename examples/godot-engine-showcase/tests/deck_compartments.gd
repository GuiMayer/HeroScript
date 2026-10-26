extends SceneTree
var failures := 0
class ReceiptFixture extends RefCounted:
	var prepared := 0
	var sent: Array = []
	func prepare_command(_scope, _id, _type, _payload, _sequence, _step) -> Dictionary:
		prepared += 1; return {"identity": prepared}
	func send_prepared(command: Dictionary) -> Dictionary:
		sent.append(command.duplicate(true))
		return {"ok": true, "data": null} if sent.size() == 1 else {"ok": true, "data": {"sequence": 3}}
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
	var facts := {"identity": {"definitionId": "card"}, "conditions": [{"componentId": "source.requirement", "failureReason": "Requires positive Energy"}],
		"componentContributions": [{"nameKey": "Ward compartment", "descriptionKey": "Grants Block"}]}
	var model: Dictionary = load("res://scripts/presentation/card_facts_presenter.gd").new(editor.visual_context.translator, {}).project(facts)
	check(model.requirements.size() == 1 and model.requirements[0].text == "Requires positive Energy", "typed REST condition arrays render without an artificial discriminator")
	check(model.changeBadges.size() == 1 and not model.upgraded, "source contribution is presented separately from permanent upgrades")
	var receipts := ReceiptFixture.new()
	var gateway = load("res://scripts/engine/deck_compartment_gateway.gd").new(null, receipts, "run")
	var invalid: Dictionary = await gateway.apply(source, [])
	check(not invalid.ok and not gateway.pending.is_empty(), "malformed successful receipt is uncertain and retains the command identity")
	var recovered: Dictionary = await gateway.apply(source, [])
	check(recovered.ok and gateway.pending.is_empty() and receipts.prepared == 1 and receipts.sent[0] == receipts.sent[1], "retry reuses the exact command instead of submitting a second mutation")
	editor.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
