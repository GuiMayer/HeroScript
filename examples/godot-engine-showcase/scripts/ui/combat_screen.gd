extends VBoxContainer

const Presenter = preload("res://scripts/presentation/combat_presenter.gd")
const CardZonePresenter = preload("res://scripts/presentation/card_zone_presenter.gd")
const HandView = preload("res://scripts/ui/card_hand_view.gd")
var presenter
var zone_presenter
var router
var presentation: Dictionary
var selected_target := ""
var selected_card := ""
var hand
var hand_section: VBoxContainer
var _view_key := ""
var detail_label: RichTextLabel
var queue_label: Label
var next_frame_button: Button
var actor_portraits: Dictionary = {}
var target_buttons: Dictionary = {}
var card_buttons: Dictionary = {}
var action_buttons: Array[Button] = []
var choices: VBoxContainer
var hint_label: Label
var selection_label: Label
var frame_delay := 0.0
var submitting := false
var reconnect_button: Button
var selection_actions: HBoxContainer
var animate_card_entry := true
var render_epoch := 0
var gameplay_column: Control
var bottom_hud: VBoxContainer
var battlefield_actors: ScrollContainer
var floating_hud
var candidate_panel: PanelContainer
var _gameplay_layout_pending := false
var character_sidebar: PanelContainer
var character_sidebar_content: ScrollContainer
var character_sidebar_title: Label
var character_sidebar_reopen: Button
var character_sidebar_expanded := false

func _ready() -> void:
	visibility_changed.connect(func():
		if is_instance_valid(floating_hud): floating_hud.visible = is_visible_in_tree())
	if is_instance_valid(floating_hud): floating_hud.visible = is_visible_in_tree()
	_queue_gameplay_layout()

func setup(owner, data: Dictionary) -> void:
	router = owner
	if not Playback.frame_presented.is_connected(_on_frame):
		Playback.frame_presented.connect(_on_frame)
	_render(data, true)

func refresh_state(data: Dictionary) -> void:
	_render(data, false)

func _render(data: Dictionary, play_entry_animation: bool) -> void:
	var key := JSON.stringify([GameSession.run, GameSession.combat, GameSession.legal_actions, GameSession.card_zones, data, I18n.locale, Preferences.text_scale, Preferences.high_contrast])
	if key == _view_key and is_instance_valid(hand):
		submitting = false
		_update_controls()
		return
	_view_key = key
	var preserve := is_instance_valid(hand) and is_instance_valid(hand_section)
	if preserve: hand.cancel_gesture()
	if not preserve or hand.scope != _hand_scope(): selected_card = ""; selected_target = ""
	render_epoch += 1
	presentation = data
	animate_card_entry = play_entry_animation
	submitting = false
	frame_delay = 0.0
	actor_portraits.clear()
	target_buttons.clear()
	action_buttons.clear()
	zone_presenter = CardZonePresenter.new(GameSession.card_zones, I18n)
	presenter = Presenter.new(GameSession.run, GameSession.combat, GameSession.legal_actions,
		I18n, presentation, GameSession.card_zones)
	if preserve:
		_refresh_structure()
		return
	card_buttons.clear()
	for child in get_children(): remove_child(child); child.queue_free()
	add_theme_constant_override("separation", 8)
	_build_header()
	var body := HBoxContainer.new()
	body.name = "CombatBody"
	body.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_theme_constant_override("separation", 10)
	gameplay_column = Control.new()
	gameplay_column.name = "CombatGameplay"
	gameplay_column.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	gameplay_column.size_flags_vertical = Control.SIZE_EXPAND_FILL
	gameplay_column.mouse_filter = Control.MOUSE_FILTER_IGNORE
	gameplay_column.resized.connect(_queue_gameplay_layout)
	body.add_child(gameplay_column)
	_ensure_bottom_hud()
	_build_battlefield()
	_build_hand()
	_build_footer()
	add_child(body)
	_ensure_floating_hud()
	_build_character_sidebar()
	# Apply font and contrast preferences before this snapshot can be drawn. The
	# deferred focus pass must never be responsible for a visible relayout.
	AppTheme.apply_view_preferences(self)
	_update_controls()
	_queue_gameplay_layout()
	_load_inspection.call_deferred(render_epoch)

func _hand_scope() -> String:
	return JSON.stringify([GameSession.run.get("runId", ""), GameSession.run.get("branchId", ""),
		GameSession.combat.get("combatId", ""), GameSession.input_actor_id(),
		GameSession.card_zones.get("zones", []).filter(func(zone): return zone.get("allowsCardPlay", false)).map(func(zone): return zone.get("zoneId", ""))])

func _refresh_structure() -> void:
	# Keep the actual hand in the tree: removing/reparenting it loses focus too.
	var body := gameplay_column.get_parent()
	for child in get_children():
		if child != body and child != floating_hud: remove_child(child); child.queue_free()
	for child in floating_hud.host.get_children():
		if child != character_sidebar_reopen:
			floating_hud.host.remove_child(child); child.queue_free()
	for child in gameplay_column.get_children():
		if child != bottom_hud and child != character_sidebar_reopen:
			gameplay_column.remove_child(child); child.queue_free()
	for child in bottom_hud.get_children():
		if child != hand_section: bottom_hud.remove_child(child); child.queue_free()
	for child in body.get_children():
		if child != gameplay_column: body.remove_child(child); child.queue_free()
	_build_header()
	move_child(get_child(get_child_count() - 1), 0)
	_build_battlefield()
	_build_hand()
	_build_footer()
	_build_character_sidebar()
	AppTheme.apply_view_preferences(self)
	_update_controls()
	_queue_gameplay_layout()
	_load_inspection.call_deferred(render_epoch)

