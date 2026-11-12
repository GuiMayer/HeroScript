extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(value: bool, message: String) -> void:
	print("[PASS] " if value else "[FAIL] ", message)
	if not value: failures += 1
func run() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var run := {"settingId": "prototype-test", "runId": "test", "currentNodeId": "workshop", "resources": {"parts": {"current": 2}}, "map": {"resolvedNodeIds": [], "nodes": [{"nodeId": "workshop", "activity": {"type": "CardUpgrade", "parameters": {"modifierItemIds": ["split", "guard"], "costs": [{"resourceId": "parts", "amount": 3}]}}, "metadata": {"displayName": "Pattern workshop", "description": "Choose one modifier or leave."}, "nextNodeIds": []}]}}
	var commands := [{"type": "ACQUIRE_CARD_MODIFIER_ITEM", "validPayload": {"itemDefinitionIds": ["split", "guard"], "costs": [{"resourceId": "parts", "amount": 3}]}}, {"type": "RESOLVE_NODE", "validPayload": {"currentNodeId": "workshop"}}]
	var choices: Array = preload("res://scripts/application/activity_choices.gd").build(run, commands)
	var catalog := {"items": [], "catalog": [{"itemDefinitionId": "split", "presentation": {"name": "Split", "description": "Three hits."}}, {"itemDefinitionId": "guard", "presentation": {"name": "Guard", "description": "Gain block."}}]}
	var presenter = preload("res://scripts/presentation/activity_presenter.gd").new(run, choices, {}, context.translator, [], catalog)
	var route = preload("res://scripts/ui/gameplay_route_resolver.gd")
	check(route.resolve_route(run, {}, choices) == "activity", "MVP workshop opens a dedicated activity")
	var view = preload("res://scenes/ui/components/pattern_workshop.tscn").instantiate(); root.add_child(view)
	view.update_inventory(catalog, [], {}); view.present(run, choices, presenter, context, catalog, [], {})
	var first = view.get_node("ContentScroll/Content/Offers/Offer1/Column")
	check(first.get_node("Title").text == "Split" and first.get_node("Description").text == "Three hits." and first.get_node("Cost").text.contains("3"), "offer displays authored name, effect and price")
	first.get_node("Select").pressed.emit()
	check(view.get_node("Footer/Purchase").disabled and view.get_node("Footer/Selection").text == "Insufficient resources", "insufficient balance prevents purchase and explains why")
	run.resources.parts.current = 3
	view.present(run, choices, presenter, context, catalog, [], {})
	check(not view.get_node("Footer/Purchase").disabled, "affordable purchase becomes available")
	var emitted: Array = []
	view.purchase_requested.connect(func(ref): emitted.append(ref))
	view.get_node("Footer/Purchase").pressed.emit()
	check(view.get_node("PurchaseConfirm").visible and emitted.is_empty(), "purchase asks for confirmation before submitting")
	view.get_node("PurchaseConfirm").confirmed.emit()
	check(emitted == [presenter.option_reference(choices[0])], "confirmation emits the exact published offer")
	view.get_node("PurchaseConfirm").hide()
	var complete_choices: Array = choices.filter(func(c): return c.type == "RESOLVE_NODE")
	check(route.resolve_route(run, {}, complete_choices) == "activity", "completed workshop remains accessible until resolved")
	run["completedActivityNodeIds"] = ["workshop"]
	view.present(run, complete_choices, presenter, context, catalog, [], {})
	check(view.get_node("Notice").text.contains("complete") and not view.get_node("Footer/Purchase").visible, "completed workshop offers apply later and continue without another purchase")
	view.get_node("Footer/Manage").pressed.emit()
	check(view.get_node("ContentScroll/Content/Inventory").visible, "modifier inventory opens inside workshop")
	var applications: Array = []
	view.apply_requested.connect(func(ref): applications.append(ref))
	var sample: Dictionary = view.get_node("Compare/Content/Scroll/Comparison/Before/Card").preview_data
	view.show_comparison("valid-reference", [sample, sample])
	check(view.get_node("Compare").visible and applications.is_empty(), "preview opens without applying a pattern")
	view.get_node("Compare").confirmed.emit()
	check(applications == ["valid-reference"], "comparison confirmation applies exactly the selected reference")

	view.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
