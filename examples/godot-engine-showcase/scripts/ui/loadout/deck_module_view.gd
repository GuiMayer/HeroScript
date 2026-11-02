@tool
class_name DeckModuleView
extends VBoxContainer
## Passive current-deck view. Uses combat cards and canonical component presentations.
signal move_requested(card_id: String, compartment_id: String)
signal clear_requested
signal modifier_requested(reference: String)
var visual_context
var appearance := {}
var subtab := "cards"
var selected_id := ""
var _model := {}
var _signature := ""
var _search := ""
var _resource := ""
var _tag := ""
var _order := "name"
var _body: VBoxContainer
var _sub_buttons := {}
var _card_buttons: Array = []
var _actions: Array = []
var _card_cache := {}
var _parking: Control
var _bound := false

static func create() -> DeckModuleView:
	return (load("res://scenes/ui/components/loadout/deck_module.tscn") as PackedScene).instantiate()
func _ready() -> void:
	if visual_context == null: mount(preload("res://scripts/ui/visual_context.gd").new())
func mount(context) -> void:
	visual_context = context; theme = context.theme
	if _bound:
		for entry in [["cards", "Cards"], ["compartments", "Compartments"], ["modifiers", "Card modifiers"]]: _sub_buttons[entry[0]].text = context.translator.text(entry[1])
		get_node("Body/CardBody/Tools/Search").placeholder_text = context.translator.text("Search cards")
		for entry in [["Targets/Clear", "Clear distribution"], ["Targets/Inspect", "Inspect card"], ["Hint", "Drag a card to a compartment, or select a card and a destination. Changes save automatically."]]: get_node("Body/CardBody/" + entry[0]).text = context.translator.text(entry[1])
		var sort_picker: OptionButton = get_node("Body/CardBody/Tools/Sort")
		for index in 2: sort_picker.set_item_text(index, context.translator.text(["Name", "Resource cost"][index]))
		return
	_bound = true
	for path in ["res://data/presentation.json", "res://data/volatile_core_presentation.json", "res://data/world_one_presentation.json"]:
		var overlay = JSON.parse_string(FileAccess.get_file_as_string(path))
		if not overlay is Dictionary: continue
		for category in overlay:
			if not appearance.has(category): appearance[category] = {}
			if overlay[category] is Dictionary: appearance[category].merge(overlay[category], true)
	for entry in [["cards", "Cards"], ["compartments", "Compartments"], ["modifiers", "Card modifiers"]]:
		var id: String = entry[0]
		var control: Button = get_node("Subtabs/" + id.capitalize())
		control.text = context.translator.text(entry[1])
		control.pressed.connect(func(): subtab = id; _render()); _sub_buttons[id] = control
	_body = get_node("Body"); _parking = get_node("Parking")
	var search: LineEdit = get_node("Body/CardBody/Tools/Search")
	search.placeholder_text = context.translator.text("Search cards")
	search.text_changed.connect(func(value): _search = value; _filter_cards())
	var tags: OptionButton = get_node("Body/CardBody/Tools/Tags")
	tags.item_selected.connect(func(index): _tag = str(tags.get_item_metadata(index)); _filter_cards())
	var sort_picker: OptionButton = get_node("Body/CardBody/Tools/Sort")
	for label in ["Name", "Resource cost"]: sort_picker.add_item(context.translator.text(label))
	sort_picker.item_selected.connect(func(index): _order = "cost" if index == 1 else "name"; _render())
	var resources: OptionButton = get_node("Body/CardBody/Tools/Resources")
	resources.item_selected.connect(func(index): _resource = str(resources.get_item_metadata(index)); _filter_cards())
	get_node("Body/CardBody/Targets/Clear").pressed.connect(clear_requested.emit)
	get_node("Body/CardBody/Targets/Clear").text = context.translator.text("Clear distribution")
	get_node("Body/CardBody/Targets/Inspect").text = context.translator.text("Inspect card")
	get_node("Body/CardBody/Targets/Inspect").pressed.connect(func():
		for entry in _card_buttons:
			if str(entry.model.get("cardInstanceId", "")) == selected_id: _inspect(entry.model))
	get_node("Body/CardBody/Hint").text = context.translator.text("Drag a card to a compartment, or select a card and a destination. Changes save automatically.")
	get_node("Body/ModifierInventory").choice_requested.connect(func(reference):
		if not _model.get("busy", false): modifier_requested.emit(reference))

