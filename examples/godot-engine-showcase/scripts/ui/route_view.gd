extends Control
## Shows only engine edges. List order is presentation, never inferred travel permission.
signal travel_requested(choice: Dictionary)
var nodes: Array = []
var markers := {}

func setup(route: Array) -> void:
	nodes = route.duplicate(true)
	custom_minimum_size = Vector2(278, nodes.size() * 76 + 12)
	for index in nodes.size():
		var node: Dictionary = nodes[index]
		markers[str(node.id)] = Vector2(18, index * 76 + 38)
		var button := Button.new()
		button.position = Vector2(46, index * 76 + 6)
		button.size = Vector2(230, 64)
		button.text = str(node.name) + "\n" + (I18n.text("YOU ARE HERE") if node.current else (I18n.text("COMPLETED") if node.resolved else I18n.content_name(str(node.kind))))
		button.add_theme_font_size_override("font_size", 14)
		button.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		button.disabled = node.travel.is_empty()
		button.add_theme_color_override("font_disabled_color", AppTheme.GOLD if node.current else AppTheme.MUTED)
		button.add_theme_stylebox_override("disabled", AppTheme.box(Color("#242133") if node.current else Color("#141623"), 9, AppTheme.GOLD if node.current else Color("#343448"), 1))
		button.tooltip_text = str(node.name)
		button.pressed.connect(func(): travel_requested.emit(node.travel))
		add_child(button)
	queue_redraw()

func _draw() -> void:
	for node in nodes:
		var origin: Vector2 = markers[str(node.id)]
		for next in node.next:
			if not markers.has(str(next)): continue
			var destination: Vector2 = markers[str(next)]
			var color := AppTheme.TEAL.darkened(.4) if node.resolved else Color("#575267")
			var bend := 8.0 if absf(destination.y - origin.y) > 80 else 18.0
			draw_polyline(PackedVector2Array([origin, Vector2(bend, origin.y + 12), Vector2(bend, destination.y - 12), destination]), color, 2, true)
			draw_line(destination, destination + Vector2(-4, -7 if destination.y > origin.y else 7), color, 2, true)
	for node in nodes:
		var point: Vector2 = markers[str(node.id)]
		draw_circle(point, 7, AppTheme.GOLD if node.current else (AppTheme.TEAL if node.resolved else Color("#575267")))
