@tool
extends HBoxContainer
## All card slots and controls are authored in the scene; pages support any deck size.
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
var _item_index := -1
var _page := 0
var _rows: Array = []
var _generation := 0
const RuneDrag = preload("res://scripts/ui/components/rune_drag.gd")

func configure(snapshot: Dictionary, choices: Array, facts: Array, appearance: Dictionary, context, editable := true) -> void:
	_context = context; theme = context.theme; _editable = editable
	_generation += 1
	_snapshot = snapshot.duplicate(true); _choices = choices.duplicate(true)
	_facts = facts.duplicate(true); _appearance = appearance.duplicate(true)
	if not _bound:
		get_node("ItemsColumn/Items").item_selected.connect(_select_item)
		get_node("ItemsColumn/Items").set_drag_forwarding(_start_drag, Callable(), Callable())
		get_node("CardsColumn/Preview").pressed.connect(func():
			if not _reference.is_empty(): choice_requested.emit(_reference))
		get_node("CardsColumn/Paging/Previous").pressed.connect(func(): _page -= 1; _reference = ""; _render_cards())
		get_node("CardsColumn/Paging/Next").pressed.connect(func(): _page += 1; _reference = ""; _render_cards())
		for i in 8:
			var slot_index := i
			var target: CardView = get_node("CardsColumn/Cards/Slot%d/Card" % (i + 1))
			target.pressed.connect(func(): _select_card(_page * 8 + slot_index))
			target.set_drag_forwarding(Callable(), func(_at, data): return _accept_drag(_page * 8 + slot_index, data), func(_at, data): _drop_rune(_page * 8 + slot_index, data))
		_bound = true
	get_node("ItemsColumn/Title").text = context.translator.text("RUNES")
	get_node("CardsColumn/Title").text = context.translator.text("Your cards")
	get_node("CardsColumn/Preview").text = context.translator.text("Compare and apply")
	get_node("CardsColumn/Paging/Previous").text = context.translator.text("Previous")
	get_node("CardsColumn/Paging/Next").text = context.translator.text("Next")
	get_node("ItemsColumn/Description").text = context.translator.text("Drag a rune onto a card.") if not snapshot.get("items", []).is_empty() else context.translator.text("No modifier items owned.")
	var list: ItemList = get_node("ItemsColumn/Items"); list.clear()
	for item in _snapshot.get("items", []):
		var definition: Dictionary = item.get("definition", {})
		var id := str(definition.get("itemDefinitionId", ""))
		var label: String = context.translator.content_name(id, str(definition.get("presentation", {}).get("name", id)))
		var state := str(item.get("instance", {}).get("state", ""))
		if state != "Available": label += " · " + context.translator.text("Applied" if state == "Bound" else "Unavailable")
		var index := list.add_item(label, load("res://assets/ui/rune_token.svg") if ResourceLoader.exists("res://assets/ui/rune_token.svg") else null)
		list.set_item_tooltip(index, preload("res://scripts/presentation/activity_presenter.gd").modifier_description(definition, context.translator))
	_item_index = -1; _page = 0; _reference = ""; selected_pattern_name = ""; selected_card_name = ""
	_rebuild_cards()

func _start_drag(at: Vector2) -> Variant:
	var list: ItemList = get_node("ItemsColumn/Items")
	var index := list.get_item_at_position(at, true)
	if not _editable or index < 0 or index >= _snapshot.get("items", []).size(): return null
	var item: Dictionary = _snapshot.items[index]
	if str(item.get("instance", {}).get("state", "")) != "Available": return null
	var old_page := _page
	list.select(index); _select_item(index); _page = old_page; _render_cards()
	list.set_drag_preview(RuneDrag.preview(selected_pattern_name, theme))
	return RuneDrag.payload(get_instance_id(), _generation, _snapshot, item)

func _accept_drag(index: int, data: Variant) -> bool:
	return _editable and _item_index >= 0 and _item_index < _snapshot.get("items", []).size() and index >= 0 and index < _rows.size() and _rows[index].valid and RuneDrag.matches(data, get_instance_id(), _generation, _snapshot, _snapshot.items[_item_index])

func _drop_rune(index: int, data: Variant) -> void:
	if not _accept_drag(index, data): return
	_select_card(index)
	if not _reference.is_empty(): choice_requested.emit(_reference)

func _select_item(index: int) -> void:
	if index < 0 or index >= _snapshot.get("items", []).size(): return
	_item_index = index; _page = 0; _reference = ""; selected_card_name = ""
	var item: Dictionary = _snapshot.items[index]
	var definition: Dictionary = item.get("definition", {})
	var definition_id := str(definition.get("itemDefinitionId", ""))
	selected_pattern_name = _context.translator.content_name(definition_id, str(definition.get("presentation", {}).get("name", definition_id)))
	get_node("ItemsColumn/Description").text = preload("res://scripts/presentation/activity_presenter.gd").modifier_description(definition, _context.translator)
	_rebuild_cards()

