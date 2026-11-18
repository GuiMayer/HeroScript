@tool
class_name ActorPanel
extends PanelContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
const ResourceScene = preload("res://scenes/ui/components/resource_view.tscn")
const MAX_VISIBLE_EFFECTS := 5
const STATUS_GAP := 4.0
const BadgeScene = preload("res://scenes/ui/components/status_badge.tscn")
signal target_selected(actor_id: String)
var visual_context: ContextType = ContextType.new()
var portrait_view: Control
var target_button: Button
var plate: PanelContainer
var _plate_surface: StyleBoxFlat
var _battlefield_content: VBoxContainer
var _projection := []
var _model: Dictionary = {}
var _root_content: BoxContainer
var _content: VBoxContainer
var _title: Label
var _intent: Label
var _intent_row: HBoxContainer
var _intent_icon: TextureRect
var _resources: VBoxContainer
var _counters: HFlowContainer
var _status_slot: Control
var _statuses: Control
var _health: VBoxContainer
var _block: VBoxContainer
var _effects_button: Button
var _effects_dialog: AcceptDialog
var _effects_text: RichTextLabel
var _effect_details: Array[String] = []
var _surface: StyleBoxFlat
var _authored_surface: StyleBoxFlat
var _layout_mode := ""
var _authored_content_size: Vector2
var _authored_target_size: Vector2
var _authored_root_gap: int
var _authored_content_gap: int
@export var authored_battlefield_layout := false
@export var hide_unknown_intent_icon := false
@export var compact_content_width := 264.0
@export var compact_target_size := Vector2(76, 112)
@export var battlefield_target_size := Vector2(180, 100)
var _authored_intent_height := 36.0
@export var battlefield_intent_height := 28.0
@export var battlefield_margin_vertical := 4.0
@export var compact_margin_vertical := 6.0
@export var battlefield_content_gap := 2
@export var preview_hostile := true
@export var preview_intent := "Attack 12"
@export var preview_compact := false
@export var preview_battlefield := false
@export var preview_actor: Dictionary = {"instanceId": "preview", "name": "Stone Golem", "definitionId": "stone_golem", "resources": {"health": {"current": 80, "minimum": 0, "maximum": 100}}, "statuses": [{"statusId": "burn", "stacks": 4}]}

static func create(context = null) -> ActorPanel:
	var panel = (load("res://scenes/ui/components/actor_panel.tscn") as PackedScene).instantiate()
	if context != null: panel.visual_context = context
	return panel

func _ready() -> void:
	_bind_nodes()
	if authored_battlefield_layout:
		get_viewport().size_changed.connect(_layout_effects)
		_layout_effects()
	if _model.is_empty() and (Engine.is_editor_hint() or get_parent() == get_tree().root): setup(preview_actor, preview_hostile, {}, preview_intent, preview_compact, preview_battlefield)

func _bind_nodes() -> void:
	_root_content = get_node("%ActorRootContent")
	_root_content.mouse_filter = Control.MOUSE_FILTER_PASS
	for control in _root_content.find_children("*", "Control", true, false):
		if not control is BaseButton: control.mouse_filter = Control.MOUSE_FILTER_PASS
	_content = get_node("%ActorContent")
	_title = get_node("%ActorTitle")
	_intent = get_node("%ActorIntention")
	_intent_row = get_node("%ActorIntentRow")
	_intent_icon = get_node("%IntentSymbol")
	_resources = get_node("%ActorResources")
	_counters = get_node("%ActorCounters")
	_status_slot = get_node_or_null("%ActorStatusSlot")
	_statuses = get_node("%ActorStatusBadges")
	_health = get_node_or_null("%ActorHealth")
	_block = get_node_or_null("%ActorBlock")
	target_button = get_node("%ActorTarget")
	portrait_view = get_node("%ActorArtworkAnchor")
	plate = get_node_or_null("%ActorPlate")
	if is_instance_valid(plate) and _plate_surface == null:
		_plate_surface = plate.get_theme_stylebox("panel").duplicate()
		plate.add_theme_stylebox_override("panel", _plate_surface)
	if not target_button.pressed.is_connected(_on_target_pressed): target_button.pressed.connect(_on_target_pressed)
	if plate != null and not plate.focus_entered.is_connected(_refresh_plate_focus):
		plate.focus_entered.connect(_refresh_plate_focus)
		plate.focus_exited.connect(_refresh_plate_focus)
	if _surface == null:
		_authored_content_size = _content.custom_minimum_size
		_authored_target_size = target_button.custom_minimum_size
		_authored_root_gap = _root_content.get_theme_constant("separation")
		_authored_content_gap = _content.get_theme_constant("separation")
		_authored_intent_height = _intent.custom_minimum_size.y if _intent.custom_minimum_size.y > 0 else 36.0
		_authored_surface = get_theme_stylebox("panel").duplicate()
		_surface = _authored_surface.duplicate()
		add_theme_stylebox_override("panel", _surface)

