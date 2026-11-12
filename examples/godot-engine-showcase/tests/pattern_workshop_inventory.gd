extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func run() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var facts := {"identity": {"kind": "Instance", "definitionId": "strike", "cardInstanceId": "card"}, "faceValueMode": "ComposedBase", "rarity": "Common", "costs": [], "effects": [{"effectPath": "main", "definition": {"type": "DAMAGE", "targetResource": "health", "flatValue": 6}, "parameters": []}]}
	var item := {"instance": {"itemInstanceId": "unit", "state": "Available"}, "definition": {"itemDefinitionId": "fire", "presentation": {"name": "Fire imprint", "description": "Apply Scorch."}}, "cards": [{"cardInstanceId": "card", "compatible": true}, {"cardInstanceId": "card2", "compatible": false, "reason": "No available slot"}]}
	var duplicate: Dictionary = facts.duplicate(true); duplicate.identity.cardInstanceId = "card2"
	var choices := [{"type": "APPLY_CARD_MODIFIER_ITEM", "subjectId": "unit", "payload": {"itemInstanceId": "unit", "cardInstanceId": "card"}}]
	var inventory = load("res://scenes/ui/components/pattern_workshop_inventory.tscn").instantiate(); root.add_child(inventory)
	inventory.configure({"items": [item]}, choices, [facts, duplicate], {}, context)
	var items: ItemList = inventory.get_node("ItemsColumn/Items")
	var cards: ItemList = inventory.get_node("CardsColumn/Cards")
	check(items.item_count == 1 and cards.item_count == 0, "inventory starts with item selection")
	items.item_selected.emit(0)
	check(cards.item_count == 2 and cards.get_item_text(0).contains("Copy 1 of 2") and cards.get_item_text(1).contains("Copy 2 of 2"), "same-name cards are distinguished by visible copy order")
	cards.item_selected.emit(1)
	check(inventory.get_node("PreviewColumn/Preview").disabled and inventory.get_node("CardsColumn/Hint").text == "No available slot", "incompatible card remains inspectable with a visible reason and cannot be applied")
	cards.item_selected.emit(0)
	check(inventory.get_node("PreviewColumn/CardScroll/Card").visible and not inventory.get_node("PreviewColumn/Preview").disabled, "authored card component displays selected card")
	var sent: Array = []
	inventory.choice_requested.connect(func(ref): sent.append(ref))
	inventory.get_node("PreviewColumn/Preview").pressed.emit()
	check(sent == [preload("res://scripts/presentation/activity_presenter.gd").option_reference(choices[0])], "comparison requests only published card and item")
	inventory.configure({"items": [item]}, choices, [facts, duplicate], {}, context, false)
	items.item_selected.emit(0); cards.item_selected.emit(0)
	check(inventory.get_node("PreviewColumn/Preview").disabled, "unsynchronized inventory remains read-only")
	var applied: Dictionary = item.duplicate(true); applied.instance.state = "Bound"
	inventory.configure({"items": [applied]}, choices, [facts, duplicate], {}, context)
	check(items.get_item_text(0).contains("Applied"), "bound item exposes its state without relying on disabled color")
	items.item_selected.emit(0); cards.item_selected.emit(0)
	check(inventory.get_node("PreviewColumn/Preview").disabled, "bound item cannot be applied again even if a stale choice is present")

	inventory.queue_free(); await process_frame; root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
