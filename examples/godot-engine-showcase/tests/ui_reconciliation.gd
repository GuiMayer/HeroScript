extends SceneTree
var failures := 0
class GatewayFixture extends RefCounted:
	var inspections := 0
	func inspect_hand(_combat: String, _actor: String, _targets: Array) -> Dictionary:
		inspections += 1
		return {"ok": true, "data": {"cards": []}}
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func _run() -> void:
	var session = root.get_node("GameSession")
	var fixture := GatewayFixture.new()
	session.configure(fixture)
	session._run = {"runId": "stable", "sequence": 1}
	session._combat = {"combatId": "combat", "activeActorId": "hero", "actors": [
		{"instanceId": "hero", "resources": {"health": {"current": 10, "minimum": 0, "maximum": 20}}, "statuses": []}]}
	session._card_zones = {"zones": [{"zoneId": "play", "allowsCardPlay": true, "contentsVisible": true, "count": 1, "cards": [{"cardInstanceId": "c", "definitionId": "defend"}]}]}
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	router.show_combat()
	await settle()
	var screen = router.host.get_child(0)
	var ids: Array = [screen.view.header_view.get_instance_id(), screen.view.battlefield_view.get_instance_id(), screen.view.turn_view.get_instance_id(),
		screen.view.character_sidebar.get_instance_id(), screen.view.hand.get_instance_id(), screen.view.target_buttons.hero.get_instance_id()]
	var reconciler = load("res://scripts/ui/view_reconciler.gd")
	var before: int = reconciler.replaced
	screen.refresh_state(router.presentation)
	await settle()
	check(reconciler.replaced == before and fixture.inspections == 1, "unchanged snapshot has no replacements or inspection requests")
	session._combat.actors[0].resources.health.current = 7
	session._run.sequence = 2
	screen.refresh_state(router.presentation)
	await settle()
	var after: Array = [screen.view.header_view.get_instance_id(), screen.view.battlefield_view.get_instance_id(), screen.view.turn_view.get_instance_id(),
		screen.view.character_sidebar.get_instance_id(), screen.view.hand.get_instance_id(), screen.view.target_buttons.hero.get_instance_id()]
	check(ids == after, "resource update retains header battlefield footer sidebar hand and target identity")
	var label: Array = screen.view.battlefield_view.panels.hero.find_children("*", "Label", true, false).filter(func(node): return node.text == "7/20")
	check(not label.is_empty(), "retained actor renders the current resource")
	var inspection_count := fixture.inspections
	root.get_node("I18n").set_locale("pt_BR")
	await settle()
	check(fixture.inspections == inspection_count, "locale rebuild reuses versioned inspection")
	router.queue_free()
	await settle()
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
