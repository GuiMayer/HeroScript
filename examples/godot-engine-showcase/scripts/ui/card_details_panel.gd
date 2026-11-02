extends PanelContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
## Shared visual policy with explicit passive-summary and persistent-inspection modes.
const EffectText = preload("res://scripts/ui/card_effect_text.gd")
enum DisplayMode { INSPECTION, TOOLTIP }
var visual_style: Resource
var display_mode := DisplayMode.INSPECTION

func configure(model: Dictionary, style: Resource, mode := DisplayMode.INSPECTION) -> void:
	# The hand reuses an open tooltip when its active instance changes.
	for child in get_children():
		remove_child(child)
		child.queue_free()
	theme = visual_context.theme
	visual_style = style
	display_mode = mode
	name = "CardTooltip" if mode == DisplayMode.TOOLTIP else "CardDetails"
	var border: Color = style.rarity_color(str(model.get("rarityId", "")))
	set_meta("semantic_border_color", border)
	add_theme_stylebox_override("panel", visual_context.styles.box(visual_context.styles.PANEL, style.face_radius, border, 2))
	custom_minimum_size = Vector2(style.tooltip_width, 0 if mode == DisplayMode.TOOLTIP else style.inspection_height)
	var body := VBoxContainer.new()
	body.name = "CardDetailContent"
	body.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	body.add_theme_constant_override("separation", 8)
	if mode == DisplayMode.TOOLTIP:
		# A native tooltip is transient. It must not offer scrollbars, selection or
		# focusable controls that cannot be reached while leaving the hovered card.
		add_child(body)
	else:
		var scroll := ScrollContainer.new()
		scroll.name = "CardDetailsScroll"
		scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
		scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
		scroll.focus_mode = Control.FOCUS_ALL
		scroll.follow_focus = true
		add_child(scroll)
		scroll.add_child(body)
	var glossary: Array = preload("res://scripts/presentation/card_glossary_presenter.gd").new(visual_context.translator).describe(model)
	for term in glossary:
		var entry := VBoxContainer.new()
		entry.name = "RuleExplanation_" + str(term.name).validate_node_name()
		entry.add_child(visual_context.styles.title(str(term.name), 16))
		entry.add_child(visual_context.styles.caption(str(term.description), 14, 260))
		body.add_child(entry)
	if model.get("presentationFacts") is Dictionary:
		var descriptions: Array[String] = preload("res://scripts/presentation/card_calculation_presenter.gd").new(visual_context.translator).describe(model.presentationFacts, mode == DisplayMode.TOOLTIP)
		if not descriptions.is_empty(): body.add_child(visual_context.styles.title(visual_context.translator.text("Scaling"), 17))
		for line in descriptions: body.add_child(visual_context.styles.caption(line, 14, 260))
		if body.get_child_count() == 0: body.add_child(visual_context.styles.caption(visual_context.translator.text("No additional effects or scaling."), 14, 260))
		if mode == DisplayMode.INSPECTION:
			var changes: Array[String] = []
			var seen: Array[String] = []
			for change in model.presentationFacts.get("permanentChanges", []):
				var identity := str(change.get("transformationId", "")) + "/" + str(change.get("upgradeId", ""))
				if identity in seen: continue
				seen.append(identity)
				changes.append(visual_context.translator.text("Base upgrade" if str(change.get("category", "")) == "Base" else "Card modifier") + ": " + visual_context.translator.content_name(str(change.get("upgradeId", ""))))
			for line in changes: body.add_child(visual_context.styles.caption(line, 14, 260))
			for requirement in model.get("requirements", []): body.add_child(visual_context.styles.caption(visual_context.translator.text("Requires: %s") % str(requirement.get("text", "")), 14, 260))
			for behavior in model.get("behaviors", []): body.add_child(visual_context.styles.caption(str(behavior), 13, 260))
		else:
			_disable_tooltip_input(self)
		_wrap_detail_labels(body)
		visual_context.styles.apply_view_preferences(self)
		return
	body.add_child(visual_context.styles.title(str(model.get("name", "")), 22))
	body.add_child(visual_context.styles.caption(" · ".join([str(model.get("cardType", "")), str(model.get("rarity", ""))]), 13, 260))
	var options: Array = model.get("costOptions", [])
	if options.is_empty(): options = [model.get("costs", [])]
	var payments: Array[String] = []
	for option in options:
		var parts: Array[String] = []
		for cost in option: parts.append("%s %s" % [visual_context.translator.number(float(cost.get("amount", 0))), str(cost.get("name", cost.get("resourceId", "")))])
		payments.append(" + ".join(parts) if not parts.is_empty() else (str(model.get("cost", "0")) if model.get("costsKnown", true) else str(model.get("costUnknownLabel", visual_context.translator.text("Alternative cost — inspect")))))
	body.add_child(visual_context.styles.caption(visual_context.translator.text("Cost: %s") % (" " + visual_context.translator.text("OR") + " ").join(payments), 14, 260))
	if not model.get("costsKnown", true): body.add_child(visual_context.styles.caption(str(model.get("costUnknownLabel", visual_context.translator.text("Alternative cost — inspect"))), 14, 260))
	var requirements: Array = model.get("requirements", [])
	if mode == DisplayMode.TOOLTIP: requirements = requirements.slice(0, 1)
	for requirement in requirements: body.add_child(visual_context.styles.caption(visual_context.translator.text("Requires: %s") % str(requirement.get("text", "")), 14, 260))
	var rules := EffectText.new(visual_context)
	rules.name = "DetailedEffects"
	rules.fit_content = mode == DisplayMode.INSPECTION
	rules.scroll_active = false
	rules.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var rows: Array = model.get("effectRows", [])
	if rows.is_empty(): rows = EffectText.plain_rows(Array(model.get("effects", str(model.get("summary", "")).split("\n"))))
	if mode == DisplayMode.TOOLTIP: rows = rows.slice(0, maxi(1, style.tooltip_effect_rows))
	rules.configure(rows, style)
	var effect_panel := PanelContainer.new()
	effect_panel.name = "EffectPanel"
	effect_panel.set_meta("contrast_surface_role", "surface.effects")
	effect_panel.add_theme_stylebox_override("panel", visual_context.styles.box(style.effect_surface, 1))
	effect_panel.add_child(rules)
	body.add_child(effect_panel)
	if mode == DisplayMode.TOOLTIP:
		_build_tooltip_summary(body, rules, model)
		return
	for row in rows:
		for segment in row.get("segments", []):
			if (segment.get("value") is float or segment.get("value") is int) and (segment.get("baseValue") is float or segment.get("baseValue") is int):
				body.add_child(visual_context.styles.caption(visual_context.translator.text("Base: %s → Current: %s") % [visual_context.translator.number(float(segment.baseValue)), visual_context.translator.number(float(segment.value))], 12, 260))
	for behavior in model.get("behaviors", []): body.add_child(visual_context.styles.caption(str(behavior), 13, 260))
	for badge in model.get("changeBadges", []):
		body.add_child(visual_context.styles.caption(str(badge.get("label", "")) + ": " + ", ".join(badge.get("sources", [])), 13, 260))
	for field in ["previewNote", "availability", "inspectionText"]:
		if not str(model.get(field, "")).is_empty(): body.add_child(visual_context.styles.caption(str(model[field]), 13, 260))
	# Detailed inspection must wrap rather than silently ellipsize descriptions.
	for child in body.get_children():
		if child is Label: visual_context.styles.wrap(child, 260)
	visual_context.styles.apply_view_preferences(self)

