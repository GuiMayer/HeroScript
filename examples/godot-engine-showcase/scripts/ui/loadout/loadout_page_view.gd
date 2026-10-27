extends Control
## Stable shell. All domain operations belong to the controller, never to this view.
signal tab_requested(id: String)
signal back_requested
signal recovery_requested
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

func mount(context) -> void:
	visual_context = context; theme = context.theme
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var paper := ColorRect.new(); paper.color = context.styles.NIGHT
	paper.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT); paper.mouse_filter = Control.MOUSE_FILTER_IGNORE; add_child(paper)
	var margin := MarginContainer.new(); margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + side, 24)
	add_child(margin)
	var page := VBoxContainer.new(); page.add_theme_constant_override("separation", 12); margin.add_child(page)
	var header := HBoxContainer.new(); page.add_child(header)
	header.add_child(context.styles.title(tr_text("LOADOUT"), 32))
	heading = context.styles.muted(""); heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL; header.add_child(heading)
	attributes_button = button("Attributes", func(): _drawer_open = not _drawer_open; _responsive())
	header.add_child(attributes_button)
	header.add_child(button("BACK", back_requested.emit))
	var tabs := HBoxContainer.new(); tabs.name = "LoadoutModuleTabs"; page.add_child(tabs)
	for entry in preload("res://scripts/ui/loadout/module_catalog.gd").entries():
		var id: String = entry.id
		var control := button(entry.label, func(): tab_requested.emit(id))
		control.toggle_mode = true; control.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		tabs.add_child(control); buttons[id] = control
	page.add_child(HSeparator.new())
	body = HBoxContainer.new(); body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_theme_constant_override("separation", 18); page.add_child(body)
	content = Control.new(); content.name = "LoadoutModuleHost"; content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.size_flags_vertical = Control.SIZE_EXPAND_FILL; body.add_child(content)
	attributes_view = preload("res://scripts/ui/loadout/character_attributes_view.gd").new()
	attributes_view.editorial_layout = true; attributes_view.mount(context)
	attributes_panel = context.styles.panel(attributes_view); attributes_panel.name = "SharedCharacterAttributes"
	attributes_panel.custom_minimum_size.x = 310; body.add_child(attributes_panel)
	var footer := HBoxContainer.new(); page.add_child(footer)
	feedback = context.styles.muted(""); feedback.size_flags_horizontal = Control.SIZE_EXPAND_FILL; footer.add_child(feedback)
	recovery = button("Retry pending operation", recovery_requested.emit); recovery.hide(); footer.add_child(recovery)
	resized.connect(_responsive); _responsive.call_deferred()

func tr_text(key: String) -> String: return visual_context.translator.text(key)
func button(key: String, action: Callable) -> Button:
	var result: Button = visual_context.styles.button(tr_text(key)); result.pressed.connect(action); return result
func install(id: String, control: Control) -> void:
	if modules.has(id): return
	modules[id] = control; content.add_child(control)
	control.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT); control.visible = id == active_tab
func select(id: String) -> void:
	if not buttons.has(id): return
	var viewport = get_viewport()
	var focus = viewport.gui_get_focus_owner() if viewport != null else null
	if is_instance_valid(focus) and content.is_ancestor_of(focus): _last_focus[active_tab] = weakref(focus)
	active_tab = id
	for key in buttons: buttons[key].set_pressed_no_signal(key == id)
	for key in modules: modules[key].visible = key == id
	if _last_focus.has(id):
		var previous = _last_focus[id].get_ref()
		if is_instance_valid(previous) and previous.is_visible_in_tree(): previous.grab_focus()
	elif is_instance_valid(focus) and content.is_ancestor_of(focus): buttons[id].grab_focus()
func set_feedback(message: String, recoverable := false) -> void:
	feedback.text = tr_text(message); recovery.visible = recoverable
func present_attributes(character: Dictionary) -> void:
	attributes_view.present(character.duplicate(true))
func _responsive() -> void:
	if not is_instance_valid(attributes_panel) or size.x <= 0: return
	var narrow: bool = size.x < 1450 * visual_context.text_scale
	attributes_button.visible = narrow
	if narrow:
		if attributes_panel.get_parent() != self: attributes_panel.reparent(self)
		attributes_panel.position = Vector2(maxf(24, size.x - 360), 130)
		attributes_panel.size = Vector2(336, maxf(300, size.y - 190))
		attributes_panel.visible = _drawer_open
	else:
		if attributes_panel.get_parent() != body: attributes_panel.reparent(body)
		attributes_panel.visible = true

