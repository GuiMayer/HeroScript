extends "res://scripts/ui/route_view.gd"
## Temporary specialization: nodes inspect; the inspector owns the travel action.
signal inspection_requested(node_id: String)
var _selected := ""
func setup(route: Array) -> void:
	super.setup(route)
	for id in buttons:
		var button: Button = buttons[id]
		for connection in button.pressed.get_connections(): button.pressed.disconnect(connection.callable)
		button.pressed.connect(func(): inspection_requested.emit(str(id)))
		button.disabled = false
		button.set_meta("inspected_node", id)
	set_selection(_selected)
func set_selection(id: String) -> void:
	_selected = id
	for key in buttons:
		buttons[key].toggle_mode = true
		buttons[key].set_pressed_no_signal(str(key) == id)
