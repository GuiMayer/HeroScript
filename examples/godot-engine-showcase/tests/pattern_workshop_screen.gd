extends SceneTree
class Fixture extends RefCounted:
	var busy := false
	var synchronized := true
	var run := {}
	var choices: Array = []
	var submitted: Array = []
	func activity_choices() -> Array: return choices.duplicate(true)
	func submit_activity(choice: Dictionary) -> bool: submitted.append(choice); return false
var failures := 0
func _init() -> void: call_deferred("run")
func check(value: bool, message: String) -> void:
	print("[PASS] " if value else "[FAIL] ", message)
	if not value: failures += 1
func run() -> void:
	var session = root.get_node("GameSession")
	session._run = {"settingId": "prototype-test", "runId": "r", "sequence": 1, "currentNodeId": "workshop", "map": {"nodes": [{"nodeId": "workshop", "activity": {"type": "CardUpgrade", "parameters": {"modifierItemIds": ["split"]}}, "nextNodeIds": []}]}, "resources": {"parts": {"current": 3}}}
	session._available_commands = [{"type": "ACQUIRE_CARD_MODIFIER_ITEM", "validPayload": {"itemDefinitionIds": ["split"], "costs": [{"resourceId": "parts", "amount": 3}]}}, {"type": "RESOLVE_NODE", "validPayload": {"currentNodeId": "workshop"}}]
	session._modifier_items = {"runId": "r", "runSequence": 1, "catalog": [{"itemDefinitionId": "split", "presentation": {"name": "Split", "description": "Three hits."}}]}
	session.synchronized = true
	var router = load("res://main.tscn").instantiate(); root.add_child(router); router.open_game()
	for i in 10: await process_frame
	var screen = router.host.get_child(0)
	check(router.current_screen == "activity" and screen.workshop.visible and not screen.view.visible, "workshop is rendered as full activity")
	var offer = screen.workshop.get_node("ContentScroll/Content/Offers/Offer1/Column")
	var acquire: Button = offer.get_node("Acquire")
	acquire.grab_focus(); await process_frame
	acquire.pressed.emit(); await process_frame
	check(screen.workshop.get_node("PurchaseConfirm").visible, "purchase confirmation is displayed")
	screen.workshop.get_node("PurchaseConfirm").hide()
	for i in 4: await process_frame
	check(acquire.has_focus(), "cancelling purchase returns keyboard focus to the same action")

	var fake := Fixture.new(); fake.run = session.run; fake.choices = session.activity_choices()
	screen.controller.dispose(); screen.controller = preload("res://scripts/ui/controllers/activity_controller.gd").new(fake); screen.controller.bind(fake.run, fake.choices)
	await screen._workshop_resolve()
	check(fake.submitted.size() == 1 and fake.submitted[0].type == "RESOLVE_NODE", "leaving without purchase explicitly submits the published completion")
	fake.submitted.clear()
	router.show_map()
	for i in 10: await process_frame
	check(router.current_screen == "map" and fake.submitted.is_empty(), "visiting map does not resolve or purchase")
	var map_view = router.host.get_child(0).view
	check(map_view.find_child("MapResumeWorkshop", true, false).visible, "map offers return to workshop")
	check(map_view.find_children("MapAction_ACQUIRE_CARD_MODIFIER_ITEM*", "Button", true, false).is_empty(), "map does not duplicate workshop purchase actions")
	check(not map_view.find_child("MapResumeRun", true, false).visible, "map shows one clear return to workshop")
	router.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