func _ensure_bottom_hud() -> void:
	if is_instance_valid(bottom_hud): return
	bottom_hud = VBoxContainer.new()
	bottom_hud.name = "CombatBottomHUD"
	bottom_hud.mouse_filter = Control.MOUSE_FILTER_IGNORE
	bottom_hud.add_theme_constant_override("separation", 4)
	gameplay_column.add_child(bottom_hud)
	bottom_hud.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_WIDE)
	bottom_hud.minimum_size_changed.connect(_queue_gameplay_layout)

func _ensure_floating_hud() -> void:
	if is_instance_valid(floating_hud): return
	floating_hud = preload("res://scripts/ui/floating_hud.gd").new()
	add_child(floating_hud)
	floating_hud.visible = is_visible_in_tree()

func _queue_gameplay_layout() -> void:
	if _gameplay_layout_pending: return
	_gameplay_layout_pending = true
	_layout_gameplay.call_deferred()

func _layout_gameplay() -> void:
	_gameplay_layout_pending = false
	if not is_inside_tree() or not is_instance_valid(bottom_hud): return
	var height := bottom_hud.get_combined_minimum_size().y
	bottom_hud.offset_top = -height
	if is_instance_valid(floating_hud):
		floating_hud.fit_to(gameplay_column.get_global_rect(), router.theme)
		if is_instance_valid(character_sidebar):
			character_sidebar.offset_bottom = minf(520, gameplay_column.size.y - 84)
		if is_instance_valid(candidate_panel):
			var dock_height: float = hand.toolbar.get_combined_minimum_size().y + bottom_hud.get_node("TurnControls").get_combined_minimum_size().y + 10
			candidate_panel.offset_bottom = -dock_height
			candidate_panel.offset_top = -dock_height - 144
	# The scenery extends behind the floating hand, but actor information stays
	# above even the raised cards. No rules or card count limits affect this layout.
	if is_instance_valid(battlefield_actors):
		battlefield_actors.offset_bottom = -height
		var available := battlefield_actors.size.y
		if battlefield_actors.get_h_scroll_bar().visible: available -= battlefield_actors.get_h_scroll_bar().size.y
		for actor in battlefield_actors.find_children("ActorView_*", "PanelContainer", true, false):
			actor.fit_battlefield_height(available)

func _exit_tree() -> void:
	if Playback.frame_presented.is_connected(_on_frame):
		Playback.frame_presented.disconnect(_on_frame)

func _build_header() -> void:
	var row := HBoxContainer.new()
	var activation: Dictionary = GameSession.combat.get("activation", {}) if GameSession.combat.get("activation", {}) is Dictionary else {}
	var title := AppTheme.title(I18n.text("COMBAT"), 24)
	title.tooltip_text = str(GameSession.combat.get("runNodeId", "")) + " · " + str(GameSession.combat.get("stateHash", ""))
	row.add_child(title)
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(push)
	var turn := AppTheme.title(I18n.text("ROUND %s") % int(activation.get("round", GameSession.combat.get("currentTurn", 1))), 18, AppTheme.GOLD)
	row.add_child(turn)
	row.add_child(_button(I18n.text("TIMELINE  [%s]") % Preferences.action_label("open_timeline"), router.show_timeline, 165))
	if not CardZonePresenter.tool_flows(GameSession.run).is_empty():
		var tools := _button(I18n.text("ZONE TOOLS"), _open_zone_tools, 125)
		action_buttons.append(tools)
		row.add_child(tools)
	row.add_child(_button(I18n.text("PAUSE"), router.toggle_pause, 100))
	add_child(row)

func _build_battlefield() -> void:
	var stage := Control.new()
	stage.name = "Battlefield"
	stage.mouse_filter = Control.MOUSE_FILTER_IGNORE
	stage.clip_contents = true
	var backdrop := preload("res://scripts/ui/art_slot.gd").new()
	backdrop.setup("backgrounds", "combat")
	stage.add_child(backdrop)
	backdrop.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var scroller := ScrollContainer.new()
	scroller.name = "BattlefieldActors"
	battlefield_actors = scroller
	scroller.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	scroller.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	var field := HBoxContainer.new()
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	field.size_flags_vertical = Control.SIZE_EXPAND_FILL
	field.alignment = BoxContainer.ALIGNMENT_CENTER
	field.add_theme_constant_override("separation", 22)
	var allies := HBoxContainer.new()
	var opponents := HBoxContainer.new()
	allies.alignment = BoxContainer.ALIGNMENT_CENTER
	opponents.alignment = BoxContainer.ALIGNMENT_CENTER
	for side in [allies, opponents]:
		side.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		side.size_flags_vertical = Control.SIZE_EXPAND_FILL
		side.add_theme_constant_override("separation", 18)
	for actor in presenter.actors():
		var hostile: bool = presenter.relationship(actor, GameSession.input_actor_id()) == "Enemy"
		(opponents if hostile else allies).add_child(_actor_card(actor, hostile))
	field.add_child(allies)
	var divider := CenterContainer.new()
	divider.custom_minimum_size.x = 88
	divider.add_child(AppTheme.title("◇", 26, AppTheme.GOLD))
	field.add_child(divider)
	field.add_child(opponents)
	scroller.add_child(field)
	stage.add_child(scroller)
	scroller.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	gameplay_column.add_child(stage)
	stage.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	gameplay_column.move_child(stage, 0)

