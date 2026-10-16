extends Control
## Engine-published topology and choices; geometry is a separate pure dependency.
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType = ContextType.new()
var layout_profile: Resource = preload("res://scripts/ui/styles/map_layout_profile.gd").new()
var _layout = preload("res://scripts/presentation/map_layout.gd").new()
var _geometry: Dictionary = {}
signal inspection_requested(node_id: String)
signal focus_requested(node_id: String)
signal hover_changed(node_id: String)
var suppress_activation := false
var _selected := ""
var _highlight_kind := ""
var _hovered := ""
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
			button.pressed.connect(func():
				if not suppress_activation: inspection_requested.emit(str(button.get_meta("node_id", ""))))
			button.focus_entered.connect(func():
				var target := str(button.get_meta("node_id", ""))
				focus_requested.emit(target))
			button.mouse_entered.connect(func(): hover_changed.emit(str(button.get_meta("node_id", "")))); button.mouse_exited.connect(func(): hover_changed.emit(""))
			add_child(button)
			buttons[id] = button
		button.set_meta("travel", node.travel.duplicate(true))
		button.set_meta("node_id", id)
		button.position = _geometry.rects[id].position + Vector2((layout_profile.node_size.x - layout_profile.hit_size.x) * .5, 0)
		button.size = layout_profile.hit_size
		button.text = ""
		var caption: Label = button.get_node_or_null("Caption")
		if caption == null:
			caption = Label.new(); caption.name = "Caption"; caption.mouse_filter = Control.MOUSE_FILTER_IGNORE
			caption.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
			caption.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
			caption.clip_text = true; caption.add_theme_font_size_override("font_size", 13)
			button.add_child(caption)
		caption.position = Vector2((layout_profile.hit_size.x - layout_profile.node_size.x) * .5, layout_profile.hit_size.y + 2)
		caption.size = Vector2(layout_profile.node_size.x, 38)
		var state_label := "● " if node.current else ("✓ " if node.resolved else ("◇ " if not node.travel.is_empty() else ""))
		caption.text = state_label + str(node.name)
		if str(node.get("risk", "")) == "High": caption.text += "\n! " + visual_context.translator.text("High risk")
		button.add_theme_font_size_override("font_size", 14)
		button.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		button.clip_text = true
		button.disabled = false # Published visibility permits reading, not travel.
		button.toggle_mode = true
		button.set_pressed_no_signal(id == _selected)
		button.alignment = HORIZONTAL_ALIGNMENT_CENTER
		var art = button.get_meta("map_art") if button.has_meta("map_art") else null
		if not is_instance_valid(art):
			art = preload("res://scripts/ui/art_slot.gd").new(visual_context)
			button.add_child(art); button.set_meta("map_art", art)
			art.mouse_filter = Control.MOUSE_FILTER_IGNORE
			art.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
			art.offset_left = -layout_profile.symbol_size * .5; art.offset_right = layout_profile.symbol_size * .5
			art.offset_top = -layout_profile.symbol_size * .5; art.offset_bottom = layout_profile.symbol_size * .5
		art.rebind_context(visual_context); art.setup("activities", str(node.kind))
		var auxiliary: bool = layout_profile.compact_auxiliary and str(node.kind) in layout_profile.auxiliary_kinds
		var symbol_size: float = layout_profile.auxiliary_symbol_size if auxiliary else layout_profile.symbol_size
		art.offset_left = -symbol_size * .5; art.offset_right = symbol_size * .5
		art.offset_top = -symbol_size * .5; art.offset_bottom = symbol_size * .5
		var glyph = button.get_node_or_null("MapGlyph")
		if glyph == null:
			glyph = preload("res://scripts/ui/components/map_node_glyph.gd").new(); glyph.name = "MapGlyph"
			glyph.mouse_filter = Control.MOUSE_FILTER_IGNORE; button.add_child(glyph)
		glyph.kind = str(node.kind); glyph.ink = visual_context.styles.color("text.primary")
		glyph.position = (layout_profile.hit_size - Vector2.ONE * symbol_size) * .5; glyph.size = Vector2.ONE * symbol_size
		glyph.visible = art.is_placeholder; art.visible = not art.is_placeholder; glyph.queue_redraw()
		button.set_meta("auxiliary", auxiliary)
		for state in ["normal", "hover", "pressed", "disabled", "focus"]:
			var fill := visual_context.styles.color("surface.route_current") if node.current else visual_context.styles.color("surface.route_locked")
			var border := visual_context.styles.GOLD if node.current or not node.travel.is_empty() else visual_context.styles.color("stroke.route_locked")
			var box := visual_context.styles.box(fill, visual_context.profile.corner_radius, border, 2 if node.current else 1)
			box.content_margin_left = 0; box.content_margin_right = 0
			if state in ["hover", "focus", "pressed"]: box.border_color = visual_context.styles.color("text.primary"); box.border_width_left = 2
			button.add_theme_stylebox_override(state, box)
		button.add_theme_color_override("font_disabled_color", visual_context.styles.GOLD if node.current else visual_context.styles.MUTED)
		button.add_theme_stylebox_override("disabled", visual_context.styles.box(visual_context.styles.color("surface.route_current") if node.current else visual_context.styles.color("surface.route_locked"), 9, visual_context.styles.GOLD if node.current else visual_context.styles.color("stroke.route_locked"), 1))
		button.tooltip_text = str(node.name) + "\n" + visual_context.translator.content_name(str(node.kind)) + "\n" + str(node.get("description", ""))
		var text_role := "accent.warm" if str(node.get("risk", "")) == "High" else "text.primary"
		button.set_meta("text_role", text_role)
		button.add_theme_color_override("font_color", visual_context.styles.color(text_role))
		caption.add_theme_color_override("font_color", visual_context.styles.color("text.primary") if node.current or not node.travel.is_empty() else visual_context.styles.MUTED)
	for id in buttons.keys():
		if id not in live:
			remove_child(buttons[id]); buttons[id].queue_free(); buttons.erase(id)
	_configure_neighbors()
	queue_redraw()

