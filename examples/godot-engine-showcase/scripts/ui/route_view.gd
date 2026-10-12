extends Control
## Engine-published topology and choices; geometry is a separate pure dependency.
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType = ContextType.new()
var layout_profile: Resource = preload("res://scripts/ui/styles/map_layout_profile.gd").new()
var _layout = preload("res://scripts/presentation/map_layout.gd").new()
var _geometry: Dictionary = {}
signal inspection_requested(node_id: String)
var _selected := ""
var _highlight_kind := ""
var nodes: Array = []
var markers := {}
var buttons := {}
func reconcile_from(other) -> void:
	visual_context = other.visual_context
	layout_profile = other.layout_profile
	setup(other.nodes)

func setup(route: Array) -> void:
	nodes = route.duplicate(true)
	theme = visual_context.theme
	_geometry = _layout.calculate(nodes, layout_profile)
	for diagnostic in _geometry.diagnostics: push_warning(diagnostic)
	markers = _geometry.markers
	var live: Array = []
	custom_minimum_size = _geometry.bounds
	for index in nodes.size():
		var node: Dictionary = nodes[index]
		var id := str(node.get("id", ""))
		if not _geometry.rects.has(id) or id in live: continue
		live.append(id)
		var button: Button = buttons.get(id)
		if button == null:
			button = Button.new()
			button.name = "Stop_" + id.validate_node_name()
			button.pressed.connect(func(): inspection_requested.emit(str(button.get_meta("node_id", ""))))
			button.focus_entered.connect(func(): inspection_requested.emit(str(button.get_meta("node_id", ""))))
			add_child(button)
			buttons[id] = button
		button.set_meta("travel", node.travel.duplicate(true))
		button.set_meta("node_id", id)
		button.position = _geometry.rects[id].position
		button.size = _geometry.rects[id].size
		button.text = str(node.name) + "\n" + (visual_context.translator.text("YOU ARE HERE") if node.current else (visual_context.translator.text("COMPLETED") if node.resolved else visual_context.translator.content_name(str(node.kind))))
		if str(node.get("risk", "")) == "High": button.text += " · " + visual_context.translator.text("High risk")
		button.add_theme_font_size_override("font_size", 14)
		button.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		button.clip_text = true
		button.disabled = false # Published visibility permits reading, not travel.
		button.toggle_mode = true
		button.set_pressed_no_signal(id == _selected)
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		var art = button.get_meta("map_art") if button.has_meta("map_art") else null
		if not is_instance_valid(art):
			art = preload("res://scripts/ui/art_slot.gd").new(visual_context)
			button.add_child(art); button.set_meta("map_art", art)
			art.set_anchors_and_offsets_preset(Control.PRESET_CENTER_LEFT)
			art.offset_left = 8; art.offset_right = 36; art.offset_top = -14; art.offset_bottom = 14
		art.rebind_context(visual_context); art.setup("activities", str(node.kind))
		for state in ["normal", "hover", "pressed", "disabled", "focus"]:
			var fill := visual_context.styles.color("surface.route_current") if node.current else visual_context.styles.color("surface.route_locked")
			var border := visual_context.styles.GOLD if node.current or not node.travel.is_empty() else visual_context.styles.color("stroke.route_locked")
			var box := visual_context.styles.box(fill, visual_context.profile.corner_radius, border, 2 if node.current else 1)
			box.content_margin_left = 44
			if state in ["hover", "focus", "pressed"]: box.border_color = visual_context.styles.color("text.primary"); box.border_width_left = 2
			button.add_theme_stylebox_override(state, box)
		button.add_theme_color_override("font_disabled_color", visual_context.styles.GOLD if node.current else visual_context.styles.MUTED)
		button.add_theme_stylebox_override("disabled", visual_context.styles.box(visual_context.styles.color("surface.route_current") if node.current else visual_context.styles.color("surface.route_locked"), 9, visual_context.styles.GOLD if node.current else visual_context.styles.color("stroke.route_locked"), 1))
		button.tooltip_text = str(node.name) + "\n" + str(node.get("description", ""))
		var text_role := "accent.warm" if str(node.get("risk", "")) == "High" else "text.primary"
		button.set_meta("text_role", text_role)
		button.add_theme_color_override("font_color", visual_context.styles.color(text_role))
	for id in buttons.keys():
		if id not in live:
			remove_child(buttons[id]); buttons[id].queue_free(); buttons.erase(id)
	queue_redraw()

func set_selection(id: String) -> void:
	_selected = id
	for key in buttons: buttons[key].set_pressed_no_signal(str(key) == id)

func highlight_kind(kind: String) -> void:
	_highlight_kind = kind
	queue_redraw()

func _draw() -> void:
	for edge in _geometry.get("edges", []):
		var source: Array = nodes.filter(func(node): return str(node.get("id", "")) == str(edge.from))
		var resolved: bool = not source.is_empty() and bool(source[0].get("resolved", false))
		var color := visual_context.styles.TEAL.darkened(.4) if resolved else visual_context.styles.color("stroke.route")
		var path: PackedVector2Array = edge.path
		draw_polyline(path, color, 2, true)
		var end: Vector2 = path[-1]
		var direction := (end - path[-2]).normalized()
		var wing := direction.rotated(PI * .75) * 7
		draw_line(end, end + wing, color, 2, true)
		draw_line(end, end + wing.rotated(PI * .5), color, 2, true)
	for node in nodes:
		if not markers.has(str(node.get("id", ""))): continue
		var point: Vector2 = markers[str(node.get("id", ""))]
		draw_circle(point, 7, visual_context.styles.GOLD if node.current else (visual_context.styles.TEAL if node.resolved else visual_context.styles.color("stroke.route")))
		if not _highlight_kind.is_empty() and str(node.get("kind", "")) == _highlight_kind:
			draw_arc(point, 11, 0, TAU, 24, visual_context.styles.color("text.primary"), 2, true)