func _wrap_detail_labels(node: Node) -> void:
	# Captions default to one clipped line. Rule names/descriptions can be nested
	# and translated, so every label must wrap and contribute its full height.
	if node is Label:
		visual_context.styles.wrap(node, 260)
		node.max_lines_visible = -1
	for child in node.get_children(): _wrap_detail_labels(child)

func _build_tooltip_summary(body: VBoxContainer, rules: RichTextLabel, model: Dictionary) -> void:
	# Logs, contribution sources, comparisons and technical data belong only to
	# the persistent inspector. Large content cannot make this tooltip grow forever.
	if not model.get("behaviors", []).is_empty(): body.add_child(visual_context.styles.caption(" · ".join(model.behaviors), 13, 260))
	var changes: Array[String] = []
	for badge in model.get("changeBadges", []).slice(0, maxi(0, visual_style.visible_change_badges)):
		changes.append(str(badge.get("label", "")))
	if not changes.is_empty(): body.add_child(visual_context.styles.caption(" · ".join(changes), 13, 260))
	for field in ["previewNote", "availability"]:
		if not str(model.get(field, "")).is_empty(): body.add_child(visual_context.styles.caption(str(model[field]), 13, 260))
	var hint := visual_context.styles.caption(visual_context.translator.text("Summary — open card inspection for full details."), 12, 260)
	hint.name = "InspectionHint"
	body.add_child(hint)
	for child in body.get_children():
		if child is Label:
			visual_context.styles.wrap(child, 260)
			child.max_lines_visible = maxi(1, visual_style.tooltip_label_lines)
			child.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	visual_context.styles.apply_view_preferences(self)
	var font := rules.get_theme_font("normal_font")
	var font_size := rules.get_theme_font_size("normal_font_size")
	rules.custom_minimum_size.y = ceilf(font.get_height(font_size)) * maxi(1, visual_style.tooltip_effect_lines)
	_disable_tooltip_input(self)

func _disable_tooltip_input(node: Node) -> void:
	# RichTextLabel adjusts its focus mode when selection_enabled changes. Apply
	# the passive focus policy AFTER that setter, not before it.
	if node is RichTextLabel: node.selection_enabled = false
	if node is Control:
		node.mouse_filter = Control.MOUSE_FILTER_IGNORE
		node.focus_mode = Control.FOCUS_NONE
		node.tooltip_text = ""
	for child in node.get_children(): _disable_tooltip_input(child)


func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