func _actor_card(actor: Dictionary, hostile: bool) -> Control:
	var panel := preload("res://scripts/ui/actor_panel.gd").new()
	var id := str(actor.get("instanceId", ""))
	panel.name = "ActorView_" + id.validate_node_name()
	panel.setup(actor, hostile, presentation, presenter._intent_for(id), false, true)
	panel.minimum_size_changed.connect(_queue_gameplay_layout)
	panel.target_selected.connect(_select_target)
	actor_portraits[id] = panel.portrait_view
	target_buttons[id] = panel.target_button
	return panel

func _build_hand() -> void:
	var section := hand_section if is_instance_valid(hand_section) else VBoxContainer.new()
	hand_section = section
	section.name = "HandSection"
	for child in section.get_children():
		if child != hand: section.remove_child(child); child.queue_free()
	if is_instance_valid(hand):
		var prior_heading: Node = hand.toolbar.get_node_or_null("HandHeading")
		if is_instance_valid(prior_heading): hand.toolbar.remove_child(prior_heading); prior_heading.queue_free()
	section.add_theme_constant_override("separation", 4)
	var heading := HBoxContainer.new()
	heading.name = "HandHeading"
	heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	heading.add_theme_constant_override("separation", 10)
	hint_label = AppTheme.caption(I18n.text("Select a card, then choose a highlighted target."), 13, 260)
	hint_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	# The footer already presents this hint; keep the same data hook without
	# reserving a second line above the cards.
	hint_label.hide()
	section.add_child(hint_label)
	var zones_scroll := ScrollContainer.new()
	zones_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	zones_scroll.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	zones_scroll.custom_minimum_size = Vector2(360, 40)
	zones_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var zones := HBoxContainer.new()
	zones.add_theme_constant_override("separation", 6)
	for zone in zone_presenter.auxiliary_zones():
		var zone_id := str(zone.get("zoneId", ""))
		var label: String = zone_presenter.zone_label(zone)
		var pile := _button("%s · %s" % [label, int(zone.get("count", 0))],
			func(): _inspect_pile(label, zone_id))
		pile.add_theme_font_size_override("font_size", 13)
		pile.custom_minimum_size = Vector2(136, 34)
		pile.clip_text = true
		pile.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		pile.tooltip_text = "%s · %s" % [label, int(zone.get("count", 0))]
		zones.add_child(pile)
	zones_scroll.add_child(zones)
	heading.add_child(zones_scroll)
	if not is_instance_valid(hand):
		hand = HandView.new()
		hand.card_activated.connect(_choose_card)
		hand.card_previewed.connect(_show_preview)
		hand.inspection_requested.connect(_inspect_instance)
		hand.selection_requested.connect(func(id):
			if _locked() or id not in card_buttons: return
			selected_card = id; selected_target = ""
			_clear_choices(); _show_preview(id); _update_controls())
		hand.drop_requested.connect(_drop_card)
		section.add_child(hand)
	hand.toolbar.add_child(heading)
	hand.toolbar.move_child(heading, 0)
	hand.set_tools_below_cards(true)
	hand.counter.size_flags_horizontal = Control.SIZE_FILL
	var models: Array = []
	for card in zone_presenter.playable_cards():
		models.append(presenter.card_view_model(str(card.get("cardInstanceId", ""))))
	hand.sync_models(models, _hand_scope(), I18n.text("Your hand is empty."))
	card_buttons = hand.cards()
	for id in hand.order:
		if card_buttons[id].has_meta("initial_focus"): card_buttons[id].remove_meta("initial_focus")
	if selected_card not in card_buttons: selected_card = ""; selected_target = ""
	_ensure_bottom_hud()
	if section.get_parent() == null: bottom_hud.add_child(section)

func _empty_hand_state() -> Control:
	return HandView.empty_state(I18n.text("Your hand is empty."))

func _drop_card(id: String, point: Vector2) -> void:
	if _locked(): return
	for actor_id in target_buttons:
		if target_buttons[actor_id].get_global_rect().has_point(point) and _candidates(id).any(func(candidate): return actor_id in _targets(candidate)):
			# Same canonical selection/payment/multi-target path as clicks.
			selected_card = id
			_select_target(actor_id)
			return
	var stage := gameplay_column.find_child("Battlefield", true, false)
	if is_instance_valid(stage) and stage.get_global_rect().has_point(point):
		var candidates := _candidates(id).filter(func(candidate): return _targets(candidate).is_empty())
		if not candidates.is_empty(): selected_card = id; _update_controls(); _offer_candidates(candidates)

