@tool
extends HBoxContainer
## ItemList rows are data; every control and the card preview are authored in the scene.
signal choice_requested(reference: String)
var _context
var _snapshot: Dictionary = {}
var _choices: Array = []
var _facts: Array = []
var _appearance: Dictionary = {}
var _reference := ""
var _bound := false
var _editable := true
func configure(snapshot: Dictionary, choices: Array, facts: Array, appearance: Dictionary, context, editable := true) -> void:
	_context = context; theme = context.theme; _editable = editable
	_snapshot = snapshot.duplicate(true); _choices = choices.duplicate(true)
	_facts = facts.duplicate(true); _appearance = appearance.duplicate(true)
	if not _bound:
		get_node("ItemsColumn/Items").item_selected.connect(_select_item)
		get_node("CardsColumn/Cards").item_selected.connect(_select_card)
		get_node("PreviewColumn/Preview").pressed.connect(func():
			if not _reference.is_empty(): choice_requested.emit(_reference))
		_bound = true
	get_node("ItemsColumn/Title").text = context.translator.text("Modifier items")
	get_node("CardsColumn/Title").text = context.translator.text("Compatible cards")
	get_node("PreviewColumn/Title").text = context.translator.text("Card preview")
	get_node("PreviewColumn/Preview").text = context.translator.text("Compare and apply")
	get_node("ItemsColumn/Description").text = context.translator.text("Select an item, then a card.") if not snapshot.get("items", []).is_empty() else context.translator.text("No modifier items owned.")
	var list: ItemList = get_node("ItemsColumn/Items"); list.clear()
	for item in _snapshot.get("items", []):
		var definition: Dictionary = item.get("definition", {})
		var id := str(definition.get("itemDefinitionId", ""))
		var label: String = context.translator.content_name(id, str(definition.get("presentation", {}).get("name", id)))
		var index := list.add_item(label)
		list.set_item_disabled(index, str(item.get("instance", {}).get("state", "")) != "Available")
		list.set_item_tooltip(index, preload("res://scripts/presentation/activity_presenter.gd").modifier_description(definition, context.translator))
	_clear_card_selection()
func _clear_card_selection() -> void:
	_reference = ""
	get_node("CardsColumn/Cards").clear()
	get_node("CardsColumn/Hint").text = _context.translator.text("Select an item, then a card.")
	get_node("PreviewColumn/CardScroll/Card").hide()
	get_node("PreviewColumn/Preview").disabled = true
func _select_item(index: int) -> void:
	_clear_card_selection()
	var item: Dictionary = _snapshot.get("items", [])[index]
	get_node("ItemsColumn/Description").text = preload("res://scripts/presentation/activity_presenter.gd").modifier_description(item.get("definition", {}), _context.translator)
	var list: ItemList = get_node("CardsColumn/Cards")
	for compatibility in item.get("cards", []):
		var id := str(compatibility.get("cardInstanceId", ""))
		var facts: Array = _facts.filter(func(value): return str(value.get("identity", {}).get("cardInstanceId", "")) == id)
		var options: Array = _choices.filter(func(choice): return str(choice.get("type", "")) == "APPLY_CARD_MODIFIER_ITEM" and str(choice.get("payload", {}).get("itemInstanceId", "")) == str(item.get("instance", {}).get("itemInstanceId", "")) and str(choice.get("payload", {}).get("cardInstanceId", "")) == id)
		var model: Dictionary = preload("res://scripts/presentation/card_facts_presenter.gd").new(_context.translator, _appearance).project(facts[0]) if not facts.is_empty() else {}
		var card_index := list.add_item(str(model.get("name", _context.translator.text("Card details unavailable"))))
		list.set_item_disabled(card_index, not bool(compatibility.get("compatible", false)) or options.size() != 1 or facts.is_empty())
		list.set_item_tooltip(card_index, str(compatibility.get("reason", "")))
		list.set_item_metadata(card_index, {"model": model, "reference": preload("res://scripts/presentation/activity_presenter.gd").option_reference(options[0]) if options.size() == 1 else ""})
	get_node("CardsColumn/Hint").text = _context.translator.text("Select a card to preview the change.") if list.item_count > 0 else _context.translator.text("No cards are available for this pattern.")
func _select_card(index: int) -> void:
	var entry: Dictionary = get_node("CardsColumn/Cards").get_item_metadata(index)
	_reference = str(entry.get("reference", ""))
	var card = get_node("PreviewColumn/CardScroll/Card")
	card.visual_context = _context; card.configure(entry.model); card.show()
	get_node("PreviewColumn/Preview").disabled = _reference.is_empty() or not _editable
