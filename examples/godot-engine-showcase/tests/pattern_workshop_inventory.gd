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
	var cards = inventory.get_node("CardsColumn/Cards")
	check(items.item_count == 1 and cards.get_node("Slot1").visible and cards.get_node("Slot2").visible, "deck shows complete cards before selecting an item")
	items.item_selected.emit(0)
	check(cards.get_node("Slot1/Card").modulate == Color.WHITE and cards.get_node("Slot2/Status").text.contains("Unavailable"), "compatible and incompatible cards have distinct states")
	cards.get_node("Slot2/Card").pressed.emit()
	check(inventory.get_node("CardsColumn/Preview").disabled and inventory.get_node("Reason").visible and inventory.get_node("Reason/Message").text == "No available slot", "clicking an incompatible card explains the reason and cannot apply")
	inventory.get_node("Reason").hide()
	cards.get_node("Slot1/Card").pressed.emit()
	check(not inventory.get_node("CardsColumn/Preview").disabled, "compatible card can be selected")
	var sent: Array = []
	inventory.choice_requested.connect(func(ref): sent.append(ref))
	inventory.get_node("CardsColumn/Preview").pressed.emit()
	check(sent == [preload("res://scripts/presentation/activity_presenter.gd").option_reference(choices[0])], "comparison requests only published card and item")
	inventory.configure({"items": [item]}, choices, [facts, duplicate], {}, context, false)
	items.item_selected.emit(0); cards.get_node("Slot1/Card").pressed.emit()
	check(inventory.get_node("CardsColumn/Preview").disabled, "unsynchronized inventory remains read-only")
	var applied: Dictionary = item.duplicate(true); applied.instance.state = "Bound"
	inventory.configure({"items": [applied]}, choices, [facts, duplicate], {}, context)
	check(items.get_item_text(0).contains("Applied"), "bound item exposes its state")
	items.item_selected.emit(0); cards.get_node("Slot1/Card").pressed.emit()
	check(inventory.get_node("CardsColumn/Preview").disabled, "bound item cannot be applied again")
	var many: Array = []
	for i in 9:
		var copy: Dictionary = facts.duplicate(true); copy.identity.cardInstanceId = "copy%d" % i; many.append(copy)
	inventory.configure({"items": []}, [], many, {}, context)
	check(inventory.get_node("CardsColumn/Paging/Next").visible and not inventory.get_node("CardsColumn/Paging/Next").disabled, "large decks have another page")
	inventory.get_node("CardsColumn/Paging/Next").pressed.emit()
	check(cards.get_node("Slot1").visible and not cards.get_node("Slot2").visible and inventory.get_node("CardsColumn/Paging/Page").text == "2 / 2", "remaining cards are accessible on the next page")

	inventory.queue_free(); await process_frame; root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
