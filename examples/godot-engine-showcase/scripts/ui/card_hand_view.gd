@tool
class_name CardHandView
extends VBoxContainer
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
## Shared hand. Instance identities and intents only; no REST or game rules.
signal card_activated(instance_id: String)
signal card_previewed(instance_id: String)
signal card_hovered(instance_id: String)
## Physical card entry only; focus-to-pointer resumption is not another entry.
signal card_hover_entered(instance_id: String)
signal inspection_requested(instance_id: String)
signal selection_requested(instance_id: String)
signal drop_requested(instance_id: String, global_point: Vector2)
signal drag_previewed(instance_id: String, global_point: Vector2)
signal drag_ended
signal visual_order_changed(ids: Array)
signal reorder_settled
const Layout = preload("res://scripts/ui/hand_layout.gd")
const Retarget = preload("res://scripts/ui/hand_retarget_motion.gd")
const CardReconciler = preload("res://scripts/ui/hand_card_reconciler.gd")
const PointerCoordinator = preload("res://scripts/ui/hand_pointer_coordinator.gd")
@export var visual_style: HandVisualStyle = preload("res://data/default_hand_visual_style.tres")
var interaction = preload("res://scripts/ui/hand_interaction.gd").new()
var read_only := false
var reading_modal := false
var _reading_windows := {}
var mode_override := ""
## Component policy for inspection-only collections, never a player preference.
var drag_enabled := true
var scope := ""
var order: Array[String] = []
var arrangement = preload("res://scripts/ui/hand_arrangement.gd").new()
var _arrangement_bound := false
var reorder_enabled := true
var reorder_drop_filter: Callable
var _preview_slot := -1
var _reorder_scroll_fraction := 0.0
var _reorder_settling := false
var _cards := {}
var _slots := {}
var _motions := {}
var _emphasis := {}
var _transit := {}
var _entries := {}
var _preparations := {}
var _destination_order: Array = []
var _final_canonical_order: Array = []
var _presentation_order: Array = []
var _presenting := false
var _aligning_arrivals := false
var _arrival_transition_limit := INF
var _layout: Dictionary = {}
var relayout_count := 0
var scroll: ScrollContainer
var surface: Control
var hit_surface
var empty: Control
var counter: Label
var toolbar: HBoxContainer
var previous: Button
var next: Button
var _ghost: CardView
var _ghost_id := ""
var _parked_drag := ""
var _drag_pose: Dictionary = {}
var _grip_offset := Vector2.ZERO
var _last_drag_point := Vector2.ZERO
var _last_drag_usec := 0
var _drag_tilt := 0.0
var _return_tween: Tween
var _reading_proxy: CardView
var _layout_pending := false
var _syncing := false
var _reserved_card_size := Vector2.ZERO
var _layout_text_scale := -1.0
var _layout_card_style_key := ""
var _inspection_anchor := ""
var _hover_point := Vector2.INF
var overview_enabled := true
@export_file("*.json") var preview_document := "res://data/labs/ui_editor_preview.json"
@export var toolbar_path := NodePath("Toolbar")
var toolbar_external := false

var _nodes_bound := false
static func create(context = null) -> CardHandView:
	var hand = (load("res://scenes/ui/components/card_hand.tscn") as PackedScene).instantiate()
	hand.mount(context if context != null else ContextType.new())
	return hand
func _init(context = null) -> void:
	visual_context = context if context != null else ContextType.new()
func _bind_nodes() -> void:
	if _nodes_bound: return
	_nodes_bound = true
	toolbar = get_node(toolbar_path); counter = toolbar.get_node("Counter")
	previous = toolbar.get_node("Previous"); next = toolbar.get_node("Next")
	scroll = get_node("HandScroll"); surface = scroll.get_node("HandSlots")
	hit_surface = surface.get_node("HandHitSurface"); hit_surface.hand = self
	previous.pressed.connect(func(): page(-1)); next.pressed.connect(func(): page(1))
	toolbar.get_node("HandInspectButton").pressed.connect(func(): inspection_requested.emit(active_card_id()))
	toolbar.get_node("HandOverviewButton").pressed.connect(show_overview)
	hit_surface.mouse_exited.connect(func(): interaction.hovered = ""; _hover_point = Vector2.INF; hit_surface.follow_card(""); _apply_emphasis())
func mount(context) -> void:
	if visual_context != context and visual_context.changed.is_connected(_preferences_changed): visual_context.changed.disconnect(_preferences_changed)
	visual_context = context; theme = context.theme; _bind_nodes()
	toolbar.get_node("HandInspectButton").text = context.translator.text("Inspect card")
	var overview = toolbar.get_node_or_null("HandOverviewButton")
	if overview != null: overview.text = context.translator.text("Hand overview")
	for button in toolbar.get_children():
		if not button is Button: continue
		for state in ["normal", "hover", "pressed", "disabled", "focus"]:
			var box: StyleBoxFlat = button.get_theme_stylebox(state).duplicate()
			box.bg_color = context.styles.PANEL_LIGHT
			box.border_color = context.styles.GOLD if state in ["hover", "focus"] else context.styles.MUTED.darkened(.4)
			button.add_theme_stylebox_override(state, box)
	if is_inside_tree() and not context.changed.is_connected(_preferences_changed): context.changed.connect(_preferences_changed)