func _on_target_pressed() -> void:
	target_selected.emit(str(_model.get("actor", {}).get("instanceId", "")))

func present_resource(application: Dictionary, duration: float) -> Tween:
	var id := str(application.get("resourceId", ""))
	var view = resource_view(id)
	if view != null:
		var motion = view.present_change(application, duration)
		if _health != null: _update_accessible_vitals()
		return motion
	var counter = resource_counter(id)
	if counter != null and application.get("currentValue") != null and str(application.get("resourceField", "Current")) in ["Current", "0", "0.0"]:
		_set_counter(counter, id, float(application.currentValue), _block != null and id == "block")
		if _block != null and id == "block": _update_accessible_vitals()
	return null

func set_health_preview(projected: Dictionary) -> void:
	if _health == null: return
	var view := resource_view("health") as ResourceView
	if view != null: view.set_health_preview(projected)
	_update_accessible_vitals()

func resource_view(resource_id: String) -> Control:
	var key := "Resource_" + resource_id.validate_node_name()
	if _resources != null and _resources.has_node(NodePath(key)):
		return _resources.get_node(NodePath(key))
	return find_child(key, true, false) as Control

func resource_counter(resource_id: String) -> Control:
	var key := "ResourceCounter_" + resource_id.validate_node_name()
	if _counters != null and _counters.has_node(NodePath(key)):
		var node = _counters.get_node(NodePath(key))
		return node.get_node("Value") if node.has_node("Value") else node
	if _block != null and _block.has_node(NodePath(key)):
		return _block.get_node(NodePath(key)).get_node("Value")
	return find_child(key, true, false) as Control

func status_badge(status_id: String) -> Control:
	var key := "StatusBadge_" + status_id.validate_node_name()
	if _statuses != null and _statuses.has_node(NodePath(key)):
		return _statuses.get_node(NodePath(key))
	return find_child(key, true, false) as Control

func presentation_model() -> Dictionary: return _model.duplicate(true)

func apply_model(data: Dictionary) -> void:
	setup(data.get("actor", {}), bool(data.get("hostile", false)), data.get("presentation", {}), str(data.get("intent", "")), bool(data.get("compact", false)), bool(data.get("battlefield", false)))

func reconcile_from(other) -> void:
	visual_context = other.visual_context
	apply_model(other.presentation_model())

