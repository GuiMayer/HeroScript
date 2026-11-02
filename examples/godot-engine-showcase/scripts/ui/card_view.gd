@tool
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
@export var preview_data: Dictionary = {"definitionId": "basic_attack", "name": "Flame Strike", "tone": "attack", "rarityId": "uncommon", "rarity": "Uncommon", "costs": [{"resourceId": "energy", "amount": 1}], "effects": ["Deal 8 damage.", "Apply 2 Burn."]}
@export var animate_entry := true
@export var hand_managed := false
var configuration_count := 0
var _configuration_key := ""
var _context_card_style: CardVisualStyle
@export var use_profile_metrics := true
@export var visual_style: CardVisualStyle = preload("res://data/default_card_visual_style.tres")
const EffectText = preload("res://scripts/ui/card_effect_text.gd")
const CostSymbol = preload("res://scripts/ui/card_cost_symbol.gd")

func configure(data: Dictionary) -> void:
	theme = visual_context.theme
	# Follow profile changes unless the consumer explicitly injected another policy.
	if visual_style == _context_card_style or visual_style == preload("res://data/default_card_visual_style.tres"):
		_context_card_style = visual_context.card_style
		visual_style = _context_card_style
	# Diagnostics belong to persistent inspection, not the printed face. Keep
	# the latest model without rebuilding the card for invisible journal changes.
	model = data.duplicate(true)
	var face_data := data.duplicate(true)
	face_data.erase("inspectionText")
	face_data.erase("presentationFacts") # Version/impact traces affect inspection, not the printed face.
	face_data.erase("availability")
	var style_key: String = visual_style.presentation_key() if visual_style and visual_style.has_method("presentation_key") else ""
	var ctx_key: String = visual_context.presentation_key() if visual_context and visual_context.has_method("presentation_key") else ""
	var presentation_key: String = JSON.stringify(face_data) + style_key + ctx_key
	if presentation_key == _configuration_key: return
	_configuration_key = presentation_key
	configuration_count += 1
	if has_meta("visual_preferences_revision"): remove_meta("visual_preferences_revision")
	text = ""
	_bind_face()
	custom_minimum_size = visual_style.effective_size(visual_context.text_scale) if use_profile_metrics else custom_minimum_size
	var tone: Color = {"attack": visual_context.styles.BLOOD, "power": visual_context.styles.color("accent.power")}.get(str(data.get("tone", "skill")), visual_context.styles.TEAL)
	var border: Color = visual_style.rarity_color(str(data.get("rarityId", "")))
	set_meta("semantic_border_color", border)
	for key in get_meta_list():
		if str(key).begins_with("base_style_"): remove_meta(key)
	for state in ["normal", "disabled", "pressed", "hover", "focus"]:
		var box: StyleBoxFlat = get_theme_stylebox(state).duplicate()
		box.bg_color = Color.TRANSPARENT if state == "focus" else (visual_context.styles.PANEL if visual_style.printed_face else tone.darkened(.62 if state in ["pressed", "hover"] else .76))
		box.border_color = border
		box.set_corner_radius_all(visual_style.face_radius)
		add_theme_stylebox_override(state, box)
	var keyline = get_node("%PrintedKeyline")
	keyline.visible = visual_style.printed_face
	keyline.ink = visual_context.styles.INK; keyline.style = visual_style.keyline_style; keyline.queue_redraw()
	if use_profile_metrics:
		get_node("%CardHeader").custom_minimum_size.y = ceilf(visual_style.title_line_height * visual_context.text_scale)
		get_node("%Art").custom_minimum_size.y = visual_style.art_height
	get_node("%CardTitle").configure(str(data.get("name", "")), visual_context, visual_style)
	get_node("%UpgradeMarker").visible = bool(data.get("upgraded", false))
	_cost_zone(data)
	var art_frame = get_node("%Art")
	var art_style: StyleBoxFlat = art_frame.get_theme_stylebox("panel").duplicate()
	art_style.bg_color = visual_context.styles.color("surface.card_art"); art_style.border_color = tone.darkened(.28)
	art_frame.add_theme_stylebox_override("panel", art_style)
	var art = get_node("%CardArtwork")
	art.rebind_context(visual_context); art.setup("cards", str(data.get("definitionId", "")))
	var placeholder: bool = art.is_placeholder and bool(data.get("artPlaceholder", true))
	var name_text: String = str(data.get("name", "")).trim_suffix(" +").to_upper()
	get_node("%ArtPlaceholderName").visible = placeholder and visual_style.printed_face
	get_node("%ArtPlaceholderName").text = name_text
	get_node("%ArtOverlayName").visible = placeholder and not visual_style.printed_face
	get_node("%ArtOverlayName").text = name_text
	get_node("%CardType").text = str(data.get("cardType", str(data.get("tone", "skill")).to_upper()))
	get_node("%CardType").add_theme_color_override("font_color", tone.darkened(.15) if visual_style.printed_face else tone.lightened(.28))
	get_node("%Rarity").text = str(data.get("rarity", "")); get_node("%Rarity").visible = not str(data.get("rarity", "")).is_empty()
	get_node("%IdentityTags").text = " · ".join(data.get("identityTags", []))
	get_node("%IdentityTags").visible = not data.get("identityTags", []).is_empty()
	var requirements = get_node("%Requirements")
	_clear_items(requirements)
	for requirement in data.get("requirements", []):
		var line = _inline(visual_context.translator.text("Requires: %s") % str(requirement.get("text", "")), 11)
		line.custom_minimum_size.x = 160; line.clip_text = true; line.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		line.add_theme_color_override("font_color", visual_context.styles.BLOOD if requirement.get("passed") == false else visual_context.styles.INK)
		requirements.add_child(line)
	requirements.visible = requirements.get_child_count() > 0
	get_node("%EffectsTitle").text = visual_context.translator.text("EFFECTS")
	get_node("%EffectsTitle").add_theme_color_override("font_color", visual_context.styles.MUTED.darkened(.05))
	var rules = get_node("%Rules"); rules.visual_context = visual_context
	var rows: Array = data.get("effectRows", [])
	if rows.is_empty(): rows = EffectText.plain_rows(Array(data.get("effects", str(data.get("summary", "")).split("\n"))))
	rules.configure(rows, visual_style)
	var effect_panel = get_node("%EffectPanel")
	var effect_style: StyleBoxFlat = effect_panel.get_theme_stylebox("panel").duplicate()
	effect_style.bg_color = visual_style.effect_surface if visual_style.printed_face else Color.TRANSPARENT
	if not visual_style.printed_face:
		for side in [SIDE_LEFT, SIDE_RIGHT, SIDE_TOP, SIDE_BOTTOM]: effect_style.set_content_margin(side, 0)
	effect_panel.add_theme_stylebox_override("panel", effect_style)
	get_node("%Behaviors").text = " · ".join(data.get("behaviors", [])); get_node("%Behaviors").visible = not data.get("behaviors", []).is_empty()
	_change_badges(data.get("changeBadges", []))
	var tooltip: Array[String] = [str(data.get("name", "")), _cost_accessible_text(data), str(data.get("summary", ""))]
	for badge in data.get("changeBadges", []):
		var sources: Array = badge.get("sources", [])
		tooltip.append(str(badge.get("label", "")) + (": " + ", ".join(sources) if not sources.is_empty() else ""))
	tooltip_text = "\n".join(tooltip.filter(func(line): return not line.is_empty()))
	for child in get_children(): visual_context.styles.apply_view_preferences(child, visual_context.text_scale, visual_context.high_contrast)