func _build_footer() -> void:
	_ensure_floating_hud()
	detail_label = RichTextLabel.new()
	detail_label.bbcode_enabled = false
	detail_label.custom_minimum_size = Vector2(0, 42)
	detail_label.add_theme_font_size_override("normal_font_size", 14)
	detail_label.text = I18n.text("Select a card, then choose a highlighted target.")
	var row := HBoxContainer.new()
	row.name = "TurnControls"
	row.add_theme_constant_override("separation", 8)
	var feedback := VBoxContainer.new()
	feedback.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	feedback.add_theme_constant_override("separation", 0)
	detail_label.custom_minimum_size.x = 200
	detail_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	feedback.add_child(detail_label)
	row.add_child(feedback)
	choices = VBoxContainer.new()
	# Explicit choices float above the dock; their presence cannot move the hand.
	candidate_panel = PanelContainer.new()
	candidate_panel.name = "CandidatePanel"
	candidate_panel.mouse_filter = Control.MOUSE_FILTER_STOP
	candidate_panel.add_theme_stylebox_override("panel", AppTheme.box(AppTheme.PANEL, 10, AppTheme.GOLD, 1))
	floating_hud.host.add_child(candidate_panel)
	candidate_panel.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_LEFT)
	candidate_panel.offset_right = 600
	candidate_panel.hide()
	var alternatives := ScrollContainer.new()
	alternatives.name = "CandidateChoicesSlot"
	alternatives.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	choices.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	alternatives.add_child(choices)
	candidate_panel.add_child(alternatives)
	selection_label = AppTheme.caption("", 13, 80)
	feedback.add_child(selection_label)
	selection_label.hide() # Hint already appears beside the action; keep the data hook.
	queue_label = AppTheme.caption("", 13, 80)
	queue_label.add_theme_color_override("font_color", AppTheme.TEAL)
	queue_label.custom_minimum_size.y = 18
	feedback.add_child(queue_label)
	var context_slot := CenterContainer.new()
	context_slot.name = "ContextActionSlot"
	context_slot.custom_minimum_size = Vector2(190, 44)
	next_frame_button = _button(I18n.text("NEXT ANIMATION  [%s]") % Preferences.action_label("confirm_action"), _next_frame, 180)
	next_frame_button.clip_text = true
	next_frame_button.tooltip_text = next_frame_button.text
	context_slot.add_child(next_frame_button)
	selection_actions = HBoxContainer.new()
	selection_actions.add_theme_constant_override("separation", 6)
	var cancel := _button(I18n.text("CANCEL SELECTION"), _cancel_selection, 180)
	cancel.set_meta("selection_only", true)
	action_buttons.append(cancel)
	selection_actions.add_child(cancel)
	# Inspection remains in the hand toolbar; do not duplicate it in the turn dock.
	cancel.clip_text = true
	cancel.tooltip_text = cancel.text
	context_slot.add_child(selection_actions)
	row.add_child(context_slot)
	var system_slot := CenterContainer.new()
	system_slot.name = "SystemActionSlot"
	system_slot.custom_minimum_size = Vector2(100, 44)
	var pass_candidate := _system_candidate(["PASS", "PASS_PRIORITY"])
	if not pass_candidate.is_empty():
		var pass_label := "PASS PRIORITY" if str(pass_candidate.command.get("actionType", "")) == "PASS_PRIORITY" else "PASS"
		var pass_button := _button(I18n.text(pass_label), func(): _execute_system(pass_candidate), 100)
		pass_button.clip_text = true
		pass_button.tooltip_text = pass_button.text
		action_buttons.append(pass_button)
		system_slot.add_child(pass_button)
	row.add_child(system_slot)
	var end := _button(I18n.text("END TURN  [%s]") % Preferences.action_label("end_turn"), _end_turn, 195)
	end.name = "EndTurnButton"
	end.add_theme_stylebox_override("normal", AppTheme.box(Color("#705033"), 10, AppTheme.GOLD, 2))
	end.set_meta("requires_end", true)
	action_buttons.append(end)
	row.add_child(end)
	reconnect_button = _button(I18n.text("RECONNECT"), _reconnect, 130)
	floating_hud.host.add_child(reconnect_button)
	reconnect_button.set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT)
	reconnect_button.offset_right = 150
	reconnect_button.offset_bottom = 44
	bottom_hud.add_child(row)

func _build_character_sidebar() -> Control:
	_ensure_floating_hud()
	if not is_instance_valid(character_sidebar_reopen):
		# The collapsed affordance is an overlay, not an empty column in CombatBody.
		character_sidebar_reopen = _button("‹", _toggle_character_sidebar, 36)
		character_sidebar_reopen.name = "CharacterSidebarReopen"
		character_sidebar_reopen.z_index = 1
		floating_hud.host.add_child(character_sidebar_reopen)
		character_sidebar_reopen.set_anchors_and_offsets_preset(Control.PRESET_TOP_RIGHT)
		character_sidebar_reopen.offset_left = -44
		character_sidebar_reopen.offset_bottom = 44
	character_sidebar = PanelContainer.new()
	character_sidebar.name = "CharacterSidebar"
	character_sidebar.mouse_filter = Control.MOUSE_FILTER_STOP
	floating_hud.host.add_child(character_sidebar)
	character_sidebar.set_anchors_and_offsets_preset(Control.PRESET_TOP_RIGHT)
	character_sidebar.offset_left = -328
	character_sidebar.offset_bottom = 520
	character_sidebar.add_theme_stylebox_override("panel", AppTheme.box(Color("#171927"), 12, Color("#454660"), 1))
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 8)
	character_sidebar.add_child(column)
	var toolbar := HBoxContainer.new()
	toolbar.add_theme_constant_override("separation", 6)
	character_sidebar_title = AppTheme.title(I18n.text("CHARACTER"), 16, AppTheme.GOLD)
	character_sidebar_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	character_sidebar_title.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	toolbar.add_child(character_sidebar_title)
	var toggle := _button("›", _toggle_character_sidebar, 36)
	toggle.name = "CharacterSidebarToggle"
	toggle.custom_minimum_size = Vector2(36, 36)
	toolbar.add_child(toggle)
	column.add_child(toolbar)
	character_sidebar_content = ScrollContainer.new()
	character_sidebar_content.name = "CharacterSidebarContent"
	character_sidebar_content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	character_sidebar_content.size_flags_vertical = Control.SIZE_EXPAND_FILL
	character_sidebar_content.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	character_sidebar_content.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	var details := VBoxContainer.new()
	details.name = "CharacterSidebarDetails"
	details.custom_minimum_size.x = 272
	details.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	details.add_theme_constant_override("separation", 7)
	_populate_character_sidebar(details)
	character_sidebar_content.add_child(details)
	column.add_child(character_sidebar_content)
	_apply_character_sidebar_state()
	return character_sidebar

