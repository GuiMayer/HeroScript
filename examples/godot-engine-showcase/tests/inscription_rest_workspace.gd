extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
class Transport extends RefCounted:
	var requests: Array = []
	func request(method, path, body = null) -> Dictionary:
		requests.append([method, path, body]); return {"ok": true, "data": {}}
func sample(actor := "alice", inventory := "actor:alice", sequence := 2) -> Dictionary:
	return {"runId": "run-one", "runSequence": sequence, "expectedStep": 5, "contentRevision": "revision",
		"actorId": actor, "actorIds": ["alice", "bob"], "inventoryId": inventory, "canEquip": true, "usedCapacity": 0,
		"policy": {"inventoryScope": "Actor", "capacity": 12, "slots": [{"slotId": "one"}, {"slotId": "two"}]},
		"items": [{"instance": {"instanceId": "one-item"}, "definition": {"inscriptionId": "focus", "equipmentCost": 3,
			"presentation": {"name": "Focus"}}, "equippedActorId": null}], "assignments": {},
		"attributes": {"attributes": [{"attributeId": "critical", "displayName": "Critical chance", "unit": "percent", "baseValue": 25, "effectiveValue": 25}]}}
func _run() -> void:
	var workspace = preload("res://scripts/application/loadout_workspace.gd").new()
	var source := sample()
	check(workspace.accept(source), "REST snapshot accepted without running local stat math")
	workspace.source.attributes.attributes[0].unit = "%"
	check(workspace.snapshot().character.attributes[0].format == "percent", "canonical percent unit is preserved in the character panel")
	workspace.select_item("one-item"); workspace.select_slot("one"); workspace.stage_selected()
	check(workspace.dirty() and not workspace.can_confirm() and not workspace.snapshot().attributesReady, "draft hides uncomputed attributes until authoritative preview")
	check(source.assignments.is_empty(), "draft never mutates server snapshot")
	var version: int = workspace.generation
	var captured: Dictionary = workspace.assignments.duplicate()
	var after := source.duplicate(true); after.assignments = captured; after.usedCapacity = 3
	after.attributes.attributes[0].effectiveValue = 777
	check(workspace.accept_preview({"before": source, "after": after}, version, captured), "exact version preview accepted")
	check(workspace.snapshot().character.attributes[0].value == 777, "attributes are precisely the server projection, not sum of item values")
	check(workspace.can_confirm(), "confirmation enabled only with matching preview")
	workspace.select_slot("two"); workspace.stage_selected()
	check(not workspace.accept_preview({"before": source, "after": after}, version, captured), "late response after editing rejected")
	check(not workspace.can_confirm(), "edit invalidates old confirmation")
	var bob := sample("bob", "actor:bob")
	workspace.accept(bob)
	check(not workspace.accept_preview({"before": source, "after": after}, workspace.generation, {}), "response from another character/inventory rejected")
	check(workspace.assignments.is_empty() and workspace.selected_item.is_empty(), "character change clears local selection and draft")
	workspace.accept(sample("alice", "run:shared"))
	var shared_key = workspace.snapshot().contextKey
	workspace.accept(sample("bob", "run:shared"))
	check(workspace.snapshot().contextKey != shared_key, "shared inventory never shares draft/drag context between actors")
	workspace.source.canEquip = false
	check(not workspace.stage_drop("one-item", "one", ""), "closed equipment window cannot stage through drag")
	var unmaterialized := sample("bob", "actor:bob")
	unmaterialized.attributes = null
	unmaterialized.attributesUnavailableReason = "Character is not materialized in this context"
	workspace.accept(unmaterialized)
	check(not workspace.snapshot().attributesReady and workspace.snapshot().character.attributes.is_empty(), "unmaterialized actor never displays stale or fabricated attributes")
	check(workspace.snapshot().status == unmaterialized.attributesUnavailableReason, "unavailable attributes retain the server explanation")
	var transport := Transport.new()
	var gateway = preload("res://scripts/engine/engine_gateway.gd").new(transport)
	await gateway.read_loadout("run /one", "actor&two")
	await gateway.preview_loadout(source, {"one": "one-item"})
	check(transport.requests[0][1].contains("run%20%2Fone/loadout?actorId=actor%26two"), "REST adapter escapes run and actor identities")
	check(transport.requests[1][2].expectedSequence == 2 and transport.requests[1][2].expectedStep == 5, "preview transports captured sequence and step")
	var lab = load("res://scenes/inscription_loadout_lab.tscn").instantiate()
	root.add_child(lab)
	await process_frame
	lab._actor_picker.select(1); lab._select_visual()
	check(str(lab.draft.snapshot().items[0].instanceId).begins_with("second_"), "Actor example has separately owned identities")
	lab._scope_picker.select(1); lab._select_visual()
	check(lab.draft.snapshot().assignments.is_empty() and not str(lab.draft.snapshot().items[0].instanceId).begins_with("second_"), "Run example uses shared identities with independent equipment")
	var panel = lab.view.attributes_view
	panel.present({"name": "Other actor", "attributes": []})
	check(panel._rows.is_empty(), "switching to an empty projection clears previous character rows")
	lab._refresh()
	check(not panel._rows.is_empty(), "returning to a materialized actor restores its own schema")
	lab.queue_free(); await process_frame
	print("INSCRIPTION REST: ", failures, " failure(s)")
	quit(1 if failures else 0)
