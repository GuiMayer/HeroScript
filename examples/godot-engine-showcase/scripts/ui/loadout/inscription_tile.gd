class_name InscriptionTile
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

static func create(editorial := true) -> InscriptionTile:
	var path := "res://scenes/ui/components/loadout/inscription_tile_" + ("editorial" if editorial else "compact") + ".tscn"
	return (load(path) as PackedScene).instantiate()

func _ready() -> void:
	set_process(false)

func drag_payload() -> Variant:
	if item_id.is_empty(): return null
	return {"kind": "inscription_draft", "scope": drag_scope, "revision": revision,
		"item": item_id, "source": source_slot}

func _get_drag_data(_at_position: Vector2) -> Variant:
	var data = drag_payload()
	if data == null: return null
	var cursor_anchor: Control = preload("res://scenes/ui/components/loadout/inscription_drag_preview.tscn").instantiate()
	var preview: PanelContainer = cursor_anchor.get_node("Surface")
	preview.theme = get_theme()
	preview.add_theme_stylebox_override("panel", get_theme_stylebox("normal"))
	var label: Label = preview.get_node("Description")
	label.text = tooltip_text
	label.add_theme_color_override("font_color", find_child("TileTitle", true, false).get_theme_color("font_color"))
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
