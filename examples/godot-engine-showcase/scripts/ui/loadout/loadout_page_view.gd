@tool
class_name LoadoutPageView
extends Control
## Stable shell. All domain operations belong to the controller, never to this view.
signal tab_requested(id: String)
signal back_requested
signal recovery_requested
signal attribute_sources_requested
var visual_context
var active_tab := "overview"
var modules := {}
var buttons := {}
var heading: Label
var feedback: Label
var recovery: Button
var body: HBoxContainer
var content: Control
var attributes_panel: PanelContainer
var attributes_view
var attributes_button: Button
var _drawer_open := false
var _last_focus := {}

static func create() -> LoadoutPageView:
	return (load("res://scenes/ui/components/loadout/loadout_page.tscn") as PackedScene).instantiate()
func _ready() -> void:
	if visual_context == null:
		mount(preload("res://scripts/ui/visual_context.gd").new(null, {}, preload("res://data/themes/ivory_theme.tres")))
func mount(context) -> void:
	visual_context = context; theme = context.theme
	heading = get_node("%Heading"); feedback = get_node("%Feedback"); recovery = get_node("%Recovery")
	body = get_node("%Body"); content = get_node("%LoadoutModuleHost")
	attributes_panel = get_node("%SharedCharacterAttributes")
	attributes_view = attributes_panel.get_node("LoadoutCharacterAttributes")
	attributes_view.mount(context)
	attributes_button = get_node("%AttributesButton")
	get_node("%Paper").color = context.styles.NIGHT
	get_node("%Title").text = tr_text("LOADOUT")
	for entry in [[attributes_button, "Attributes"], [get_node("%BackButton"), "BACK"], [recovery, "Retry pending operation"], [attributes_view.get_node("AttributeSources"), "Attribute sources"]]: entry[0].text = tr_text(entry[1])
	var surface: StyleBoxFlat = attributes_panel.get_theme_stylebox("panel").duplicate()
	surface.bg_color = context.styles.color("surface.panel"); surface.border_color = context.styles.color("stroke.panel")
	attributes_panel.add_theme_stylebox_override("panel", surface)
	if buttons.is_empty():
		attributes_button.pressed.connect(func(): _drawer_open = not _drawer_open; _responsive())
		get_node("%BackButton").pressed.connect(back_requested.emit)
		recovery.pressed.connect(recovery_requested.emit)
		attributes_view.get_node("AttributeSources").pressed.connect(attribute_sources_requested.emit)
		for entry in preload("res://scripts/ui/loadout/module_catalog.gd").entries():
			var id: String = entry.id
			var control: Button = preload("res://scenes/ui/components/loadout/module_tab.tscn").instantiate()
			control.name = "Tab_" + id; control.text = tr_text(entry.label)
			control.pressed.connect(func(): tab_requested.emit(id))
			get_node("%LoadoutModuleTabs").add_child(control); buttons[id] = control
	for entry in preload("res://scripts/ui/loadout/module_catalog.gd").entries():
		buttons[entry.id].text = tr_text(entry.label)
	if not resized.is_connected(_responsive): resized.connect(_responsive)
	_responsive.call_deferred()

func tr_text(key: String) -> String: return visual_context.translator.text(key)
func install(id: String, control: Control) -> void:
	if modules.has(id): return
	modules[id] = control
	if control.get_parent() != content: content.add_child(control)
	control.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT); control.visible = id == active_tab
func select(id: String) -> void:
	if not buttons.has(id): return
	var viewport = get_viewport()
	var focus = viewport.gui_get_focus_owner() if viewport != null else null
	if is_instance_valid(focus) and content.is_ancestor_of(focus): _last_focus[active_tab] = weakref(focus)
	active_tab = id
	for key in buttons: buttons[key].set_pressed_no_signal(key == id)
	for key in modules: modules[key].visible = key == id
	var restored := false
	if _last_focus.has(id):
		var previous = _last_focus[id].get_ref()
		if is_instance_valid(previous) and previous.is_visible_in_tree(): previous.grab_focus(); restored = true
	if not restored and is_instance_valid(focus) and content.is_ancestor_of(focus): buttons[id].grab_focus()
func set_feedback(message: String, recoverable := false) -> void:
	feedback.text = tr_text(message); recovery.visible = recoverable
func present_attributes(character: Dictionary) -> void:
	attributes_view.present(character.duplicate(true))
func _responsive() -> void:
	if not is_instance_valid(attributes_panel) or size.x <= 0 or visual_context == null: return
	if Engine.is_editor_hint(): return
	var narrow: bool = size.x < 1450 * visual_context.text_scale
	attributes_button.visible = narrow
	if narrow:
		if attributes_panel.get_parent() != self:
			attributes_panel.owner = null; attributes_panel.reparent(self); attributes_panel.owner = self
		attributes_panel.position = Vector2(maxf(24, size.x - 360), 130)
		attributes_panel.size = Vector2(336, maxf(300, size.y - 190))
		var viewport = get_viewport()
		var focused = viewport.gui_get_focus_owner() if viewport != null else null
		attributes_panel.visible = _drawer_open
		if not _drawer_open and is_instance_valid(focused) and attributes_panel.is_ancestor_of(focused): attributes_button.grab_focus()
	else:
		if attributes_panel.get_parent() != body:
			attributes_panel.owner = null; attributes_panel.reparent(body); attributes_panel.owner = self
		attributes_panel.visible = true

func set_save_status(busy: bool, uncertain: bool) -> void:
	get_node("%SaveStatus").text = tr_text("Attention — recovery required" if uncertain else ("Saving…" if busy else "Synced"))
func present_counts(counts: Dictionary) -> void:
	for entry in preload("res://scripts/ui/loadout/module_catalog.gd").entries():
		var suffix := " (%s)" % counts[entry.id] if counts.has(entry.id) else ""
		buttons[entry.id].text = tr_text(entry.label) + suffix
