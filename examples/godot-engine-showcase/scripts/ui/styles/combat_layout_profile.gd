@tool
extends Resource
## Geometry only. No card limits, input rules or combat state.
@export var layout_id := "bottom_preview"
@export_enum("bottom", "floating_left") var preview_placement := "bottom"
@export var preview_width := 310.0
@export var floating_preview_height := 110.0
@export var floating_dock_gap := 10.0
@export var sidebar_width := 328.0
@export var sidebar_height := 520.0
@export var handle_width := 44.0
@export var candidate_width := 600.0
@export var candidate_height := 144.0
@export var toolbar_min_size := Vector2(144, 44)
@export var reconnect_button_size := Vector2(150, 44)
@export var choice_button_height := 44.0
@export_range(.5, 1.0) var hand_width_ratio := .78
@export var hand_max_width := 1280.0
@export var safe_margin := 8.0
func presentation_key() -> String:
	return JSON.stringify([layout_id, preview_placement, preview_width, floating_preview_height, floating_dock_gap, sidebar_width, sidebar_height, handle_width, candidate_width, candidate_height, toolbar_min_size, reconnect_button_size, choice_button_height, hand_width_ratio, hand_max_width, safe_margin])
