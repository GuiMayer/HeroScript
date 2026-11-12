extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(value: bool, label: String) -> void:
	print("[PASS] " if value else "[FAIL] ", label)
	if not value: failures += 1
func run() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").new()
	var facts := {"identity": {"kind": "Instance", "definitionId": "strike", "cardInstanceId": "card"}, "faceValueMode": "ComposedBase", "rarity": "Common", "costs": [], "effects": [{"effectPath": "main", "definition": {"type": "DAMAGE", "targetResource": "health", "flatValue": 6}, "parameters": []}]}
	var item := {"instance": {"itemInstanceId": "unit", "state": "Available"}, "definition": {"itemDefinitionId": "fire", "presentation": {"name": "Fire imprint", "description": "Apply Scorch."}}, "cards": [{"cardInstanceId": "card", "compatible": true}, {"cardInstanceId": "unknown", "compatible": false, "reason": "No available slot"}]}
	var choices := [{"type": "APPLY_CARD_MODIFIER_ITEM", "subjectId": "unit", "payload": {"itemInstanceId": "unit", "cardInstanceId": "card"}}]
	var inventory = load("res://scenes/ui/components/pattern_workshop_inventory.tscn").instantiate(); root.add_child(inventory)
	inventory.configure({"items": [item]}, choices, [facts], {}, context)
	var items: ItemList = inventory.get_node("ItemsColumn/Items")
	var cards: ItemList = inventory.get_node("CardsColumn/Cards")
	check(items.item_count == 1 and cards.item_count == 0, "inventory starts with item selection")
	items.item_selected.emit(0)
	check(cards.item_count == 2 and not cards.is_item_disabled(0) and cards.is_item_disabled(1), "only engine-approved cards can be selected")
	cards.item_selected.emit(0)
	check(inventory.get_node("PreviewColumn/CardScroll/Card").visible and not inventory.get_node("PreviewColumn/Preview").disabled, "authored card component displays selected card")
	var sent: Array = []
	inventory.choice_requested.connect(func(ref): sent.append(ref))
	inventory.get_node("PreviewColumn/Preview").pressed.emit()
	check(sent == [preload("res://scripts/presentation/activity_presenter.gd").option_reference(choices[0])], "comparison requests only published card and item")
	inventory.configure({"items": [item]}, choices, [facts], {}, context, false)
	items.item_selected.emit(0); cards.item_selected.emit(0)
	check(inventory.get_node("PreviewColumn/Preview").disabled, "unsynchronized inventory remains read-only")
	inventory.queue_free(); await process_frame; root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
