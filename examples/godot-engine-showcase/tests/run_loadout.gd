extends SceneTree
var failures := 0
class Session extends RefCounted:
	signal changed
	signal release
	var has_pending_command := false
	var modifier_items := {}
	var held := false
	var fail := true
	var foreign := false
	var invalid_preview := false
	var hold_preview := false
	var reads: Array = []
	var previews: Array = []
	var commits: Array = []
	var run := {"runId": "run", "playerEntityId": "hero", "settingId": "setting", "sequence": 2, "step": 5, "contentRevision": "revision"}
	var assignments := {}
	var visual_fixture := {}
	func loadout_context_matches(value: Dictionary) -> bool:
		return value.get("runId") == run.runId and value.get("runSequence") == run.sequence and value.get("expectedStep") == run.step and value.get("contentRevision") == run.contentRevision
	func snapshot(actor: String) -> Dictionary:
		var result := {"runId": run.runId, "runSequence": run.sequence, "expectedStep": run.step, "contentRevision": run.contentRevision,
			"actorId": actor, "actorIds": ["hero", "other"], "inventoryId": "actor:" + actor, "canEquip": true, "usedCapacity": assignments.size() * 3,
			"policy": {"inventoryScope": "Actor", "capacity": 12, "slots": [{"slotId": "one"}]}, "assignments": assignments.duplicate(),
			"items": [{"instance": {"instanceId": "owned"}, "definition": {"inscriptionId": "focus", "equipmentCost": 3, "presentation": {"name": "Actual inscription"}}}],
			"attributes": {"attributes": [{"attributeId": "critical_chance", "displayName": "Critical chance", "unit": "percent", "baseValue": 0, "effectiveValue": 777 if not assignments.is_empty() else 0}]}}
		if not visual_fixture.is_empty():
			result.policy.slots = visual_fixture.slots.map(func(id): return {"slotId": id})
			result.policy.capacity = visual_fixture.capacity; result.usedCapacity = visual_fixture.usedCapacity
			result.items = visual_fixture.items.map(func(item): return {"instance": {"instanceId": item.instanceId}, "definition": {"inscriptionId": item.instanceId, "equipmentCost": item.cost, "tags": item.tags, "attributeBonuses": item.attributeBonuses, "presentation": {"name": item.name, "description": item.get("description", ""), "rarity": item.rarity}}})
			result.attributes.attributes = visual_fixture.character.attributes.map(func(stat): return {"attributeId": stat.id, "displayName": stat.label, "unit": "percent" if stat.format == "percent" else "points", "baseValue": stat.base, "effectiveValue": stat.value})
		return result
	func read_loadout(actor: String) -> Dictionary:
		reads.append(actor)
		if held: await release
		if fail: return {"ok": false, "error": "Offline test"}
		return {"ok": true, "data": snapshot("other" if foreign else actor)}
	func preview_loadout(source: Dictionary, draft: Dictionary) -> Dictionary:
		previews.append(source.actorId)
		if hold_preview: await release
		if invalid_preview: return {"ok": false, "error": "Equipment budget exceeded"}
		var after := source.duplicate(true); after.assignments = draft.duplicate()
		return {"ok": true, "data": {"before": source, "after": after}}
	func apply_loadout(source: Dictionary, draft: Dictionary) -> bool:
		commits.append(source.actorId); assignments = draft.duplicate(); run.sequence += 1; changed.emit(); return true
	func read_loadout_info() -> Dictionary:
		if fail: return {"ok": false, "error": "Offline test"}
		return {"ok": true, "data": {"relics": {"relics": []}, "attributes": snapshot(str(run.playerEntityId)).attributes}}
	func load_collection_presentations() -> Dictionary: return {"ok": true, "data": {"cards": []}}
	func read_deck_compartments() -> Dictionary: return {"ok": false}
	func activity_choices() -> Array: return []
	func recover_pending() -> bool: return true
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func settle() -> void:
	for _frame in 6: await process_frame
