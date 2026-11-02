@tool
class_name CardDetailsView
extends PanelContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
const EffectText = preload("res://scripts/ui/card_effect_text.gd")
const LineScene = preload("res://scenes/ui/components/card_detail_line.tscn")
const RuleScene = preload("res://scenes/ui/components/card_rule_explanation.tscn")
enum DisplayMode { INSPECTION, TOOLTIP }
var visual_context: ContextType
@export var visual_style: CardVisualStyle = preload("res://data/themes/ivory_run_card_style.tres")
@export var preview_data: Dictionary = {"name": "Preview card", "cardType": "ATTACK", "rarityId": "common", "costs": [{"resourceId": "energy", "amount": 1}], "effects": ["Reduce 8 Health on a selected target."]}
var display_mode := DisplayMode.INSPECTION
var _model: Dictionary = {}
var _scroll: ScrollContainer
var _body: VBoxContainer
var _facts: VBoxContainer
var _classic: VBoxContainer
var _rules: RichTextLabel
var _surface: StyleBoxFlat
func _init(context = null) -> void:
	visual_context = context if context != null else ContextType.new()
static func create(context = null) -> CardDetailsView:
	var view = (load("res://scenes/ui/components/card_details_panel.tscn") as PackedScene).instantiate()
	if context != null: view.visual_context = context
	return view
func _ready() -> void:
	_bind_nodes()
	if _model.is_empty() and (Engine.is_editor_hint() or get_parent() == get_tree().root): configure(preview_data, visual_style)
func _bind_nodes() -> void:
	if _body != null: return
	_scroll = get_node("%CardDetailsScroll"); _body = get_node("%CardDetailContent")
	_facts = get_node("%Facts"); _classic = get_node("%Classic"); _rules = get_node("%DetailedEffects")
	_surface = get_theme_stylebox("panel").duplicate()
	add_theme_stylebox_override("panel", _surface)
func _notification(what: int) -> void:
	if what != NOTIFICATION_PREDELETE: return
	# Presentation variants detach existing authored nodes; release the inactive ones.
	for node in [_scroll, _facts, _classic]:
		if is_instance_valid(node) and node.get_parent() == null: node.free()
func _attach(node: Node, parent: Node) -> void:
	if node is CanvasItem: node.visible = true
	if Engine.is_editor_hint(): return # Keep authored ownership intact when the editor saves.
	if node.get_parent() == parent: return
	if node.get_parent() != null:
		node.owner = null
		node.reparent(parent, false)
		node.owner = self
	else:
		node.owner = null
		parent.add_child(node)
		node.owner = self
func _detach(node: Node) -> void:
	if Engine.is_editor_hint():
		if node is CanvasItem: node.visible = false
		return
	if node.get_parent() != null:
		node.owner = null
		node.get_parent().remove_child(node)
func _clear(host: Node) -> void:
	for child in host.get_children(): host.remove_child(child); child.queue_free()
func _line(text: String, size := 14) -> Label:
	var label: Label = LineScene.instantiate()
	label.text = text; label.add_theme_font_size_override("font_size", size)
	return label
func _lines(host: Node, lines: Array) -> void:
	_clear(host); host.visible = not lines.is_empty()
	for item in lines:
		if item is Array: host.add_child(_line(str(item[0]), int(item[1])))
		else: host.add_child(_line(str(item)))