func _rebuild_cards() -> void:
	_rows.clear()
	var item: Dictionary = _snapshot.items[_item_index] if _item_index >= 0 else {}
	var facts_presenter = preload("res://scripts/presentation/card_facts_presenter.gd").new(_context.translator, _appearance)
	for fact in _facts:
		var id := str(fact.get("identity", {}).get("cardInstanceId", ""))
		if id.is_empty(): continue
		var matches: Array = item.get("cards", []).filter(func(value): return str(value.get("cardInstanceId", "")) == id)
		var compatibility: Dictionary = matches[0] if matches.size() == 1 else {}
		var options: Array = _choices.filter(func(choice): return str(choice.get("type", "")) == "APPLY_CARD_MODIFIER_ITEM" and str(choice.get("payload", {}).get("itemInstanceId", "")) == str(item.get("instance", {}).get("itemInstanceId", "")) and str(choice.get("payload", {}).get("cardInstanceId", "")) == id)
		var valid := _item_index >= 0 and str(item.get("instance", {}).get("state", "")) == "Available" and bool(compatibility.get("compatible", false)) and options.size() == 1
		var reason := str(compatibility.get("reason", ""))
		if reason.is_empty() and not valid: reason = "Card details or application are unavailable."
		_rows.append({"model": facts_presenter.project(fact), "valid": valid, "reason": reason, "reference": preload("res://scripts/presentation/activity_presenter.gd").option_reference(options[0]) if valid else ""})
	_render_cards()

func _render_cards() -> void:
	_page = clampi(_page, 0, maxi(0, ceili(float(_rows.size()) / 8.0) - 1))
	get_node("CardsColumn/Hint").text = _context.translator.text("Drag a rune onto a card.") if _item_index < 0 else _context.translator.text("Drop on a compatible card to review the change.")
	get_node("CardsColumn/Preview").disabled = _reference.is_empty() or not _editable
	for i in 8:
		var slot = get_node("CardsColumn/Cards/Slot%d" % (i + 1))
		var index := _page * 8 + i
		slot.visible = index < _rows.size()
		if not slot.visible: continue
		var row: Dictionary = _rows[index]
		var card = slot.get_node("Card"); card.visual_context = _context; card.configure(row.model)
		RuneDrag.highlight(card, _item_index >= 0 and row.valid and _editable)
		card.modulate = Color(0.55, 0.55, 0.55, 1) if _item_index >= 0 and not row.valid else Color.WHITE
		card.tooltip_text = _reason_text(str(row.reason)) if _item_index >= 0 and not row.valid else str(row.model.get("name", ""))
		slot.get_node("Status").text = _context.translator.text("Unavailable — click for reason") if _item_index >= 0 and not row.valid else ""
	var pages := maxi(1, ceili(float(_rows.size()) / 8.0))
	get_node("CardsColumn/Paging/Page").text = "%d / %d" % [_page + 1, pages]
	get_node("CardsColumn/Paging/Previous").disabled = _page == 0
	get_node("CardsColumn/Paging/Next").disabled = _page + 1 >= pages
	get_node("CardsColumn/Paging").visible = pages > 1

func _select_card(index: int) -> void:
	if index < 0 or index >= _rows.size(): return
	var entry: Dictionary = _rows[index]
	if _item_index < 0 or not entry.valid:
		_reference = ""; get_node("CardsColumn/Preview").disabled = true
		var popup: PopupPanel = get_node("Reason")
		popup.get_node("Message").text = _context.translator.text("Select an item, then a card.") if _item_index < 0 else _reason_text(str(entry.reason))
		var card: Control = get_node("CardsColumn/Cards/Slot%d/Card" % (index % 8 + 1))
		popup.popup(Rect2i(Vector2i(card.get_screen_position()), Vector2i(360, 100)))
		return
	_reference = str(entry.reference); selected_card_name = str(entry.model.get("name", ""))
	_render_cards()
	get_node("CardsColumn/Cards/Slot%d/Status" % (index % 8 + 1)).text = _context.translator.text("Selected")
	get_node("CardsColumn/Hint").text = _context.translator.text("Select Compare and apply to review the change." if _editable else "Synchronize the game before applying a pattern.")
	get_node("CardsColumn/Preview").disabled = not _editable

func _reason_text(reason: String) -> String:
	if reason.begins_with("Upgrade application limit reached:"):
		return _context.translator.text("This card has reached the application limit for this pattern.")
	if reason.begins_with("Upgrade ") and reason.contains(" does not apply to "):
		return _context.translator.text("This pattern cannot be applied to this card.")
	return _context.translator.text(reason)