static func create(context = null) -> CardView:
	var card = (load("res://scenes/ui/components/card_view.tscn") as PackedScene).instantiate()
	if context != null: card.visual_context = context
	return card

func _bind_face() -> void:
	assert(has_node("%Rules"), "Instantiate CardView through its PackedScene/create()")

func _clear_items(parent: Node) -> void:
	for child in parent.get_children():
		parent.remove_child(child); child.queue_free()

func _inline(value: String, point_size: int) -> Label:
	var label = (load("res://scenes/ui/components/card_inline_label.tscn") as PackedScene).instantiate()
	label.text = value; label.add_theme_font_size_override("font_size", point_size)
	label.add_theme_color_override("font_color", visual_context.styles.INK)
	return label

func _cost_zone(data: Dictionary) -> Control:
	var line = get_node("%CostLine")
	line.configure(maxf(visual_style.cost_symbol_size.x, visual_style.cost_symbol_size.y) * visual_context.text_scale)
	var zone: HBoxContainer = line.content
	_clear_items(zone)
	var options: Array = data.get("costOptions", [])
	if options.size() > 1:
		for index in options.size():
			if index > 0: zone.add_child(_inline(visual_context.translator.text("OR"), 11))
			for j in options[index].size():
				if j > 0: zone.add_child(_inline("+", 11))
				zone.add_child(_cost_chip(options[index][j]))
			if options[index].is_empty(): zone.add_child(_inline("0", 12))
		return line
	var costs: Array = data.get("costs", []) if data.get("costs") is Array else []
	for index in costs.size():
		if index > 0: zone.add_child(_inline("+", 11))
		zone.add_child(_cost_chip(costs[index]))
	if not bool(data.get("costsKnown", true)):
		zone.add_child(_inline(str(data.get("costUnknownLabel", visual_context.translator.text("Alternative cost — inspect"))), 11))
		return line
	if costs.is_empty():
		var fallback := str(data.get("cost", ""))
		if fallback.is_empty(): fallback = "0"
		var neutral = (load("res://scenes/ui/components/card_cost_chip.tscn") as PackedScene).instantiate()
		var style: StyleBoxFlat = neutral.get_theme_stylebox("panel").duplicate()
		style.bg_color = visual_context.styles.color("surface.cost_unknown"); style.border_color = visual_context.styles.color("stroke.cost_unknown")
		neutral.add_theme_stylebox_override("panel", style)
		neutral.get_node("Label").text = fallback
		neutral.get_node("Label").add_theme_color_override("font_color", visual_context.styles.INK)
		zone.add_child(neutral)
	return line