func setup(actor: Dictionary, hostile: bool, presentation: Dictionary, intent: String, compact := false, battlefield := false) -> void:
	_bind_nodes()
	theme = visual_context.theme
	_model = {"actor": actor.duplicate(true), "hostile": hostile, "presentation": presentation.duplicate(true), "intent": intent, "compact": compact, "battlefield": battlefield}
	_battlefield_content = _content if (battlefield or authored_battlefield_layout) else null
	var mode := str(compact) + str(battlefield)
	if mode != _layout_mode:
		_layout_mode = mode
		_content.custom_minimum_size = _authored_content_size
		target_button.custom_minimum_size = _authored_target_size
		_surface.content_margin_top = _authored_surface.content_margin_top
		_surface.content_margin_bottom = _authored_surface.content_margin_bottom
		_root_content.add_theme_constant_override("separation", _authored_root_gap)
		_content.add_theme_constant_override("separation", _authored_content_gap)
		_root_content.vertical = not compact
		if not authored_battlefield_layout and target_button.get_parent() != (_root_content if compact else _content):
			target_button.reparent(_root_content if compact else _content)
		if not authored_battlefield_layout and compact: _root_content.move_child(target_button, 0)
		elif not authored_battlefield_layout: _content.move_child(target_button, _content.get_child_count() - 1)
		if compact: _content.custom_minimum_size.x = compact_content_width
		_intent.custom_minimum_size.y = battlefield_intent_height if battlefield else _authored_intent_height
		_intent.max_lines_visible = 2 if compact or battlefield else 3
		if compact: target_button.custom_minimum_size = compact_target_size
		elif battlefield: target_button.custom_minimum_size = battlefield_target_size
		target_button.size_flags_vertical = Control.SIZE_EXPAND_FILL if (battlefield or authored_battlefield_layout) else Control.SIZE_FILL
		if battlefield:
			_surface.content_margin_top = battlefield_margin_vertical; _surface.content_margin_bottom = battlefield_margin_vertical
			_root_content.add_theme_constant_override("separation", 0)
			_content.add_theme_constant_override("separation", battlefield_content_gap)
		elif compact:
			_surface.content_margin_top = compact_margin_vertical; _surface.content_margin_bottom = compact_margin_vertical
	_surface.bg_color = Color.TRANSPARENT if (battlefield or authored_battlefield_layout) else visual_context.styles.color("surface.actor")
	if is_instance_valid(plate) and _plate_surface != null:
		_plate_surface.bg_color = visual_context.styles.color("surface.actor")
		_plate_surface.border_color = visual_context.styles.color("stroke.panel")
		_plate_surface.set_corner_radius_all(visual_context.profile.corner_radius if visual_context.profile.printed_surfaces else 6)
		_plate_surface.set_meta("background_role", "surface.actor")
		_plate_surface.set_meta("border_role", "stroke.panel")
		if authored_battlefield_layout:
			_plate_surface.bg_color = Color.WHITE if visual_context.high_contrast else visual_context.styles.color("hud.paper")
			_plate_surface.set_meta("background_role", "hud.paper")
			plate.custom_minimum_size.x = roundf(236 * visual_context.text_scale)
	var name_text: String = visual_context.translator.content_name(str(actor.get("definitionId", "")), str(actor.get("name", "")))
	_title.text = name_text
	_title.tooltip_text = name_text
	_title.visible = authored_battlefield_layout or not (battlefield and visual_context.profile.compact_actor_hud)
	_title.add_theme_color_override("font_color", visual_context.styles.INK)
	var relation := str(actor.get("relationship", "Enemy" if hostile else "Ally"))
	var affiliation := "YOUR SIDE" if relation == "Ally" else ("No intent revealed" if relation == "Enemy" else "NEUTRAL")
	var side := "Ally" if relation == "Ally" else ("Enemy" if relation == "Enemy" else "NEUTRAL")
	var side_text: String = visual_context.translator.text(side)
	if authored_battlefield_layout:
		var role: Label = get_node("%ActorRole")
		role.text = visual_context.translator.text("Hero") if actor.get("isInputActor", false) else side_text
		if actor.get("boss", false): role.text = visual_context.translator.text("Boss")
		role.add_theme_color_override("font_color", visual_context.styles.color("text.secondary") if not visual_context.high_contrast else Color.BLACK)
		role.add_theme_font_size_override("font_size", roundi(13 * visual_context.text_scale))
		_title.add_theme_font_size_override("font_size", roundi(16 * visual_context.text_scale))
		_intent.text = str(actor.get("hudIntent", intent.get_slice("\n", 0))) if not intent.is_empty() else visual_context.translator.text(affiliation)
		var recipients: Label = get_node("%ActorIntentTarget")
		recipients.text = str(actor.get("hudIntentTargets", ""))
		for line in [_intent, recipients]:
			line.add_theme_font_size_override("font_size", roundi(14 * visual_context.text_scale))
			line.add_theme_color_override("font_color", Color.BLACK if visual_context.high_contrast else visual_context.styles.INK)
		var intent_slot: Control = get_node("%ActorIntentSlot")
		intent_slot.custom_minimum_size.y = ceilf(_intent.get_minimum_size().y + recipients.get_minimum_size().y)
		for control in [plate, target_button, _title, role, _intent, recipients]:
			control.set_meta("owns_hud_preferences", true)
		plate.focus_mode = Control.FOCUS_ALL
		plate.set_meta("skip_initial_focus", true)
		_refresh_plate_focus()
	else:
		_intent.text = visual_context.translator.text("INTENT  •  %s") % intent if not intent.is_empty() else visual_context.translator.text(affiliation)
		_intent.add_theme_color_override("font_color", visual_context.styles.GOLD if hostile else visual_context.styles.TEAL)
	_intent.tooltip_text = intent if not intent.is_empty() else _intent.text
	_intent_row.visible = (not intent.is_empty() or hostile or relation == "Neutral") if authored_battlefield_layout else (not (battlefield and visual_context.profile.compact_actor_hud) or hostile or relation == "Neutral" or not intent.is_empty())
	_intent_icon.visible = (battlefield or authored_battlefield_layout) and visual_context.profile.compact_actor_hud and (hostile or not intent.is_empty()) and not (hide_unknown_intent_icon and str(actor.get("intentIconId", "unknown")) == "unknown")
	_intent_icon.texture = preload("res://scripts/ui/combat_chrome.gd").texture(str(actor.get("intentIconId", "unknown")))
	_intent_icon.modulate = visual_context.styles.GOLD if hostile else visual_context.styles.TEAL
	_sync_resources(actor.get("resources", {}), presentation)
	var live_statuses: Array[String] = []
	var statuses: Array = actor.get("statuses", []).filter(func(status): return status.get("isActive", true))
	if authored_battlefield_layout:
		statuses.sort_custom(_status_precedes)
		_effect_details.clear()
	for index in statuses.size():
		var data: Dictionary = statuses[index]
		var key := "StatusBadge_" + str(data.get("statusId", "status")).validate_node_name()
		if key in live_statuses: key += "_" + str(index)
		live_statuses.append(key)
		var badge = _statuses.get_node_or_null(NodePath(key))
		if badge == null:
			badge = BadgeScene.instantiate(); badge.name = key; _statuses.add_child(badge)
		badge.mvp_icons = authored_battlefield_layout
		badge.apply_status(data, visual_context)
		if authored_battlefield_layout:
			_effect_details.append(badge.tooltip_text)
			badge.visible = index < MAX_VISIBLE_EFFECTS
			_statuses.move_child(badge, index)
	if authored_battlefield_layout:
		_sync_effects_button(statuses.size())
		live_statuses.append(str(_effects_button.name))
	_prune(_statuses, live_statuses)
	_statuses.visible = not statuses.is_empty()
	if authored_battlefield_layout: _layout_effects()
	target_button.tooltip_text = name_text + "\n" + _intent.tooltip_text
	if authored_battlefield_layout:
		target_button.accessibility_name = "%s · %s" % [name_text, side_text]
		_update_accessible_vitals()
	portrait_view.rebind_context(visual_context)
	if not battlefield: portrait_view.setup("actors", str(actor.get("definitionId", "")))
	else: portrait_view.texture = null
	for state in ["hover", "focus", "pressed"]:
		var style: StyleBoxFlat = target_button.get_theme_stylebox(state).duplicate()
		style.bg_color = visual_context.styles.color("surface.target_hover")
		style.border_color = visual_context.styles.GOLD
		if authored_battlefield_layout and state == "focus":
			style.border_color = visual_context.styles.INK
			style.bg_color = Color.TRANSPARENT
			style.set_expand_margin_all(4.0)
		target_button.add_theme_stylebox_override(state, style)

