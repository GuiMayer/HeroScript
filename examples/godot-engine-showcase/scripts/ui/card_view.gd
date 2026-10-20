class_name CardView
extends Button
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType

var motion: Tween
var chosen := false
var model: Dictionary = {}

func reconcile_from(other) -> void:
	if visual_context != other.visual_context: _configuration_key = ""
	visual_context = other.visual_context
	configure(other.model)
	disabled = other.disabled
	for key in other.get_meta_list():
		if key in ["activity_choice", "initial_focus", "skip_initial_focus"]: set_meta(key, other.get_meta(key))
var animate_entry := true
var hand_managed := false
var configuration_count := 0
var _configuration_key := ""
var _context_card_style: CardVisualStyle
@export var visual_style: CardVisualStyle = preload("res://data/default_card_visual_style.tres")
const EffectText = preload("res://scripts/ui/card_effect_text.gd")
const CostSymbol = preload("res://scripts/ui/card_cost_symbol.gd")

func configure(data: Dictionary) -> void:
	theme = visual_context.theme
	# Follow profile changes unless the consumer explicitly injected another policy.
	if visual_style == _context_card_style:
		_context_card_style = visual_context.card_style
		visual_style = _context_card_style
	# Diagnostics belong to persistent inspection, not the printed face. Keep
	# the latest model without rebuilding the card for invisible journal changes.
	model = data.duplicate(true)
	var face_data := data.duplicate(true)
	face_data.erase("inspectionText")
	face_data.erase("presentationFacts") # Version/impact traces affect inspection, not the printed face.
	face_data.erase("availability")
	var presentation_key: String = JSON.stringify(face_data) + visual_style.presentation_key() + visual_context.presentation_key()
	if presentation_key == _configuration_key: return
	_configuration_key = presentation_key
	configuration_count += 1
	if has_meta("visual_preferences_revision"): remove_meta("visual_preferences_revision")
	text = ""
	var face := Control.new()
	custom_minimum_size = visual_style.effective_size(visual_context.text_scale)
	var tone: Color = {"attack": visual_context.styles.BLOOD, "power": visual_context.styles.color("accent.power")}.get(str(data.get("tone", "skill")), visual_context.styles.TEAL)
	var border: Color = visual_style.rarity_color(str(data.get("rarityId", "")))
	set_meta("semantic_border_color", border)
	for key in get_meta_list():
		if str(key).begins_with("base_style_"): remove_meta(key)
	for state in ["normal", "disabled", "pressed"]:
		var fill: Color = visual_context.styles.PANEL if visual_style.printed_face else tone.darkened(.76 if state != "pressed" else .62)
		add_theme_stylebox_override(state, visual_context.styles.box(fill, visual_style.face_radius, border, 2))
	add_theme_stylebox_override("hover", visual_context.styles.box(visual_context.styles.PANEL if visual_style.printed_face else tone.darkened(.62), visual_style.face_radius, border, 2))
	add_theme_stylebox_override("focus", visual_context.styles.box(Color.TRANSPARENT, visual_style.face_radius, border, 4))
	if visual_style.printed_face:
		var keyline := InkKeyline.new()
		keyline.name = "PrintedKeyline"
		keyline.ink = visual_context.styles.INK
		keyline.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		face.add_child(keyline)
	var margin := MarginContainer.new()
	margin.name = "CardFaceMargin"
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + side, 12)
	face.add_child(margin)
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 3)
	margin.add_child(column)
	var header := HBoxContainer.new()
	header.add_theme_constant_override("separation", 5)
	var title := visual_context.styles.title(str(data.get("name", "")), 17)
	title.name = "CardName"
	if visual_style.printed_face and visual_context.profile.title_font: title.add_theme_font_override("font", visual_context.profile.title_font)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	header.add_child(title)
	if data.get("upgraded", false): header.add_child(visual_context.styles.title("↑", 15, visual_context.styles.INK))
	column.add_child(header)
	column.add_child(_cost_zone(data))
	var art_frame := PanelContainer.new()
	art_frame.name = "Art"
	art_frame.custom_minimum_size.y = visual_style.art_height
	art_frame.clip_contents = true
	art_frame.add_theme_stylebox_override("panel", _compact_box(visual_context.styles.color("surface.card_art"), tone.darkened(.28), 5, 1))
	var art := preload("res://scripts/ui/art_slot.gd").new(visual_context)
	art.setup("cards", str(data.get("definitionId", "")))
	art_frame.add_child(art)
	var placeholder_name: Label
	if art.is_placeholder and bool(data.get("artPlaceholder", true)):
		var art_name := Label.new()
		art_name.text = str(data.get("name", "")).trim_suffix(" +").to_upper()
		art_name.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		art_name.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM if visual_style.printed_face else VERTICAL_ALIGNMENT_CENTER
		art_name.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		art_name.max_lines_visible = 2
		art_name.add_theme_font_size_override("font_size", 11 if visual_style.printed_face else 15)
		art_name.add_theme_color_override("font_color", visual_context.styles.INK if visual_style.printed_face else Color.WHITE)
		art_name.add_theme_constant_override("outline_size", 0 if visual_style.printed_face else 4)
		art_name.add_theme_color_override("font_outline_color", visual_context.styles.color("stroke.art_text"))
		if visual_style.printed_face: placeholder_name = art_name
		else: art_frame.add_child(art_name)
	column.add_child(art_frame)
	if placeholder_name:
		placeholder_name.name = "ArtPlaceholderName"
		placeholder_name.max_lines_visible = 1
		placeholder_name.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		column.add_child(placeholder_name)
	var type_line := HBoxContainer.new()
	var card_type := visual_context.styles.muted(str(data.get("cardType", str(data.get("tone", "skill")).to_upper())), 10)
	card_type.autowrap_mode = TextServer.AUTOWRAP_OFF
	card_type.add_theme_color_override("font_color", tone.darkened(.15) if visual_style.printed_face else tone.lightened(.28))
	type_line.add_child(card_type)
	var type_push := Control.new()
	type_push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	type_line.add_child(type_push)
	var rarity := visual_context.styles.muted(str(data.get("rarity", "")), 10)
	rarity.autowrap_mode = TextServer.AUTOWRAP_OFF
	rarity.visible = not rarity.text.is_empty()
	type_line.add_child(rarity)
	column.add_child(type_line)
	if not data.get("identityTags", []).is_empty():
		column.add_child(visual_context.styles.caption(" · ".join(data.identityTags), 11, 160))
	for requirement in data.get("requirements", []):
		var requirement_label := visual_context.styles.caption(visual_context.translator.text("Requires: %s") % str(requirement.get("text", "")), 11, 160)
		requirement_label.add_theme_color_override("font_color", visual_context.styles.BLOOD if requirement.get("passed") == false else visual_context.styles.INK)
		column.add_child(requirement_label)
	var effects_title := visual_context.styles.muted(visual_context.translator.text("EFFECTS"), 10)
	effects_title.autowrap_mode = TextServer.AUTOWRAP_OFF
	effects_title.add_theme_color_override("font_color", visual_context.styles.MUTED.darkened(.05))
	column.add_child(effects_title)
	var rules := EffectText.new(visual_context)
	rules.name = "Rules"
	var rows: Array = data.get("effectRows", [])
	if rows.is_empty(): rows = EffectText.plain_rows(Array(data.get("effects", str(data.get("summary", "")).split("\n"))))
	rules.configure(rows, visual_style)
	rules.custom_minimum_size.y = 68
	rules.scroll_active = false
	rules.size_flags_vertical = Control.SIZE_EXPAND_FILL
	if visual_style.printed_face:
		var effect_panel := PanelContainer.new()
		effect_panel.name = "EffectPanel"
		effect_panel.set_meta("contrast_surface_role", "surface.effects")
		effect_panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
		effect_panel.add_theme_stylebox_override("panel", _compact_box(visual_style.effect_surface, Color.TRANSPARENT, 1, 0))
		effect_panel.add_child(rules)
		column.add_child(effect_panel)
	else: column.add_child(rules)
	if not data.get("behaviors", []).is_empty():
		column.add_child(visual_context.styles.caption(" · ".join(data.behaviors), 11, 160))
	var badges := _change_badges(data.get("changeBadges", []))
	if badges != null:
		column.add_child(badges)
	# Interaction guidance and playability belong to surrounding controls,
	# preview and inspection. The printed face contains card information only.
	_ignore_mouse(margin)
	var tooltip: Array[String] = [str(data.get("name", "")), _cost_accessible_text(data), str(data.get("summary", ""))]
	# Target previews and execution diagnostics belong to the separate combat
	# preview/persistent inspector, including for accessible tooltip text.
	for badge in data.get("changeBadges", []):
		var sources: Array = badge.get("sources", [])
		tooltip.append(str(badge.get("label", "")) + (": " + ", ".join(sources) if not sources.is_empty() else ""))
	tooltip_text = "\n".join(tooltip.filter(func(line): return not line.is_empty()))
	# Reconcile the passive face in place. Labels, rich text and art retain their
	# native layout rather than drawing a newly-created intermediate subtree.
	preload("res://scripts/ui/view_reconciler.gd").children(self, face)
	face.free()
	# Inspection can enrich a visible card after the REST response arrives. Theme
	# the replacement subtree before configure returns so no frame is drawn with
	# the unscaled base fonts.
	for child in get_children():
		visual_context.styles.apply_view_preferences(child, visual_context.text_scale, visual_context.high_contrast)

