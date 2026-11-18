extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(value: bool, message: String) -> void:
	print("[PASS] " if value else "[FAIL] ", message)
	if not value: failures += 1
func run() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var run := {"settingId": "prototype-test", "runId": "test", "currentNodeId": "workshop", "resources": {"parts": {"current": 2}}, "map": {"resolvedNodeIds": [], "nodes": [{"nodeId": "workshop", "activity": {"type": "CardUpgrade", "parameters": {"modifierItemIds": ["split", "guard"], "costs": [{"resourceId": "parts", "amount": 3}]}}, "metadata": {"displayName": "Pattern workshop", "description": "Choose one modifier or leave."}, "nextNodeIds": []}]}}
	var commands := [{"type": "ACQUIRE_CARD_MODIFIER_ITEM", "validPayload": {"itemDefinitionIds": ["split", "guard"], "costs": [{"ResourceId": "parts", "Amount": 3}]}}, {"type": "RESOLVE_NODE", "validPayload": {"currentNodeId": "workshop"}}]
	var choices: Array = preload("res://scripts/application/activity_choices.gd").build(run, commands)
	var catalog := {"items": [], "catalog": [{"itemDefinitionId": "split", "presentation": {"name": "Split", "description": "Three hits."}}, {"itemDefinitionId": "guard", "presentation": {"name": "Guard", "description": "Gain block."}}]}
	var presenter = preload("res://scripts/presentation/activity_presenter.gd").new(run, choices, {}, context.translator, [], catalog)
	var route = preload("res://scripts/ui/gameplay_route_resolver.gd")
	check(route.resolve_route(run, {}, choices) == "activity", "MVP workshop opens a dedicated activity")
	var view = preload("res://scenes/ui/components/pattern_workshop.tscn").instantiate(); root.add_child(view)
	view.update_inventory(catalog, [], {}); view.present(run, choices, presenter, context, catalog, [], {})
	var first = view.get_node("ContentScroll/Content/Offers/Offer1/Column")
	check(first.get_node("Title").text == "Split" and first.get_node("Description").text == "Three hits." and first.get_node("Cost").text.contains("3") and first.get_node("Cost").text.to_lower().contains("parts"), "server cost displays its amount and resource")
	check(view.get_node("Balance").text.contains("2") and not first.get_node("Cost").text.contains("Balance unavailable"), "server cost is matched to the available balance")
	check(first.get_node("Acquire").disabled and first.get_node("Cost").text.contains("Insufficient resources"), "insufficient balance prevents purchase and explains why")
	run.resources.parts.current = 3
	view.present(run, choices, presenter, context, catalog, [], {})
	check(not first.get_node("Acquire").disabled, "affordable purchase becomes available")
	var camel_commands: Array = commands.duplicate(true); camel_commands[0].validPayload.costs = [{"resourceId": "parts", "amount": 3}]
	var camel_choices: Array = preload("res://scripts/application/activity_choices.gd").build(run, camel_commands)
	check(camel_choices[0].costs[0].resourceId == "parts" and camel_choices[0].costs[0].amount == 3, "camel-case costs remain supported")
	var no_price_run := run.duplicate(true); no_price_run.map.nodes[0].activity.parameters.erase("costs")
	var no_price_choices: Array = choices.duplicate(true); no_price_choices[0].erase("costs"); no_price_choices[1].erase("costs")
	var no_price_presenter = preload("res://scripts/presentation/activity_presenter.gd").new(no_price_run, no_price_choices, {}, context.translator, [], catalog)
	view.present(no_price_run, no_price_choices, no_price_presenter, context, catalog, [], {})
	check(first.get_node("Acquire").disabled and first.get_node("Cost").text == "Cost unavailable", "unknown purchase price cannot be confirmed")
	view.present(run, choices, presenter, context, catalog, [], {})

	view.present(run, choices, presenter, context, catalog, [], {}, false)
	check(first.get_node("Acquire").disabled and view.get_node("Footer/Continue").disabled, "unsynchronized workshop stays read-only")
	view.present(run, choices, presenter, context, catalog, [], {}, true)

	var emitted: Array = []
	view.purchase_requested.connect(func(ref): emitted.append(ref))
	first.get_node("Acquire").pressed.emit()
	check(view.get_node("PurchaseConfirm").visible and emitted.is_empty(), "purchase asks for confirmation before submitting")
	view.get_node("PurchaseConfirm").confirmed.emit()
	check(emitted == [presenter.option_reference(choices[0])], "confirmation emits the exact published offer")
	view.get_node("PurchaseConfirm").hide()
	view.get_node("ContentScroll/Content/Offers/Offer2/Column/Acquire").pressed.emit()
	check(view.get_node("PurchaseConfirm").visible and view.get_node("PurchaseConfirm").dialog_text.contains("Guard"), "second offer opens its own confirmation directly")
	view.get_node("PurchaseConfirm").confirmed.emit()
	check(emitted[-1] == presenter.option_reference(choices[1]), "second offer submits its own published reference")

	view.get_node("PurchaseConfirm").hide()
	var complete_choices: Array = choices.filter(func(c): return c.type == "RESOLVE_NODE")
	check(route.resolve_route(run, {}, complete_choices) == "activity", "completed workshop remains accessible until resolved")
	run["completedActivityNodeIds"] = ["workshop"]
	catalog.items = [{"instance": {"itemInstanceId": "new", "state": "Available", "acquisitionSource": "node:workshop", "acquisitionSequence": 2}, "definition": catalog.catalog[0], "cards": []}]
	view.present(run, complete_choices, presenter, context, catalog, [], {})
	check(view.get_node("Notice").text.contains("complete") and not view.get_node("ContentScroll/Content/Offers").visible, "completed workshop offers apply later and continue without another purchase")
	check(view.get_node("ContentScroll/Content/Acquired/Column/Title").text.contains("Split") and view.get_node("ContentScroll/Content/Acquired/Column/Status").text.contains("Not applied"), "completed workshop identifies acquired pattern and unapplied state")
	view.get_node("Footer/Manage").pressed.emit()
	check(view.get_node("ContentScroll/Content/Inventory").visible, "modifier inventory opens inside workshop")
	var applications: Array = []
	view.apply_requested.connect(func(ref): applications.append(ref))
	var sample: Dictionary = view.get_node("Compare/Content/Scroll/Comparison/Before/Card").preview_data
	var after: Dictionary = sample.duplicate(true); after["summary"] = "Changed effect"
	view.show_comparison("valid-reference", [sample, after])
	check(view.get_node("Compare").visible and applications.is_empty(), "preview opens without applying a pattern")
	check(view.get_node("Compare/Content/Description").text.contains("effects") and view.get_node("Compare").ok_button_text == "Apply pattern", "comparison identifies changed effects and explicit confirmation")
	view.get_node("Compare").confirmed.emit()
	check(applications == ["valid-reference"], "comparison confirmation applies exactly the selected reference")

	var alisyum_ids := ["obor_rune_echo_item", "obor_rune_ward_item", "obor_rune_pierce_item", "obor_rune_keen_item", "obor_rune_ember_item", "obor_rune_leech_item"]
	var alisyum_run := {"settingId": "alisyum", "runId": "alisyum-test", "currentNodeId": "obor_forge_1", "resources": {"parts": {"current": 3}}, "map": {"resolvedNodeIds": [], "nodes": [{"nodeId": "obor_forge_1", "activity": {"type": "CardUpgrade", "parameters": {"modifierItemIds": alisyum_ids, "costs": [{"resourceId": "parts", "amount": 3}]}}, "metadata": {"displayName": "Forja Rúnica de Obor", "description": "Escolha uma runa."}, "nextNodeIds": []}]}}
	var alisyum_commands := [{"type": "ACQUIRE_CARD_MODIFIER_ITEM", "validPayload": {"itemDefinitionIds": alisyum_ids, "costs": [{"ResourceId": "parts", "Amount": 3}]}}, {"type": "RESOLVE_NODE", "validPayload": {"currentNodeId": "obor_forge_1"}}]
	var alisyum_choices: Array = preload("res://scripts/application/activity_choices.gd").build(alisyum_run, alisyum_commands)
	var alisyum_catalog := {"items": [], "catalog": alisyum_ids.map(func(id): return {"itemDefinitionId": id, "presentation": {"name": id, "description": id}})}
	var alisyum_presenter = preload("res://scripts/presentation/activity_presenter.gd").new(alisyum_run, alisyum_choices, {}, context.translator, [], alisyum_catalog)
	check(route.is_mvp_workshop(alisyum_run) and route.resolve_route(alisyum_run, {}, alisyum_choices) == "activity", "alisyum workshop with 6 runes uses PatternWorkshop")
	var alisyum_view = preload("res://scenes/ui/components/pattern_workshop.tscn").instantiate(); root.add_child(alisyum_view)
	alisyum_view.update_inventory(alisyum_catalog, [], {}); alisyum_view.present(alisyum_run, alisyum_choices, alisyum_presenter, context, alisyum_catalog, [], {})
	check(alisyum_view.get_node("ContentScroll/Content/Offers").get_child_count() == 6 and alisyum_view.get_node("ContentScroll/Content/Offers/Offer6").visible, "workshop dynamically instantiates slots for all 6 Obor runes")
	check(alisyum_view.get_node("Header/Title").text == "Forja Rúnica de Obor", "Alisyum workshop keeps the published forge title")
	check(alisyum_view.get_node("ContentScroll").follow_focus, "keyboard focus scrolls all six offers into view")
	check(alisyum_view.get_node("ContentScroll/Content/Offers/Offer6/Column/Acquire").text == "Buy card rune", "forge distinguishes card runes from equipment runes")
	alisyum_view.queue_free()

	view.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
