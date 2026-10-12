extends SceneTree
var failures := 0
class Uncontracted extends Control:
	var internal_value := "old"
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 8: await process_frame
func _run() -> void:
	var context = load("res://scripts/ui/visual_context.gd").from_nodes(root.get_node("Preferences"), root.get_node("I18n"))
	var actor := {"instanceId": "same-instance", "definitionId": "spire_wisp", "resources": {}, "statuses": []}
	var panel = load("res://scripts/ui/actor_panel.gd").new(context)
	panel.setup(actor, true, {}, "", false, false); root.add_child(panel); await settle()
	var portrait: Control = panel.portrait_view
	actor.definitionId = "spire_sentinel"
	panel.setup(actor, true, {}, "", false, false); await settle()
	check(panel.portrait_view == portrait and portrait.slot_key == "actor_sentinel", "actor identity retains portrait node while changing visual definition")
	for _repeat in 3:
		context.changed.emit(); await settle()
		check(portrait.slot_key == "actor_sentinel", "context update never restores old actor art")
	var route = load("res://scripts/ui/route_view.gd").new()
	var nodes := [{"id": "a", "name": "Stop", "current": false, "resolved": false, "kind": "Encounter", "next": [], "risk": "High", "travel": {"type": "ADVANCE_NODE", "payload": {"targetNodeId": "a"}}}]
	root.add_child(route); route.setup(nodes)
	var button: Button = route.buttons.a
	check(button.get_theme_color("font_color") == AppTheme.EMBER, "high risk receives warning color")
	nodes[0].risk = "Normal"; route.setup(nodes)
	AppTheme.apply_view_preferences(route)
	check(route.buttons.a == button and button.get_theme_color("font_color") != AppTheme.EMBER, "risk normalization clears old color including canonical style cache")
	nodes[0].risk = "High"; route.setup(nodes)
	check(button.get_theme_color("font_color") == AppTheme.EMBER, "risk can return to High without replacing node")
	var calls: Array = []; route.inspection_requested.connect(func(id): calls.append(id))
	nodes[0].travel.payload.targetNodeId = "updated"; route.setup(nodes); button.pressed.emit()
	check(calls == ["a"] and button.get_meta("travel").payload.targetNodeId == "updated", "retained map node inspects its ID and stores the latest published travel data")
	var model: Dictionary = portrait.model; model.contentId = "spire_wisp"
	check(portrait.model.contentId == "spire_sentinel", "art model is defensively copied")
	var next_context = load("res://scripts/ui/visual_context.gd").new(context.translator)
	var incoming = load("res://scripts/ui/actor_panel.gd").new(next_context)
	incoming.setup(actor, true, {}, "", false, false)
	panel.reconcile_from(incoming); incoming.free(); await settle()
	check(panel.portrait_view == portrait and not context.changed.has_connections() and next_context.changed.has_connections(), "context replacement rebinds live art without stale subscriptions")
	var parent := Control.new(); var desired := Control.new()
	var old := Uncontracted.new(); old.name = "Stateful"
	var fresh := Uncontracted.new(); fresh.name = "Stateful"; fresh.internal_value = "new"
	parent.add_child(old); desired.add_child(fresh)
	load("res://scripts/ui/view_reconciler.gd").children(parent, desired)
	check(parent.get_child(0) == fresh and parent.get_child(0).internal_value == "new", "uncontracted scripted node is replaced instead of partially patching private state")
	parent.free(); desired.free()
	panel.queue_free(); route.queue_free(); await settle()
	check(not context.changed.has_connections() and not next_context.changed.has_connections(), "destroyed art detaches its visual context subscription")
	root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