func _status_precedes(left: Dictionary, right: Dictionary) -> bool:
	var left_id := str(left.get("statusId", ""))
	var right_id := str(right.get("statusId", ""))
	if left_id != right_id: return left_id < right_id
	return str(left.get("instanceId", "")) < str(right.get("instanceId", ""))

func _sync_effects_button(count: int) -> void:
	if not is_instance_valid(_effects_button):
		_effects_button = Button.new()
		_effects_button.name = "InspectEffects"
		_effects_button.theme_type_variation = &"SecondaryAction"
		_effects_button.clip_text = true
		_effects_button.set_meta("owns_hud_preferences", true)
		_effects_button.pressed.connect(_open_effects)
		_statuses.add_child(_effects_button)
	_statuses.move_child(_effects_button, -1)
	var title: String = visual_context.translator.text("Active effects")
	_effects_button.text = "+%d" % (count - MAX_VISIBLE_EFFECTS) if count > MAX_VISIBLE_EFFECTS else "…"
	_effects_button.accessibility_name = "%s: %s (%d)" % [_title.text, title, count]
	_effects_button.tooltip_text = _effects_button.accessibility_name
	var restore_to_actor := count == 0 and is_inside_tree() and (_effects_button.has_focus() or (is_instance_valid(_effects_dialog) and _effects_dialog.visible))
	_effects_button.visible = count > 0
	if is_instance_valid(_effects_dialog):
		_effects_text.text = "\n\n".join(_effect_details)
		if count == 0: _effects_dialog.hide()
	if restore_to_actor: plate.grab_focus.call_deferred()