func _cost_zone(data: Dictionary) -> Control:
	var zone := HFlowContainer.new()
	zone.name = "Cost"
	zone.add_theme_constant_override("h_separation", 3)
	zone.add_theme_constant_override("v_separation", 3)
	var options: Array = data.get("costOptions", [])
	if options.size() > 1:
		for index in options.size():
			if index > 0: zone.add_child(visual_context.styles.title(visual_context.translator.text("OR"), 11))
			for j in options[index].size():
				if j > 0: zone.add_child(visual_context.styles.title("+", 11))
				zone.add_child(_cost_chip(options[index][j]))
			if options[index].is_empty(): zone.add_child(visual_context.styles.title("0", 12))
		return zone
	var costs: Array = data.get("costs", []) if data.get("costs") is Array else []
	for index in costs.size():
		if index > 0: zone.add_child(visual_context.styles.title("+", 11))
		zone.add_child(_cost_chip(costs[index]))
	if not bool(data.get("costsKnown", true)):
		zone.add_child(visual_context.styles.title(str(data.get("costUnknownLabel", visual_context.translator.text("Alternative cost — inspect"))), 11))
		return zone
	if costs.is_empty():
		var fallback := str(data.get("cost", ""))
		if fallback.is_empty(): fallback = "0"
		var neutral := PanelContainer.new()
		neutral.add_theme_stylebox_override("panel", _compact_box(visual_context.styles.color("surface.cost_unknown"), visual_context.styles.color("stroke.cost_unknown"), 12, 1))
		var label := visual_context.styles.muted(fallback, 11)
		label.autowrap_mode = TextServer.AUTOWRAP_OFF
		label.add_theme_color_override("font_color", visual_context.styles.INK)
		neutral.add_child(label)
		zone.add_child(neutral)
	return zone

