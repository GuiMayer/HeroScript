@tool
class_name ActorPanel
extends PanelContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
const ResourceScene = preload("res://scenes/ui/components/resource_view.tscn")
const BadgeScene = preload("res://scenes/ui/components/status_badge.tscn")
const CounterScene = preload("res://scenes/ui/components/resource_counter.tscn")
signal target_selected(actor_id: String)
var visual_context: ContextType = ContextType.new()
var portrait_view: Control
var target_button: Button
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
var _statuses: HFlowContainer
var _surface: StyleBoxFlat
var _authored_surface: StyleBoxFlat
var _layout_mode := ""
var _authored_content_size: Vector2
var _authored_target_size: Vector2
var _authored_root_gap: int
var _authored_content_gap: int
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
	_statuses = get_node("%ActorStatusBadges")
	target_button = get_node("%ActorTarget")
	portrait_view = get_node("%ActorArtworkAnchor")
	if not target_button.pressed.is_connected(_on_target_pressed): target_button.pressed.connect(_on_target_pressed)
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
	if view != null: return view.present_change(application, duration)
	var counter = resource_counter(id)
	if counter != null and application.get("currentValue") != null and str(application.get("resourceField", "Current")) in ["Current", "0", "0.0"]:
		counter.text = "%s %s" % [visual_context.translator.number(float(application.currentValue)), visual_context.translator.content_name(id)]
	return null

func resource_view(resource_id: String) -> Control:
	var key := "Resource_" + resource_id.validate_node_name()
	if _resources != null and _resources.has_node(NodePath(key)):
		return _resources.get_node(NodePath(key))
	return find_child(key, true, false) as Control

func resource_counter(resource_id: String) -> Control:
	var key := "ResourceCounter_" + resource_id.validate_node_name()
	if _counters != null and _counters.has_node(NodePath(key)):
		return _counters.get_node(NodePath(key))
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
	_battlefield_content = _content if battlefield else null
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
		if target_button.get_parent() != (_root_content if compact else _content):
			target_button.reparent(_root_content if compact else _content)
		if compact: _root_content.move_child(target_button, 0)
		else: _content.move_child(target_button, _content.get_child_count() - 1)
		if compact: _content.custom_minimum_size.x = compact_content_width
		_intent.custom_minimum_size.y = battlefield_intent_height if battlefield else _authored_intent_height
		_intent.max_lines_visible = 2 if compact or battlefield else 3
		if compact: target_button.custom_minimum_size = compact_target_size
		elif battlefield: target_button.custom_minimum_size = battlefield_target_size
		target_button.size_flags_vertical = Control.SIZE_EXPAND_FILL if battlefield else Control.SIZE_FILL
		if battlefield:
			_surface.content_margin_top = battlefield_margin_vertical; _surface.content_margin_bottom = battlefield_margin_vertical
			_root_content.add_theme_constant_override("separation", 0)
			_content.add_theme_constant_override("separation", battlefield_content_gap)
		elif compact:
			_surface.content_margin_top = compact_margin_vertical; _surface.content_margin_bottom = compact_margin_vertical
	_surface.bg_color = Color.TRANSPARENT if battlefield else visual_context.styles.color("surface.actor")
	var name_text: String = visual_context.translator.content_name(str(actor.get("definitionId", "")), str(actor.get("name", "")))
	_title.text = name_text
	_title.visible = not (battlefield and visual_context.profile.compact_actor_hud)
	_title.add_theme_color_override("font_color", visual_context.styles.INK)
	var relation := str(actor.get("relationship", "Enemy" if hostile else "Ally"))
	var affiliation := "YOUR SIDE" if relation == "Ally" else ("No intent revealed" if relation == "Enemy" else "NEUTRAL")
	_intent.text = visual_context.translator.text("INTENT  •  %s") % intent if not intent.is_empty() else visual_context.translator.text(affiliation)
	_intent.tooltip_text = _intent.text
	_intent.add_theme_color_override("font_color", visual_context.styles.GOLD if hostile else visual_context.styles.TEAL)
	_intent_row.visible = not (battlefield and visual_context.profile.compact_actor_hud) or hostile or relation == "Neutral" or not intent.is_empty()
	_intent_icon.visible = battlefield and visual_context.profile.compact_actor_hud and hostile
	_intent_icon.texture = preload("res://scripts/ui/combat_chrome.gd").texture(str(actor.get("intentIconId", "unknown")))
	_intent_icon.modulate = visual_context.styles.GOLD
	_sync_resources(actor.get("resources", {}), presentation)
	var live_statuses: Array[String] = []
	var statuses: Array = actor.get("statuses", []).filter(func(status): return status.get("isActive", true))
	for index in statuses.size():
		var data: Dictionary = statuses[index]
		var key := "StatusBadge_" + str(data.get("statusId", "status")).validate_node_name()
		if key in live_statuses: key += "_" + str(index)
		live_statuses.append(key)
		var badge = _statuses.get_node_or_null(NodePath(key))
		if badge == null:
			badge = BadgeScene.instantiate(); badge.name = key; _statuses.add_child(badge)
		badge.apply_status(data, visual_context)
	_prune(_statuses, live_statuses)
	_statuses.visible = not statuses.is_empty()
	target_button.tooltip_text = name_text + "\n" + _intent.text + "\n" + visual_context.translator.text("Choose a highlighted target.")
	portrait_view.rebind_context(visual_context)
	if not battlefield: portrait_view.setup("actors", str(actor.get("definitionId", "")))
	else: portrait_view.texture = null
	for state in ["hover", "focus", "pressed"]:
		var style: StyleBoxFlat = target_button.get_theme_stylebox(state).duplicate()
		style.bg_color = visual_context.styles.color("surface.target_hover")
		style.border_color = visual_context.styles.GOLD
		target_button.add_theme_stylebox_override(state, style)

