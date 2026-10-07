extends VBoxContainer
## Shared hand. Instance identities and intents only; no REST or game rules.
signal card_activated(instance_id: String)
signal card_previewed(instance_id: String)
signal inspection_requested(instance_id: String)
signal selection_requested(instance_id: String)
signal drop_requested(instance_id: String, global_point: Vector2)
const Layout = preload("res://scripts/ui/hand_layout.gd")
@export var visual_style: HandVisualStyle = preload("res://data/default_hand_visual_style.tres")
var interaction = preload("res://scripts/ui/hand_interaction.gd").new()
var read_only := false
var reading_modal := false
var mode_override := ""
var drag_override = null
var scope := ""
var order: Array[String] = []
var _cards := {}
var _slots := {}
var _motions := {}
var _emphasis := {}
var _layout: Dictionary = {}
var relayout_count := 0
var scroll := ScrollContainer.new()
var surface := Control.new()
var hit_surface = preload("res://scripts/ui/hand_hit_surface.gd").new()
var empty: Control
var counter: Label
var toolbar := HBoxContainer.new()
var previous: Button
var next: Button
var _ghost: CardView
var _reading_proxy: CardView
var _layout_pending := false
var _syncing := false
var _reserved_card_size := Vector2.ZERO
var _layout_text_scale := -1.0
var _inspection_anchor := ""

func _init() -> void:
	name = "CardHandView"
	size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_theme_constant_override("separation", 2)
	var tools := toolbar
	tools.custom_minimum_size.y = 28
	counter = AppTheme.caption("", 12, 56)
	counter.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	tools.add_child(counter)
	previous = _tool("‹", func(): page(-1))
	next = _tool("›", func(): page(1))
	tools.add_child(previous)
	tools.add_child(next)
	var inspect := _tool(I18n.text("Inspect card"), func(): inspection_requested.emit(active_card_id()))
	inspect.name = "HandInspectButton"
	tools.add_child(inspect)
	var overview := _tool(I18n.text("Hand overview"), show_overview)
	overview.name = "HandOverviewButton"
	overview.set_meta("initial_focus", true)
	tools.add_child(overview)
	add_child(tools)
	scroll.name = "HandScroll"
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	scroll.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	surface.name = "HandSlots"
	surface.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	surface.mouse_filter = Control.MOUSE_FILTER_IGNORE
	scroll.add_child(surface)
	add_child(scroll)
	hit_surface.name = "HandHitSurface"
	hit_surface.hand = self
	hit_surface.tooltip_text = " "
	hit_surface.mouse_exited.connect(func(): interaction.hovered = ""; _apply_emphasis())
	surface.add_child(hit_surface)
	hit_surface.set_as_top_level(true)

func _ready() -> void:
	scroll.resized.connect(request_layout)
	scroll.get_h_scroll_bar().value_changed.connect(func(_value): _update_counter())
	scroll.get_h_scroll_bar().visibility_changed.connect(position_hit_surface)
	Preferences.changed.connect(_preferences_changed)
	set_notify_transform(true)
	request_layout()

func _tool(label: String, action: Callable) -> Button:
	var button := AppTheme.button(label)
	button.custom_minimum_size = Vector2(30, 28)
	button.add_theme_font_size_override("font_size", 12)
	for state in ["normal", "hover", "pressed", "disabled", "focus"]:
		var border := AppTheme.GOLD if state in ["hover", "focus"] else AppTheme.MUTED.darkened(.4)
		var box := AppTheme.box(AppTheme.PANEL_LIGHT, 6, border, 2 if state == "focus" else 1)
		box.content_margin_top = 4; box.content_margin_bottom = 4
		button.add_theme_stylebox_override(state, box)
	button.pressed.connect(action)
	return button

func _preferences_changed() -> void:
	interaction.cancel_pointer()
	_clear_ghost()
	for card in _cards.values(): card.configure(card.model)
	request_layout()

func layout_mode() -> String:
	return mode_override if not mode_override.is_empty() else Preferences.hand_layout_mode
func card_for(id: String): return _cards.get(id)
func active_card_id() -> String:
	var id: String = interaction.active_id()
	return id if id in order else (_inspection_anchor if _inspection_anchor in order else "")
func cards() -> Dictionary: return _cards.duplicate()
func ordered_cards() -> Array: return order.map(func(id): return _cards[id])

