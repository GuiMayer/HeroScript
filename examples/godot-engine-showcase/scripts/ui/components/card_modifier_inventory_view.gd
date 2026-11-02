@tool
class_name CardModifierInventoryView
extends HBoxContainer
## Passive collection: item → eligible cards. Engine supplies every compatibility verdict.
signal choice_requested(reference_id: String)
var _context
var _snapshot: Dictionary
var _choices: Array
var _facts: Array
var _appearance: Dictionary
var _targets: VBoxContainer
var _items := {}
var _selected_id := ""
static func create() -> CardModifierInventoryView:
	return (load("res://scenes/ui/components/card_modifier_inventory.tscn") as PackedScene).instantiate()
func _ready() -> void:
	if _context == null: theme = preload("res://scripts/ui/visual_context.gd").new().theme
func configure(snapshot: Dictionary, choices: Array, facts: Array, appearance: Dictionary, context) -> void:
	_context = context; theme = context.theme
	_snapshot = snapshot.duplicate(true); _choices = choices.duplicate(true)
	_facts = facts.duplicate(true); _appearance = appearance.duplicate(true)
	get_node("ItemScroll/Items/Title").text = context.translator.text("Modifier items")
	get_node("ItemScroll/Items/Empty").text = context.translator.text("No modifier items owned.")
	get_node("ItemScroll/Items/Empty").visible = _snapshot.get("items", []).is_empty()
	var items = get_node("ItemScroll/Items/Entries")
	var current_ids: Array = _snapshot.get("items", []).map(func(item): return _item_id(item))
	for id in _items.keys():
		if id not in current_ids:
			var removed = _items[id]; items.remove_child(removed); removed.queue_free(); _items.erase(id)
	var index := 0
	for item in _snapshot.get("items", []):
		var definition: Dictionary = item.get("definition", {})
		var presentation: Dictionary = definition.get("presentation", {})
		var id := str(definition.get("itemDefinitionId", ""))
		var instance_id := _item_id(item)
		var row = _items.get(instance_id)
		if not is_instance_valid(row):
			row = preload("res://scenes/ui/components/card_modifier_item.tscn").instantiate()
			var retained_row = row
			row.get_node("Select").pressed.connect(func(): _select(retained_row.get_meta("item")))
			items.add_child(row); _items[instance_id] = row
		row.set_meta("item", item.duplicate(true)); items.move_child(row, index); index += 1
		var art = row.get_node("Art"); art.visual_context = context
		art.setup("modifier_items", str(presentation.get("artId", id)))
		var button: Button = row.get_node("Select")
		button.text = context.translator.content_name(id, str(presentation.get("name", id)))
		button.disabled = str(item.get("instance", {}).get("state", "")) != "Available"
		button.tooltip_text = preload("res://scripts/presentation/activity_presenter.gd").modifier_description(definition, context.translator)
		row.get_node("Description").text = button.tooltip_text
	_targets = get_node("TargetScroll/Targets")
	_targets.get_node("Hint").text = context.translator.text("Select an item, then a card.")
	_targets.get_node("Hint").show(); _targets.get_node("Selection").hide()
	_clear_targets()
	if _items.has(_selected_id): _select(_items[_selected_id].get_meta("item"))
func _item_id(item: Dictionary) -> String:
	return str(item.get("instance", {}).get("itemInstanceId", item.get("definition", {}).get("itemDefinitionId", "")))
func _clear_targets() -> void:
	for child in _targets.get_node("Selection/Cards").get_children(): child.get_parent().remove_child(child); child.queue_free()
func _select(item: Dictionary) -> void:
	_selected_id = _item_id(item)
	_clear_targets()
	_targets.get_node("Hint").hide(); _targets.get_node("Selection").show()
	var presentation: Dictionary = item.get("definition", {}).get("presentation", {})
	_targets.get_node("Selection/Title").text = _context.translator.text(str(presentation.get("name", "Modifier items")))
	_targets.get_node("Selection/Description").text = preload("res://scripts/presentation/activity_presenter.gd").modifier_description(item.get("definition", {}), _context.translator)
	for compatibility in item.get("cards", []):
		var id := str(compatibility.get("cardInstanceId", ""))
		var facts: Array = _facts.filter(func(value): return str(value.get("identity", {}).get("cardInstanceId", "")) == id)
		if facts.is_empty(): continue
		var column = preload("res://scenes/ui/components/card_modifier_target.tscn").instantiate()
		var card := CardView.create(_context); card.animate_entry = false
		card.configure(preload("res://scripts/presentation/card_facts_presenter.gd").new(_context.translator, _appearance).project(facts[0]))
		card.disabled = not compatibility.get("compatible", false)
		var options: Array = _choices.filter(func(choice): return str(choice.get("type", "")) == "APPLY_CARD_MODIFIER_ITEM" and str(choice.get("payload", {}).get("itemInstanceId", "")) == str(item.get("instance", {}).get("itemInstanceId", "")) and str(choice.get("payload", {}).get("cardInstanceId", "")) == id)
		if options.size() == 1:
			var reference: String = preload("res://scripts/presentation/activity_presenter.gd").option_reference(options[0])
			card.pressed.connect(func(): choice_requested.emit(reference))
		else: card.disabled = true
		column.add_child(card); column.move_child(card, 0)
		if compatibility.get("reason") != null:
			column.get_node("Reason").text = _context.translator.text(str(compatibility.reason)); column.get_node("Reason").show()
		_targets.get_node("Selection/Cards").add_child(column)
