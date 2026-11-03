@tool
extends VBoxContainer
## Passive view: an entire replacement/module gesture is sent to the application controller.
signal edit_requested(intent: Dictionary)
var visual_context
var appearance := {}
var _model := {}
var _cards := {}
var _facts := {}
var _group := ""
var _outgoing := ""
var _signature := ""
var _bound := false

func mount(context) -> void:
	visual_context = context; theme = context.theme
	if _bound: return
	_bound = true
	get_node("Tools/Groups").item_selected.connect(func(index):
		_group = str(get_node("Tools/Groups").get_item_metadata(index)); _outgoing = ""; _signature = ""; present(_model))
	get_node("Tools/Modules").item_selected.connect(func(index):
		if _editable(): edit_requested.emit({"groupId": _group, "moduleId": str(get_node("Tools/Modules").get_item_metadata(index))}))
	get_node("Hint").text = context.translator.text("Choose an active card, then a reserve card to replace it. Changes save automatically.")
	get_node("Columns/Active/Title").text = context.translator.text("Active deck")
	get_node("Columns/Reserve/Title").text = context.translator.text("Reserve")

func _editable() -> bool: return _model.get("canChange", false) and not _model.get("busy", false)
func present(model: Dictionary) -> void:
	_model = model.duplicate(true)
	if not _bound: return
	var data := model.duplicate(true); data.erase("busy")
	var signature := JSON.stringify(data)
	if signature != _signature:
		_signature = signature; _outgoing = ""
		var groups: Array = model.get("policy", {}).get("groups", [])
		var picker: OptionButton = get_node("Tools/Groups"); picker.clear()
		if not groups.any(func(group): return str(group.get("groupId", "")) == _group): _group = str(groups[0].groupId) if not groups.is_empty() else ""
		var current := {}
		for group in groups:
			picker.add_item(visual_context.translator.text(str(group.groupId))); picker.set_item_metadata(picker.item_count - 1, str(group.groupId))
			if str(group.groupId) == _group: picker.select(picker.item_count - 1); current = group
		var modules: OptionButton = get_node("Tools/Modules"); modules.clear()
		var selected: String = str(model.get("selection", {}).get("modules", {}).get(_group, ""))
		for id in current.get("moduleIds", []):
			modules.add_item(visual_context.translator.content_name(str(id))); modules.set_item_metadata(modules.item_count - 1, str(id))
			if str(id) == selected: modules.select(modules.item_count - 1)
		modules.visible = modules.item_count > 0
		var presenter = preload("res://scripts/presentation/card_facts_presenter.gd").new(visual_context.translator, appearance)
		var live := {}
		_facts.clear()
		for fact in model.get("cards", []):
			var id := str(fact.get("cardInstanceId", "")); live[id] = true; _facts[id] = fact
			var card = _cards.get(id)
			if not is_instance_valid(card):
				card = preload("res://scripts/ui/card_view.gd").create(visual_context); card.animate_entry = false; _cards[id] = card
				card.pressed.connect(func(): _choose(id))
				card.set_drag_forwarding(func(_at):
					if not _editable(): return null
					return {"kind": "deck_build", "cardId": id, "context": _model.get("context", [])},
					func(_at, payload): return _accepts(id, payload), func(_at, payload): _replace(id, str(payload.cardId)))
			card.configure(presenter.project(fact.get("presentation", {})))
			var reserve: bool = fact.get("groupId") == null or str(fact.get("groupId", "")).is_empty()
			var target = get_node("Columns/Reserve/Scroll/Cards" if reserve else "Columns/Active/Scroll/Cards")
			if card.get_parent() == null: target.add_child(card)
			elif card.get_parent() != target: card.reparent(target, false)
			card.visible = reserve or str(fact.get("groupId", "")) == _group
		for id in _cards.keys():
			if not live.has(id): _cards[id].queue_free(); _cards.erase(id)
	_update_state()

func _update_state() -> void:
	get_node("Tools/Groups").disabled = _model.get("busy", false)
	get_node("Tools/Modules").disabled = not _editable()
	var reason = _model.get("unavailableReason")
	get_node("Reason").text = visual_context.translator.text(str(reason)) if reason != null else ""
	for id in _cards:
		_cards[id].disabled = not _editable(); _cards[id].select_card(id == _outgoing)

func _choose(id: String) -> void:
	if not _editable() or not _facts.has(id): return
	var fact: Dictionary = _facts[id]
	if fact.get("groupId") != null and not str(fact.groupId).is_empty():
		_outgoing = id; _update_state()
	elif not _outgoing.is_empty(): _replace(_outgoing, id)

func _accepts(id: String, payload) -> bool:
	return _editable() and payload is Dictionary and payload.get("kind") == "deck_build" and payload.get("context") == _model.get("context") and _facts.has(id) and _facts.has(str(payload.get("cardId", ""))) and str(_facts[id].get("groupId", "")) == _group and _facts[str(payload.cardId)].get("groupId") == null
func _replace(outgoing: String, incoming: String) -> void:
	if not _editable(): return
	_outgoing = ""; _update_state()
	edit_requested.emit({"groupId": _group, "replacedCardId": outgoing, "replacementCardId": incoming})