func set_tools_below_cards(value: bool) -> void:
	# Combat uses a floating bottom hand; labs and overview retain their toolbar.
	move_child(toolbar, get_child_count() - 1 if value else 0)

func sync_models(models: Array, context: String, empty_message := "Your hand is empty.") -> void:
	_syncing = true
	var focus_id: String = interaction.focused
	var anchor: String = focus_id if not focus_id.is_empty() else interaction.selected
	var old_index := order.find(anchor)
	var old_order := order.duplicate()
	if context != scope:
		for id in order: _remove(id)
		order.clear()
		interaction.clear()
		_inspection_anchor = ""
		scroll.scroll_horizontal = 0
		scope = context
		anchor = ""
		focus_id = ""
	var incoming: Array[String] = []
	var geometry_changed := false
	for model in models:
		var id := str(model.get("cardInstanceId", model.get("labId", "")))
		assert(not id.is_empty() and id not in incoming, "Hand models need unique instance IDs")
		incoming.append(id)
		if not _cards.has(id):
			var card := CardView.new()
			card.name = "HandCard_" + id.validate_node_name()
			card.animate_entry = false
			card.hand_managed = true
			card.toggle_mode = true
			card.mouse_filter = Control.MOUSE_FILTER_IGNORE
			card.configure(model)
			var slot := Control.new()
			slot.name = "Slot_" + id.validate_node_name()
			slot.mouse_filter = Control.MOUSE_FILTER_IGNORE
			slot.add_child(card)
			surface.add_child(slot)
			_cards[id] = card
			_slots[id] = slot
			card.pressed.connect(func():
				if read_only: card.set_pressed_no_signal(id == interaction.selected)
				else: card_activated.emit(id))
			card.focus_entered.connect(func(): _focus(id))
			card.focus_exited.connect(func():
				if interaction.focused == id: interaction.focused = ""
				_apply_emphasis())
			if is_inside_tree() and not Preferences.reduced_motion:
				card.modulate.a = 0
				card.create_tween().tween_property(card, "modulate:a", 1.0, duration())
		else:
			var card: CardView = _cards[id]
			var before := card.custom_minimum_size
			card.configure(model)
			geometry_changed = geometry_changed or before != card.custom_minimum_size
	for id in order:
		if id not in incoming: _remove(id)
	order = incoming
	if interaction.selected not in order: interaction.selected = ""
	if interaction.hovered not in order: interaction.hovered = ""
	if interaction.pressed not in order: interaction.cancel_pointer(); _clear_ghost()
	if not order.is_empty() and is_instance_valid(empty): surface.remove_child(empty); empty.queue_free(); empty = null
	if order.is_empty():
		if not is_instance_valid(empty): empty = empty_state(empty_message); surface.add_child(empty)
		else: empty.find_child("EmptyHandMessage", true, false).text = empty_message
	if old_order != order or geometry_changed or _layout.is_empty(): request_layout()
	if not anchor.is_empty() and anchor in order: reveal.call_deferred(anchor)
	elif not focus_id.is_empty() and not order.is_empty(): _cards[order[clampi(old_index, 0, order.size() - 1)]].grab_focus()
	_syncing = false
	_apply_emphasis()
	_update_counter()

func _remove(id: String) -> void:
	if id == _inspection_anchor: _inspection_anchor = ""
	if _motions.has(id) and _motions[id].is_valid(): _motions[id].kill()
	if _emphasis.has(id) and _emphasis[id].tween != null and _emphasis[id].tween.is_valid(): _emphasis[id].tween.kill()
	if _slots.has(id): surface.remove_child(_slots[id]); _slots[id].queue_free()
	_cards.erase(id)
	_slots.erase(id)
	_motions.erase(id)
	_emphasis.erase(id)

func set_selection(id: String) -> void:
	interaction.selected = id if id in order else ""
	for key in order: _cards[key].select_card(key == interaction.selected)
	_apply_emphasis()

func clear_choice() -> void:
	# An accepted play ends this choice, even if the engine retains the instance.
	# Release card focus before reconciliation can transfer it to a neighboring card.
	cancel_gesture()
	interaction.clear()
	_inspection_anchor = ""
	for card in _cards.values():
		if card.has_focus(): card.release_focus()
		card.select_card(false)
	if is_inside_tree() and not get_tree().paused:
		var neutral: Control = find_child("HandOverviewButton", true, false)
		if neutral.is_visible_in_tree(): neutral.grab_focus()
	_apply_emphasis()