func _ready() -> void:
	_preview_if_isolated.call_deferred()
	mount(visual_context)
	scroll.resized.connect(request_layout)
	scroll.get_h_scroll_bar().value_changed.connect(func(_value): _update_counter())
	scroll.get_h_scroll_bar().visibility_changed.connect(position_hit_surface)
	if not visual_context.changed.is_connected(_preferences_changed): visual_context.changed.connect(_preferences_changed)
	set_notify_transform(true); request_layout()

func _preferences_changed() -> void:
	visual_context.capture_motion()
	theme = visual_context.theme
	interaction.cancel_pointer()
	_clear_ghost()
	toolbar.get_node("HandInspectButton").text = visual_context.translator.text("Inspect card")
	var overview := toolbar.get_node_or_null("HandOverviewButton")
	if overview: overview.text = visual_context.translator.text("Hand overview")
	_update_counter()
	for card in _cards.values(): card.configure(card.model)
	request_layout()

func resolved_hand_mode() -> String:
	return mode_override if not mode_override.is_empty() else visual_context.hand_layout_mode
func card_for(id: String): return _cards.get(id)
func active_card_id() -> String:
	var id: String = interaction.active_id()
	return id if id in order else (_inspection_anchor if _inspection_anchor in order else "")
func cards() -> Dictionary: return _cards.duplicate()
func ordered_cards() -> Array: return order.map(func(id): return _cards[id])
func move_visual_card(id: String, index: int) -> bool:
	if not reorder_enabled or read_only or reading_modal or _presenting or interaction.dragging or is_instance_valid(_ghost) or id not in order: return false
	arrangement.begin(id); arrangement.move(index)
	if not arrangement.commit(): return false
	order = arrangement.confirmed.duplicate()
	request_layout(); wire_focus()
	visual_order_changed.emit(order.duplicate())
	return true

func begin_presentation(final_models: Array) -> void:
	cancel_gesture()
	_presenting = true
	_presentation_order = order.duplicate()
	_final_canonical_order = final_models.map(func(model): return str(model.cardInstanceId))
	_destination_order = arrangement.project(_final_canonical_order)
	var merged := ordered_cards().map(func(card): return card.model)
	for model in final_models:
		if not _cards.has(str(model.cardInstanceId)):
			_transit[str(model.cardInstanceId)] = true
			merged.append(model)
	sync_models(merged, scope)
	_relayout()

func prepare_zone_beat(steps: Array, role: Callable) -> void:
	_destination_order = _presentation_order.duplicate()
	_aligning_arrivals = false
	_arrival_transition_limit = INF
	var arrival_duration := 0.0
	var policy = visual_context.motion if visual_context.motion != null else visual_context.capture_motion()
	for step in steps:
		var ids: Array = step.get("instanceIds", []).duplicate()
		ids.append_array(step.get("createdInstanceIds", []))
		ids.append_array(step.get("destroyedInstanceIds", []))
		if role.call(str(step.get("sourceAddress", ""))) == "hand":
			for id in ids: _destination_order.erase(str(id))
		if role.call(str(step.get("targetAddress", ""))) == "hand":
			_aligning_arrivals = true
			arrival_duration = maxf(arrival_duration, policy.duration("create" if not step.get("createdInstanceIds", []).is_empty() else "draw", true))
			for id in ids:
				if _cards.has(str(id)) and str(id) not in _destination_order: _destination_order.append(str(id))
			# Visible playable zones can publish insertion order; do not assume append.
			if step.has("targetOrderAfter"):
				var published: Array = step.targetOrderAfter.filter(func(id): return _cards.has(str(id))).map(func(id): return str(id))
				_destination_order = preload("res://scripts/ui/hand_arrangement.gd").reconcile(published, _destination_order) if arrangement.manual else published
	for id in _destination_order:
		if id not in _presentation_order: begin_transit(str(id))
	if _aligning_arrivals: _arrival_transition_limit = arrival_duration
	_relayout()

func settle_zone_beat() -> void:
	_presentation_order = _destination_order.duplicate()
	_aligning_arrivals = false
	_arrival_transition_limit = INF
	_relayout()

func begin_transit(id: String) -> void:
	_transit[id] = true
	if _cards.has(id): Retarget.reset(_cards[id])
	if _preparations.has(id) and _preparations[id].is_valid(): _preparations[id].kill()
	_preparations.erase(id)
	if _entries.has(id):
		if _entries[id].is_valid(): _entries[id].kill()
		_entries.erase(id)
	if _emphasis.has(id):
		var tween = _emphasis[id].tween
		if tween != null and tween.is_valid(): tween.kill()
		_emphasis.erase(id)
	if _cards.has(id): _cards[id].hide()
	if hit_surface._tooltip_id == id: hit_surface.follow_card("")
	if is_instance_valid(_reading_proxy): _reading_proxy.hide()

func finish_transit(id: String, reveal_card := true) -> void:
	if reveal_card and _presenting and _aligning_arrivals and id in _destination_order and _cards.has(id):
		var index := order.find(id)
		if index >= 0 and index < _layout.get("rotations", []).size():
			_cards[id].position = Vector2.ZERO
			_cards[id].rotation = _layout.rotations[index]
			_cards[id].scale = Vector2.ONE
			_emphasis[id] = {"position": Vector2.ZERO, "rotation": _layout.rotations[index], "tween": null}
	_transit.erase(id)
	if _cards.has(id):
		_cards[id].modulate.a = 1.0
		_cards[id].visible = reveal_card
	_apply_emphasis()

func end_presentation() -> void:
	if _presenting:
		arrangement.sync(_final_canonical_order)
		order = arrangement.confirmed.filter(func(id): return _cards.has(id))
	for id in _destination_order: finish_transit(str(id))
	_presenting = false; _destination_order.clear(); _presentation_order.clear()
	_final_canonical_order.clear()
	_aligning_arrivals = false; _arrival_transition_limit = INF
	for id in _transit.keys(): finish_transit(id)
	request_layout()

