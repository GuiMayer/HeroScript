extends SceneTree
var failures := 0
class FakeSession extends RefCounted:
	var busy := false
	var calls: Array = []
	var assessment: Dictionary = {}
	func preview_transformation(_choice: Dictionary) -> Dictionary: return assessment.duplicate(true)
	func submit_activity(choice: Dictionary) -> bool:
		calls.append(choice.duplicate(true))
		return false
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func _run() -> void:
	root.get_node("I18n").set_locale("en")
	var session = root.get_node("GameSession")
	session._run = {"runId": "activity", "sequence": 1, "lifecycle": "Active", "currentNodeId": "a", "map": {"nodes": [
		{"nodeId": "a", "activity": {"type": "Encounter"}, "nextNodeIds": ["b"]},
		{"nodeId": "b", "activity": {"type": "Preparation"}, "nextNodeIds": []}]}, "resources": {"gold": {"current": 40}}}
	session._available_commands = [{"type": "START_ENCOUNTER", "validPayload": {}}]
	var router = load("res://main.tscn").instantiate()
	root.add_child(router)
	router.show_activity()
	await settle()
	var screen = router.host.get_child(0)
	var fake := FakeSession.new()
	screen.controller.dispose()
	screen.controller = load("res://scripts/ui/controllers/activity_controller.gd").new(fake)
	screen.controller.bind(session.run, session.activity_choices())
	var first_id: int = screen.view.choice_buttons[0].get_instance_id()
	session._run.resources.gold.current = 25
	session._run.sequence = 2
	session._available_commands.append({"type": "RESOLVE_NODE", "validPayload": {}})
	screen.refresh_state(router.presentation)
	await settle()
	check(screen.find_child("RouteView", true, false) == null, "activity presentation has no second embedded map")
	check(screen.view.choice_buttons.any(func(button): return button.get_instance_id() == first_id), "existing option retains identity")
	var button = screen.view.choice_buttons.filter(func(option): return option.text.contains("COMPLETE"))[0]
	button.pressed.emit()
	await settle()
	check(fake.calls.size() == 1 and fake.calls[0].type == "RESOLVE_NODE", "new reconciled option delegates to live controller, not staging view")
	var before: int = load("res://scripts/ui/view_reconciler.gd").replaced
	screen.refresh_state(router.presentation)
	await settle()
	check(load("res://scripts/ui/view_reconciler.gd").replaced == before, "unchanged activity does not build staging view")
	check(screen.presenter.current_node().nodeId == "a", "activity projection remains on the published current node")
	session._run.sequence += 1
	screen.refresh_state(router.presentation)
	await settle()
	check(load("res://scripts/ui/view_reconciler.gd").replaced == before and screen.view.get_node("ActivityHeader").find_child("RunTitle", true, false).tooltip_text.contains(str(session._run.sequence)), "sequence-only update preserves the tree but refreshes diagnostic context")
	session._run.shops = [{"shopInstanceId": "shop", "items": [{"itemId": "offer", "cardId": "defend", "costs": [{"resourceId": "gold", "amount": 30}]}]}]
	session._available_commands = [{"type": "BUY_SHOP_ITEM", "validPayload": {"shopInstanceId": "shop", "itemIds": ["offer"]}}]
	screen.refresh_state(router.presentation)
	await settle()
	var offer_card = screen.view.choice_buttons.filter(func(item): return item is CardView)[0]
	check(not offer_card.chosen and not offer_card.button_pressed and not offer_card.has_focus(), "card offers open without selection or focus outline")
	check(offer_card.get_meta("skip_initial_focus", false) and not offer_card.has_meta("initial_focus"), "offered cards never request automatic initial focus")
	var focus_graph = load("res://scripts/ui/focus_navigation.gd")
	focus_graph.wire(offer_card, true)
	check(not offer_card.has_focus(), "even a card-only offer does not gain an automatic outline")
	offer_card.grab_focus()
	check(offer_card.has_focus() and not offer_card.chosen, "explicit keyboard focus remains available without selecting an offer")
	check(not offer_card.model.costsKnown and str(offer_card.model.cost).is_empty() and screen.find_child("AcquisitionPrice", true, false).text.contains("30"), "acquisition price stays outside the face; missing play cost is unknown, never invented")
	var facts := {"identity": {"kind": "Instance", "definitionId": "w1_strike", "cardInstanceId": "first"}, "rarity": "Common", "costs": [],
		"effects": [{"effectPath": "main", "definition": {"type": "DAMAGE", "target": "TARGET", "targetResource": "health", "flatValue": 999},
		"parameters": [{"parameter": "Amount", "values": {"original": 6, "composedBase": 6}}]}]}
	var neighbor: Dictionary = facts.duplicate(true)
	neighbor.identity.cardInstanceId = "second"
	neighbor.effects[0].parameters[0].values.composedBase = 30
	session._card_presentations = {"runId": session.run.runId, "runSequence": session.run.sequence, "contentRevision": session.run.get("contentRevision", ""), "cards": [neighbor, facts]}
	session._available_commands = [{"type": "UPGRADE_CARD", "validPayload": {"options": [
		{"cardInstanceId": "first", "cardDefinitionId": "w1_strike", "upgradeId": "w1_ranked_edge", "costs": []}]}}]
	screen.refresh_state(router.presentation)
	await settle()
	offer_card = screen.view.choice_buttons.filter(func(item): return item is CardView)[0]
	check(offer_card.model.has("presentationFacts") and offer_card.model.summary.contains("6"), "forge card displays canonical effects instead of the upgrade name or placeholder")
	check(offer_card.model.presentationFacts.identity.cardInstanceId == "first", "forge binds the exact card instance, never a same-definition neighbor")
	var after: Dictionary = facts.duplicate(true)
	after.effects[0].parameters[0].values.composedBase = 9
	fake.assessment = {"isCompatible": true, "beforePresentation": facts, "afterPresentation": after}
	var models: Array = screen.presenter.transformation_models(fake.assessment)
	check(models.size() == 2 and models[0].summary.contains("6") and models[1].summary.contains("9"), "upgrade comparison uses published before/after quantities, not raw definition values")
	check(screen.presenter.transformation_models({"before": {"components": []}, "after": {"components": []}}).is_empty(), "raw effective definitions cannot masquerade as canonical presentation facts")
	screen.controller.bind(session.run, session.activity_choices())
	await screen._confirm_choice(session.activity_choices()[0], screen.presenter.offer(session.activity_choices()[0]), "UPGRADE")
	await settle()
	var dialogs: Array = screen.find_children("*", "ConfirmationDialog", true, false)
	check(dialogs.size() == 1, "upgrade opens a confirmation rather than applying immediately")
	var comparison_cards: Array = dialogs[0].find_children("*", "Button", true, false).filter(func(item): return item is CardView)
	check(comparison_cards.size() == 2 and not dialogs[0].get_ok_button().disabled, "upgrade confirmation shows two real card components with effects")
	check(comparison_cards.all(func(card): return not card.chosen and not card.has_focus()), "comparison cards are not auto-selected")
	check(dialogs[0].size.x <= root.size.x and dialogs[0].size.y <= root.size.y, "comparison fits viewport")
	dialogs[0].hide()
	await settle()
	var item := {"itemDefinitionId": "fire", "presentation": {"name": "Fire imprint", "description": "Apply 1 Scorch."}}
	session._modifier_items = {"runId": session.run.runId, "runSequence": session.run.sequence, "catalog": [item]}
	session._available_commands = [{"type": "ACQUIRE_CARD_MODIFIER_ITEM", "validPayload": {"itemDefinitionIds": ["fire"]}}]
	screen.refresh_state(router.presentation)
	await settle()
	check(screen.find_children("*", "Label", true, false).any(func(label): return label.text == "Apply 1 Scorch."), "modifier effect is visible in the offer without hovering")
	var item_presenter = load("res://scripts/presentation/activity_presenter.gd").new({}, [], {}, root.get_node("I18n"), [],
		{"items": [{"definition": item, "instance": {"itemInstanceId": "owned"}}]})
	var applied: Dictionary = item_presenter.offer({"type": "APPLY_CARD_MODIFIER_ITEM", "subjectId": "owned", "payload": {"itemInstanceId": "owned", "cardInstanceId": "first"}})
	check(applied.name == "Fire imprint" and applied.summary == "Apply 1 Scorch.", "apply confirmation resolves the item's own description, not its instance ID")
	router.queue_free()
	await settle()
	root.get_node("GameAudio").shutdown()
	quit(0 if failures == 0 else 1)
