extends Control
## Shows only engine edges. List order is presentation, never inferred travel permission.
signal travel_requested(choice: Dictionary)
var nodes: Array = []
var markers := {}
var buttons := {}

func setup(route: Array) -> void:
	nodes = route.duplicate(true)
	markers.clear()
	var live: Array = []
	custom_minimum_size = Vector2(278, nodes.size() * 76 + 12)
	for index in nodes.size():
		var node: Dictionary = nodes[index]
		markers[str(node.id)] = Vector2(18, index * 76 + 38)
		var id := str(node.id)
		live.append(id)
		var button: Button = buttons.get(id)
		if button == null:
			button = Button.new()
			button.name = "Stop_" + id.validate_node_name()
			button.pressed.connect(func(): travel_requested.emit(button.get_meta("travel", {}).duplicate(true)))
			add_child(button)
			buttons[id] = button
		button.set_meta("travel", node.travel.duplicate(true))
		button.position = Vector2(46, index * 76 + 6)
		button.size = Vector2(230, 64)
		button.text = str(node.name) + "\n" + (I18n.text("YOU ARE HERE") if node.current else (I18n.text("COMPLETED") if node.resolved else I18n.content_name(str(node.kind))))
		button.add_theme_font_size_override("font_size", 14)
		button.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		button.disabled = node.travel.is_empty()
		button.add_theme_color_override("font_disabled_color", AppTheme.GOLD if node.current else AppTheme.MUTED)
		button.add_theme_stylebox_override("disabled", AppTheme.box(AppTheme.color("surface.route_current") if node.current else AppTheme.color("surface.route_locked"), 9, AppTheme.GOLD if node.current else AppTheme.color("stroke.route_locked"), 1))
		button.tooltip_text = str(node.name) + "\n" + str(node.get("description", ""))
		if str(node.get("risk", "")) == "High":
			button.add_theme_color_override("font_color", AppTheme.EMBER)
	for id in buttons.keys():
		if id not in live:
			remove_child(buttons[id]); buttons[id].queue_free(); buttons.erase(id)
	queue_redraw()

func _draw() -> void:
	for node in nodes:
		var origin: Vector2 = markers[str(node.id)]
		for next in node.next:
			if not markers.has(str(next)): continue
			var destination: Vector2 = markers[str(next)]
			var color := AppTheme.TEAL.darkened(.4) if node.resolved else AppTheme.color("stroke.route")
			var bend := 8.0 if absf(destination.y - origin.y) > 80 else 18.0
			draw_polyline(PackedVector2Array([origin, Vector2(bend, origin.y + 12), Vector2(bend, destination.y - 12), destination]), color, 2, true)
			draw_line(destination, destination + Vector2(-4, -7 if destination.y > origin.y else 7), color, 2, true)
	for node in nodes:
		var point: Vector2 = markers[str(node.id)]
		draw_circle(point, 7, AppTheme.GOLD if node.current else (AppTheme.TEAL if node.resolved else AppTheme.color("stroke.route")))