func destination_center(id: String) -> Vector2:
	return destination_pose(id).get("center", scroll.get_global_rect().get_center())

func destination_pose(id: String) -> Dictionary:
	if not _cards.has(id): return {}
	var position: Vector2
	var rotation: float
	if _presenting and id in _destination_order:
		var target := Layout.calculate(_destination_order.size(), maxf(1, scroll.size.x), _reserved_card_size, visual_style, resolved_hand_mode())
		var index := _destination_order.find(id)
		position = target.positions[index]; rotation = target.rotations[index]
	else:
		var index := order.find(id)
		if index < 0 or index >= _layout.get("positions", []).size(): return {}
		position = _layout.positions[index]; rotation = _layout.rotations[index]
	var transform := surface.get_global_transform()
	var basis := transform * Transform2D(rotation, Vector2.ZERO)
	return {"center": transform * (position + _cards[id].size * .5), "rotation": basis.get_rotation(), "scale": basis.get_scale(), "size": _cards[id].size}

func is_in_transit(id: String) -> bool: return _transit.has(id)
func is_card_pickable(id: String) -> bool: return _pickable(id)

func borrow_card(id: String, overlay: Control) -> Control:
	var card: CardView = _cards[id]
	var pose := preload("res://scripts/ui/card_motion_pose.gd").read(card)
	begin_transit(id)
	card.reparent(overlay, false)
	card.set_as_top_level(false)
	if _parked_drag == id: _parked_drag = ""
	preload("res://scripts/ui/card_motion_pose.gd").apply(card, pose.center, pose.rotation, pose.scale)
	card.process_mode = Node.PROCESS_MODE_PAUSABLE
	card.show()
	return card

func return_card(id: String, card: Control, reveal_card: bool) -> void:
	if _cards.get(id) != card or not _slots.has(id): return
	card.set_as_top_level(false)
	card.reparent(_slots[id], false)
	card.position = Vector2.ZERO; card.rotation = 0; card.scale = Vector2.ONE
	card.z_index = 0
	card.process_mode = Node.PROCESS_MODE_INHERIT
	var destination := destination_pose(id)
	if reveal_card and not destination.is_empty(): preload("res://scripts/ui/card_motion_pose.gd").write(card, destination)
	if reveal_card and _reorder_settling and not _presenting and card.position.length() <= 1.0:
		var index := order.find(id)
		if index >= 0 and index < _layout.get("rotations", []).size():
			card.position = Vector2.ZERO; card.rotation = _layout.rotations[index]; card.scale = Vector2.ONE
	finish_transit(id, reveal_card)
	if is_inside_tree(): wire_focus()

func prepare_use(id: String, lift := true) -> void:
	if not _cards.has(id): return
	var dragged := _park_drag(id)
	if _emphasis.has(id) and _emphasis[id].tween != null and _emphasis[id].tween.is_valid(): _emphasis[id].tween.kill()
	var card: CardView = _cards[id]
	_transit[id] = true
	card.modulate.a = .94
	if not lift or dragged: return
	var policy = visual_context.motion if visual_context.motion != null else visual_context.capture_motion()
	if policy.duration("prepare", true) <= 0: return
	var tween := create_tween().set_parallel(); _preparations[id] = tween
	tween.tween_property(card, "position", Vector2(0, -visual_style.lift - 14), policy.duration("prepare", true)).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	tween.tween_property(card, "rotation", 0.0, policy.duration("prepare", true))

func reject_use(id: String) -> void:
	if _preparations.has(id) and _preparations[id].is_valid(): _preparations[id].kill()
	if _parked_drag == id and _cards.has(id):
		_preparations.erase(id); _parked_drag = ""
		_ghost = _cards[id]; _ghost_id = id; _ghost.modulate.a = 1.0
		_return_ghost()
		return
	_preparations.erase(id); _transit.erase(id); _emphasis.erase(id)
	if _cards.has(id): _cards[id].modulate.a = 1.0
	_apply_emphasis()

func _park_drag(id: String) -> bool:
	if id != _ghost_id or not is_instance_valid(_ghost): return false
	if _return_tween != null and _return_tween.is_valid(): _return_tween.kill()
	_return_tween = null
	var card := _ghost
	var pose := preload("res://scripts/ui/card_motion_pose.gd").read(card)
	_ghost = null; _ghost_id = ""; _drag_pose.clear()
	# Stay outside ScrollContainer clipping while the REST command is pending.
	# The registry will borrow this exact pose only after acceptance.
	_parked_drag = id
	preload("res://scripts/ui/card_motion_pose.gd").write(card, pose)
	interaction.cancel_pointer()
	drag_ended.emit()
	return true

func set_tools_below_cards(value: bool) -> void:
	# Combat uses a floating bottom hand; labs and overview retain their toolbar.
	move_child(toolbar, get_child_count() - 1 if value else 0)
func disable_overview() -> void:
	overview_enabled = false
	var button := toolbar.get_node_or_null("HandOverviewButton")
	if button: toolbar.remove_child(button); button.queue_free()
func set_overview_button_visible(visible_state: bool) -> void:
	var button := toolbar.get_node_or_null("HandOverviewButton")
	if button: button.visible = visible_state
func update_counter() -> void:
	_update_counter()
func relayout_now() -> void:
	_relayout()
