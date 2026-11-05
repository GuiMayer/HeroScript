@tool
class_name CombatAttributesDrawer
extends PanelContainer
## Floating shell around the exact passive attribute component used by loadout.
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
var _surface: StyleBoxFlat
var openness := 0.0:
	set(value):
		openness = value
		position.x = _left - (size.x + 12) * (1.0 - value)

static func create() -> CombatAttributesDrawer:
	return (load("res://scenes/ui/components/combat/combat_attributes_drawer.tscn") as PackedScene).instantiate()
func _ready() -> void:
	if visual_context == null and (Engine.is_editor_hint() or get_parent() == get_tree().root):
		mount(preload("res://scripts/ui/visual_context.gd").new(null,{"reduced_motion":true}))
		attributes_view.present(attributes_view.preview_character)
		expanded = true; openness = 1; show()
func mount(context) -> void:
	var first_mount := visual_context == null
	visual_context = context; theme = context.theme
	title_label = get_node("%Title"); toggle_button = get_node("%CharacterSidebarToggle")
	attributes_view = get_node("%LoadoutCharacterAttributes")
	if _surface == null:
		_surface = get_theme_stylebox("panel").duplicate()
		add_theme_stylebox_override("panel",_surface)
	title_label.text = context.translator.text("Attributes")
	title_label.add_theme_color_override("font_color",context.styles.INK)
	toggle_button.tooltip_text = context.translator.text("Close") + " [" + context.action_label("open_character") + "]"
	toggle_button.accessibility_name = context.translator.text("Close character attributes")
	if not toggle_button.pressed.is_connected(_close): toggle_button.pressed.connect(_close)
	attributes_view.mount(context)
	content_scroll = attributes_view.scroll_container
	if first_mount and not Engine.is_editor_hint(): hide()
func _close() -> void: set_open(false)

func present(model: Dictionary, context) -> void:
	visual_context = context
	theme = context.theme
	_surface.bg_color = context.styles.PANEL
	_surface.border_color = context.styles.color("stroke.panel")
	if has_meta("base_style_panel"): remove_meta("base_style_panel")
	if has_meta("visual_preferences_revision"): remove_meta("visual_preferences_revision")
	add_theme_stylebox_override("panel",_surface)
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