func _cost_chip(cost: Dictionary) -> Control:
	var appearance: Dictionary = visual_context.resource_style(str(cost.get("resourceId", "")))
	if visual_style.printed_face:
		return CostSymbol.new(cost, appearance, visual_context, visual_style)
	var color := Color.from_string(str(appearance.get("cost_color", "")), visual_context.styles.color("resource.unknown"))
	var chip := PanelContainer.new()
	chip.add_theme_stylebox_override("panel", _compact_box(color.darkened(.58), color.lightened(.18), 12, 2))
	var amount: String = visual_context.translator.number(float(cost.get("amount", 0)))
	var label := visual_context.styles.muted("%s %s" % [cost.get("symbol", "•"), amount], 12)
	label.autowrap_mode = TextServer.AUTOWRAP_OFF
	label.add_theme_color_override("font_color", Color.WHITE)
	chip.tooltip_text = "%s: %s" % [str(cost.get("name", cost.get("resourceId", ""))), amount]
	chip.add_child(label)
	return chip

func _change_badges(items) -> Control:
	if not items is Array or items.is_empty():
		return null
	var row := HFlowContainer.new()
	row.name = "Changes"
	row.add_theme_constant_override("h_separation", 4)
	for index in mini(items.size(), visual_style.visible_change_badges):
		var item: Dictionary = items[index]
		var kind := str(item.get("kind", "modified"))
		var color: Color = {
			"upgrade": visual_context.styles.color("change.upgrade"), "buff": visual_context.styles.color("change.buff"),
			"debuff": visual_context.styles.color("change.debuff"), "modified": visual_context.styles.color("change.modified")
		}.get(kind, visual_context.styles.color("change.modified"))
		var badge := PanelContainer.new()
		if visual_style.printed_face: badge.set_meta("contrast_preserve_colors", true)
		badge.add_theme_stylebox_override("panel", _compact_box(color.darkened(.72), color, 4, 1))
		var label := visual_context.styles.muted("%s %s" % [item.get("symbol", "~"), item.get("label", "")], 9)
		label.autowrap_mode = TextServer.AUTOWRAP_OFF
		label.add_theme_color_override("font_color", color.lightened(.25))
		badge.tooltip_text = ", ".join(item.get("sources", []))
		badge.add_child(label)
		row.add_child(badge)
	if items.size() > visual_style.visible_change_badges:
		var remaining := visual_context.styles.muted("+%s" % (items.size() - visual_style.visible_change_badges), 10)
		remaining.autowrap_mode = TextServer.AUTOWRAP_OFF
		row.add_child(remaining)
	return row