func set_read_only(value: bool) -> void:
	read_only = value
	if value: cancel_gesture()
func cancel_gesture() -> void:
	interaction.cancel_pointer()
	_clear_ghost()
	if is_instance_valid(_reading_proxy): _reading_proxy.hide()
func duration() -> float: return visual_style.transition_seconds / maxf(Preferences.animation_speed, .25)

func request_layout() -> void:
	if _layout_pending: return
	_layout_pending = true
	_relayout.call_deferred()
func _relayout() -> void:
	_layout_pending = false
	if not is_inside_tree(): return
	relayout_count += 1
	var default_style = preload("res://data/default_card_visual_style.tres")
	var dimensions: Vector2 = default_style.effective_size(Preferences.text_scale)
	if not is_equal_approx(_layout_text_scale, Preferences.text_scale): _reserved_card_size = Vector2.ZERO
	_layout_text_scale = Preferences.text_scale
	for card in _cards.values(): dimensions = dimensions.max(card.get_combined_minimum_size())
	dimensions = dimensions.max(_reserved_card_size)
	_reserved_card_size = dimensions
	_layout = Layout.calculate(order.size(), maxf(1, scroll.size.x), dimensions, visual_style, layout_mode())
	scroll.custom_minimum_size.y = _layout.height + 12
	surface.custom_minimum_size = Vector2(_layout.extent, _layout.height)
	for i in order.size():
		var id := order[i]
		var card: CardView = _cards[id]
		var slot: Control = _slots[id]
		card.size = dimensions
		card.pivot_offset = dimensions * .5
		slot.size = dimensions
		var target: Vector2 = _layout.positions[i]
		if slot.has_meta("placed") and slot.position != target and not Preferences.reduced_motion:
			if _motions.has(id) and _motions[id].is_valid(): _motions[id].kill()
			_motions[id] = create_tween()
			_motions[id].tween_property(slot, "position", target, duration())
		else: slot.position = target
		slot.set_meta("placed", true)
		if _emphasis.has(id): _emphasis[id].rotation = -999.0
	if is_instance_valid(empty): empty.position = Vector2(0, visual_style.lift); empty.size = Vector2(scroll.size.x, dimensions.y)
	position_hit_surface()
	_apply_emphasis()
	_update_counter()
	wire_focus()
func position_hit_surface() -> void:
	if not is_inside_tree(): return
	hit_surface.global_position = scroll.global_position
	hit_surface.size = Vector2(scroll.size.x, maxf(0, scroll.size.y - (scroll.get_h_scroll_bar().size.y if scroll.get_h_scroll_bar().visible else 0)))
func _notification(what: int) -> void:
	if what == NOTIFICATION_TRANSFORM_CHANGED and is_instance_valid(hit_surface): position_hit_surface()
	elif what == NOTIFICATION_PAUSED: cancel_gesture()

func _apply_emphasis() -> void:
	if not is_inside_tree() or _layout.is_empty() or _syncing: return
	var active: String = interaction.active_id()
	for i in order.size():
		var id := order[i]
		if not _cards.has(id): continue
		if i >= _layout.rotations.size(): continue
		var raised := id == active
		var target := Vector2(0, -visual_style.lift * Preferences.hand_emphasis) if raised else Vector2.ZERO
		var angle: float = 0 if raised else _layout.rotations[i]
		var card: CardView = _cards[id]
		_slots[id].z_index = 100 if raised else (50 if id == interaction.selected else mini(i, 40))
		if _emphasis.has(id) and _emphasis[id].position == target and is_equal_approx(_emphasis[id].rotation, angle): continue
		if _emphasis.has(id) and _emphasis[id].tween != null and _emphasis[id].tween.is_valid(): _emphasis[id].tween.kill()
		var tween: Tween = null
		if not Preferences.reduced_motion and is_inside_tree():
			tween = create_tween().set_parallel()
			tween.tween_property(card, "position", target, duration())
			tween.tween_property(card, "rotation", angle, duration())
		else: card.position = target; card.rotation = angle
		_emphasis[id] = {"position": target, "rotation": angle, "tween": tween}
	_update_reading_proxy()