func _layout_effects() -> void:
	if not is_instance_valid(_status_slot) or not is_inside_tree(): return
	var cell := StatusBadge.battlefield_cell(visual_context)
	var gap := roundf(STATUS_GAP * visual_context.text_scale)
	var columns := 3 if visual_context.text_scale > 1.2 or get_viewport_rect().size.x < 1300 else MAX_VISIBLE_EFFECTS + 1
	var rows := ceili(float(MAX_VISIBLE_EFFECTS + 1) / columns)
	var grid := Vector2(columns * cell.x + (columns - 1) * gap, rows * cell.y + (rows - 1) * gap)
	# The grid depends on the viewport and text scale. Contents never resize the plate.
	_status_slot.custom_minimum_size = grid
	_statuses.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
	_statuses.offset_left = -grid.x * 0.5
	_statuses.offset_right = grid.x * 0.5
	_statuses.offset_top = -grid.y * 0.5
	_statuses.offset_bottom = grid.y * 0.5
	var index := 0
	for control in _statuses.get_children():
		if not control.visible: continue
		var slot := MAX_VISIBLE_EFFECTS if control == _effects_button else index
		control.position = Vector2(slot % columns, floorf(float(slot) / columns)) * (cell + Vector2.ONE * gap)
		control.custom_minimum_size = cell
		control.size = cell
		if control == _effects_button:
			control.add_theme_font_size_override("font_size", roundi(16 * visual_context.text_scale))
			for state in ["normal", "pressed", "hover", "focus", "disabled"]:
				var style: StyleBox = control.get_theme_stylebox(state).duplicate()
				style.content_margin_left = 2
				style.content_margin_right = 2
				control.add_theme_stylebox_override(state, style)
		else:
			index += 1

func _open_effects() -> void:
	if _effect_details.is_empty(): return
	if not is_instance_valid(_effects_dialog):
		_effects_dialog = AcceptDialog.new()
		_effects_dialog.transient = true
		_effects_dialog.exclusive = true
		var restore := Node.new()
		restore.set_script(preload("res://scripts/ui/components/modal_focus.gd"))
		_effects_dialog.add_child(restore)
		_effects_text = RichTextLabel.new()
		_effects_text.selection_enabled = true
		_effects_text.focus_mode = Control.FOCUS_ALL
		_effects_dialog.add_child(_effects_text)
		add_child(_effects_dialog)
	_effects_dialog.title = "%s - %s" % [_title.text, visual_context.translator.text("Active effects")]
	_effects_text.add_theme_font_size_override("normal_font_size", roundi(16 * visual_context.text_scale))
	_effects_text.text = "\n\n".join(_effect_details)
	_effects_dialog.popup_centered_clamped(Vector2i(480, 320))
	_effects_text.grab_focus()

