@tool
extends Control
@export var ink := Color("#20201c")
@export_enum("frame", "registration") var style := "frame"
func _ready() -> void: resized.connect(queue_redraw)
func _draw() -> void:
    var card = get_parent()
    if card and card.get_meta("awaiting_card_selection", false):
        draw_rect(Rect2(Vector2(7, 7), (size - Vector2(14, 14)).max(Vector2.ZERO)), Color("#c44428"), false, 4.0)
    elif card and card.get_meta("card_selection_eligible", false):
        draw_rect(Rect2(Vector2(7, 7), (size - Vector2(14, 14)).max(Vector2.ZERO)), Color("#287c86"), false, 4.0 if card.get_meta("card_selection_chosen", false) else 2.0)
    var width := 2.0 if card and (card.get("chosen") or card.has_focus()) else 1.0
    if style == "registration":
        for corner in [Vector2(4, 4), Vector2(size.x - 4, 4), Vector2(4, size.y - 4), size - Vector2(4, 4)]:
            var direction := Vector2(1 if corner.x < size.x * .5 else -1, 1 if corner.y < size.y * .5 else -1)
            draw_line(corner, corner + Vector2(direction.x * 12, 0), ink, width)
            draw_line(corner, corner + Vector2(0, direction.y * 12), ink, width)
        return
    draw_rect(Rect2(Vector2(4, 4), (size - Vector2(8, 8)).max(Vector2.ZERO)), ink, false, width)