func _update_reading_proxy() -> void:
	if not is_inside_tree(): return
	# A partially clipped hover stays readable without scrolling under the pointer.
	# This copy is visual only: the stable hit surface still owns all input.
	var id: String = interaction.active_id()
	var card: CardView = _cards.get(id)
	if read_only or reading_modal or interaction.dragging or not is_instance_valid(card):
		if is_instance_valid(_reading_proxy): _reading_proxy.hide()
		return
	var clip := scroll.get_global_rect()
	var resting: Vector2 = _slots[id].global_position
	if resting.x >= clip.position.x and resting.x + card.size.x <= clip.end.x:
		if is_instance_valid(_reading_proxy): _reading_proxy.hide()
		return
	if not is_instance_valid(_reading_proxy):
		_reading_proxy = CardView.new()
		_reading_proxy.name = "HandReadingProxy"
		_reading_proxy.animate_entry = false
		_reading_proxy.hand_managed = true
		_reading_proxy.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_reading_proxy.focus_mode = Control.FOCUS_NONE
		add_child(_reading_proxy)
		_reading_proxy.set_as_top_level(true)
		_reading_proxy.z_index = 200
	_reading_proxy.visual_style = card.visual_style
	_reading_proxy.configure(card.model)
	_reading_proxy.tooltip_text = ""
	_reading_proxy.size = card.size
	_reading_proxy.global_position = Vector2(clampf(resting.x, clip.position.x + visual_style.padding,
		maxf(clip.position.x + visual_style.padding, clip.end.x - card.size.x - visual_style.padding)),
		resting.y - visual_style.lift * Preferences.hand_emphasis)
	_reading_proxy.show()

func pick(local_point: Vector2, stable := false) -> String:
	if _layout.is_empty() or not Rect2(scroll.position, scroll.size).has_point(local_point): return ""
	var point := local_point - scroll.position + Vector2(scroll.scroll_horizontal, 0)
	var current := order.find(interaction.hovered)
	if stable and current >= 0 and current < _layout.hitRects.size() and _layout.hitRects[current].grow(visual_style.pointer_hysteresis).has_point(point): return interaction.hovered
	for i in mini(order.size(), _layout.hitRects.size()):
		if _layout.hitRects[i].has_point(point): return order[i]
	return ""
func _focus(id: String) -> void:
	interaction.focused = id
	_inspection_anchor = id
	interaction.pointer_active = false
	reveal(id)
	_apply_emphasis()
	card_previewed.emit(id)
func reveal(id: String) -> void:
	var index := order.find(id)
	if index < 0 or _layout.is_empty() or index >= _layout.positions.size(): return
	var left: float = _layout.positions[index].x - visual_style.padding
	var right: float = left + _cards[id].size.x + 2 * visual_style.padding
	if left < scroll.scroll_horizontal: scroll.scroll_horizontal = int(left)
	elif right > scroll.scroll_horizontal + scroll.size.x: scroll.scroll_horizontal = int(right - scroll.size.x)
	_update_counter()
func page(direction: int) -> void:
	scroll.scroll_horizontal += int(direction * scroll.size.x * .7)
	_update_counter()
func _update_counter() -> void:
	var overflow := not _layout.is_empty() and bool(_layout.overflow)
	counter.text = (I18n.text("%s cards") % order.size()) + ("  ↔" if overflow else "")
	previous.disabled = not overflow or scroll.scroll_horizontal <= 0
	next.disabled = not overflow or scroll.scroll_horizontal >= scroll.get_h_scroll_bar().max_value - scroll.get_h_scroll_bar().page - 1
	counter.tooltip_text = I18n.text("More cards — scroll or open hand overview.") if overflow else ""
	var inspect: Button = find_child("HandInspectButton", true, false)
	var overview: Button = find_child("HandOverviewButton", true, false)
	if is_instance_valid(inspect): inspect.text = "%s [%s]" % [I18n.text("Inspect card"), Preferences.action_label("inspect_card")]
	if is_instance_valid(overview): overview.text = "%s [%s]" % [I18n.text("Hand overview"), Preferences.action_label("hand_overview")]
	_update_reading_proxy()

