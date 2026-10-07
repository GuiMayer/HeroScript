extends RefCounted
## Pointer intent only. A drop is not a legal or accepted command.
var hovered := ""
var focused := ""
var selected := ""
var pressed := ""
var press_position := Vector2.ZERO
var dragging := false
var pointer_active := false
func active_id() -> String:
	return hovered if pointer_active and not hovered.is_empty() else (focused if not pointer_active and not focused.is_empty() else selected)
func cancel_pointer() -> void:
	pressed = ""
	dragging = false
func clear() -> void:
	hovered = ""
	focused = ""
	selected = ""
	pointer_active = false
	cancel_pointer()
