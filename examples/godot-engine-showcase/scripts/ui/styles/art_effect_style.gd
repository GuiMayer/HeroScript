extends Resource
## Presentation only. Shared palette, never resource semantics or gameplay values.
@export var impact_color := Color("#b83d24")
@export_range(0.0, 1.0) var impact_strength := .7
@export_range(0.0, 2.0) var impact_seconds := .16
@export var available_color := Color("#20201c")
@export var selected_color := Color("#b83d24")
@export_range(1.0, 4.0) var available_width := 1.5
@export_range(1.0, 4.0) var selected_width := 3.0
@export var sheen_color := Color("#d6b476")
@export_range(0.0, 1.0) var sheen_strength := .65
@export_range(0.0, 2.0) var sheen_seconds := .32
