extends SceneTree
var failures := 0
class Session extends RefCounted:
	signal changed
	signal release
	var held := false
	var fail := true
	var foreign := false
	var reads: Array = []
	var previews: Array = []
	var commits: Array = []
	var run := {"runId": "run", "playerEntityId": "hero", "settingId": "setting", "sequence": 2, "step": 5, "contentRevision": "revision"}
	var assignments := {}
	func loadout_context_matches(value: Dictionary) -> bool:
		return value.get("runId") == run.runId and value.get("runSequence") == run.sequence and value.get("expectedStep") == run.step and value.get("contentRevision") == run.contentRevision
	func snapshot(actor: String) -> Dictionary:
		return {"runId": run.runId, "runSequence": run.sequence, "expectedStep": run.step, "contentRevision": run.contentRevision,
			"actorId": actor, "actorIds": ["hero", "other"], "inventoryId": "actor:" + actor, "canEquip": true, "usedCapacity": assignments.size() * 3,
			"policy": {"inventoryScope": "Actor", "capacity": 12, "slots": [{"slotId": "one"}]}, "assignments": assignments.duplicate(),
			"items": [{"instance": {"instanceId": "owned"}, "definition": {"inscriptionId": "focus", "equipmentCost": 3, "presentation": {"name": "Actual inscription"}}}],
			"attributes": {"attributes": []}}
	func read_loadout(actor: String) -> Dictionary:
		reads.append(actor)
		if held: await release
		if fail: return {"ok": false, "error": "Offline test"}
		return {"ok": true, "data": snapshot("other" if foreign else actor)}
	func preview_loadout(source: Dictionary, draft: Dictionary) -> Dictionary:
		previews.append(source.actorId)
		var after := source.duplicate(true); after.assignments = draft.duplicate()
		return {"ok": true, "data": {"before": source, "after": after}}
	func apply_loadout(source: Dictionary, draft: Dictionary) -> bool:
		commits.append(source.actorId); assignments = draft.duplicate(); run.sequence += 1; changed.emit(); return true
	func recover_pending() -> bool: return true
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func settle() -> void:
	for _frame in 6: await process_frame
func _run() -> void:
	var source := FileAccess.get_file_as_string("res://scripts/ui/run_loadout_screen.gd")
	check(not source.contains("FileAccess") and not source.contains("inscription_loadout_lab.json"), "run composition does not load offline files")
	var session := Session.new()
	var screen = load("res://scenes/run_loadout.tscn").instantiate(); screen.session = session; root.add_child(screen)
	check(screen.workspace == null and not screen.view.visible, "run screen starts empty, never with example equipment")
	await settle()
	check(screen.workspace == null and not screen.view.visible and screen._status.text.contains("Offline test"), "failed initial read shows error without offline fallback")
	check(not screen._retry_button.disabled and screen._confirm_button.disabled, "failed read leaves recovery available and confirmation disabled")
	check(screen.find_child("LoadoutActorPicker", true, false) == null and screen.find_child("LoadoutScopePicker", true, false) == null, "no actor or inventory-scope selector exists in run screen")
	session.fail = false; await screen._recover(); await settle()
	check(screen.view.visible and session.reads == ["hero", "hero"] and screen.workspace.source.actorId == "hero", "retry reads only the run's persistent player, even when other actors are registered")
	check(screen.view._inventory.keys() == ["owned"] and not screen.view.preset_picker.visible, "only canonical owned items are displayed, without example presets")
	check(not screen.view.find_child("LoadoutLayoutLabel", true, false).visible and screen.view.find_child("LoadoutRestoreButton", true, false).text == root.get_node("I18n").text("Discard draft"), "connected tools contain no example layout or restore-example action")
	screen._edit("select_item", ["owned"]); screen._edit("select_slot", ["one"]); screen._edit("stage_selected")
	await screen._preview()
	check(session.previews == ["hero"] and not screen._confirm_button.disabled, "preview belongs to the same current player")
	await screen._confirm(); await settle()
	check(session.commits == ["hero"] and screen.workspace.source.assignments == {"one": "owned"}, "confirmation and reload keep the same player and durable equipment")
	session.run.sequence += 1; session.changed.emit()
	check(not screen.view.visible and screen._confirm_button.disabled and not screen._retry_button.disabled, "stale revision cannot keep editing or confirming an old character projection")
	await screen._recover()
	check(screen.view.visible and screen.workspace.source.runSequence == session.run.sequence, "refresh restores the current version of the same character")
	session.foreign = true; await screen._read()
	check(screen.workspace == null and not screen.view.visible and screen._confirm_button.disabled, "foreign actor response is rejected rather than shown or equipped")
	session.foreign = false; session.held = true; screen._read(); await process_frame
	session.run.playerEntityId = "other"; session.release.emit(); await settle()
	check(screen.workspace == null and not screen.view.visible and screen._confirm_button.disabled, "character change during read cannot publish the previous actor's equipment")
	screen._edit("stage_selected"); await screen._confirm()
	check(session.commits == ["hero"] and screen._retry_button.disabled, "changed context requires reopening and cannot redirect a draft to another actor")
	screen.queue_free(); await settle()
	session = Session.new(); session.fail = false; session.held = true
	screen = load("res://scenes/run_loadout.tscn").instantiate(); screen.session = session; root.add_child(screen); await process_frame
	session.run.runId = "another-run"; session.release.emit(); await settle()
	check(screen.workspace == null and not screen.view.visible and session.commits.is_empty(), "late response after switching runs cannot expose or equip another run's inventory")
	screen.queue_free(); await settle()
	var lab = load("res://scenes/inscription_loadout_lab.tscn").instantiate(); root.add_child(lab); await settle()
	check(not lab.remote and lab.view._inventory.size() == 15 and lab._actor_picker.item_count == 2, "separate laboratory retains its offline examples and actor exploration")
	lab.queue_free(); await settle()
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
