extends HBoxContainer
## Passive collection: item → eligible cards. Engine supplies every compatibility verdict.
signal choice_requested(reference_id: String)
var _context
var _snapshot: Dictionary
var _choices: Array
var _facts: Array
var _appearance: Dictionary
var _targets: VBoxContainer
func configure(snapshot: Dictionary, choices: Array, facts: Array, appearance: Dictionary, context) -> void:
	_context = context; _snapshot = snapshot.duplicate(true); _choices = choices.duplicate(true)
	_facts = facts.duplicate(true); _appearance = appearance.duplicate(true)
	var items := VBoxContainer.new()
	var item_scroll := ScrollContainer.new()
	item_scroll.custom_minimum_size.x = 230
	item_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	item_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(item_scroll)
	item_scroll.add_child(items)
	items.add_child(context.styles.title(context.translator.text("Modifier items"), 20))
	for item in _snapshot.get("items", []):
		var definition: Dictionary = item.get("definition", {})
		var presentation: Dictionary = definition.get("presentation", {})
		var id := str(definition.get("itemDefinitionId", ""))
		var art := preload("res://scripts/ui/art_slot.gd").new(context)
		art.setup("modifier_items", str(presentation.get("artId", id)))
		art.custom_minimum_size = Vector2(180, 64)
		items.add_child(art)
		var button: Button = context.styles.button(context.translator.content_name(id, str(presentation.get("name", id))))
		button.disabled = str(item.get("instance", {}).get("state", "")) != "Available"
		button.tooltip_text = context.translator.text(str(presentation.get("description", "Select an item, then a card.")))
		button.pressed.connect(func(): _select(item))
		items.add_child(button)
	if _snapshot.get("items", []).is_empty(): items.add_child(context.styles.muted(context.translator.text("No modifier items owned.")))
	var scroll := ScrollContainer.new()
	scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)
	_targets = VBoxContainer.new()
	_targets.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(_targets)
	_targets.add_child(context.styles.muted(context.translator.text("Select an item, then a card.")))
func _select(item: Dictionary) -> void:
	for child in _targets.get_children(): _targets.remove_child(child); child.queue_free()
	var row := HFlowContainer.new()
	_targets.add_child(row)
	for compatibility in item.get("cards", []):
		var id := str(compatibility.get("cardInstanceId", ""))
		var facts: Array = _facts.filter(func(value): return str(value.get("identity", {}).get("cardInstanceId", "")) == id)
		if facts.is_empty(): continue
		var column := VBoxContainer.new()
		var card := CardView.new(_context)
		card.animate_entry = false
		card.configure(preload("res://scripts/presentation/card_facts_presenter.gd").new(_context.translator, _appearance).project(facts[0]))
		card.disabled = not compatibility.get("compatible", false)
		var options: Array = _choices.filter(func(choice): return str(choice.get("type", "")) == "APPLY_CARD_MODIFIER_ITEM" and str(choice.get("payload", {}).get("itemInstanceId", "")) == str(item.get("instance", {}).get("itemInstanceId", "")) and str(choice.get("payload", {}).get("cardInstanceId", "")) == id)
		if options.size() == 1:
			var reference: String = preload("res://scripts/presentation/activity_presenter.gd").option_reference(options[0])
			card.pressed.connect(func(): choice_requested.emit(reference))
		else: card.disabled = true
		column.add_child(card)
		if compatibility.get("reason") != null: column.add_child(_context.styles.caption(_context.translator.text(str(compatibility.reason)), 13, 200))
		row.add_child(column)
