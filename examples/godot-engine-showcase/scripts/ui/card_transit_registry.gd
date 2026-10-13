extends RefCounted
## One visual per instance for the lifetime of a published presentation.
var hand
var overlay: Control
var context
var _records: Dictionary = {}
var _staged: Dictionary = {}
func mark_staged(id: String) -> void: _staged[id] = true
func consume_staged(id: String) -> bool:
	var found := _staged.has(id); _staged.erase(id); return found
func _init(hand_view, effects: Control, visual_context) -> void:
	hand = hand_view; overlay = effects; context = visual_context
func acquire(id: String, model: Dictionary, source_visible: bool, start: Vector2) -> Control:
	if not id.is_empty() and _records.has(id) and is_instance_valid(_records[id]): return _records[id]
	var card = hand.card_for(id) if hand != null else null
	var borrowed: bool = source_visible and is_instance_valid(card)
	var copy_pose: bool = borrowed and card.visible
	var ghost: Control
	if borrowed:
		ghost = hand.borrow_card(id, overlay)
		ghost.set_meta("borrowed_hand_card", true)
	elif source_visible and not model.is_empty():
		ghost = CardView.new(context); ghost.animate_entry = false; ghost.hand_managed = true
		ghost.configure(model); ghost.size = context.card_style.effective_size(context.text_scale)
	else:
		ghost = preload("res://scripts/ui/card_back_view.gd").new(); ghost.ink = context.styles.INK
		ghost.size = context.card_style.effective_size(context.text_scale)
		ghost.add_theme_stylebox_override("panel", context.styles.box(context.styles.PANEL, 6, context.styles.INK, 2))
	if not borrowed: ghost.name = "CardTransit"; ghost.focus_mode = Control.FOCUS_NONE
	ghost.mouse_filter = Control.MOUSE_FILTER_IGNORE; ghost.process_mode = Node.PROCESS_MODE_PAUSABLE
	if not borrowed:
		if is_instance_valid(card): hand.begin_transit(id)
		overlay.add_child(ghost); ghost.global_position = start - ghost.size * .5
	elif not copy_pose: ghost.global_position = start - ghost.size * .5
	ghost.pivot_offset = ghost.size * .5; ghost.z_index = 100
	if not id.is_empty(): _records[id] = ghost
	return ghost
func release(id: String, ghost: Control, reveal_card := false) -> void:
	_staged.erase(id)
	if _records.get(id) == ghost: _records.erase(id)
	if is_instance_valid(ghost):
		if ghost.has_meta("borrowed_hand_card") and hand != null and is_instance_valid(hand):
			hand.return_card(id, ghost, reveal_card)
		else: ghost.hide(); ghost.queue_free()
	if hand != null and is_instance_valid(hand): hand.finish_transit(id, reveal_card)
func dispose() -> void:
	for id in _records.keys(): release(id, _records[id])
	_records.clear()