func pointer_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		interaction.pointer_active = true
		var id := pick(event.position + scroll.position, true)
		if id != interaction.hovered:
			interaction.hovered = id
			if not id.is_empty(): _inspection_anchor = id; card_previewed.emit(id)
		_apply_emphasis()
		if not read_only and not interaction.pressed.is_empty() and (Preferences.hand_drag_enabled if drag_override == null else drag_override):
			if event.global_position.distance_to(interaction.press_position) >= visual_style.drag_threshold:
				interaction.dragging = true
				_drag_visual(event.global_position)
	elif event is InputEventMouseButton:
		if event.button_index in [MOUSE_BUTTON_WHEEL_UP, MOUSE_BUTTON_WHEEL_DOWN] and event.pressed:
			page(-1 if event.button_index == MOUSE_BUTTON_WHEEL_UP else 1)
			return
		if event.button_index != MOUSE_BUTTON_LEFT: return
		var id := pick(event.position + scroll.position)
		if event.pressed:
			if not read_only: interaction.pressed = id; interaction.press_position = event.global_position
		elif not interaction.pressed.is_empty():
			var pressed: String = interaction.pressed
			var dragging: bool = interaction.dragging
			interaction.cancel_pointer()
			_clear_ghost()
			if read_only or pressed not in order: return
			if dragging: drop_requested.emit(pressed, event.global_position)
			elif id == pressed: _cards[id].grab_focus(); card_activated.emit(id)
func _input(event: InputEvent) -> void:
	if interaction.pressed.is_empty(): return
	if event.is_action_pressed("ui_cancel") or (event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT):
		interaction.cancel_pointer()
		_clear_ghost()
		get_viewport().set_input_as_handled()
func _drag_visual(point: Vector2) -> void:
	if not is_instance_valid(_ghost):
		_ghost = CardView.new()
		_ghost.hand_managed = true
		_ghost.animate_entry = false
		_ghost.visual_style = _cards[interaction.pressed].visual_style
		_ghost.configure(_cards[interaction.pressed].model)
		_ghost.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_ghost.focus_mode = Control.FOCUS_NONE
		_ghost.tooltip_text = ""
		add_child(_ghost)
		_ghost.set_as_top_level(true)
		_ghost.z_index = 500
		_ghost.size = _cards[interaction.pressed].size
		_ghost.modulate.a = .85
	_ghost.global_position = point - _ghost.size * .5
func _clear_ghost() -> void:
	if is_instance_valid(_ghost): remove_child(_ghost); _ghost.queue_free()
	_ghost = null
func show_overview() -> void:
	if reading_modal: return
	var dialog = load("res://scripts/ui/hand_overview_dialog.gd").new()
	dialog.setup(order.map(func(id): return _cards[id].model.duplicate(true)), scope, interaction.selected)
	var return_focus := {"id": active_card_id()}
	dialog.card_chosen.connect(func(id): return_focus.id = id; reveal(id); selection_requested.emit(id))
	dialog.tree_exiting.connect(func():
		if str(return_focus.id) in order: _cards[str(return_focus.id)].grab_focus())
	add_child(dialog)
	track_reading_modal(dialog)
	dialog.popup_centered()
func track_reading_modal(dialog: Window) -> void:
	reading_modal = true
	cancel_gesture()
	dialog.tree_exiting.connect(func(): reading_modal = false; _apply_emphasis())
func wire_focus() -> void:
	for i in order.size():
		var card: Control = _cards[order[i]]
		card.focus_neighbor_left = card.get_path_to(_cards[order[maxi(0, i - 1)]])
		card.focus_neighbor_right = card.get_path_to(_cards[order[mini(order.size() - 1, i + 1)]])
	if not order.is_empty():
		# Leaving focus on a tool must not prevent deliberate navigation back to
		# the cards. Geometry alone can choose another same-row tool instead.
		var id := active_card_id()
		var destination: Control = _cards[id if id in order else order[0]]
		var direction := "focus_neighbor_top" if toolbar.get_index() > scroll.get_index() else "focus_neighbor_bottom"
		for tool in [previous, next, find_child("HandInspectButton", true, false), find_child("HandOverviewButton", true, false)]:
			tool.set(direction, tool.get_path_to(destination))
static func empty_state(message: String) -> Control:
	var state := CenterContainer.new()
	state.name = "EmptyHandState"
	state.custom_minimum_size = Vector2(320, 180)
	state.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var label := AppTheme.muted(message, 15, 280)
	label.name = "EmptyHandMessage"
	label.autowrap_mode = TextServer.AUTOWRAP_OFF
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	state.add_child(label)
	return state
