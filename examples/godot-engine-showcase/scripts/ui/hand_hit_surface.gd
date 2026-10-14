extends Control
## One face-aware resolver for hover, click and tooltip; no gameplay authority.
var hand
var _tooltip_id := ""
func _gui_input(event: InputEvent) -> void: hand.pointer_input(event)
func _get_tooltip(at_position: Vector2) -> String:
	_tooltip_id = ""
	if hand.read_only or hand.reading_modal or hand.interaction.dragging: return ""
	_tooltip_id = hand.pick_global(get_global_transform() * at_position)
	var card = hand.card_for(_tooltip_id)
	return card.tooltip_text if is_instance_valid(card) else ""
func _make_custom_tooltip(for_text: String) -> Object:
	var card = hand.card_for(_tooltip_id)
	return card._make_custom_tooltip(for_text) if hand._pickable(_tooltip_id) and card.tooltip_text == for_text else null