func _populate_character_sidebar(details: VBoxContainer) -> void:
	var actor := _player_actor()
	if actor.is_empty():
		details.add_child(AppTheme.muted(I18n.text("Character data is unavailable."), 13, 260))
		return
	var definition_id := str(actor.get("definitionId", ""))
	var actor_name := I18n.content_name(definition_id, str(actor.get("name", actor.get("instanceId", ""))))
	var name_label := AppTheme.title(actor_name, 20)
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	name_label.custom_minimum_size.x = 260
	details.add_child(name_label)
	if not definition_id.is_empty():
		details.add_child(AppTheme.caption(definition_id, 11, 260))
	_add_sidebar_section(details, I18n.text("Resources"))
	var resources: Dictionary = actor.get("resources", {}) if actor.get("resources", {}) is Dictionary else {}
	var ordered_resources: Array = presentation.get("resource_order", []).duplicate()
	for resource_id in resources:
		if resource_id not in ordered_resources:
			ordered_resources.append(resource_id)
	for resource_id in ordered_resources:
		if not resources.has(resource_id) or not (resources[resource_id] is Dictionary):
			continue
		var resource_view := preload("res://scripts/ui/resource_view.gd").new()
		resource_view.name = "CharacterResource_" + str(resource_id).validate_node_name()
		resource_view.set_meta("resource_id", str(resource_id))
		resource_view.custom_minimum_size.x = 260
		resource_view.setup(str(resource_id), resources[resource_id], presentation.get("resources", {}).get(resource_id, {}))
		details.add_child(resource_view)
	_add_sidebar_section(details, I18n.text("Statuses"))
	var statuses: Array = actor.get("statuses", []).filter(func(status): return status is Dictionary and bool(status.get("isActive", true)))
	if statuses.is_empty():
		details.add_child(AppTheme.muted(I18n.text("No active statuses"), 12, 260))
	else:
		for status in statuses:
			var status_text := "%s ×%s" % [I18n.content_name(str(status.get("statusId", "status"))), int(status.get("stacks", 1))]
			var duration := int(status.get("duration", -1))
			if duration >= 0:
				status_text += "  ·  " + I18n.text("Duration: %s") % duration
			details.add_child(_sidebar_item(status_text, AppTheme.EMBER))
	_add_sidebar_section(details, I18n.text("Relics"))
	var relics: Array = GameSession.run.get("relics", []) if GameSession.run.get("relics", []) is Array else []
	if relics.is_empty():
		details.add_child(AppTheme.muted(I18n.text("No relics"), 12, 260))
	else:
		for relic in relics:
			details.add_child(_sidebar_item(_owned_content_label(relic, ["definitionId", "relicId", "id"]), AppTheme.GOLD))
	var modifiers: Array = GameSession.run.get("modifiers", []) if GameSession.run.get("modifiers", []) is Array else []
	if not modifiers.is_empty():
		_add_sidebar_section(details, I18n.text("Modifiers"))
		for modifier in modifiers:
			details.add_child(_sidebar_item(_owned_content_label(modifier, ["modifierId", "definitionId", "id"]), AppTheme.TEAL))
	_add_sidebar_section(details, I18n.text("RUN INFO"))
	var activation: Dictionary = GameSession.combat.get("activation", {}) if GameSession.combat.get("activation", {}) is Dictionary else {}
	_add_sidebar_value(details, I18n.text("Round"), str(int(activation.get("round", GameSession.combat.get("currentTurn", 1)))))
	_add_sidebar_value(details, I18n.text("Deck"), I18n.text("%s cards") % zone_presenter.total_cards())
	_add_sidebar_value(details, I18n.text("Playable"), str(zone_presenter.playable_cards().size()))
	var setting := GameSession.selected_setting
	_add_sidebar_value(details, I18n.text("Setting"), I18n.text(str(setting.get("displayName", GameSession.selected_setting_id))))

func _player_actor() -> Dictionary:
	var actors: Array = presenter.actors()
	for actor in actors:
		var binding: Dictionary = actor.get("controllerBinding", {}) if actor.get("controllerBinding", {}) is Dictionary else {}
		if str(binding.get("kind", "")).to_lower() == "player":
			return actor
	var input_actor_id := GameSession.input_actor_id()
	for actor in actors:
		if str(actor.get("instanceId", "")) == input_actor_id:
			return actor
	return actors[0] if not actors.is_empty() else {}

