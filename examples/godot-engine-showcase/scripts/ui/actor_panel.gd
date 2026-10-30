@tool
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

func _ready() -> void:
	if Engine.is_editor_hint() and _model.is_empty():
		# Live preview in Godot editor 2D canvas
		var dummy := {
			"instanceId": "golem",
			"definitionId": "stone_golem",
			"name": "Stone Golem",
			"relationship": "Enemy",
			"resources": {
				"health": {"current": 80, "minimum": 0, "maximum": 100},
				"shield": {"current": 15, "minimum": 0, "maximum": 15}
			},
			"statuses": [
				{"statusId": "burn", "stacks": 4, "duration": 2, "isActive": true},
				{"statusId": "shield", "stacks": 1, "duration": 0, "isActive": true}
			]
		}
		setup(dummy, true, {}, "Attack 12", false, true)

func present_resource(application: Dictionary, duration: float) -> Tween:
	var id := str(application.get("resourceId", ""))
	var view = find_child("Resource_" + id.validate_node_name(), true, false)
	if view != null: return view.present_change(application, duration)
	var counter = find_child("ResourceCounter_" + id.validate_node_name(), true, false)
	if counter != null and application.get("currentValue") != null and str(application.get("resourceField", "Current")) in ["Current", "0", "0.0"]:
		counter.text = "%s %s" % [visual_context.translator.number(float(application.currentValue)), visual_context.translator.content_name(id)]
	return null

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
		target_button = find_child("ActorTarget", true, false)
		portrait_view = find_child("ActorArtworkAnchor", true, false)
		if battlefield:
			_battlefield_content = find_child("ActorContent", true, false)
		return

	var root_content: BoxContainer = HBoxContainer.new() if compact else VBoxContainer.new()
	root_content.name = "ActorRootContent"
	root_content.add_theme_constant_override("separation", 8)

	var content := VBoxContainer.new()
	content.name = "ActorContent"
	content.custom_minimum_size.x = 264 if compact else 258
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	if battlefield:
		_battlefield_content = content
		size_flags_vertical = Control.SIZE_EXPAND_FILL
		root_content.size_flags_vertical = Control.SIZE_EXPAND_FILL
		content.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content.add_theme_constant_override("separation", 3 if compact else 4)

	# 1. Header: Actor Title
	var title := visual_context.styles.title(visual_context.translator.content_name(str(actor.get("definitionId", "")), str(actor.get("name", ""))), 16)
	title.name = "ActorTitle"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	title.add_theme_color_override("font_color", visual_context.styles.INK)
	content.add_child(title)
	if battlefield and visual_context.profile.compact_actor_hud: title.hide()

	# 2. Intention indicator
	var intention := Label.new()
	intention.name = "ActorIntention"
	var relation := str(actor.get("relationship", "Enemy" if hostile else "Ally"))
	var affiliation := "YOUR SIDE" if relation == "Ally" else ("No intent revealed" if relation == "Enemy" else "NEUTRAL")
	intention.text = visual_context.translator.text("INTENT  •  %s") % intent if not intent.is_empty() else visual_context.translator.text(affiliation)
	intention.tooltip_text = intention.text
	intention.custom_minimum_size.y = 28 if battlefield else 36
	intention.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	intention.max_lines_visible = 2 if compact or battlefield else 3
	intention.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	intention.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	intention.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	intention.add_theme_font_size_override("font_size", 13)
	intention.add_theme_color_override("font_color", visual_context.styles.GOLD if hostile else visual_context.styles.TEAL)
	if battlefield and visual_context.profile.compact_actor_hud:
		var intent_row := HBoxContainer.new()
		intent_row.name = "ActorIntentRow"
		intent_row.alignment = BoxContainer.ALIGNMENT_CENTER
		if hostile:
			var symbol := TextureRect.new()
			symbol.name = "IntentSymbol"
			symbol.texture = preload("res://scripts/ui/combat_chrome.gd").texture(str(actor.get("intentIconId", "unknown")))
			symbol.custom_minimum_size = Vector2(24, 24)
			symbol.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
			symbol.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
			symbol.modulate = visual_context.styles.GOLD
			symbol.mouse_filter = Control.MOUSE_FILTER_IGNORE
			intent_row.add_child(symbol)
		intent_row.add_child(intention)
		intent_row.visible = hostile or relation == "Neutral" or not intent.is_empty()
		content.add_child(intent_row)
	else:
		content.add_child(intention)

	# 3. Resources: Health bar at the TOP of the actor panel
	var counters := HFlowContainer.new()
	counters.name = "ActorCounters"
	counters.alignment = FlowContainer.ALIGNMENT_CENTER
	counters.add_theme_constant_override("h_separation", 10)
	var resources: Dictionary = actor.get("resources", {})
	var ordered: Array = presentation.get("resource_order", []).duplicate()
	for id in resources:
		if id not in ordered: ordered.append(id)
	for id in ordered:
		if not resources.has(id): continue
		var appearance: Dictionary = visual_context.resource_appearance(str(id), presentation.get("resources", {}).get(id, {}))
		if str(appearance.get("display", "bar")) == "bar":
			var resource := preload("res://scripts/ui/resource_view.gd").new(visual_context)
			resource.name = "Resource_" + str(id).validate_node_name()
			resource.setup(id, resources[id], appearance)
			content.add_child(resource)
		else:
			var amount := visual_context.styles.muted("%s %s" % [visual_context.translator.number(float(resources[id].get("current", 0))), visual_context.translator.content_name(id)], 13)
			amount.name = "ResourceCounter_" + str(id).validate_node_name()
			amount.autowrap_mode = TextServer.AUTOWRAP_OFF
			amount.add_theme_color_override("font_color", Color.from_string(str(appearance.get("color", "")), visual_context.styles.color("resource.unknown")))
			amount.tooltip_text = "%s: %s / %s" % [visual_context.translator.content_name(id), resources[id].get("current", 0), resources[id].get("maximum", 0)]
			counters.add_child(amount)
	content.add_child(counters)

	# 4. Status Badges: Distinct square badges directly beneath the health bar
	var statuses: Array = actor.get("statuses", []).filter(func(status): return status.get("isActive", true))
	var status_container := HFlowContainer.new()
	status_container.name = "ActorStatusBadges"
	status_container.alignment = FlowContainer.ALIGNMENT_CENTER
	status_container.add_theme_constant_override("h_separation", 4)
	status_container.add_theme_constant_override("v_separation", 4)
	status_container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for status_data in statuses:
		var badge := _create_status_badge(status_data)
		status_container.add_child(badge)
	content.add_child(status_container)
	if statuses.is_empty():
		status_container.visible = false

	# 5. Target Button & Artwork
	target_button = Button.new()
	target_button.name = "ActorTarget"
	target_button.custom_minimum_size = Vector2(76, 112) if compact else Vector2(0, 136)
	if battlefield:
		target_button.custom_minimum_size = Vector2(180, 100)
		target_button.size_flags_vertical = Control.SIZE_EXPAND_FILL
		target_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	target_button.toggle_mode = true
	target_button.tooltip_text = visual_context.translator.content_name(str(actor.get("definitionId", "")), str(actor.get("name", ""))) + "\n" + intention.text + "\n" + visual_context.translator.text("Choose a highlighted target.")
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

const StatusBadgeScene = preload("res://scenes/ui/components/status_badge.tscn")

func _create_status_badge(status_data: Dictionary) -> Control:
	var status_id := str(status_data.get("statusId", "status"))
	var badge = StatusBadgeScene.instantiate()
	badge.name = "StatusBadge_" + status_id.validate_node_name()
	badge.apply_status(status_data, visual_context)
	return badge

func fit_battlefield_height(available: float) -> void:
	if not is_instance_valid(_battlefield_content): return
	# Reserve readable information first. Artwork uses all remaining height, with
	# a modest minimum for accessible fonts; content-heavy mods can still scroll.
	var panel_style := get_theme_stylebox("panel")
	var fixed := panel_style.get_minimum_size().y
	fixed += _battlefield_content.get_theme_constant("separation") * (_battlefield_content.get_child_count() - 1)
	for child in _battlefield_content.get_children():
		if child != target_button and child is Control and child.visible: fixed += child.get_combined_minimum_size().y
	target_button.custom_minimum_size.y = clampf(available - fixed - 1, 60, visual_context.profile.actor_art_max_height)

func _init(context = null) -> void:
	visual_context = context if context != null else preload("res://scripts/ui/visual_context.gd").new()
