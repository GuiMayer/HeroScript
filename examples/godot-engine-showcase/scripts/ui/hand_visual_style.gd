class_name HandVisualStyle
extends Resource
## Presentation only; never a pile capacity or a rule of gameplay.
@export var gap := 12.0
@export var minimum_step := 92.0
@export var maximum_angle_degrees := 8.0
@export var minimum_fan_angle_degrees := 4.0
@export var preferred_step_ratio := .86
@export var arc_height := 14.0
@export var lift := 32.0
@export var transition_seconds := .13
@export var pointer_hysteresis := 4.0
@export var drag_threshold := 16.0
@export var padding := 6.0
@export var reorder_edge_band := 32.0
@export var reorder_scroll_speed := 480.0
func presentation_key() -> String:
	var values := {}
	for property in get_property_list():
		if int(property.usage) & PROPERTY_USAGE_SCRIPT_VARIABLE: values[str(property.name)] = get(property.name)
	return JSON.stringify(values)
