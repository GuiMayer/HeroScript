@tool
class_name ActivityOptionsView
extends VBoxContainer
## Passive offers. The presenter and published choices retain all rule authority.
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
var _entries := {}
var _bound := false
static func create() -> ActivityOptionsView:
	return (load("res://scenes/ui/components/activity_options.tscn") as PackedScene).instantiate()
func _ready() -> void:
	if visual_context == null:
		visual_context = ContextType.new(); theme = visual_context.theme
func _choice_label(choice: Dictionary, model: Dictionary) -> String: return presenter.choice_label(choice, model)
func _current_node() -> Dictionary: return presenter.current_node()
func _confirm_ref(reference_id: String) -> void: intent_requested.emit("confirm", [reference_id])
func _recover_actions() -> void: intent_requested.emit("recover", [])
func _execute(choice: Dictionary) -> void: intent_requested.emit("execute", [presenter.option_reference(choice)])
func apply_projection(run: Dictionary, choices: Array, projection, context) -> void:
	visual_context = context; theme = context.theme; presenter = projection
	_run_snapshot = run.duplicate(true); _choices_snapshot = choices.duplicate(true)
	var display_run := _run_snapshot.duplicate(true); display_run.erase("sequence")
	var key: Array = [display_run, choices, context.presentation_key(), presenter.presentation_key()]
	if key == _key: return
	_key = key.duplicate(true); action_panel = get_node("ActionPanel")
	if not _bound:
		action_panel.get_node("Waiting/RefreshActionsButton").pressed.connect(_recover_actions); _bound = true
	action_panel.get_node("Waiting/Message").text = context.translator.text("Waiting for the next available action.")
	action_panel.get_node("Waiting/RefreshActionsButton").text = context.translator.text("REFRESH ACTIONS")
	action_panel.get_node("SecondaryTitle").text = context.translator.text("OTHER OPTIONS")
	action_panel.get_node("Waiting").visible = choices.is_empty()
	action_panel.get_node("Primary").visible = not choices.is_empty()
	choice_buttons.clear(); var live: Array = []; var primary_index := 0; var secondary_index := 0
	if choices.is_empty(): choice_buttons.append(action_panel.get_node("Waiting/RefreshActionsButton"))
	for choice in choices:
		var type := str(choice.type)
		if type in ["CHOOSE_DIALOGUE_OPTION","ABANDON_RUN","APPLY_CARD_MODIFIER_ITEM","ADVANCE_NODE"]: continue
		var reference: String = presenter.option_reference(choice)
		var model: Dictionary = presenter.offer(choice)
		var label := _choice_label(choice,model)
		var secondary: bool = bool(choice.get("secondary",false)) or type in ["RESOLVE_NODE","RESOLVE_COMBAT"]
		var kind := "secondary" if secondary else ("card" if str(model.category) == "cards" else "generic")
		var entry = _entries.get(reference)
		if is_instance_valid(entry) and str(entry.get_meta("kind")) != kind:
			entry.get_parent().remove_child(entry); entry.queue_free(); _entries.erase(reference); entry = null
		if not is_instance_valid(entry):
			var scene: PackedScene = {"secondary":preload("res://scenes/ui/components/activity_action.tscn"),"card":preload("res://scenes/ui/components/activity_card_offer.tscn"),"generic":preload("res://scenes/ui/components/activity_generic_offer.tscn")}[kind]
			entry = scene.instantiate(); entry.set_meta("kind",kind)
			_entries[reference] = entry
			var button: Button
			if kind == "card":
				button = CardView.create(context); button.animate_entry = animate_card_entry; button.set_meta("skip_initial_focus",true)
				entry.add_child(button); entry.move_child(button,0)
			else: button = entry if secondary else entry.get_node("Content/Choose")
			entry.set_meta("choice_button",button)
			button.name = "Choice_" + reference; button.set_meta("activity_choice",true)
			button.pressed.connect(func(): _confirm_ref(reference))
			entry.name = "Offer_" + reference if not secondary else "Choice_"+reference
			var parent = action_panel.get_node("Secondary" if secondary else "Primary"); parent.add_child(entry)
		live.append(reference)
		entry.get_parent().move_child(entry,secondary_index if secondary else primary_index)
		if secondary: secondary_index += 1
		else: primary_index += 1
		var button: Button = entry.get_meta("choice_button")
		if secondary: button.text = label + (" · " + str(model.cost) if not str(model.cost).is_empty() else "")
		elif kind == "card":
			model["availability"] = label; var price := str(model.get("acquisitionPrice",model.cost))
			model["cost"] = ""; model["costsKnown"] = model.has("presentationFacts"); model["costUnknownLabel"] = context.translator.text("Cost unavailable")
			button.visual_context = context; button.configure(model)
			entry.get_node("Variant").visible = choice.has("variantId")
			entry.get_node("Variant").text = context.translator.content_name(str(choice.get("variantId","")))
			var destination := str(model.get("rewardDestination", ""))
			entry.get_node("AcquisitionPrice").visible = not price.is_empty() or not destination.is_empty()
			entry.get_node("AcquisitionPrice").text = (context.translator.text("Destination: %s") % context.translator.text(destination)) if not destination.is_empty() else (context.translator.text("Acquisition price: %s") % price)
		else:
			var content = entry.get_node("Content"); var art = content.get_node("Art")
			art.rebind_context(context); art.setup("modifier_items" if str(model.get("category","")) == "modifier_items" else "activities",str(model.get("artId",_current_node().get("activity",{}).get("type",""))))
			content.get_node("Title").text = str(model.name); content.get_node("Title").visible = not str(choice.get("subjectId","")).is_empty()
			content.get_node("Cost").text = str(model.cost); content.get_node("Cost").visible = not str(model.cost).is_empty()
			content.get_node("Cost").add_theme_color_override("font_color",context.styles.GOLD)
			content.get_node("Description").text = str(model.summary)
			content.get_node("Description").visible = (str(choice.get("subjectType","")) == "inscription" or str(model.category) == "modifier_items") and not str(model.summary).is_empty()
			button.text = label
	for reference in _entries.keys():
		if reference not in live:
			var obsolete = _entries[reference]; obsolete.get_parent().remove_child(obsolete); obsolete.queue_free(); _entries.erase(reference)
	action_panel.get_node("SecondaryTitle").visible = secondary_index > 0
	action_panel.get_node("Secondary").visible = secondary_index > 0
	if not choices.is_empty():
		_collect_choices(action_panel.get_node("Primary")); _collect_choices(action_panel.get_node("Secondary"))
	context.styles.apply_view_preferences(self)
func _collect_choices(node: Node) -> void:
	if node is Button and node.get_meta("activity_choice",false): choice_buttons.append(node)
	for child in node.get_children(): _collect_choices(child)
func reconcile_from(other) -> void: apply_projection(other._run_snapshot,other._choices_snapshot,other.presenter,other.visual_context)