func _configure_neighbors() -> void:
	for id in buttons:
		var button: Button = buttons[id]
		for direction in [["focus_neighbor_left", Vector2.LEFT], ["focus_neighbor_right", Vector2.RIGHT], ["focus_neighbor_top", Vector2.UP], ["focus_neighbor_bottom", Vector2.DOWN]]:
			var best: String = id; var score := INF
			for other in buttons:
				if id == other: continue
				var delta: Vector2 = _geometry.rects[other].get_center() - _geometry.rects[id].get_center()
				var forward: float = delta.dot(direction[1])
				if forward <= 1: continue
				var sideways: float = absf(delta.cross(direction[1]))
				var value := forward + sideways * 3.0
				if value < score or (is_equal_approx(value, score) and str(other) < best): best = str(other); score = value
			button.set(direction[0], button.get_path_to(buttons[best]))

func set_selection(id: String) -> void:
	_selected = id
	for key in buttons: buttons[key].set_pressed_no_signal(str(key) == id)
	queue_redraw()

func set_hover(id: String) -> void:
	if _hovered == id: return
	_hovered = id; queue_redraw()

func highlight_kind(kind: String) -> void:
	_highlight_kind = kind
	queue_redraw()

func _draw() -> void:
	for edge in _geometry.get("edges", []):
		var source: Array = nodes.filter(func(node): return str(node.get("id", "")) == str(edge.from))
		var resolved: bool = not source.is_empty() and bool(source[0].get("resolved", false))
		var color := visual_context.styles.TEAL.darkened(.4) if resolved else visual_context.styles.color("stroke.route")
		var highlighted: bool = edge.from in [_hovered, _selected] or edge.to in [_hovered, _selected]
		if highlighted: color = visual_context.styles.color("text.primary")
		var path: PackedVector2Array = edge.path
		draw_polyline(path, color, 3 if highlighted else 1.5, true)
		var end: Vector2 = path[-1]
		var direction := (end - path[-2]).normalized()
		var wing := direction.rotated(PI * .75) * 7
		draw_line(end, end + wing, color, 2, true)
		draw_line(end, end + wing.rotated(PI * .5), color, 2, true)
	for node in nodes:
		if not markers.has(str(node.get("id", ""))): continue
		if not _highlight_kind.is_empty() and str(node.get("kind", "")) == _highlight_kind:
			draw_rect(Rect2(buttons[str(node.id)].position, buttons[str(node.id)].size).grow(5), visual_context.styles.color("text.primary"), false, 3, true)