func configure(model: Dictionary, style: Resource, mode := DisplayMode.INSPECTION) -> void:
	_bind_nodes(); _model = model.duplicate(true)
	theme = visual_context.theme; visual_style = style; display_mode = mode
	name = "CardTooltip" if mode == DisplayMode.TOOLTIP else "CardDetails"
	set_meta("semantic_border_color", style.rarity_color(str(model.get("rarityId", ""))))
	if has_meta("base_style_panel"): remove_meta("base_style_panel")
	if has_meta("visual_preferences_revision"): remove_meta("visual_preferences_revision")
	_surface.bg_color = visual_context.styles.PANEL
	_surface.border_color = get_meta("semantic_border_color"); _surface.set_corner_radius_all(style.face_radius)
	_surface.set_meta("background_role", "surface.panel")
	custom_minimum_size = Vector2(style.tooltip_width, 0 if mode == DisplayMode.TOOLTIP else style.inspection_height)
	mouse_filter = Control.MOUSE_FILTER_STOP; _body.mouse_filter = Control.MOUSE_FILTER_PASS
	if mode == DisplayMode.TOOLTIP:
		_attach(_body, self); _detach(_scroll)
	else:
		_attach(_scroll, self); _attach(_body, _scroll)
		_scroll.focus_mode = Control.FOCUS_ALL
	var glossary: Array = preload("res://scripts/presentation/card_glossary_presenter.gd").new(visual_context.translator).describe(model)
	var glossary_host = _body.get_node("Glossary")
	_clear(glossary_host); glossary_host.visible = not glossary.is_empty()
	for term in glossary:
		var entry = RuleScene.instantiate()
		entry.name = "RuleExplanation_" + str(term.name).validate_node_name()
		entry.get_node("RuleName").text = str(term.name)
		entry.get_node("RuleName").add_theme_color_override("font_color", visual_context.styles.INK)
		entry.get_node("Description").text = str(term.description)
		glossary_host.add_child(entry)
	if model.get("presentationFacts") is Dictionary:
		_detach(_classic); _attach(_facts, _body)
		var descriptions: Array = preload("res://scripts/presentation/card_calculation_presenter.gd").new(visual_context.translator).describe(model.presentationFacts, mode == DisplayMode.TOOLTIP)
		var heading = _facts.get_node("ScalingHeading")
		heading.text = visual_context.translator.text("Scaling"); heading.visible = not descriptions.is_empty()
		_lines(_facts.get_node("ScalingLines"), descriptions)
		_facts.get_node("EmptyFacts").text = visual_context.translator.text("No additional effects or scaling.")
		_facts.get_node("EmptyFacts").visible = descriptions.is_empty() and glossary.is_empty()
		var extra: Array = []; var seen: Array[String] = []
		if mode == DisplayMode.INSPECTION:
			for change in model.presentationFacts.get("permanentChanges", []):
				var identity := str(change.get("transformationId", "")) + "/" + str(change.get("upgradeId", ""))
				if identity in seen: continue
				seen.append(identity)
				extra.append(visual_context.translator.text("Base upgrade" if str(change.get("category", "")) == "Base" else "Card modifier") + ": " + visual_context.translator.content_name(str(change.get("upgradeId", ""))))
			for requirement in model.get("requirements", []): extra.append(visual_context.translator.text("Requires: %s") % str(requirement.get("text", "")))
			for behavior in model.get("behaviors", []): extra.append([str(behavior),13])
		_lines(_facts.get_node("FactExtras"), extra)
		_wrap_detail_labels(_body)
		visual_context.styles.apply_view_preferences(self)
		if mode == DisplayMode.TOOLTIP: _disable_tooltip_input(self)
		return
	_detach(_facts); _attach(_classic, _body)
	_classic.get_node("PrintedIdentity/DetailTitle").text = str(model.get("name", ""))
	_classic.get_node("PrintedIdentity/TypeRarity").text = " · ".join([str(model.get("cardType", "")), str(model.get("rarity", ""))])
	var options: Array = model.get("costOptions", [])
	if options.is_empty(): options = [model.get("costs", [])]
	var payments: Array[String] = []
	for option in options:
		var parts: Array[String] = []
		for cost in option: parts.append("%s %s" % [visual_context.translator.number(float(cost.get("amount", 0))), str(cost.get("name", cost.get("resourceId", "")))])
		payments.append(" + ".join(parts) if not parts.is_empty() else (str(model.get("cost", "0")) if model.get("costsKnown", true) else str(model.get("costUnknownLabel", visual_context.translator.text("Alternative cost — inspect")))))
	_classic.get_node("PrintedIdentity/CostSummary").text = visual_context.translator.text("Cost: %s") % (" " + visual_context.translator.text("OR") + " ").join(payments)
	var unknown = _classic.get_node("PrintedIdentity/UnknownCost")
	unknown.visible = not model.get("costsKnown", true)
	unknown.text = str(model.get("costUnknownLabel", visual_context.translator.text("Alternative cost — inspect")))
	var requirements: Array = model.get("requirements", [])
	if mode == DisplayMode.TOOLTIP: requirements = requirements.slice(0,1)
	_lines(_classic.get_node("DetailRequirements"), requirements.map(func(item): return visual_context.translator.text("Requires: %s") % str(item.get("text", ""))))
	_rules.visual_context = visual_context; _rules.fit_content = mode == DisplayMode.INSPECTION
	_rules.scroll_active = false; _rules.custom_minimum_size.y = 0
	var rows: Array = model.get("effectRows", [])
	if rows.is_empty(): rows = EffectText.plain_rows(Array(model.get("effects", str(model.get("summary", "")).split("\n"))))
	if mode == DisplayMode.TOOLTIP: rows = rows.slice(0,maxi(1,style.tooltip_effect_rows))
	_rules.configure(rows,style)
	var effect_panel = _classic.get_node("EffectPanel")
	var effect_style = effect_panel.get_theme_stylebox("panel").duplicate()
	effect_style.bg_color = style.effect_surface
	effect_panel.add_theme_stylebox_override("panel",effect_style)
	var extra: Array = []
	if mode == DisplayMode.INSPECTION:
		for row in rows:
			for segment in row.get("segments", []):
				if (segment.get("value") is float or segment.get("value") is int) and (segment.get("baseValue") is float or segment.get("baseValue") is int):
					extra.append([visual_context.translator.text("Base: %s → Current: %s") % [visual_context.translator.number(float(segment.baseValue)),visual_context.translator.number(float(segment.value))],12])
		for behavior in model.get("behaviors", []): extra.append([str(behavior),13])
		for badge in model.get("changeBadges", []): extra.append([str(badge.get("label", "")) + ": " + ", ".join(badge.get("sources", [])),13])
		for field in ["previewNote","availability","inspectionText"]:
			if not str(model.get(field,"" )).is_empty(): extra.append([str(model[field]),13])
	else:
		if not model.get("behaviors",[]).is_empty(): extra.append([" · ".join(model.behaviors),13])
		var changes: Array[String] = []
		for badge in model.get("changeBadges",[]).slice(0,maxi(0,style.visible_change_badges)): changes.append(str(badge.get("label","")))
		if not changes.is_empty(): extra.append([" · ".join(changes),13])
		for field in ["previewNote","availability"]:
			if not str(model.get(field,"")).is_empty(): extra.append([str(model[field]),13])
	_lines(_classic.get_node("DetailExtras"),extra)
	var hint = _classic.get_node("InspectionHint")
	hint.text = visual_context.translator.text("Summary — open card inspection for full details.")
	hint.visible = mode == DisplayMode.TOOLTIP
	_wrap_detail_labels(_body)
	if mode == DisplayMode.TOOLTIP:
		_bound_labels(_body)
	visual_context.styles.apply_view_preferences(self)
	if mode == DisplayMode.TOOLTIP:
		var font := _rules.get_theme_font("normal_font")
		var font_size := _rules.get_theme_font_size("normal_font_size")
		_rules.custom_minimum_size.y = ceilf(font.get_height(font_size)) * maxi(1,style.tooltip_effect_lines)
		_disable_tooltip_input(self)
func _wrap_detail_labels(node: Node) -> void:
	if node is Label:
		visual_context.styles.wrap(node,260); node.max_lines_visible = -1
	for child in node.get_children(): _wrap_detail_labels(child)
func _bound_labels(node: Node) -> void:
	if node is Label:
		node.max_lines_visible = maxi(1,visual_style.tooltip_label_lines)
		node.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	for child in node.get_children(): _bound_labels(child)
func _disable_tooltip_input(node: Node) -> void:
	if node is RichTextLabel: node.selection_enabled = false
	if node is Control:
		node.mouse_filter = Control.MOUSE_FILTER_IGNORE; node.focus_mode = Control.FOCUS_NONE; node.tooltip_text = ""
	for child in node.get_children(): _disable_tooltip_input(child)