func begin_drag_at(id: String, start_center: Vector2, point: Vector2) -> void:
	interaction.pressed = id
	interaction.press_position = start_center
	interaction.dragging = true
	_drag_visual(point)
func drag_to(point: Vector2) -> void:
	_drag_visual(point)
func return_drag_ghost() -> void:
	_return_ghost()
func check_reorder_settled() -> void:
	_check_reorder_settled()

func sync_models(models: Array, context: String, empty_message := "Your hand is empty.") -> void:
	_syncing = true
	var focus_id: String = interaction.focused
	var anchor: String = focus_id if not focus_id.is_empty() else interaction.selected
	var old_index := order.find(anchor)
	var old_order := order.duplicate()
	if context != scope or not _arrangement_bound:
		cancel_gesture()
		for id in order: _remove(id)
		order.clear()
		interaction.clear()
		_hover_point = Vector2.INF
		_inspection_anchor = ""
		scroll.scroll_horizontal = 0
		scope = context
		arrangement = visual_context.hand_arrangements.state_for(scope)
		_arrangement_bound = true
		anchor = ""
		focus_id = ""
	var reconciled := CardReconciler.reconcile_cards(
		models, _cards, _slots, _transit, _entries, surface, visual_context, duration(), is_inside_tree(),
		func(id: String, card: CardView):
			if read_only: card.set_pressed_no_signal(id == interaction.selected)
			else: card_activated.emit(id),
		func(id: String): _focus(id),
		func(id: String):
			if interaction.focused == id: interaction.focused = ""
			_apply_emphasis())
	var incoming: Array[String] = reconciled.incoming
	var geometry_changed: bool = bool(reconciled.geometry_changed)
	if not _presenting:
		var was_dragging: bool = interaction.dragging
		if arrangement.sync(incoming) and was_dragging: cancel_gesture()
	for id in _cards.keys():
		if id not in incoming: _remove(id)
	order = incoming if _presenting else arrangement.confirmed.duplicate()
	if interaction.selected not in order: interaction.selected = ""
	if interaction.hovered not in order: interaction.hovered = ""
	if interaction.pressed not in order: interaction.cancel_pointer(); _clear_ghost()
	if not order.is_empty() and is_instance_valid(empty): surface.remove_child(empty); empty.queue_free(); empty = null
	if order.is_empty():
		if not is_instance_valid(empty): empty = empty_state(empty_message, visual_context); surface.add_child(empty)
		else: empty.find_child("EmptyHandMessage", true, false).text = empty_message
	if old_order != order or geometry_changed or _layout.is_empty(): request_layout()
	if not anchor.is_empty() and anchor in order: reveal.call_deferred(anchor)
	elif not focus_id.is_empty() and not order.is_empty(): _cards[order[clampi(old_index, 0, order.size() - 1)]].grab_focus()
	_syncing = false
	_apply_emphasis()
	_update_counter()
	if not hit_surface._tooltip_id.is_empty(): hit_surface.follow_card(hit_surface._tooltip_id)

func _remove(id: String) -> void:
	if _parked_drag == id: _parked_drag = ""
	if _cards.has(id) and _slots.has(id) and _cards[id].get_parent() != _slots[id]:
		_cards[id].hide(); _cards[id].queue_free()
	if _entries.has(id) and _entries[id].is_valid(): _entries[id].kill()
	_entries.erase(id); _transit.erase(id)
	if _preparations.has(id) and _preparations[id].is_valid(): _preparations[id].kill()
	_preparations.erase(id)
	if id == _inspection_anchor: _inspection_anchor = ""
	if _motions.has(id) and _motions[id].is_valid(): _motions[id].kill()
	if _emphasis.has(id) and _emphasis[id].tween != null and _emphasis[id].tween.is_valid(): _emphasis[id].tween.kill()
	if _slots.has(id): surface.remove_child(_slots[id]); _slots[id].queue_free()
	_cards.erase(id)
	_slots.erase(id)
	_motions.erase(id)
	_emphasis.erase(id)

func set_selection(id: String) -> void:
	var previous: String = interaction.selected
	interaction.selected = id if id in order else ""
	if not interaction.selected.is_empty(): hit_surface.follow_card(interaction.selected)
	if interaction.pointer_active and not previous.is_empty() and interaction.selected.is_empty():
		# Click focus draws its own outline, independently of chosen/button_pressed.
		# Keep the current hover, but park navigation away from every card face.
		for card in _cards.values():
			if card.has_focus(): card.release_focus()
		var neutral: Control = toolbar.get_node_or_null("HandInspectButton")
		if is_inside_tree() and not get_tree().paused and is_instance_valid(neutral) and neutral.is_visible_in_tree():
			neutral.grab_focus()
	for key in order: _cards[key].select_card(key == interaction.selected)
	_apply_emphasis()

func clear_choice() -> void:
	# An accepted play ends this choice, even if the engine retains the instance.
	# Release card focus before reconciliation can transfer it to a neighboring card.
	cancel_gesture()
	interaction.clear()
	_hover_point = Vector2.INF
	_inspection_anchor = ""
	for card in _cards.values():
		if card.has_focus(): card.release_focus()
		card.select_card(false)
	if is_inside_tree() and not get_tree().paused:
		var neutral: Control = toolbar.get_node_or_null("HandInspectButton")
		if is_instance_valid(neutral) and neutral.is_visible_in_tree(): neutral.grab_focus()
	_apply_emphasis()
func set_read_only(value: bool) -> void:
	read_only = value
	if value: cancel_gesture(); hit_surface.follow_card("")
