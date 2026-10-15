extends VBoxContainer
## Focused offer/choice renderer; unknown published choices retain a generic action.
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
signal intent_requested(action: String, arguments: Array)
var presenter
var _run_snapshot: Dictionary
var _choices_snapshot: Array
var choice_buttons: Array[Button] = []
var action_panel: VBoxContainer
var animate_card_entry := false
var _key: Array = []
func _build_actions() -> void:
	var choices := _choices_snapshot
	if choices.is_empty():
		action_panel.add_child(visual_context.styles.muted(visual_context.translator.text("Waiting for the next available action.")))
		var retry := _button(visual_context.translator.text("REFRESH ACTIONS"), _recover_actions)
		retry.name = "RefreshActionsButton"
		action_panel.add_child(retry)
		choice_buttons.append(retry)
		retry.set_meta("activity_choice", true)
		return
	var primary := HFlowContainer.new()
	primary.add_theme_constant_override("h_separation", 14)
	primary.add_theme_constant_override("v_separation", 14)
	action_panel.add_child(primary)
	var secondary := VBoxContainer.new()
	secondary.add_theme_constant_override("separation", 8)
	for choice in choices:
		var type := str(choice.type)
		if type == "CHOOSE_DIALOGUE_OPTION": continue
		if type == "ABANDON_RUN": continue # Run exit belongs to the pause menu.
		if type == "APPLY_CARD_MODIFIER_ITEM": continue # Item collection owns this optional workflow.
		if type == "ADVANCE_NODE": continue # The route itself is the navigation control.
		var reference_id: String = presenter.option_reference(choice)
		var model: Dictionary = presenter.offer(choice)
		var label := _choice_label(choice, model)
		if bool(choice.get("secondary", false)) or type in ["RESOLVE_NODE", "RESOLVE_COMBAT"]:
			var display_label := label + (" · " + str(model.cost) if not str(model.cost).is_empty() else "")
			var button := _button(display_label, func(): _confirm_ref(reference_id))
			button.alignment = HORIZONTAL_ALIGNMENT_LEFT
			button.add_theme_font_size_override("font_size", 14)
			button.name = "Choice_" + reference_id
			button.set_meta("activity_choice", true)
			choice_buttons.append(button)
			secondary.add_child(button)
		elif str(model.category) == "cards":
			var card := CardView.new(visual_context)
			card.animate_entry = animate_card_entry
			model["availability"] = label
			# Offer price is not a card's play cost. This projection has no engine
			# inspection of the offered container, so report unknown instead of zero.
			var price := str(model.get("acquisitionPrice", model.cost))
			model["cost"] = ""
			model["costsKnown"] = model.has("presentationFacts")
			model["costUnknownLabel"] = visual_context.translator.text("Cost unavailable")
			card.configure(model)
			# Offers are not a preselected choice. Navigation may focus them explicitly.
			card.set_meta("skip_initial_focus", true)
			card.pressed.connect(func(): _confirm_ref(reference_id))
			card.name = "Choice_" + reference_id
			card.set_meta("activity_choice", true)
			choice_buttons.append(card)
			var offer := VBoxContainer.new()
			offer.name = "Offer_" + reference_id
			offer.add_child(card)
			if not price.is_empty():
				var price_label := visual_context.styles.caption(visual_context.translator.text("Acquisition price: %s") % price, 14, 200)
				price_label.name = "AcquisitionPrice"
				offer.add_child(price_label)
			primary.add_child(offer)
		else:
			var column := VBoxContainer.new()
			column.name = "Offer_" + reference_id
			column.custom_minimum_size.x = 236
			var art := preload("res://scripts/ui/art_slot.gd").new(visual_context)
			art.setup("modifier_items" if str(model.get("category", "")) == "modifier_items" else "activities", str(model.get("artId", _current_node().get("activity", {}).get("type", ""))))
			art.custom_minimum_size.y = 100
			column.add_child(art)
			if not str(choice.get("subjectId", "")).is_empty():
				var title := visual_context.styles.wrap(visual_context.styles.title(str(model.name), 19), 180)
				column.add_child(title)
			if not str(model.cost).is_empty(): column.add_child(visual_context.styles.title(str(model.cost), 17, visual_context.styles.GOLD))
			if str(choice.get("subjectType", "")) == "inscription" and not str(model.summary).is_empty():
				column.add_child(visual_context.styles.muted(str(model.summary), 14, 200))
			var button := _button(label, func(): _confirm_ref(reference_id))
			button.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
			button.name = "Choice_" + reference_id
			button.set_meta("activity_choice", true)
			choice_buttons.append(button)
			column.add_child(button)
			primary.add_child(visual_context.styles.panel(column))
	if secondary.get_child_count() > 0:
		action_panel.add_child(visual_context.styles.muted(visual_context.translator.text("OTHER OPTIONS"), 13))
		action_panel.add_child(secondary)
	else: secondary.free()



func _collect_choices(node: Node) -> void:
	if node is Button and node.get_meta("activity_choice", false): choice_buttons.append(node)
	for child in node.get_children(): _collect_choices(child)



func _choice_label(choice: Dictionary, model: Dictionary) -> String:
	return presenter.choice_label(choice, model)



func _current_node() -> Dictionary:
	return presenter.current_node()



func _button(text: String, action: Callable, width := 0) -> Button:
	var value := visual_context.styles.button(text, width)
	value.name = "ActivityAction_" + str(action.get_method()).validate_node_name()
	value.pressed.connect(action)
	return value




func _execute(choice: Dictionary) -> void:
	intent_requested.emit("execute", [presenter.option_reference(choice)])

func _confirm_ref(reference_id: String) -> void: intent_requested.emit("confirm", [reference_id])

func _recover_actions() -> void: intent_requested.emit("recover", [])

func apply_projection(run: Dictionary, choices: Array, projection, context) -> void:
	visual_context = context; theme = context.theme
	presenter = projection; _run_snapshot = run.duplicate(true); _choices_snapshot = choices.duplicate(true)
	var display_run := _run_snapshot.duplicate(true); display_run.erase("sequence")
	var key: Array = [display_run, choices, context.presentation_key()]
	if key == _key: return
	_key = key.duplicate(true)
	action_panel = VBoxContainer.new()
	action_panel.add_theme_constant_override("separation", 14)
	_build_actions()
	preload("res://scripts/ui/view_reconciler.gd").children(self, action_panel)
	action_panel.free(); action_panel = null
	choice_buttons.clear(); _collect_choices(self)
	context.styles.apply_view_preferences(self)
func reconcile_from(other) -> void:
	apply_projection(other._run_snapshot, other._choices_snapshot, other.presenter, other.visual_context)