func _sync_resources(resources: Dictionary, presentation: Dictionary) -> void:
	if _health != null and not resources.has("health") and (_model.hostile or _model.actor.get("isInputActor", false)):
		resources = resources.duplicate()
		resources["health"] = {}
	var ordered: Array = presentation.get("resource_order", []).duplicate()
	for id in resources:
		if id not in ordered: ordered.append(id)
	var bars: Array[String] = []; var counters: Array[String] = []
	var vitals: Array[String] = []; var shield: Array[String] = []
	if _block != null:
		_block.custom_minimum_size.x = roundf(68 * visual_context.text_scale)
	for id in ordered:
		if not resources.has(id) or (authored_battlefield_layout and str(id) == "energy"): continue
		var appearance: Dictionary = visual_context.resource_appearance(str(id), presentation.get("resources", {}).get(id, {}))
		var is_health := _health != null and str(id) == "health"
		var is_block := _block != null and str(id) == "block"
		if is_health:
			appearance["display"] = "bar"
			appearance["hud_tone"] = "hud.enemy_health" if _model.hostile else "hud.ally_health"
			appearance["hud_defeated"] = _model.actor.get("isAlive", true) == false
		if is_block: appearance["display"] = "counter"
		if str(appearance.get("display", "bar")) == "bar":
			var key := "Resource_" + str(id).validate_node_name()
			var parent := _health if is_health else _resources
			var live := vitals if is_health else bars
			live.append(key)
			var resource = parent.get_node_or_null(NodePath(key))
			if resource == null:
				resource = (preload("res://scenes/ui/components/mvp_health_bar.tscn") if is_health else ResourceScene).instantiate()
				resource.name = key
				parent.add_child(resource)
			resource.visual_context = visual_context
			if is_health:
				resource.settle_motion()
				resource.set_health_preview({})
			resource.setup(id, resources[id], appearance)
			if is_health:
				resource.label.set_meta("owns_hud_preferences", true)
				resource.amount.set_meta("owns_hud_preferences", true)
				resource.label.text = visual_context.translator.content_name("health")
				resource.label.add_theme_font_size_override("font_size", roundi(16 * visual_context.text_scale))
				resource.amount.add_theme_font_size_override("font_size", roundi(21 * visual_context.text_scale))
				resource.label.add_theme_color_override("font_color", visual_context.styles.INK)
				resource.amount.add_theme_color_override("font_color", visual_context.styles.INK)
			parent.move_child(resource, live.size() - 1)
		else:
			var key := "ResourceCounter_" + str(id).validate_node_name()
			var parent: Container = _block if is_block else _counters
			var live := shield if is_block else counters
			live.append(key)
			var counter = parent.get_node_or_null(NodePath(key))
			if counter == null:
				counter = preload("res://scenes/ui/components/mvp_shield_counter.tscn").instantiate() if is_block else Label.new()
				if counter is Label: counter.theme_type_variation = &"ResourceCounter"
				counter.mouse_filter = Control.MOUSE_FILTER_PASS if counter is HBoxContainer else Control.MOUSE_FILTER_IGNORE
				counter.name = key; parent.add_child(counter)
			var counter_label: Label = counter.get_node("Value") if counter.has_node("Value") else counter
			_set_counter(counter_label, str(id), float(resources[id].get("current", 0)), is_block)
			var ink := Color.from_string(str(appearance.get("color", "")), visual_context.styles.color("resource.unknown"))
			if is_block:
				counter_label.set_meta("owns_hud_preferences", true)
				ink = Color.BLACK if visual_context.high_contrast else visual_context.styles.color("hud.block")
				counter_label.add_theme_font_size_override("font_size", roundi(18 * visual_context.text_scale))
				var icon: TextureRect = counter.get_node("Icon")
				icon.modulate = ink
				icon.custom_minimum_size = Vector2.ONE * roundf(18 * visual_context.text_scale)
			counter_label.add_theme_color_override("font_color", ink)
			counter.tooltip_text = counter_label.tooltip_text
			if resources[id].has("maximum"):
				counter.tooltip_text += " / " + visual_context.translator.number(float(resources[id].maximum))
			parent.move_child(counter, live.size() - 1)
	_prune(_resources, bars); _prune(_counters, counters)
	if _health != null: _prune(_health, vitals)
	if _block != null: _prune(_block, shield)
	_resources.visible = not bars.is_empty()
	_counters.visible = not counters.is_empty()

func _set_counter(label: Label, id: String, value: float, is_block: bool) -> void:
	var number: String = visual_context.translator.number(value)
	var name_text: String = visual_context.translator.content_name(id)
	label.tooltip_text = "%s %s" % [number, name_text]
	label.text = number if is_block else label.tooltip_text
	if is_block: label.accessibility_name = label.tooltip_text

func _update_accessible_vitals() -> void:
	var parts: Array[String] = [_title.text, get_node("%ActorRole").text]
	var hp := resource_view("health") as ResourceView
	if hp != null:
		parts.append(hp.amount.accessibility_name)
		var state: Label = hp.get_node("%HealthState")
		if not state.text.is_empty(): parts.append(state.text)
	var shield := resource_counter("block") as Label
	if shield != null: parts.append(shield.accessibility_name)
	if _intent_row.visible: parts.append(_intent.tooltip_text)
	plate.accessibility_name = " · ".join(parts)

func _refresh_plate_focus() -> void:
	if _plate_surface == null: return
	_plate_surface.border_color = visual_context.styles.color("stroke.panel")
	if plate.has_focus():
		_plate_surface.border_color = visual_context.styles.color("stroke.focus")
	if visual_context.high_contrast: _plate_surface.border_color = Color.BLACK
	_plate_surface.set_border_width_all(3 if plate.has_focus() else 1)

func _prune(parent: Node, live: Array[String]) -> void:
	for child in parent.get_children():
		if str(child.name) not in live:
			parent.remove_child(child); child.queue_free()

func minimum_battlefield_height() -> float:
	if not is_instance_valid(_battlefield_content): return 120.0
	var panel_style := get_theme_stylebox("panel")
	var fixed := panel_style.get_minimum_size().y
	fixed += _battlefield_content.get_theme_constant("separation") * (_battlefield_content.get_child_count() - 1)
	for child in _battlefield_content.get_children():
		if child != target_button and child is Control and child.visible: fixed += child.get_combined_minimum_size().y
	return fixed + 64.0

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

func _gui_input(event: InputEvent) -> void:
	if Engine.is_editor_hint() or not is_instance_valid(target_button) or target_button.disabled: return
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		accept_event(); _on_target_pressed()
