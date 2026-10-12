extends Resource
## Geometry only; node availability comes exclusively from engine choices.
@export var node_size := Vector2(230, 64)
@export var padding := Vector2(24, 20)
@export var lane_gap := 24.0
@export var layer_gap := 44.0
@export var maximum_panel_width := 620.0
@export var panel_width_ratio := .4
func presentation_key() -> String:
	return JSON.stringify([node_size, padding, lane_gap, layer_gap, maximum_panel_width, panel_width_ratio])