func cancel_gesture() -> void:
	interaction.cancel_pointer()
	_cancel_reorder_preview()
	_clear_ghost()
	if is_instance_valid(_reading_proxy): _reading_proxy.hide()
func duration() -> float:
	var policy = visual_context.motion if visual_context.motion != null else visual_context.capture_motion()
	return minf(policy.duration("hand_transition", true), _arrival_transition_limit) if _aligning_arrivals else policy.duration("hand_transition", true)

func request_layout() -> void:
	if _layout_pending: return
	_layout_pending = true
	_relayout.call_deferred()
func _visual_style_key() -> String:
	# Editor hot reload may retain a placeholder from before the resource was @tool.
	# Exported properties remain readable; avoid invoking a method on that instance.
	var values := {}
	for property in visual_style.get_property_list():
		if int(property.usage) & PROPERTY_USAGE_SCRIPT_VARIABLE:
			values[str(property.name)] = visual_style.get(property.name)
	return JSON.stringify(values)

func _relayout() -> void:
	_layout_pending = false
	if not is_inside_tree(): return
	relayout_count += 1
	var default_style = visual_context.card_style
	var dimensions: Vector2 = default_style.effective_size(visual_context.text_scale)
	var policy_key: String = default_style.presentation_key() + _visual_style_key() + resolved_hand_mode()
	if not is_equal_approx(_layout_text_scale, visual_context.text_scale) or policy_key != _layout_card_style_key:
		_reserved_card_size = Vector2.ZERO; custom_minimum_size.y = 0
	_layout_card_style_key = policy_key
	_layout_text_scale = visual_context.text_scale
	for card in _cards.values(): dimensions = dimensions.max(card.get_combined_minimum_size())
	dimensions = dimensions.max(_reserved_card_size)
	_reserved_card_size = dimensions
	_layout = Layout.presentation_layout(order, _presentation_order, _destination_order, _presenting, _aligning_arrivals, maxf(1, scroll.size.x), dimensions, visual_style, resolved_hand_mode())
	scroll.custom_minimum_size.y = _layout.height + 12
	surface.custom_minimum_size = Vector2(_layout.extent, _layout.height)
	# Reserve the scroll chrome even when the native bar toggles during layout.
	# Read-only panels/focus changes must not move the dock by the bar's height.
	var reserved: float = ceilf(_layout.height + 12 + scroll.get_h_scroll_bar().get_combined_minimum_size().y)
	if not toolbar_external: reserved += toolbar.get_combined_minimum_size().y + get_theme_constant("separation")
	custom_minimum_size.y = reserved
	for i in order.size():
		var id := order[i]
		var card: CardView = _cards[id]
		var slot: Control = _slots[id]
		card.size = dimensions
		card.pivot_offset = dimensions * .5
		slot.size = dimensions
		var target: Vector2 = _layout.positions[i]
		# Every new layout owns the slot, even if it already sits at the target.
		# A previous tween may not have ticked yet; leaving it alive would move
		# a centered small hand back to the obsolete, larger hand's positions.
		if _motions.has(id):
			if _motions[id].is_valid(): _motions[id].kill()
			_motions.erase(id)
		if slot.has_meta("placed") and slot.position != target and duration() > 0 and not _transit.has(id):
			_motions[id] = create_tween()
			Retarget.append(_motions[id], slot, "position", target, duration())
		else: slot.position = target; Retarget.reset(slot)
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
	var policy = visual_context.motion if visual_context.motion != null else visual_context.capture_motion()
	var hover_seconds: float = policy.duration("hover", true)
	var active: String = interaction.active_id()
	for i in order.size():
		var id := order[i]
		if not _cards.has(id): continue
		if _transit.has(id): continue
		if i >= _layout.rotations.size(): continue
		var raised: bool = id == active and not interaction.dragging
		var target := Vector2(0, -visual_style.lift * visual_context.hand_emphasis) if raised else Vector2.ZERO
		var angle: float = 0 if raised else _layout.rotations[i]
		var card: CardView = _cards[id]
		_slots[id].z_index = 100 if raised else (50 if id == interaction.selected else mini(i, 40))
		if _emphasis.has(id) and _emphasis[id].position == target and is_equal_approx(_emphasis[id].rotation, angle): continue
		if _emphasis.has(id) and _emphasis[id].tween != null and _emphasis[id].tween.is_valid(): _emphasis[id].tween.kill()
		var tween: Tween = null
		if hover_seconds > 0 and is_inside_tree() and (not card.position.is_equal_approx(target) or not is_equal_approx(card.rotation, angle)):
			tween = create_tween().set_parallel()
			# Start at the displayed pose, including an interrupted hover. Hover
			# reverses immediately rather than carrying the layout's velocity.
			Retarget.reset(card)
			tween.tween_property(card, "position", target, hover_seconds).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
			tween.tween_property(card, "rotation", angle, hover_seconds).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
		else: card.position = target; card.rotation = angle; Retarget.reset(card)
		_emphasis[id] = {"position": target, "rotation": angle, "tween": tween}
	_update_reading_proxy()

