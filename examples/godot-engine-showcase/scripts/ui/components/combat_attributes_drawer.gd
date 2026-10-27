extends PanelContainer
## Floating shell around the exact passive attribute component used by loadout.
const Attributes = preload("res://scripts/ui/loadout/character_attributes_view.gd")
const Presenter = preload("res://scripts/presentation/loadout_presenter.gd")
signal open_changed(value: bool)
@export var transition_seconds := .22
var visual_context
var attributes_view
var content_scroll: ScrollContainer
var title_label: Label
var toggle_button: Button
var expanded := false
var _hover_open := false
var _edge_armed := true
var _tween: Tween
var _left := 0.0
var _projection: Array = []
var openness := 0.0:
	set(value):
		openness = value
		position.x = _left - (size.x + 12) * (1.0 - value)

func mount(context) -> void:
	visual_context = context
	name = "CharacterSidebar"
	mouse_filter = Control.MOUSE_FILTER_STOP
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 10)
	add_child(column)
	var toolbar := HBoxContainer.new()
	title_label = context.styles.title(context.translator.text("Attributes"), 18)
	title_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	toolbar.add_child(title_label)
	toggle_button = context.styles.button("×", 36)
	toggle_button.name = "CharacterSidebarToggle"
	toggle_button.tooltip_text = context.translator.text("Close")
	toggle_button.pressed.connect(func(): set_open(false))
	toolbar.add_child(toggle_button); column.add_child(toolbar)
	attributes_view = Attributes.new()
	attributes_view.editorial_layout = true
	attributes_view.fill_available_height = true
	attributes_view.value_heading = "Current"
	attributes_view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	attributes_view.mount(context)
	column.add_child(attributes_view)
	content_scroll = attributes_view._scroll
	hide()

func present(model: Dictionary, context) -> void:
	visual_context = context
	theme = context.theme
	add_theme_stylebox_override("panel", context.styles.box(context.styles.PANEL, 2, context.styles.color("stroke.panel"), 1))
	attributes_view.visual_context = context
	title_label.text = "%s [%s]" % [context.translator.text("Attributes"), context.action_label("open_character")]
	var actors: Array = model.get("actors", [])
	var player_id := str(model.get("run", {}).get("playerEntityId", ""))
	var actor: Dictionary = {}
	for entry in actors:
		if str(entry.get("instanceId", "")) == player_id: actor = entry; break
	if actor.is_empty():
		for entry in actors:
			if str(entry.get("controllerBinding", {}).get("kind", "")).to_lower() == "player": actor = entry; break
	if actor.is_empty() and player_id.is_empty():
		for entry in actors:
			if str(entry.get("instanceId", "")) == str(model.get("input_actor", "")): actor = entry; break
	var facts: Dictionary = model.get("attributes", {})
	if str(facts.get("actorId", "")) != str(actor.get("instanceId", "")): facts = {}
	var display_name: String = context.translator.content_name(str(actor.get("definitionId", player_id)), str(actor.get("displayName", actor.get("definitionId", player_id))))
	var character := Presenter.character(facts, display_name, actor.get("resources", {}), context.translator)
	var key := [character, context.presentation_key()]
	if key == _projection: return
	_projection = key.duplicate(true)
	attributes_view.present(character)

func fit_bounds(bounds: Rect2, width: float, height: float) -> void:
	# Parent follows gameplay bounds; negative local left aligns drawer to viewport.
	_left = -bounds.position.x
	custom_minimum_size.x = minf(width, bounds.size.x)
	size = Vector2(custom_minimum_size.x, maxf(180, minf(height, bounds.size.y - 84)))
	position.y = 0
	openness = openness

func set_open(value: bool, by_hover := false) -> void:
	if expanded == value: return
	expanded = value
	_hover_open = value and by_hover
	if not value: _edge_armed = false
	if _tween != null and _tween.is_valid(): _tween.kill()
	if value: show()
	var viewport := get_viewport()
	if not value and viewport != null:
		var focused := viewport.gui_get_focus_owner()
		if focused != null and is_ancestor_of(focused): focused.release_focus()
	open_changed.emit(value)
	var target := 1.0 if value else 0.0
	if visual_context.reduced_motion or not is_inside_tree():
		openness = target
		visible = value
		return
	_tween = create_tween().set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	_tween.tween_property(self, "openness", target, transition_seconds * absf(target - openness))
	_tween.tween_callback(func(): visible = expanded)

func pointer_moved(point: Vector2) -> void:
	if point.x > 40: _edge_armed = true
	if not expanded and _edge_armed and point.x >= 0 and point.x <= 20:
		set_open(true, true)
	elif expanded and _hover_open:
		var intended := Rect2(Vector2(0, global_position.y), size).grow(20)
		if not intended.has_point(point): set_open(false)

func _exit_tree() -> void:
	if _tween != null and _tween.is_valid(): _tween.kill()