func _add_sidebar_section(parent: VBoxContainer, text: String) -> void:
	var title := AppTheme.title(text.to_upper(), 12, AppTheme.GOLD)
	title.custom_minimum_size = Vector2(260, 24)
	title.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	parent.add_child(title)

func _sidebar_item(text: String, color: Color) -> Label:
	var label := AppTheme.muted(text, 12, 260)
	label.add_theme_color_override("font_color", color)
	label.tooltip_text = text
	return label

func _owned_content_label(item, id_keys: Array) -> String:
	if not (item is Dictionary):
		return I18n.content_name(str(item))
	var id := ""
	for key in id_keys:
		if item.has(key) and not str(item[key]).is_empty():
			id = str(item[key])
			break
	var result := I18n.content_name(id, id)
	var stacks := int(item.get("stacks", 1))
	if stacks > 1:
		result += " ×%s" % stacks
	return result

func _add_sidebar_value(parent: VBoxContainer, label_text: String, value_text: String) -> void:
	var row := HBoxContainer.new()
	row.custom_minimum_size.x = 260
	var label := AppTheme.muted(label_text, 12, 112)
	label.autowrap_mode = TextServer.AUTOWRAP_OFF
	row.add_child(label)
	var value := AppTheme.caption(value_text, 12, 140)
	value.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	value.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	value.tooltip_text = value_text
	row.add_child(value)
	parent.add_child(row)

func _toggle_character_sidebar() -> void:
	character_sidebar_expanded = not character_sidebar_expanded
	_apply_character_sidebar_state()

func _apply_character_sidebar_state() -> void:
	if not is_instance_valid(character_sidebar) or not is_instance_valid(character_sidebar_content):
		return
	var toggle: Button = character_sidebar.find_child("CharacterSidebarToggle", true, false)
	var transfer_focus := (is_instance_valid(toggle) and toggle.has_focus()) or character_sidebar_reopen.has_focus()
	character_sidebar.custom_minimum_size.x = 328
	character_sidebar.visible = character_sidebar_expanded
	character_sidebar_reopen.visible = not character_sidebar_expanded
	character_sidebar_reopen.tooltip_text = I18n.text("EXPAND CHARACTER PANEL")
	character_sidebar_content.visible = character_sidebar_expanded
	character_sidebar_title.visible = character_sidebar_expanded
	if is_instance_valid(toggle):
		toggle.tooltip_text = I18n.text("COLLAPSE CHARACTER PANEL")
	if transfer_focus and is_inside_tree():
		(toggle if character_sidebar_expanded else character_sidebar_reopen).grab_focus()
	preload("res://scripts/ui/focus_navigation.gd").wire.call_deferred(self)

func _input(event: InputEvent) -> void:
	# Consume cancel before the router handles pause; the first Escape cancels targeting.
	if not get_tree().paused and not selected_card.is_empty() and event.is_action_pressed("ui_cancel"):
		_cancel_selection()
		get_viewport().set_input_as_handled()

func _unhandled_input(event: InputEvent) -> void:
	if get_tree().paused or not is_visible_in_tree():
		return
	if event.is_action_pressed("inspect_card") and not event.is_echo():
		_inspect_instance(hand.active_card_id())
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("hand_overview") and not event.is_echo():
		hand.show_overview()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("end_turn") and not event.is_echo():
		_end_turn()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("confirm_action") and not event.is_echo():
		_next_frame()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("ui_cancel") or (event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT):
		_cancel_selection()

func _select_target(actor_id: String) -> void:
	if _locked() or selected_card.is_empty():
		return
	selected_target = actor_id
	var matches: Array = _candidates(selected_card).filter(func(candidate): return actor_id in _targets(candidate))
	_update_controls()
	_offer_candidates(matches)

func _play_candidate(candidate: Dictionary) -> void:
	if _locked() or candidate.is_empty():
		return
	submitting = true
	_update_controls()
	var id := str(candidate.command.get("cardInstanceId", ""))
	var success := await GameSession.submit_candidate(candidate)
	if not is_inside_tree():
		return
	if success:
		selected_card = ""
		selected_target = ""
		hand.clear_choice()
		_clear_choices()
		detail_label.text = I18n.text("Select a card, then choose a highlighted target.")
		GameAudio.card()
		var target_ids := _targets(candidate)
		var destination := size * .5 + global_position
		if not target_ids.is_empty() and actor_portraits.has(target_ids[0]):
			var portrait: Control = actor_portraits[target_ids[0]]
			destination = portrait.global_position + portrait.size * .5
		if card_buttons.has(id):
			var still_present := CardZonePresenter.new(GameSession.card_zones, I18n).playable_cards().any(func(card): return str(card.get("cardInstanceId", "")) == id)
			if not still_present: card_buttons[id].modulate.a = 0
			await card_buttons[id].fly_to(destination, router)
		if is_inside_tree():
			router.open_game()
	else:
		submitting = false
		_update_controls()

func _end_turn() -> void:
	if _locked():
		return
	var candidate := _system_candidate(["END_TURN"])
	if candidate.is_empty():
		return
	submitting = true
	_update_controls()
	var accepted := await GameSession.submit_candidate(candidate)
	if not is_inside_tree():
		return
	if accepted:
		router.open_game()
	else:
		submitting = false
		_update_controls()