func _update_reading_proxy() -> void:
	if not is_inside_tree(): return
	# A partially clipped hover stays readable without scrolling under the pointer.
	# This copy is visual only: the stable hit surface still owns all input.
	var id: String = interaction.active_id()
	var card: CardView = _cards.get(id)
	if read_only or reading_modal or interaction.dragging or _transit.has(id) or not is_instance_valid(card):
		if is_instance_valid(_reading_proxy): _reading_proxy.hide()
		return
	var clip := scroll.get_global_rect()
	var resting: Vector2 = _slots[id].global_position
	if resting.x >= clip.position.x and resting.x + card.size.x <= clip.end.x:
		if is_instance_valid(_reading_proxy): _reading_proxy.hide()
		return
	if not is_instance_valid(_reading_proxy):
		_reading_proxy = CardView.create(visual_context)
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
		card.global_position.y)
	_reading_proxy.pivot_offset = card.pivot_offset
	_reading_proxy.rotation = card.rotation
	_reading_proxy.show()

func pick(local_point: Vector2, stable := true) -> String:
	return pick_global(get_global_transform() * local_point, stable)

func _pickable(id: String) -> bool:
	return PointerCoordinator.is_pickable(id, _cards, _slots, _transit)

func _contains_card(id: String, point: Vector2, padding := 0.0) -> bool:
	return PointerCoordinator.contains_card(_cards[id], point, padding)

func _draw_rank(id: String) -> int:
	return PointerCoordinator.draw_rank(id, _cards, _slots)
func _above(a: String, b: String) -> bool:
	return PointerCoordinator.is_above(a, b, _cards, _slots)

func pick_global(point: Vector2, stable := true) -> String:
	return PointerCoordinator.pick_global(
		point, stable, order, _cards, _slots, _transit, _layout, reading_modal,
		hit_surface, _reading_proxy, interaction.hovered, _hover_point, visual_style.pointer_hysteresis)
func _focus(id: String) -> void:
	interaction.focused = id
	hit_surface.follow_card(id)
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
	counter.text = (visual_context.translator.text("%s cards") % order.size()) + ("  ↔" if overflow else "")
	previous.disabled = not overflow or scroll.scroll_horizontal <= 0
	next.disabled = not overflow or scroll.scroll_horizontal >= scroll.get_h_scroll_bar().max_value - scroll.get_h_scroll_bar().page - 1
	if toolbar_external:
		previous.visible = overflow; next.visible = overflow
		preload("res://scripts/ui/combat_chrome.gd").apply(previous, "left", visual_context.translator.text("Previous cards"), visual_context, false, true)
		preload("res://scripts/ui/combat_chrome.gd").apply(next, "arrow", visual_context.translator.text("Next cards"), visual_context, false, true)
	counter.tooltip_text = visual_context.translator.text("More cards — scroll to browse.") if overflow else ""
	var inspect: Button = toolbar.get_node_or_null("HandInspectButton")
	var overview: Button = toolbar.get_node_or_null("HandOverviewButton")
	if is_instance_valid(inspect):
		if toolbar_external: preload("res://scripts/ui/combat_chrome.gd").apply(inspect, "inspect", "%s [%s]" % [visual_context.translator.text("Inspect card"), visual_context.action_label("inspect_card")], visual_context, false, true)
		else: inspect.text = "%s [%s]" % [visual_context.translator.text("Inspect card"), visual_context.action_label("inspect_card")]
	if is_instance_valid(overview): overview.text = visual_context.translator.text("Hand overview")
	_update_reading_proxy()

func pointer_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		if interaction.dragging:
			if not read_only: _drag_visual(event.global_position)
			return
		var pointer_resumed: bool = not interaction.pointer_active
		interaction.pointer_active = true
		var point: Vector2 = hit_surface.get_global_transform() * event.position
		var id := pick_global(point)
		_hover_point = point
		var entered: bool = id != interaction.hovered
		if entered or pointer_resumed:
			interaction.hovered = id
			hit_surface.follow_card(id)
			if not id.is_empty(): _inspection_anchor = id; card_previewed.emit(id)
			if not id.is_empty() and not reading_modal and not interaction.dragging:
				card_hovered.emit(id)
				if entered: card_hover_entered.emit(id)
		_apply_emphasis()
		if drag_enabled and not read_only and not interaction.pressed.is_empty():
			if event.global_position.distance_to(interaction.press_position) >= visual_style.drag_threshold:
				interaction.dragging = true
				_drag_visual(event.global_position)
	elif event is InputEventMouseButton:
		if event.button_index in [MOUSE_BUTTON_WHEEL_UP, MOUSE_BUTTON_WHEEL_DOWN] and event.pressed:
			page(-1 if event.button_index == MOUSE_BUTTON_WHEEL_UP else 1)
			return
		if event.button_index != MOUSE_BUTTON_LEFT: return
		if event.pressed and _return_tween != null and _return_tween.is_valid(): _clear_ghost()
		var id := pick_global(hit_surface.get_global_transform() * event.position)
		if event.pressed:
			if not read_only: interaction.pressed = id; interaction.press_position = event.global_position
		elif not interaction.pressed.is_empty():
			var pressed: String = interaction.pressed
			var dragging: bool = interaction.dragging
			interaction.cancel_pointer()
			if read_only or pressed not in order: _clear_ghost(); return
			if dragging:
				finish_drag(pressed, event.global_position)
			elif id == pressed:
				_cards[id].grab_focus()
				# _focus is shared with keyboard navigation; a mouse click stays pointer-driven.
				interaction.pointer_active = true
				card_activated.emit(id)
func _input(event: InputEvent) -> void:
	if interaction.pressed.is_empty(): return
	if event.is_action_pressed("ui_cancel") or (event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT):
		interaction.cancel_pointer()
		_return_ghost()
		get_viewport().set_input_as_handled()
