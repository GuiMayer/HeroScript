extends CanvasLayer
## Non-modal reading/context overlays. Independent of battlefield layout and card z.
var host := Control.new()

func _init() -> void:
	name = "FloatingCombatHUD"
	layer = preload("res://scripts/ui/shell/overlay_layers.gd").FLOATING
	host.name = "FloatingHUDHost"
	host.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(host)

func fit_to(rect: Rect2, view_theme: Theme) -> void:
	host.theme = view_theme
	host.position = rect.position
	host.size = rect.size