func _execute_system(candidate: Dictionary) -> void:
	if _locked():
		return
	submitting = true
	_update_controls()
	var accepted := await GameSession.submit_candidate(candidate)
	if not is_inside_tree():
		return
	if accepted:
		router.open_game()
	else:
		submitting = false
		_update_controls()

func _next_frame() -> void:
	if get_tree().paused:
		return
	Playback.advance()
	_update_controls()

func _on_frame(frame: Dictionary, index: int, total: int) -> void:
	detail_label.text = "%s %s/%s  •  %s" % [I18n.text("Animations"), index + 1, total, I18n.content_name(str(frame.get("transitionType", "")))]
	var flashed: Array[String] = []
	for application in frame.get("applications", []):
		detail_label.text += "\n" + presenter.application_text(application, true)
		var target := str(application.get("targetEntityId", ""))
		if actor_portraits.has(target):
			if not Preferences.reduced_motion and target not in flashed:
				actor_portraits[target].flash_hit()
				flashed.append(target)
			_float_application(application, actor_portraits[target])
	GameAudio.hit()

func _queue_text() -> String:
	var remaining := Playback.total - Playback.index
	return I18n.text("%s animation(s) remaining") % maxi(remaining, 0)

func _candidate_for_card(instance_id: String, target_id := "") -> Dictionary:
	return presenter._candidate_for_card(instance_id, target_id)

func _system_candidate(action_types: Array) -> Dictionary:
	return presenter._system_candidate(action_types)

func _preview(candidate: Dictionary) -> String:
	return presenter._preview(candidate)

func _card_instance(instance_id: String) -> Dictionary:
	return presenter._card_instance(instance_id)

func _intent_for(actor_id: String) -> String:
	return presenter._intent_for(actor_id)

func _button(text: String, action: Callable, width := 0) -> Button:
	var value := AppTheme.button(text, width)
	value.custom_minimum_size.y = 44
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
func _process(delta: float) -> void:
	if get_tree().paused:
		return
	frame_delay -= delta
	if Preferences.auto_animations and frame_delay <= 0.0 and _has_frames():
		_next_frame()
		frame_delay = .32 / maxf(Preferences.animation_speed, .25)

func _has_frames() -> bool:
	return Playback.has_frames()

func _locked() -> bool:
	return presenter.input_state(submitting or GameSession.busy, GameSession.synchronized,
		_has_frames(), router.get_tree().paused, selected_card) not in [Presenter.InputState.READY, Presenter.InputState.SELECTING]

func _choose_card(id: String) -> void:
	if _locked():
		return
	var matches := _candidates(id)
	if selected_card == id and matches.size() == 1:
		_play_candidate(matches[0])
		return
	selected_card = id
	selected_target = ""
	GameAudio.ui()
	_clear_choices()
	_show_preview(id)
	_update_controls()
	if matches.all(func(candidate): return _targets(candidate).is_empty()):
		_offer_candidates(matches)

func _cancel_selection() -> void:
	if submitting or GameSession.busy:
		return
	selected_card = ""
	selected_target = ""
	_clear_choices()
	detail_label.text = I18n.text("Select a card, then choose a highlighted target.")
	_update_controls()

func _candidates(id: String) -> Array:
	return presenter._candidates(id)

func _targets(candidate: Dictionary) -> Array:
	return presenter._targets(candidate)

func _clear_choices() -> void:
	if is_instance_valid(candidate_panel): candidate_panel.hide()
	for child in choices.get_children():
		choices.remove_child(child)
		child.queue_free()

func _offer_candidates(candidates: Array) -> void:
	_clear_choices()
	if candidates.size() == 1:
		_play_candidate(candidates[0])
		return
	# Keep alternative costs and multi-target combinations explicit.
	for candidate in candidates:
		var label := "%s  •  %s  •  %s" % [
			I18n.text("PLAY"), _cost_text(candidate), _preview(candidate)]
		var option := _button(label, func(): _play_candidate(candidate))
		option.clip_text = true
		option.tooltip_text = label
		choices.add_child(option)
	candidate_panel.visible = not candidates.is_empty()
	preload("res://scripts/ui/focus_navigation.gd").wire.call_deferred(self)

func _show_preview(id: String) -> void:
	if not is_instance_valid(detail_label) or _has_frames():
		return
	var matches := _candidates(id)
	detail_label.text = _preview(matches[0]) if matches.size() == 1 else I18n.text("Choose a highlighted target.")
	if matches.is_empty():
		detail_label.text = presenter.unavailable_reason(id)

