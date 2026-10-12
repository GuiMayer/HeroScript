extends PanelContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
## Passive actor view: the portrait is a target, not just a small text button.
signal target_selected(actor_id: String)
var portrait_view: Control
var target_button: Button
var _battlefield_content: VBoxContainer
var _projection := []
var _model: Dictionary = {}
func presentation_model() -> Dictionary: return _model.duplicate(true)

func apply_model(data: Dictionary) -> void:
	setup(data.get("actor", {}), bool(data.get("hostile", false)), data.get("presentation", {}), str(data.get("intent", "")), bool(data.get("compact", false)), bool(data.get("battlefield", false)))

func reconcile_from(other) -> void:
	visual_context = other.visual_context
	apply_model(other.presentation_model())

func setup(actor: Dictionary, hostile: bool, presentation: Dictionary, intent: String, compact := false, battlefield := false) -> void:
	theme = visual_context.theme
	_model = {"actor": actor.duplicate(true), "hostile": hostile, "presentation": presentation.duplicate(true), "intent": intent, "compact": compact, "battlefield": battlefield}
	var projection: Array = [actor, hostile, presentation, intent, compact, battlefield, visual_context.translator.locale, visual_context.profile.presentation_key(), visual_context.get_instance_id()]
	if projection == _projection: return
	_projection = projection.duplicate(true)
	if get_child_count() > 0:
		var desired = get_script().new(visual_context)
		desired.setup(actor, hostile, presentation, intent, compact, battlefield)
		preload("res://scripts/ui/view_reconciler.gd").patch_contents(self, desired)
		desired.free()
		return
	var root_content: BoxContainer = HBoxContainer.new() if compact else VBoxContainer.new()
	root_content.add_theme_constant_override("separation", 8)
	var content := VBoxContainer.new()
	content.custom_minimum_size.x = 264 if compact else 258
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	if battlefield:
		_battlefield_content = content
		size_flags_vertical = Control.SIZE_EXPAND_FILL
		root_content.size_flags_vertical = Control.SIZE_EXPAND_FILL
		content.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content.add_theme_constant_override("separation", 3 if compact else 5)
	var intention := Label.new()
	var relation := str(actor.get("relationship", "Enemy" if hostile else "Ally"))
	var affiliation := "YOUR SIDE" if relation == "Ally" else ("No intent revealed" if relation == "Enemy" else "NEUTRAL")
	intention.text = visual_context.translator.text("INTENT  •  %s") % intent if not intent.is_empty() else visual_context.translator.text(affiliation)
	intention.tooltip_text = intention.text
	intention.custom_minimum_size.y = 36 if battlefield else 48
	intention.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	intention.max_lines_visible = 2 if compact or battlefield else 3
	intention.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	intention.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	intention.add_theme_font_size_override("font_size", 14)
	intention.add_theme_color_override("font_color", visual_context.styles.GOLD if hostile else visual_context.styles.TEAL)
	content.add_child(intention)
	target_button = Button.new()
	target_button.name = "ActorTarget"
	target_button.custom_minimum_size = Vector2(76, 112) if compact else Vector2(0, 136)
	if battlefield:
		target_button.custom_minimum_size = Vector2(180, 100)
		target_button.size_flags_vertical = Control.SIZE_EXPAND_FILL
		target_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	target_button.toggle_mode = true
	target_button.tooltip_text = visual_context.translator.text("Choose a highlighted target.")
	for state in ["normal", "disabled"]: target_button.add_theme_stylebox_override(state, visual_context.styles.box(Color.TRANSPARENT, 12))
	for state in ["hover", "focus", "pressed"]: target_button.add_theme_stylebox_override(state, visual_context.styles.box(visual_context.styles.color("surface.target_hover"), 12, visual_context.styles.GOLD, 2))
	target_button.pressed.connect(func(): target_selected.emit(str(actor.get("instanceId", ""))))
	(root_content if compact else content).add_child(target_button)
	var portrait: Control
	if battlefield:
		# Stable visual anchor only; artwork is owned by CombatSceneryView.
		portrait = Control.new()
		portrait.mouse_filter = Control.MOUSE_FILTER_IGNORE
	else:
		portrait = preload("res://scripts/ui/art_slot.gd").new(visual_context)
		portrait.setup("actors", str(actor.get("definitionId", "")))
	portrait.name = "ActorArtworkAnchor"
	target_button.add_child(portrait)
	portrait.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	portrait_view = portrait
	var title := visual_context.styles.title(visual_context.translator.content_name(str(actor.get("definitionId", "")), str(actor.get("name", ""))), 18)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	title.add_theme_color_override("font_color", visual_context.styles.INK)
	content.add_child(title)
	if compact: content.move_child(title, 0)
	var counters := HFlowContainer.new()
	counters.alignment = FlowContainer.ALIGNMENT_CENTER
	counters.add_theme_constant_override("h_separation", 12)
	var resources: Dictionary = actor.get("resources", {})
	var ordered: Array = presentation.get("resource_order", []).duplicate()
	for id in resources:
		if id not in ordered: ordered.append(id)
	for id in ordered:
		if not resources.has(id): continue
		var appearance: Dictionary = presentation.get("resources", {}).get(id, {})
		if str(appearance.get("display", "bar")) == "bar":
			var resource := preload("res://scripts/ui/resource_view.gd").new(visual_context)
			resource.name = "Resource_" + str(id).validate_node_name()
			resource.setup(id, resources[id], appearance)
			content.add_child(resource)
		else:
			var amount := visual_context.styles.muted("%s %s" % [visual_context.translator.number(float(resources[id].get("current", 0))), visual_context.translator.content_name(id)], 14)
			amount.autowrap_mode = TextServer.AUTOWRAP_OFF
			amount.add_theme_color_override("font_color", Color.from_string(str(appearance.get("color", "")), visual_context.styles.color("resource.unknown")))
			amount.tooltip_text = "%s: %s / %s" % [visual_context.translator.content_name(id), resources[id].get("current", 0), resources[id].get("maximum", 0)]
			counters.add_child(amount)
	content.add_child(counters)
	var statuses: Array = actor.get("statuses", []).filter(func(status): return status.get("isActive", true))
	var status_text := statuses.map(func(status): return "%s ×%s" % [visual_context.translator.content_name(str(status.get("statusId", "status"))), status.get("stacks", 1)])
	var status := visual_context.styles.muted(" · ".join(status_text) if not statuses.is_empty() else visual_context.translator.text("No active statuses"), 12)
	status.autowrap_mode = TextServer.AUTOWRAP_OFF
	status.tooltip_text = status.text
	status.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	status.add_theme_color_override("font_color", visual_context.styles.EMBER if not statuses.is_empty() else visual_context.styles.MUTED)
	content.add_child(status)
	var panel_style := visual_context.styles.box(visual_context.styles.color("surface.actor"), 16)
	if battlefield:
		panel_style = visual_context.styles.box(Color.TRANSPARENT, 0)
		panel_style.content_margin_top = 4; panel_style.content_margin_bottom = 4
		root_content.add_theme_constant_override("separation", 0)
		content.add_theme_constant_override("separation", 2)
	if compact: panel_style.content_margin_top = 6; panel_style.content_margin_bottom = 6
	add_theme_stylebox_override("panel", panel_style)
	root_content.add_child(content)
	add_child(root_content)

func fit_battlefield_height(available: float) -> void:
	if not is_instance_valid(_battlefield_content): return
	# Reserve readable information first. Artwork uses all remaining height, with
	# a modest minimum for accessible fonts; content-heavy mods can still scroll.
	var panel_style := get_theme_stylebox("panel")
	var fixed := panel_style.get_minimum_size().y
	fixed += _battlefield_content.get_theme_constant("separation") * (_battlefield_content.get_child_count() - 1)
	for child in _battlefield_content.get_children():
		if child != target_button and child is Control: fixed += child.get_combined_minimum_size().y
	target_button.custom_minimum_size.y = clampf(available - fixed - 1, 60, 100)


func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