func _run() -> void:
	var source := FileAccess.get_file_as_string("res://scripts/ui/unified_loadout_screen.gd")
	check(not source.contains("FileAccess") and not source.contains("inscription_loadout_lab.json"), "run composition never loads offline examples")
	var session := Session.new()
	var screen = load("res://scenes/run_loadout.tscn").instantiate(); screen.session = session; root.add_child(screen)
	await settle()
	check(screen.controller.inscriptions == null and not screen.inscriptions_view.visible, "failed initial read cannot publish example equipment")
	check(screen.shell.recovery.visible, "read failure exposes canonical recovery")
	check(screen.find_child("LoadoutActorPicker", true, false) == null and screen.find_child("LoadoutScopePicker", true, false) == null, "run has no actor or inventory-scope selector")
	session.fail = false; await screen.controller.refresh(); await settle()
	check(screen.inscriptions_view.visible and screen.controller.inscriptions.source.actorId == "hero" and session.reads == ["hero", "hero"], "recovery reads only the current run player")
	check(screen.inscriptions_view._inventory.keys() == ["owned"] and not screen.inscriptions_view.preset_picker.visible, "only owned canonical items, without lab presets")
	screen._select_item("owned"); screen._select_slot("one"); await settle()
	check(session.previews == ["hero"] and session.commits == ["hero"] and session.assignments == {"one":"owned"}, "item then slot validates saves and rereads exactly once")
	check(screen.controller.inscriptions.snapshot().character.attributes[0].value == 777, "attributes reflect authoritative equipment")
	var reads := session.reads.size()
	screen._select_tab("relics"); screen._select_tab("inscriptions"); await settle()
	check(session.reads.size() == reads and session.commits.size() == 1, "tab inspection neither polls nor writes equipment")
	screen.controller.edit_inscription("clear"); await settle()
	check(session.assignments.is_empty() and not screen.controller.inscriptions.dirty(), "clear automatically persists empty equipment")
	check(screen.controller.inscriptions.snapshot().character.attributes[0].value == 0, "clear rereads authoritative attributes")
	session.invalid_preview = true
	screen._select_item("owned"); screen._select_slot("one"); await settle()
	check(session.commits.size() == 2 and not screen.controller.inscriptions.dirty() and session.assignments.is_empty(), "rejected validation restores confirmed equipment without an extra commit")
	session.invalid_preview = false; session.run.sequence += 1; session.changed.emit(); await settle()
	check(screen.controller.inscriptions.source.runSequence == session.run.sequence, "version changes refresh current canonical projection")
	session.foreign = true; await screen.controller.refresh(); await settle()
	check(screen.controller.inscriptions == null and not screen.inscriptions_view.visible, "foreign actor response cannot be shown or equipped")
	session.foreign = false; session.held = true; screen.controller.refresh(); await process_frame
	session.run.playerEntityId = "other"; session.release.emit(); await settle()
	check(not screen.controller.current() and not screen.inscriptions_view.visible, "late read after character switch cannot publish previous inventory")
	screen.controller.edit_inscription("clear"); check(session.commits.size() == 2, "changed actor cannot redirect a write")
	screen.queue_free(); await settle()
	session = Session.new(); session.fail = false; session.held = true
	screen = load("res://scenes/run_loadout.tscn").instantiate(); screen.session = session; root.add_child(screen); await process_frame
	session.run.runId = "another-run"; session.release.emit(); await settle()
	check(not screen.inscriptions_view.visible and session.commits.is_empty(), "late response cannot expose another run inventory")
	screen.queue_free(); await settle()
	session = Session.new(); session.fail = false; session.hold_preview = true
	screen = load("res://scenes/run_loadout.tscn").instantiate(); screen.session = session; root.add_child(screen); await settle()
	screen._select_item("owned"); screen._select_slot("one"); await process_frame
	check(screen.controller.busy and screen.inscriptions_view.visible and screen.shell.attributes_view._rows.has("critical_chance"), "pending validation retains confirmed visual equipment and attributes")
	check(screen.inscriptions_view.find_child("LoadoutClearButton", true, false).disabled, "equipment input is locked during validation")
	screen.controller.edit_inscription("clear"); check(session.commits.is_empty(), "second intent cannot race pending validation")
	session.run.playerEntityId = "other"; session.release.emit(); await settle()
	check(session.commits.is_empty() and not screen.inscriptions_view.visible, "late validation cannot equip after an actor switch")
	screen.queue_free(); await settle()
	var lab = load("res://scenes/unified_loadout_lab.tscn").instantiate(); root.add_child(lab); await settle()
	check(lab.inscriptions_view._inventory.size() == 15 and lab.shell.scene_file_path == "res://scenes/ui/components/loadout/loadout_page.tscn", "separate offline laboratory shares production shell and item components")
	lab.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