func finish_drag(id: String, point: Vector2) -> void:
	interaction.cancel_pointer()
	if read_only or reading_modal or id not in order: cancel_gesture(); return
	if contains_reorder_point(point):
		_update_reorder_preview(point)
		var changed: bool = arrangement.commit()
		order = arrangement.confirmed.duplicate()
		_relayout()
		if changed: visual_order_changed.emit(order.duplicate())
		_return_ghost()
		return
	_cancel_reorder_preview()
	drop_requested.emit(id, point)
	_return_ghost()
func _drag_visual(point: Vector2) -> void:
	if not is_instance_valid(_ghost):
		if not _cards.has(interaction.pressed): return
		_ghost_id = interaction.pressed
		_drag_pose = preload("res://scripts/ui/card_motion_pose.gd").read(_cards[_ghost_id])
		_grip_offset = _drag_pose.center - interaction.press_position
		_last_drag_point = point; _last_drag_usec = Time.get_ticks_usec(); _drag_tilt = 0
		_ghost = borrow_card(_ghost_id, self)
		_ghost.set_as_top_level(true)
		preload("res://scripts/ui/card_motion_pose.gd").write(_ghost, _drag_pose)
		_ghost.z_index = 500
		hit_surface.follow_card("")
		_apply_emphasis()
	if _return_tween != null and _return_tween.is_valid(): _return_tween.kill()
	var now := Time.get_ticks_usec()
	var dt := maxf(.001, (now - _last_drag_usec) / 1000000.0)
	var policy = visual_context.motion if visual_context.motion != null else visual_context.capture_motion()
	_drag_tilt = 0.0 if policy.reduced or policy.instant else lerpf(_drag_tilt, clampf((point.x-_last_drag_point.x) / dt * .004, -8, 8), 1.0-exp(-24.0*dt))
	preload("res://scripts/ui/card_motion_pose.gd").apply(_ghost, point + _grip_offset,
		float(_drag_pose.rotation) + deg_to_rad(_drag_tilt), _drag_pose.scale)
	_last_drag_point = point; _last_drag_usec = now
	_update_reorder_preview(point)
	drag_previewed.emit(_ghost_id, point)
func contains_reorder_point(point: Vector2) -> bool:
	if not reorder_enabled or read_only or reading_modal or _presenting or not is_inside_tree(): return false
	if not hit_surface.is_visible_in_tree(): return false
	if toolbar.is_visible_in_tree() and toolbar.get_global_rect().has_point(point): return false
	var bar := scroll.get_h_scroll_bar()
	if bar.is_visible_in_tree() and bar.get_global_rect().has_point(point): return false
	if reorder_drop_filter.is_valid() and not reorder_drop_filter.call(point): return false
	return Rect2(Vector2.ZERO, hit_surface.size).has_point(hit_surface.get_global_transform().affine_inverse() * point)
func _update_reorder_preview(point: Vector2) -> void:
	if not contains_reorder_point(point) or not is_instance_valid(_ghost): _cancel_reorder_preview(); return
	if arrangement.dragged != _ghost_id: arrangement.begin(_ghost_id); _preview_slot = order.find(_ghost_id)
	var geometry := Layout.calculate(order.size(), maxf(1, scroll.size.x), _reserved_card_size, visual_style, resolved_hand_mode())
	var centers: Array = geometry.positions.map(func(position): return position.x + _reserved_card_size.x * .5)
	var center: Vector2 = surface.get_global_transform().affine_inverse() * preload("res://scripts/ui/card_motion_pose.gd").read(_ghost).center
	_preview_slot = Layout.insertion_index(centers, center.x, _preview_slot, visual_style.pointer_hysteresis)
	if arrangement.move(_preview_slot): order = arrangement.preview.duplicate(); request_layout()
func _cancel_reorder_preview() -> void:
	_preview_slot = -1
	if arrangement.dragged.is_empty(): return
	arrangement.cancel()
	if order != arrangement.confirmed: order = arrangement.confirmed.duplicate(); request_layout()
func _process(delta: float) -> void:
	# The clipped-card reading copy follows the animated pose, never the final
	# hover endpoint (which made clipped cards appear to teleport).
	if is_instance_valid(_reading_proxy) and _reading_proxy.visible:
		var source: CardView = _cards.get(interaction.active_id())
		if is_instance_valid(source):
			_reading_proxy.global_position.y = source.global_position.y
			_reading_proxy.rotation = source.rotation
	_check_reorder_settled()
	if not interaction.dragging or not is_instance_valid(_ghost): return
	_auto_scroll_reorder(delta)
	if Time.get_ticks_usec() - _last_drag_usec > 35000:
		_drag_tilt *= exp(-12.0 * delta)
		_ghost.rotation = float(_drag_pose.rotation) + deg_to_rad(_drag_tilt)
func _check_reorder_settled() -> void:
	if not _reorder_settling or _layout_pending or is_instance_valid(_ghost): return
	if _motions.values().any(func(tween): return tween != null and tween.is_valid() and tween.is_running()): return
	if _emphasis.values().any(func(state): return state.tween != null and state.tween.is_valid() and state.tween.is_running()): return
	_reorder_settling = false
	reorder_settled.emit()