func _sync_resources(resources: Dictionary, presentation: Dictionary) -> void:
	var ordered: Array = presentation.get("resource_order", []).duplicate()
	for id in resources:
		if id not in ordered: ordered.append(id)
	var bars: Array[String] = []; var counters: Array[String] = []
	for id in ordered:
		if not resources.has(id): continue
		var appearance: Dictionary = visual_context.resource_appearance(str(id), presentation.get("resources", {}).get(id, {}))
		if str(appearance.get("display", "bar")) == "bar":
			var key := "Resource_" + str(id).validate_node_name(); bars.append(key)
			var resource = _resources.get_node_or_null(NodePath(key))
			if resource == null:
				resource = ResourceScene.instantiate(); resource.name = key; _resources.add_child(resource)
			resource.visual_context = visual_context
			resource.setup(id, resources[id], appearance)
			_resources.move_child(resource, bars.size() - 1)
		else:
			var key := "ResourceCounter_" + str(id).validate_node_name(); counters.append(key)
			var counter = _counters.get_node_or_null(NodePath(key))
			if counter == null:
				counter = CounterScene.instantiate(); counter.name = key; _counters.add_child(counter)
			counter.text = "%s %s" % [visual_context.translator.number(float(resources[id].get("current", 0))), visual_context.translator.content_name(id)]
			counter.add_theme_color_override("font_color", Color.from_string(str(appearance.get("color", "")), visual_context.styles.color("resource.unknown")))
			counter.tooltip_text = "%s: %s / %s" % [visual_context.translator.content_name(id), resources[id].get("current", 0), resources[id].get("maximum", 0)]
			_counters.move_child(counter, counters.size() - 1)
	_prune(_resources, bars); _prune(_counters, counters)

func _prune(parent: Node, live: Array[String]) -> void:
	for child in parent.get_children():
		if str(child.name) not in live:
			parent.remove_child(child); child.queue_free()

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
