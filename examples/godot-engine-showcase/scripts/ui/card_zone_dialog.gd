class_name CardZoneDialogView
extends AcceptDialog
signal card_requested(id: String)
var visual_context
var collection
var detail: Window
func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
static func create(context = null) -> CardZoneDialogView:
	var dialog = (load("res://scenes/ui/overlays/card_zone_dialog.tscn") as PackedScene).instantiate()
	if context != null: dialog.visual_context = context
	return dialog
func setup(model: Dictionary) -> void:
	theme = visual_context.theme; title = "%s · %s" % [model.label,model.count]
	collection = get_node("%Collection"); collection.visual_context = visual_context
	if not collection.card_requested.is_connected(card_requested.emit): collection.card_requested.connect(card_requested.emit)
	collection.apply_model(model)
	ok_button_text = visual_context.translator.text("CLOSE")
	if not confirmed.is_connected(queue_free): confirmed.connect(queue_free)
	if not canceled.is_connected(queue_free): canceled.connect(queue_free)
	if not visibility_changed.is_connected(_on_visibility): visibility_changed.connect(_on_visibility)
func _on_visibility() -> void:
	if not is_instance_valid(collection): return
	if visible: preload("res://scripts/ui/panel_motion.gd").enter(collection,visual_context)
	else: preload("res://scripts/ui/panel_motion.gd").cancel(collection)
func show_detail(id: String, card_model: Dictionary, inspection: Dictionary = {}, status := "") -> void:
	if is_instance_valid(detail): detail.queue_free()
	detail = preload("res://scripts/ui/inspection_dialog.gd").create(visual_context)
	detail.setup(str(card_model.name), status if not status.is_empty() else str(card_model.summary), inspection, card_model)
	detail.set_meta("card_instance_id", id); add_child(detail); detail.exclusive = true; detail.popup_centered_clamped(Vector2i(720, 560), .9)
	var opened: Window = detail
	opened.tree_exiting.connect(func():
		if detail == opened and is_instance_valid(collection) and collection.cards.has(id) and collection.cards[id].is_inside_tree(): collection.cards[id].grab_focus())
	detail.set_status(status)
func capture_local_state() -> Dictionary: return collection.capture_local_state()