func _cost_chip(cost: Dictionary) -> Control:
	var appearance: Dictionary = visual_context.resource_style(str(cost.get("resourceId", "")))
	if visual_style.printed_face:
		return CostSymbol.create(cost, appearance, visual_context, visual_style)
	var color := Color.from_string(str(appearance.get("cost_color", "")), visual_context.styles.color("resource.unknown"))
	var chip = (load("res://scenes/ui/components/card_cost_chip.tscn") as PackedScene).instantiate()
	var style: StyleBoxFlat = chip.get_theme_stylebox("panel").duplicate()
	style.bg_color = color.darkened(.58); style.border_color = color.lightened(.18)
	chip.add_theme_stylebox_override("panel", style)
	var label: Label = chip.get_node("Label")
	label.text = "%s %s" % [cost.get("symbol", "•"), visual_context.translator.number(float(cost.get("amount", 0)))]
	label.add_theme_color_override("font_color", Color.WHITE)
	chip.tooltip_text = "%s: %s" % [str(cost.get("name", cost.get("resourceId", ""))), visual_context.translator.number(float(cost.get("amount", 0)))]
	return chip

func _change_badges(items) -> Control:
	var row = get_node("%Changes")
	_clear_items(row)
	row.visible = items is Array and not items.is_empty()
	if not row.visible: return row
	for index in mini(items.size(), visual_style.visible_change_badges):
		var item: Dictionary = items[index]
		var kind := str(item.get("kind", "modified"))
		var color: Color = {"upgrade": visual_context.styles.color("change.upgrade"), "buff": visual_context.styles.color("change.buff"), "debuff": visual_context.styles.color("change.debuff")}.get(kind, visual_context.styles.color("change.modified"))
		var badge = (load("res://scenes/ui/components/card_change_badge.tscn") as PackedScene).instantiate()
		if visual_style.printed_face: badge.set_meta("contrast_preserve_colors", true)
		var style: StyleBoxFlat = badge.get_theme_stylebox("panel").duplicate()
		style.bg_color = color.darkened(.72); style.border_color = color
		badge.add_theme_stylebox_override("panel", style)
		var label = badge.get_node("Label")
		label.text = "%s %s" % [item.get("symbol", "~"), item.get("label", "")]
		label.add_theme_color_override("font_color", color.lightened(.25))
		badge.tooltip_text = ", ".join(item.get("sources", [])); row.add_child(badge)
	if items.size() > visual_style.visible_change_badges: row.add_child(_inline("+%s" % (items.size() - visual_style.visible_change_badges), 10))
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

func _ready() -> void:
	if Engine.is_editor_hint() and model.is_empty():
		if visual_context == null:
			visual_context = preload("res://scripts/ui/visual_context.gd").new()
		configure(preview_data)
	if Engine.is_editor_hint(): return
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
	var ghost := CardView.create(visual_context)
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

func _exit_tree() -> void:
	if motion: motion.kill()


func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
	_context_card_style = null

func artwork() -> TextureRect:
	# A single shared seam for cosmetic art effects; the printed face stays intact.
	for item in find_children("*", "TextureRect", true, false):
		if item.get_script() == preload("res://scripts/ui/art_slot.gd"): return item
	return null
