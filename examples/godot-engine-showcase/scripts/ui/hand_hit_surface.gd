extends Control
## Stable pointer zones remain independent of moving faces and z order.
var hand
func _gui_input(event: InputEvent) -> void: hand.pointer_input(event)
func _get_tooltip(at_position: Vector2) -> String:
	if hand.read_only or hand.reading_modal or hand.interaction.dragging: return ""
	var card = hand.card_for(hand.pick(at_position + hand.scroll.position))
	return card.tooltip_text if is_instance_valid(card) else ""
func _make_custom_tooltip(for_text: String) -> Object:
	var card = hand.card_for(hand.interaction.hovered)
	return card._make_custom_tooltip(for_text) if is_instance_valid(card) else null