func _make_custom_tooltip(for_text: String) -> Object:
	if for_text.is_empty(): return null
	var detail := preload("res://scripts/ui/card_details_panel.gd").new(visual_context)
	detail.configure(model, visual_style, detail.DisplayMode.TOOLTIP)
	return detail

func _cost_accessible_text(data: Dictionary) -> String:
	if not data.get("costsKnown", true): return str(data.get("costUnknownLabel", visual_context.translator.text("Alternative cost — inspect")))
	var parts: Array[String] = []
	for cost in data.get("costs", []):
		parts.append("%s %s" % [visual_context.translator.number(float(cost.get("amount", 0))), str(cost.get("name", cost.get("resourceId", "")))])
	if parts.is_empty():
		return str(data.get("cost", "0"))
	return visual_context.translator.text("Cost: %s") % " + ".join(parts)

func _compact_box(color: Color, border: Color, radius: int, width: int) -> StyleBoxFlat:
	var style := visual_context.styles.box(color, radius, border, width)
	style.content_margin_left = 6
	style.content_margin_right = 6
	style.content_margin_top = 2
	style.content_margin_bottom = 2
	return style

func _ignore_mouse(control: Control) -> void:
	control.mouse_filter = Control.MOUSE_FILTER_IGNORE
	for child in control.get_children():
		if child is Control: _ignore_mouse(child)

func _ready() -> void:
	pivot_offset = size * 0.5
	resized.connect(func(): pivot_offset = size * 0.5)
	mouse_entered.connect(func(): _emphasize(true))
	mouse_exited.connect(func(): _emphasize(chosen))
	focus_entered.connect(func(): _emphasize(true))
	focus_exited.connect(func(): _emphasize(chosen))
	var policy = visual_context.motion if visual_context.motion != null else visual_context.capture_motion()
	if animate_entry and not hand_managed and policy.duration("card_entry", true) > 0:
		modulate.a = 0.0
		scale = Vector2(.92, .92)
		motion = create_tween().set_parallel()
		motion.tween_property(self, "modulate:a", 1.0, policy.duration("card_entry", true))
		motion.tween_property(self, "scale", Vector2.ONE, policy.duration("card_entry", true)).set_trans(policy.curve("card_entry")).set_ease(policy.easing("card_entry"))

