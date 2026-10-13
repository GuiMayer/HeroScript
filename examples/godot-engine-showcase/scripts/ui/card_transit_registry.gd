extends RefCounted
## One visual per instance for the lifetime of a published presentation.
var hand
var overlay: Control
var context
var _records: Dictionary = {}
func _init(hand_view, effects: Control, visual_context) -> void:
	hand = hand_view; overlay = effects; context = visual_context
func acquire(id: String, model: Dictionary, source_visible: bool, start: Vector2) -> Control:
	if not id.is_empty() and _records.has(id) and is_instance_valid(_records[id]): return _records[id]
	var card = hand.card_for(id) if hand != null else null
	if is_instance_valid(card): hand.begin_transit(id)
	var ghost: Control
	if source_visible and not model.is_empty():
		ghost = CardView.new(context); ghost.animate_entry = false; ghost.hand_managed = true
		ghost.configure(model); ghost.size = context.card_style.effective_size(context.text_scale)
	else:
		ghost = Panel.new(); ghost.size = context.card_style.effective_size(context.text_scale)
		ghost.add_theme_stylebox_override("panel", context.styles.box(context.styles.PANEL, 6, context.styles.INK, 2))
	ghost.name = "CardTransit"; ghost.mouse_filter = Control.MOUSE_FILTER_IGNORE
	ghost.focus_mode = Control.FOCUS_NONE; ghost.process_mode = Node.PROCESS_MODE_PAUSABLE
	overlay.add_child(ghost); ghost.pivot_offset = ghost.size * .5
	ghost.global_position = start - ghost.size * .5; ghost.z_index = 100
	if is_instance_valid(card):
		ghost.size = card.size; ghost.pivot_offset = card.size * .5
		ghost.global_position = card.global_position; ghost.rotation = card.rotation
	if not id.is_empty(): _records[id] = ghost
	return ghost
func release(id: String, ghost: Control, reveal_card := false) -> void:
	if _records.get(id) == ghost: _records.erase(id)
	if is_instance_valid(ghost): ghost.queue_free()
	if hand != null and is_instance_valid(hand): hand.finish_transit(id, reveal_card)
func dispose() -> void:
	for id in _records.keys(): release(id, _records[id])
	_records.clear()
