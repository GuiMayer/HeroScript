extends Resource
## Visual geometry and camera timing; availability comes exclusively from engine choices.
@export var node_size := Vector2(112, 104)
@export var hit_size := Vector2(72, 64)
@export var symbol_size := 42.0
@export var compact_auxiliary := true
@export var auxiliary_kinds := PackedStringArray(["CardSelection", "RelicSelection", "CardUpgrade", "InscriptionSelection"])
@export var auxiliary_symbol_size := 30.0
@export var padding := Vector2(24, 20)
@export var lane_gap := 16.0
@export var layer_gap := 32.0
@export var maximum_panel_width := 620.0
@export var panel_width_ratio := .4
@export_range(0.0, 1.0, .01) var camera_transition_seconds := .28
func presentation_key() -> String:
	return JSON.stringify([node_size, hit_size, symbol_size, padding, lane_gap, layer_gap])