func _button(key: String, callback: Callable) -> Button:
	var button: Button = preload("res://scenes/ui/components/loadout/deck_destination.tscn").instantiate()
	button.text = visual_context.translator.text(key); button.pressed.connect(callback); return button
func present(model: Dictionary) -> void:
	_model = model.duplicate(true)
	var data := model.duplicate(true); data.erase("busy"); data.erase("context")
	var signature := JSON.stringify(data)
	if signature != _signature: _signature = signature; _render()
	for action in _actions: if is_instance_valid(action): action.disabled = model.get("busy", false) or not model.get("canChange", false)
func _render() -> void:
	if not is_instance_valid(_body): return
	for entry in _card_buttons:
		var card = entry.column.get_child(0)
		card.reparent(_parking, false)
	var grid: HFlowContainer = get_node("Body/CardBody/Scroll/Cards")
	for child in grid.get_children(): grid.remove_child(child); child.queue_free()
	var targets: HFlowContainer = get_node("Body/CardBody/Targets")
	for child in targets.get_children():
		if child.has_meta("destination"): targets.remove_child(child); child.queue_free()
	_card_buttons.clear(); _actions.clear()
	var instance_ids: Array = _model.get("cards", []).map(func(fact): return str(fact.get("identity", {}).get("cardInstanceId", "")))
	for id in _card_cache.keys():
		if not id in instance_ids: _card_cache[id].queue_free(); _card_cache.erase(id)
	for id in _sub_buttons: _sub_buttons[id].set_pressed_no_signal(id == subtab)
	_sub_buttons.compartments.visible = _model.get("enabled", false)
	_sub_buttons.modifiers.visible = not _model.get("modifierItems", {}).get("items", []).is_empty()
	if not _sub_buttons.get(subtab, _sub_buttons.cards).visible: subtab = "cards"; _sub_buttons.cards.set_pressed_no_signal(true)
	get_node("Body/ModifierInventory").visible = subtab == "modifiers"
	get_node("Body/CardBody").visible = subtab != "modifiers"
	if subtab == "modifiers":
		get_node("Body/ModifierInventory").configure(_model.get("modifierItems", {}), _model.get("choices", []), _model.get("cards", []), appearance, visual_context)
		return
	var tags: OptionButton = get_node("Body/CardBody/Tools/Tags")
	tags.clear(); tags.add_item(visual_context.translator.text("All tags")); tags.set_item_metadata(0, "")
	var all_tags: Array = []
	for facts in _model.get("cards", []):
		for tag in facts.get("tags", []):
			if not tag in all_tags: all_tags.append(tag)
	all_tags.sort()
	for tag in all_tags:
		tags.add_item(str(tag)); tags.set_item_metadata(tags.item_count - 1, str(tag))
		if str(tag) == _tag: tags.select(tags.item_count - 1)
	var presenter = preload("res://scripts/presentation/card_facts_presenter.gd").new(visual_context.translator, appearance)
	var facts: Array = _model.get("cards", []).duplicate(true)
	if subtab == "compartments":
		_actions.append(targets.get_node("Clear"))
		var destinations: Array = [{"compartmentId": "", "nameKey": "Neutral", "descriptionKey": "No compartment contribution"}]
		destinations.append_array(_model.get("profile", {}).get("compartments", []))
		for destination in destinations:
			var id := str(destination.compartmentId)
			var control := _button(str(destination.get("nameKey", id)), func():
				if not selected_id.is_empty(): move_requested.emit(selected_id, id))
			control.tooltip_text = visual_context.translator.text(str(destination.get("descriptionKey", "")))
			control.set_drag_forwarding(Callable(), func(_at, payload): return _accepts(payload), func(_at, payload): move_requested.emit(str(payload.cardInstanceId), id))
			targets.add_child(control); targets.move_child(control, _actions.size() - 1); _actions.append(control)
	targets.visible = subtab == "compartments"
	get_node("Body/CardBody/Hint").visible = subtab == "compartments"
	get_node("Body/CardBody/Summary").text = visual_context.translator.text("Current deck: %s cards") % facts.size()
	var costs := {}
	for fact in facts:
		for cost in presenter.project(fact).get("costs", []):
			var key := str(cost.get("resourceId", ""))
			if not key.is_empty(): costs[key] = true
	var resource_picker: OptionButton = get_node("Body/CardBody/Tools/Resources")
	resource_picker.clear(); resource_picker.add_item(visual_context.translator.text("All resources")); resource_picker.set_item_metadata(0, "")
	for key in costs:
		resource_picker.add_item(str(key)); resource_picker.set_item_metadata(resource_picker.item_count - 1, str(key))
		if str(key) == _resource: resource_picker.select(resource_picker.item_count - 1)
	# Sort costs within resource identifiers; unrelated resources are never added together.
	facts.sort_custom(func(a, b):
		var first: Dictionary = presenter.project(a); var second: Dictionary = presenter.project(b)
		return _cost_key(first) < _cost_key(second) if _order == "cost" else str(first.name).naturalnocasecmp_to(str(second.name)) < 0)
	for fact in facts:
		var model: Dictionary = presenter.project(fact)
		var id := str(fact.get("identity", {}).get("cardInstanceId", ""))
		var column = preload("res://scenes/ui/components/loadout/deck_card_slot.tscn").instantiate(); grid.add_child(column)
		var card = _card_cache.get(id)
		if not is_instance_valid(card):
			card = preload("res://scripts/ui/card_view.gd").create(visual_context); card.animate_entry = false; _card_cache[id] = card
		card.configure(model); card.select_card(subtab == "compartments" and selected_id == id)
		if card.get_parent() == null: column.add_child(card)
		else: card.reparent(column, false)
		column.move_child(card, 0)
		for connection in card.get_signal_connection_list("pressed"): card.pressed.disconnect(connection.callable)
		card.pressed.connect(func():
			selected_id = id
			if subtab == "compartments":
				for entry in _card_buttons:
					var button = entry.column.get_child(0)
					button.select_card(str(entry.model.get("cardInstanceId", "")) == selected_id)
			else: _inspect(model))
		if subtab == "compartments":
			card.set_drag_forwarding(func(_at):
				if _model.get("busy", false) or not _model.get("canChange", false): return null
				return {"kind": "loadout_compartment", "context": _model.get("context", []), "cardInstanceId": id}, Callable(), Callable())
			var allocation := str(_model.get("allocations", {}).get(id, ""))
			column.get_node("Allocation").text = visual_context.translator.text(allocation if not allocation.is_empty() else "Neutral"); column.get_node("Allocation").show()
		_card_buttons.append({"column": column, "model": model, "facts": fact})
	_filter_cards()
