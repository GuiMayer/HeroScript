class_name HandCardReconciler
extends RefCounted
## Reconciles card slots and CardView instances by identity without recreating surviving cards.

const SlotScene = preload("res://scenes/ui/components/hand_card_slot.tscn")


static func reconcile_cards(
	models: Array,
	cards: Dictionary,
	slots: Dictionary,
	transit: Dictionary,
	entries: Dictionary,
	surface: Control,
	visual_context,
	entry_duration: float,
	inside_tree: bool,
	on_pressed: Callable,
	on_focus_entered: Callable,
	on_focus_exited: Callable
) -> Dictionary:
	var incoming: Array[String] = []
	var geometry_changed := false
	for model in models:
		var id := str(model.get("cardInstanceId", model.get("labId", "")))
		assert(not id.is_empty() and id not in incoming, "Hand models need unique instance IDs")
		incoming.append(id)
		if not cards.has(id):
			var card := CardView.create(visual_context)
			card.name = "HandCard_" + id.validate_node_name()
			card.animate_entry = false
			card.hand_managed = true
			card.toggle_mode = true
			card.mouse_filter = Control.MOUSE_FILTER_IGNORE
			card.configure(model)
			var slot: Control = SlotScene.instantiate()
			slot.name = "Slot_" + id.validate_node_name()
			slot.mouse_filter = Control.MOUSE_FILTER_IGNORE
			slot.add_child(card)
			surface.add_child(slot)
			cards[id] = card
			slots[id] = slot
			card.pressed.connect(func(): on_pressed.call(id, card))
			card.focus_entered.connect(func(): on_focus_entered.call(id))
			card.focus_exited.connect(func(): on_focus_exited.call(id))
			if transit.has(id):
				card.hide()
			elif inside_tree and entry_duration > 0:
				card.modulate.a = 0
				entries[id] = card.create_tween()
				entries[id].tween_property(card, "modulate:a", 1.0, entry_duration)
		else:
			var card: CardView = cards[id]
			var before := card.custom_minimum_size
			card.configure(model)
			geometry_changed = geometry_changed or before != card.custom_minimum_size
	return {
		"incoming": incoming,
		"geometry_changed": geometry_changed,
	}
