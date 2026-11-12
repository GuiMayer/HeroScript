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
var selected_pattern_name := ""
var selected_card_name := ""
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
		var state := str(item.get("instance", {}).get("state", ""))
		if state != "Available": label += " · " + context.translator.text("Applied" if state == "Bound" else "Unavailable")
		var index := list.add_item(label)
		list.set_item_tooltip(index, preload("res://scripts/presentation/activity_presenter.gd").modifier_description(definition, context.translator))
	_clear_card_selection()
func _clear_card_selection() -> void:
	_reference = ""
	selected_card_name = ""
	get_node("CardsColumn/Cards").clear()
	get_node("CardsColumn/Hint").text = _context.translator.text("Select an item, then a card.")
	get_node("PreviewColumn/CardScroll/Card").hide()
	get_node("PreviewColumn/Preview").disabled = true
func _select_item(index: int) -> void:
	if index < 0 or index >= _snapshot.get("items", []).size(): return
	_clear_card_selection()
	var item: Dictionary = _snapshot.get("items", [])[index]
	var definition: Dictionary = item.get("definition", {})
	var definition_id := str(definition.get("itemDefinitionId", ""))
	selected_pattern_name = _context.translator.content_name(definition_id, str(definition.get("presentation", {}).get("name", definition_id)))
	get_node("ItemsColumn/Description").text = preload("res://scripts/presentation/activity_presenter.gd").modifier_description(item.get("definition", {}), _context.translator)
	var list: ItemList = get_node("CardsColumn/Cards")
	var rows: Array = []
	var totals := {}
	for compatibility in item.get("cards", []):
		var id := str(compatibility.get("cardInstanceId", ""))
		var facts: Array = _facts.filter(func(value): return str(value.get("identity", {}).get("cardInstanceId", "")) == id)
		var options: Array = _choices.filter(func(choice): return str(choice.get("type", "")) == "APPLY_CARD_MODIFIER_ITEM" and str(choice.get("payload", {}).get("itemInstanceId", "")) == str(item.get("instance", {}).get("itemInstanceId", "")) and str(choice.get("payload", {}).get("cardInstanceId", "")) == id)
		var model: Dictionary = preload("res://scripts/presentation/card_facts_presenter.gd").new(_context.translator, _appearance).project(facts[0]) if not facts.is_empty() else {}
		var name: String = str(model.get("name", _context.translator.text("Card details unavailable")))
		var valid := str(item.get("instance", {}).get("state", "")) == "Available" and bool(compatibility.get("compatible", false)) and options.size() == 1 and not facts.is_empty()
		var reason: String = str(compatibility.get("reason", ""))
		if reason.is_empty() and not valid: reason = "Card details or application are unavailable."
		var reference: String = preload("res://scripts/presentation/activity_presenter.gd").option_reference(options[0]) if valid else ""
		rows.append({"name": name, "model": model, "reference": reference, "reason": reason, "valid": valid})
		if not facts.is_empty(): totals[name] = int(totals.get(name, 0)) + 1
	var ordinal := {}
	for row in rows:
		var name: String = row.name
		if int(totals.get(name, 0)) > 1:
			ordinal[name] = int(ordinal.get(name, 0)) + 1
			name += " · " + (_context.translator.text("Copy %s of %s") % [ordinal[name], totals[name]])
		if not row.valid: name += " · " + _context.translator.text("Unavailable")
		elif bool(row.model.get("upgraded", false)): name += " · " + _context.translator.text("Modified")
		var card_index := list.add_item(name)
		list.set_item_tooltip(card_index, _context.translator.text(str(row.reason)))
		list.set_item_metadata(card_index, row)
	get_node("CardsColumn/Hint").text = _context.translator.text("Select a card to preview the change.") if list.item_count > 0 else _context.translator.text("No cards are available for this pattern.")
func _select_card(index: int) -> void:
	if index < 0 or index >= get_node("CardsColumn/Cards").item_count: return
	var entry: Dictionary = get_node("CardsColumn/Cards").get_item_metadata(index)
	_reference = str(entry.get("reference", ""))
	selected_card_name = get_node("CardsColumn/Cards").get_item_text(index)
	var card = get_node("PreviewColumn/CardScroll/Card")
	if entry.model.is_empty(): card.hide()
	else:
		card.visual_context = _context; card.configure(entry.model); card.show()
	get_node("CardsColumn/Hint").text = _context.translator.text(str(entry.reason)) if not bool(entry.valid) else _context.translator.text("Select Compare and apply to review the change." if _editable else "Synchronize the game before applying a pattern.")
	get_node("PreviewColumn/Preview").disabled = _reference.is_empty() or not _editable
