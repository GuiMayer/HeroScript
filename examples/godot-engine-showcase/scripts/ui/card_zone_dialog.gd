extends AcceptDialog
signal card_requested(id: String)
var visual_context
var collection
var detail: Window
func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
func setup(model: Dictionary) -> void:
	theme = visual_context.theme; title = "%s · %s" % [model.label, model.count]
	min_size = Vector2i(650, 560); exclusive = true
	collection = preload("res://scripts/ui/card_collection_view.gd").new(visual_context)
	collection.card_requested.connect(card_requested.emit); add_child(collection); collection.apply_model(model)
	ok_button_text = visual_context.translator.text("CLOSE")
	confirmed.connect(queue_free); canceled.connect(queue_free)
func show_detail(id: String, card_model: Dictionary, inspection: Dictionary = {}, status := "") -> void:
	if is_instance_valid(detail): detail.queue_free()
	detail = preload("res://scripts/ui/inspection_dialog.gd").new(visual_context)
	detail.setup(str(card_model.name), status if not status.is_empty() else str(card_model.summary), inspection, card_model)
	detail.set_meta("card_instance_id", id); add_child(detail); detail.exclusive = true; detail.popup_centered_clamped(Vector2i(720, 560), .9)
	var indicator: Label = visual_context.styles.muted(status, 15); indicator.name = "CollectionInspectionStatus"
	detail.content.add_child(indicator); detail.content.move_child(indicator, 0)
func capture_local_state() -> Dictionary: return collection.capture_local_state()