func select_card(value: bool) -> void:
	var changed := chosen != value
	chosen = value
	set_pressed_no_signal(value)
	if changed: _emphasize(value or is_hovered() or has_focus())

func _emphasize(value: bool) -> void:
	var keyline := get_node_or_null("PrintedKeyline")
	if keyline: keyline.queue_redraw()
	if hand_managed: return
	if motion:
		motion.kill()
	modulate.a = 1.0
	z_index = 2 if value else 0
	var policy = visual_context.motion if visual_context.motion != null else visual_context.capture_motion()
	if policy.duration("hover", true) <= 0:
		scale = Vector2.ONE
		return
	motion = create_tween()
	motion.tween_property(self, "scale", Vector2(1.035, 1.035) if value else Vector2.ONE,
		policy.duration("hover", true)).set_trans(policy.curve("hover")).set_ease(policy.easing("hover"))

func fly_to(destination: Vector2, overlay: Node, handle = null) -> void:
	var policy = visual_context.motion if visual_context.motion != null else visual_context.capture_motion()
	if policy.duration("card_flight", true) <= 0:
		if handle != null: handle.hit(); handle.finish()
		return
	var ghost := CardView.new(visual_context)
	ghost.animate_entry = false
	ghost.hand_managed = true
	ghost.visual_style = visual_style
	ghost.configure(model)
	ghost.process_mode = Node.PROCESS_MODE_PAUSABLE
	ghost.text = text
	ghost.autowrap_mode = autowrap_mode
	ghost.mouse_filter = Control.MOUSE_FILTER_IGNORE
	ghost.focus_mode = Control.FOCUS_NONE
	ghost.add_theme_stylebox_override("normal", get_theme_stylebox("normal"))
	overlay.add_child(ghost)
	if ghost.motion: ghost.motion.kill()
	ghost.modulate.a = 1.0
	ghost.global_position = global_position
	ghost.size = size
	ghost.pivot_offset = size * .5
	ghost.z_index = 100
	var tween := ghost.create_tween().set_parallel()
	if handle != null:
		handle.own(tween)
		handle.cleanup(func(): if is_instance_valid(ghost): ghost.queue_free())
		tween.finished.connect(func(): handle.hit(); handle.finish(), CONNECT_ONE_SHOT)
	tween.tween_property(ghost, "global_position", destination - size * .5, policy.duration("card_flight", true)).set_trans(policy.curve("card_flight")).set_ease(policy.easing("card_flight"))
	tween.tween_property(ghost, "scale", Vector2(.45, .45), policy.duration("card_flight", true))
	tween.tween_property(ghost, "modulate:a", 0.0, policy.duration("card_flight", true))
	var completion := FlightCompletion.new()
	tween.finished.connect(completion.complete)
	ghost.tree_exiting.connect(completion.complete)
	if handle != null: ghost.tree_exiting.connect(func(): handle.finish("cancelled"), CONNECT_ONE_SHOT)
	await completion.finished
	if is_instance_valid(ghost) and not ghost.is_queued_for_deletion(): ghost.queue_free()

class FlightCompletion extends RefCounted:
	signal finished
	var done := false
	func complete() -> void:
		if done: return
		done = true
		finished.emit()

class InkKeyline extends Control:
	var ink := Color("#20201c")
	func reconcile_from(other) -> void:
		ink = other.ink
		queue_redraw()
	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		focus_mode = Control.FOCUS_NONE
		resized.connect(queue_redraw)
	func _draw() -> void:
		var owner_card := get_parent() as CardView
		var width := 2.0 if owner_card and (owner_card.chosen or owner_card.has_focus()) else 1.0
		draw_rect(Rect2(Vector2(4, 4), (size - Vector2(8, 8)).max(Vector2.ZERO)), ink, false, width)

func _exit_tree() -> void:
	if motion: motion.kill()


func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
	visual_style = visual_context.card_style
	_context_card_style = visual_style

func artwork() -> TextureRect:
	# A single shared seam for cosmetic art effects; the printed face stays intact.
	for item in find_children("*", "TextureRect", true, false):
		if item.get_script() == preload("res://scripts/ui/art_slot.gd"): return item
	return null
