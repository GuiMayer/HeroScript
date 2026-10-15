extends Button
## Native drag/drop presentation only. Payloads belong to one live view revision.
signal inscription_dropped(item_id: String, slot_id: String, source_slot: String)
var item_id := ""
var slot_id := ""
var source_slot := ""
var drag_scope: int
var revision := ""
var known_items: Array = []
var drop_color := Color("286853")

func _ready() -> void:
	set_process(false)

func drag_payload() -> Variant:
	if item_id.is_empty(): return null
	return {"kind": "inscription_draft", "scope": drag_scope, "revision": revision,
		"item": item_id, "source": source_slot}

func _get_drag_data(_at_position: Vector2) -> Variant:
	var data = drag_payload()
	if data == null: return null
	var cursor_anchor := Control.new()
	cursor_anchor.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var preview := PanelContainer.new()
	preview.theme = get_theme()
	preview.add_theme_stylebox_override("panel", get_theme_stylebox("normal"))
	preview.position = Vector2(16, 16)
	preview.size = Vector2(230, 95)
	preview.modulate.a = 0.92
	preview.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var label := Label.new()
	label.text = tooltip_text
	label.add_theme_color_override("font_color", find_child("TileTitle", true, false).get_theme_color("font_color"))
	label.add_theme_font_size_override("font_size", 16)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	preview.add_child(label)
	cursor_anchor.add_child(preview)
	set_drag_preview(cursor_anchor)
	return data

func _can_drop_data(_at_position: Vector2, data: Variant) -> bool:
	return not slot_id.is_empty() and data is Dictionary \
		and data.get("kind") == "inscription_draft" and data.get("scope") == drag_scope \
		and data.get("revision") == revision and data.get("item") in known_items \
		and data.get("source") is String and data.get("source") != slot_id

func _drop_data(at_position: Vector2, data: Variant) -> void:
	if _can_drop_data(at_position, data):
		inscription_dropped.emit(data.item, slot_id, data.source)

func _notification(what: int) -> void:
	if what == NOTIFICATION_DRAG_BEGIN:
		set_process(true)
		queue_redraw()
	elif what == NOTIFICATION_DRAG_END:
		set_process(false)
		queue_redraw()

func _process(_delta: float) -> void: queue_redraw()

func _draw() -> void:
	if get_viewport().gui_is_dragging() and _can_drop_data(Vector2.ZERO, get_viewport().gui_get_drag_data()):
		var hovered := Rect2(Vector2.ZERO, size).has_point(get_local_mouse_position())
		draw_rect(Rect2(Vector2(3, 3), size - Vector2(6, 6)), drop_color, false, 4.0 if hovered else 2.0)