func _filter_cards() -> void:
	for entry in _card_buttons:
		var searchable: String = str(entry.model.name) + " " + str(entry.model.get("summary", "")) + " " + " ".join(entry.facts.get("tags", []))
		entry.column.visible = (_search.is_empty() or searchable.to_lower().contains(_search.to_lower())) and (_tag.is_empty() or _tag in entry.facts.get("tags", [])) and (_resource.is_empty() or entry.model.get("costs", []).any(func(cost): return str(cost.get("resourceId", "")) == _resource))
func _cost_key(model: Dictionary) -> String:
	var tokens: Array = model.get("costs", []).duplicate(true)
	tokens.sort_custom(func(a, b): return str(a.get("resourceId", "")) < str(b.get("resourceId", "")))
	var key := ""
	for cost in tokens: key += "%s:%020.4f;" % [cost.get("resourceId", ""), float(cost.get("amount", 0))]
	return key + str(model.name)
func _accepts(payload) -> bool:
	return not _model.get("busy", false) and _model.get("canChange", false) and payload is Dictionary and payload.get("kind") == "loadout_compartment" and payload.get("context") == _model.get("context")
func _inspect(model: Dictionary) -> void:
	var dialog = preload("res://scripts/ui/inspection_dialog.gd").create(visual_context)
	dialog.size = Vector2i(500, 540)
	dialog.setup(visual_context.translator.text("Card details"), "", {}, model, visual_context.card_style)
	add_child(dialog); dialog.popup_centered()
