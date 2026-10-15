extends Control
## One face-aware resolver for hover, click and tooltip; no gameplay authority.
var hand
var _tooltip_id := ""
var _live_tooltip: WeakRef
var _tooltip_model: Dictionary = {}
const TOOLTIP_TOKEN := " "
func _gui_input(event: InputEvent) -> void: hand.pointer_input(event)
func follow_card(id: String) -> void:
	var card = hand.card_for(id)
	var available: bool = not hand.read_only and not hand.reading_modal and not hand.interaction.dragging and hand._pickable(id)
	_tooltip_id = id if available else ""
	var detail = _live_tooltip.get_ref() if _live_tooltip != null else null
	if not is_instance_valid(detail): return
	if not available:
		detail.hide()
		return
	if _tooltip_model != card.model or not detail.visible:
		_tooltip_model = card.model.duplicate(true)
		detail.configure(card.model, card.visual_style, detail.DisplayMode.TOOLTIP)
		detail.show()
func _get_tooltip(at_position: Vector2) -> String:
	follow_card(hand.pick_global(get_global_transform() * at_position) if hand.interaction.pointer_active else hand.interaction.active_id())
	# A stable token prevents native hide/re-delay when crossing cards in this
	# shared hit surface. The live passive panel holds the actual accessible text.
	return TOOLTIP_TOKEN if not _tooltip_id.is_empty() else ""
func _make_custom_tooltip(for_text: String) -> Object:
	var card = hand.card_for(_tooltip_id)
	if for_text != TOOLTIP_TOKEN or not hand._pickable(_tooltip_id): return null
	var detail = card._make_custom_tooltip(card.tooltip_text)
	if detail != null:
		_live_tooltip = weakref(detail)
		_tooltip_model = card.model.duplicate(true)
	return detail
