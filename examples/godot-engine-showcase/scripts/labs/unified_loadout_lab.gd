extends Control
## Isolated presentation fixture. No session, API, gameplay write or preference change.
signal back_requested
var shell
var draft = preload("res://scripts/labs/inscription_loadout_draft.gd").new()
var inscriptions_view
func _ready() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").from_nodes(Preferences, I18n).derive(preload("res://data/themes/ivory_theme.tres"))
	shell = preload("res://scripts/ui/loadout/loadout_page_view.gd").new(); add_child(shell); shell.mount(context)
	shell.heading.text = I18n.text("Visual laboratory — no run changes")
	shell.back_requested.connect(back_requested.emit)
	shell.tab_requested.connect(shell.select)
	var fixture = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/inscription_loadout_lab.json"))
	if not fixture is Dictionary or not draft.configure(fixture).is_empty(): return
	inscriptions_view = preload("res://scripts/ui/loadout/loadout_view.gd").new()
	inscriptions_view.editorial_layout = true; inscriptions_view.shared_attributes = true; inscriptions_view.mount(context)
	var scroll := ScrollContainer.new(); scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	inscriptions_view.size_flags_horizontal = Control.SIZE_EXPAND_FILL; scroll.add_child(inscriptions_view); shell.install("inscriptions", scroll)
	inscriptions_view.item_requested.connect(func(id): draft.select_item(id); _refresh())
	inscriptions_view.slot_requested.connect(func(id): draft.select_slot(id); draft.stage_selected(); _refresh())
	inscriptions_view.drop_requested.connect(func(item, slot, source): draft.stage_drop(item, slot, source); _refresh())
	inscriptions_view.remove_requested.connect(func(): draft.remove_selected(); _refresh())
	inscriptions_view.equip_requested.connect(func(): draft.stage_selected(); _refresh())
	inscriptions_view.clear_requested.connect(func(): draft.clear(); _refresh())
	inscriptions_view.search_requested.connect(func(value): draft.search = value; _refresh())
	var deck_scroll := ScrollContainer.new(); deck_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	var cards := HFlowContainer.new(); cards.size_flags_horizontal = Control.SIZE_EXPAND_FILL; deck_scroll.add_child(cards)
	var hand_fixture = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_hand_lab.json"))
	for id in ["strike", "ward", "fireball", "heal", "arcane"]:
		var model: Dictionary = hand_fixture.cards[id].duplicate(true)
		if I18n.locale == "pt_BR" and model.get("pt_BR") is Dictionary: model.merge(model.pt_BR, true)
		model.erase("availability")
		var card = preload("res://scripts/ui/card_view.gd").new(context); card.animate_entry = false; card.configure(model); cards.add_child(card)
	shell.install("deck", deck_scroll)
	for id in ["overview", "relics"]:
		var summary = preload("res://scripts/ui/loadout/loadout_summary_view.gd").new(); summary.mount(context); summary.relics_only = id == "relics"
		summary.module_requested.connect(shell.select); summary.present({"equipped": 3, "cards": 5, "relics": []}); shell.install(id, summary)
	var planned = preload("res://scripts/ui/loadout/planned_companions_view.gd").new(); planned.mount(context); shell.install("companions", planned)
	_refresh()
	shell.set_feedback("Visual laboratory — no run changes"); shell.select("overview")
func _refresh() -> void:
	var model: Dictionary = draft.snapshot(); inscriptions_view.present(model); shell.present_attributes(model.character)

