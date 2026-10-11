extends Resource
## Geometry only. No card limits, input rules or combat state.
@export var layout_id := "bottom_preview"
@export_enum("bottom", "floating_left") var preview_placement := "bottom"
@export var preview_width := 310.0
@export var sidebar_width := 328.0
@export var sidebar_height := 520.0
@export var handle_width := 44.0
@export var candidate_width := 600.0
@export var candidate_height := 144.0
func presentation_key() -> String:
	return JSON.stringify([layout_id, preview_placement, preview_width, sidebar_width, sidebar_height, handle_width, candidate_width, candidate_height])