func _update_controls() -> void:
	preload("res://scripts/ui/focus_navigation.gd").wire.call_deferred(self)
	if not is_instance_valid(queue_label):
		return
	var locked := _locked()
	hand.set_read_only(locked)
	hand.set_selection(selected_card)
	reconnect_button.visible = not GameSession.synchronized
	queue_label.text = _queue_text() if _has_frames() else ""
	queue_label.tooltip_text = queue_label.text
	next_frame_button.visible = _has_frames()
	selection_actions.visible = not selected_card.is_empty() and not _has_frames()
	selection_label.text = I18n.text("PROCESSING…") if GameSession.busy or submitting else (
		I18n.text("Choose a highlighted target.") if not selected_card.is_empty() else "")
	selection_label.tooltip_text = selection_label.text
	if GameSession.busy or submitting: queue_label.text = selection_label.text
	queue_label.tooltip_text = queue_label.text
	for id in card_buttons:
		var card: CardView = card_buttons[id]
		card.disabled = false # Still focusable for reading; activation is gated by the hand/screen.
		# Availability is explicit text; tinting the entire card corrupts semantic colors.
		card.modulate = Color.WHITE
		card.select_card(id == selected_card)
	for button in action_buttons:
		button.disabled = locked or (button.has_meta("selection_only") and selected_card.is_empty()) or \
			(button.has_meta("requires_end") and _system_candidate(["END_TURN"]).is_empty())
	for id in target_buttons:
		var allowed := not selected_card.is_empty() and _candidates(selected_card).any(func(candidate): return id in _targets(candidate))
		target_buttons[id].disabled = locked or not allowed
		target_buttons[id].button_pressed = selected_target == id
		target_buttons[id].modulate = AppTheme.GOLD if allowed else Color.WHITE

func _actor_name(id: String) -> String:
	return presenter._actor_name(id)

func _float_application(application: Dictionary, portrait: Control) -> void:
	var previous = application.get("previousValue")
	var current = application.get("currentValue")
	if previous == null or current == null:
		return
	var delta := float(current) - float(previous)
	if is_zero_approx(delta):
		return
	var label := Label.new()
	label.text = "%s%s %s" % ["+" if delta > 0 else "", I18n.number(delta),
		I18n.content_name(str(application.get("resourceId", "")))]
	label.add_theme_font_size_override("font_size", 22)
	label.add_theme_color_override("font_color", AppTheme.TEAL if delta > 0 else AppTheme.GOLD)
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.process_mode = Node.PROCESS_MODE_PAUSABLE
	router.add_child(label)
	label.global_position = portrait.global_position + Vector2(20, 45)
	var tween := label.create_tween().set_parallel()
	var duration := .6 / maxf(Preferences.animation_speed, .25)
	if not Preferences.reduced_motion:
		tween.tween_property(label, "position:y", label.position.y - 36, duration)
	tween.tween_property(label, "modulate:a", 0.0, duration)
	tween.chain().tween_callback(label.queue_free)

func _reconnect() -> void:
	var accepted := await GameSession.refresh()
	if not is_inside_tree():
		return
	if accepted:
		router.open_game()

func _cost_text(candidate: Dictionary) -> String:
	return presenter._cost_text(candidate)

func _load_inspection(ticket: int) -> void:
	var representative_targets: Array = []
	if not selected_target.is_empty(): representative_targets = [selected_target]
	for candidate in GameSession.legal_actions:
		if not representative_targets.is_empty(): break
		var targets: Array = presenter._targets(candidate)
		if not targets.is_empty():
			representative_targets = [targets[0]]
			break
	var response := await GameSession.inspect_hand(representative_targets)
	if not is_inside_tree() or ticket != render_epoch or not response.ok:
		return
	presenter.accept_evaluations(response.data.get("cards", []))
	hand.sync_models(hand.order.map(func(id): return presenter.card_view_model(id)), _hand_scope(), I18n.text("Your hand is empty."))
	card_buttons = hand.cards()
	hand.set_selection(selected_card)
	if not selected_card.is_empty(): _show_preview(selected_card)

func _inspect_card() -> void:
	_inspect_instance(selected_card)

func _inspect_instance(id: String) -> void:
	if id not in card_buttons: return
	var dialog := preload("res://scripts/ui/inspection_dialog.gd").new()
	dialog.setup(I18n.text("Card inspection"), presenter.inspection_text(id), presenter.inspection_data(id), presenter.card_view_model(id), card_buttons[id].visual_style)
	dialog.tree_exiting.connect(func(): if id in card_buttons: card_buttons[id].grab_focus())
	add_child(dialog)
	hand.track_reading_modal(dialog)
	dialog.popup_centered()

func _inspect_pile(label: String, zone: String) -> void:
	var lines: Array[String] = []
	for card in presenter.pile_cards(zone):
		lines.append(I18n.content_name(str(card.get("definitionId", ""))) + (" +" if not card.get("upgrades", []).is_empty() else ""))
	var dialog := preload("res://scripts/ui/inspection_dialog.gd").new()
	dialog.setup(I18n.text(label), "\n".join(lines) if not lines.is_empty() else I18n.text("Empty"))
	add_child(dialog)
	hand.track_reading_modal(dialog)
	dialog.popup_centered()

func _open_zone_tools() -> void:
	if _locked(): return
	var flows: Array = CardZonePresenter.tool_flows(GameSession.run)
	if flows.is_empty(): return
	var dialog := preload("res://scripts/ui/card_zone_tool_dialog.gd").new()
	dialog.setup(flows, selected_card)
	dialog.flow_requested.connect(_execute_zone_flow)
	add_child(dialog)
	hand.track_reading_modal(dialog)
	dialog.popup_centered()

func _execute_zone_flow(payload: Dictionary) -> void:
	if _locked(): return
	submitting = true
	_update_controls()
	var accepted := await GameSession.execute_run_command("INVOKE_CARD_ZONE_FLOW", payload, str(payload.get("flowId", "")))
	if not is_inside_tree(): return
	if accepted:
		router.open_game()
	else:
		submitting = false
		_update_controls()