func _auto_scroll_reorder(delta: float) -> void:
	if not contains_reorder_point(_last_drag_point): _reorder_scroll_fraction = 0; return
	var bar := scroll.get_h_scroll_bar()
	var maximum := maxf(0, bar.max_value - bar.page)
	if maximum <= 0: _reorder_scroll_fraction = 0; return
	var direction := PointerCoordinator.auto_scroll_direction(hit_surface, _last_drag_point, visual_style.reorder_edge_band)
	_reorder_scroll_fraction += direction * maxf(0, visual_style.reorder_scroll_speed) * maxf(0, delta)
	var step := int(_reorder_scroll_fraction)
	_reorder_scroll_fraction -= step
	if step == 0: return
	var before := scroll.scroll_horizontal
	scroll.scroll_horizontal = clampi(before + step, 0, int(maximum))
	if scroll.scroll_horizontal == before: _reorder_scroll_fraction = 0
	else: _update_reorder_preview(_last_drag_point)
func _return_ghost() -> void:
	_cancel_reorder_preview()
	if not is_instance_valid(_ghost): return
	_reorder_settling = true
	var id := _ghost_id
	var start := preload("res://scripts/ui/card_motion_pose.gd").read(_ghost)
	var destination := destination_pose(id)
	if not destination.is_empty() and start.center.distance_to(destination.center) < .1 and absf(angle_difference(start.rotation, destination.rotation)) < .001 and start.scale.distance_to(destination.scale) < .001:
		_clear_ghost()
		return
	var policy = visual_context.motion if visual_context.motion != null else visual_context.capture_motion()
	var seconds: float = policy.duration("hand_transition", true)
	if seconds <= 0 or not _cards.has(id): _clear_ghost(); return
	_return_tween = create_tween()
	_return_tween.tween_method(func(raw: float):
		var target := destination_pose(id)
		if is_instance_valid(_ghost) and not target.is_empty():
			preload("res://scripts/ui/card_motion_pose.gd").write(_ghost,
				preload("res://scripts/ui/card_motion_pose.gd").blend(start, target, policy.progress("drag_return", raw))), 0.0, 1.0, seconds)
	_return_tween.finished.connect(_clear_ghost, CONNECT_ONE_SHOT)
func _clear_ghost() -> void:
	_cancel_reorder_preview()
	_reorder_scroll_fraction = 0
	if _return_tween != null and _return_tween.is_valid(): _return_tween.kill()
	_return_tween = null
	var ghost := _ghost; var id := _ghost_id
	_ghost = null; _ghost_id = ""; _drag_pose.clear()
	if is_instance_valid(ghost) and _cards.get(id) == ghost and _slots.has(id): return_card(id, ghost, true)
	if not id.is_empty(): drag_ended.emit()
	_check_reorder_settled()
func show_overview() -> void:
	if reading_modal or not overview_enabled: return
	var dialog = load("res://scripts/ui/hand_overview_dialog.gd").new(visual_context)
	dialog.setup(order.map(func(id): return _cards[id].model.duplicate(true)), scope, interaction.selected)
	var return_focus := {"id": active_card_id()}
	dialog.card_chosen.connect(func(id): return_focus.id = id; reveal(id); selection_requested.emit(id))
	dialog.tree_exiting.connect(func():
		if str(return_focus.id) in order: _cards[str(return_focus.id)].grab_focus())
	add_child(dialog)
	track_reading_modal(dialog)
	dialog.popup_centered()
func track_reading_modal(dialog: Window) -> void:
	var id := dialog.get_instance_id()
	if _reading_windows.has(id): return
	var reference: WeakRef = weakref(dialog)
	_reading_windows[id] = reference
	reading_modal = true
	cancel_gesture()
	dialog.visibility_changed.connect(func():
		var window = reference.get_ref()
		if window != null and window.visible: _reading_windows[id] = reference
		else: _reading_windows.erase(id)
		_refresh_reading_modals())
	dialog.tree_exiting.connect(func(): _reading_windows.erase(id); _refresh_reading_modals(), CONNECT_ONE_SHOT)
func _refresh_reading_modals() -> void:
	for id in _reading_windows.keys():
		if _reading_windows[id].get_ref() == null: _reading_windows.erase(id)
	reading_modal = not _reading_windows.is_empty()
	_apply_emphasis()
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
		var direction := "focus_neighbor_top" if toolbar_external or toolbar.get_index() > scroll.get_index() else "focus_neighbor_bottom"
		for tool in [previous, next, toolbar.get_node_or_null("HandInspectButton"), toolbar.get_node_or_null("HandOverviewButton")]:
			if is_instance_valid(tool): tool.set(direction, tool.get_path_to(destination))
static func empty_state(message: String, context: ContextType = null) -> Control:
	if context == null: context = ContextType.new()
	var state: Control = preload("res://scenes/ui/components/empty_hand.tscn").instantiate()
	state.theme = context.theme; state.get_node("EmptyHandMessage").text = message
	return state

func dispose() -> void:
	if visual_context.changed.is_connected(_preferences_changed): visual_context.changed.disconnect(_preferences_changed)
	cancel_gesture()
	for tween in _motions.values():
		if tween: tween.kill()
	_motions.clear()
	for tween in _entries.values():
		if tween != null and tween.is_valid(): tween.kill()
	_entries.clear(); _transit.clear()
	for tween in _preparations.values():
		if tween != null and tween.is_valid(): tween.kill()
	_preparations.clear()
	for item in _emphasis.values():
		if item.tween != null and item.tween.is_valid(): item.tween.kill()
	_emphasis.clear()

func _exit_tree() -> void: dispose()

func _preview_if_isolated() -> void:
	if order.is_empty() and (Engine.is_editor_hint() or get_tree().current_scene == self): show_editor_preview()
func show_editor_preview() -> void:
	var data = JSON.parse_string(FileAccess.get_file_as_string(preview_document))
	if not data is Dictionary: return
	sync_models(data.get("cards", []), "editor-preview", "No cards")
