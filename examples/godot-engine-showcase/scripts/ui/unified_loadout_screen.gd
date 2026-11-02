extends Control
## Run-only composition root. Modules stay alive while tabs change, including pending saves.
signal back_requested
var session
var controller
var shell
var inscriptions_view
var deck_view
var overview_view
var relics_view
var _pending_item := ""
var _clear_dialog: ConfirmationDialog
var _inscriptions_empty: Label
@export var initial_tab := "overview"
@export var initial_deck_tab := "cards"

func _ready() -> void:
	if session == null: session = GameSession
	controller = preload("res://scripts/application/unified_loadout_controller.gd").new(session)
	var context = preload("res://scripts/ui/visual_context.gd").from_nodes(Preferences, I18n).derive(preload("res://data/themes/ivory_theme.tres"))
	shell = get_node("LoadoutPage"); shell.mount(context)
	var setting_id := str(session.run.get("settingId", session.run.get("configName", "")))
	shell.heading.text = I18n.content_name(str(session.run.get("playerEntityId", ""))) + " · " + I18n.content_name(setting_id, setting_id.capitalize())
	shell.tab_requested.connect(_select_tab); shell.back_requested.connect(_back); shell.recovery_requested.connect(controller.recover)
	shell.attribute_sources_requested.connect(_attribute_sources)
	inscriptions_view = shell.content.get_node("InscriptionsScroll/InscriptionBody/Inscriptions")
	inscriptions_view.editorial_layout = true; inscriptions_view.shared_attributes = true; inscriptions_view.mount(context)
	var scroll: ScrollContainer = shell.content.get_node("InscriptionsScroll")
	var inscription_body: VBoxContainer = scroll.get_node("InscriptionBody")
	_inscriptions_empty = inscription_body.get_node("InscriptionsUnavailable")
	_inscriptions_empty.text = I18n.text("Inscriptions are not available for this character or setting.")
	inscriptions_view.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	shell.install("inscriptions", scroll)
	inscriptions_view.item_requested.connect(_select_item)
	inscriptions_view.slot_requested.connect(_select_slot)
	inscriptions_view.drop_requested.connect(func(item, slot, origin): _pending_item = ""; controller.edit_inscription("stage_drop", [item, slot, origin]))
	inscriptions_view.equip_requested.connect(func(): controller.edit_inscription("stage_selected"))
	inscriptions_view.remove_requested.connect(func(): controller.edit_inscription("remove_selected"))
	inscriptions_view.clear_requested.connect(func(): _clear_dialog.popup_centered())
	inscriptions_view.search_requested.connect(func(value):
		if controller.inscriptions != null: controller.inscriptions.search = value; _render())
	_clear_dialog = get_node("ClearEquipment"); _clear_dialog.dialog_text = I18n.text("Unequip all inscriptions? Changes are saved automatically.")
	_clear_dialog.confirmed.connect(func(): controller.edit_inscription("clear"))
	deck_view = shell.content.get_node("Deck"); deck_view.mount(context); shell.install("deck", deck_view)
	deck_view.move_requested.connect(controller.move_card)
	deck_view.modifier_requested.connect(controller.apply_modifier)
	deck_view.clear_requested.connect(func():
		var confirm := (preload("res://scenes/ui/overlays/confirmation_dialog.tscn").instantiate() as ConfirmationDialog); confirm.dialog_text = I18n.text("Clear all compartment allocations? Changes are saved automatically.")
		confirm.confirmed.connect(func(): controller.move_card("", "", true); confirm.queue_free())
		confirm.canceled.connect(confirm.queue_free); add_child(confirm); confirm.popup_centered())
	overview_view = shell.content.get_node("Overview"); overview_view.mount(context)
	overview_view.module_requested.connect(_select_tab); shell.install("overview", overview_view)
	relics_view = shell.content.get_node("Relics"); relics_view.relics_only = true; relics_view.mount(context); shell.install("relics", relics_view)
	var planned = shell.content.get_node("Companions"); planned.mount(context); shell.install("companions", planned)
	controller.changed.connect(_render)
	_select_tab(initial_tab); _render(); controller.refresh()
func _select_tab(id: String) -> void:
	shell.select(id); _render()
	if id in ["deck", "overview"]: controller.ensure_deck()
func _select_item(id: String) -> void:
	if controller.inscriptions == null: return
	_pending_item = id; controller.inscriptions.select_item(id); _render()
func _select_slot(id: String) -> void:
	if controller.inscriptions == null: return
	controller.inscriptions.select_slot(id)
	if not _pending_item.is_empty() and controller.editable():
		controller.inscriptions.select_item(_pending_item); _pending_item = ""; controller.edit_inscription("stage_selected")
	else: _render()
func _render() -> void:
	if not is_instance_valid(shell): return
	var usable: bool = controller.current()
	if not usable: _present_attributes({"name": "", "attributes": []})
	_inscriptions_empty.visible = usable and controller.inscriptions == null
	for button in shell.buttons.values(): button.disabled = not usable
	shell.set_feedback(controller.message if usable else "Run or character changed. Reopen loadout.", controller.uncertain or controller.read_error)
	inscriptions_view.visible = controller.inscriptions != null and shell.active_tab == "inscriptions" and usable
	if controller.inscriptions != null and usable:
		var model: Dictionary = controller.inscriptions.snapshot(); model.liveEquipment = true
		model.canEdit = controller.editable() and model.canEdit
		inscriptions_view.present(model)
		inscriptions_view.find_child("LoadoutClearButton", true, false).text = I18n.text("Unequip all")
		_present_attributes(model.character)
	if is_instance_valid(deck_view):
		deck_view.visible = usable and shell.active_tab == "deck"
		var snapshot: Dictionary = controller.compartments.source if controller.compartments != null else {}
		if initial_deck_tab == "compartments" and snapshot.get("enabled", false) or initial_deck_tab == "modifiers" and not controller.modifier_items.get("items", []).is_empty():
			deck_view.subtab = initial_deck_tab; initial_deck_tab = "cards"
		var deck_model := {"cards": controller.deck_cards, "modifierItems": controller.modifier_items, "choices": controller.choices,
			"profile": snapshot.get("profile", {}), "allocations": controller.compartments.draft if controller.compartments != null else {},
			"enabled": snapshot.get("enabled", false), "canChange": snapshot.get("canChange", false),
			"busy": not controller.editable(), "context": controller.identity + controller.version()}
		deck_view.present(deck_model)
	if controller.info_ready and usable:
		_present_attributes(preload("res://scripts/presentation/loadout_presenter.gd").character(controller.attributes, str(session.run.get("playerEntityId", "")), session.run.get("characterResources", {}), I18n))
	elif controller.inscriptions == null: _present_attributes({"name": "", "attributes": []})
	var summary := {"equipped": controller.inscriptions.assignments.size() if controller.inscriptions != null and usable else 0, "cards": controller.deck_cards.size() if usable else 0, "relics": controller.relics if usable else []}
	if is_instance_valid(overview_view): overview_view.present(summary)
	if is_instance_valid(relics_view): relics_view.present(summary)
func _back() -> void:
	if controller.can_leave(): back_requested.emit()
	else: shell.set_feedback("Wait for equipment to finish saving, or recover the pending operation.", controller.uncertain)

func _present_attributes(character: Dictionary) -> void:
	var model := character.duplicate(true)
	if controller.inscriptions != null:
		var snapshot: Dictionary = controller.inscriptions.snapshot()
		for item in snapshot.get("items", []):
			if str(item.get("instanceId", "")) == str(snapshot.get("selectedItem", "")):
				model["highlightedAttributeIds"] = item.get("attributeIds", item.get("attributeBonuses", []).map(func(bonus): return str(bonus.get("attributeId", ""))))
	shell.present_attributes(model)
func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("pause_game") or event.is_action_pressed("ui_cancel"):
		_back(); get_viewport().set_input_as_handled()
func _attribute_sources() -> void:
	var dialog = preload("res://scenes/ui/overlays/attribute_sources.tscn").instantiate()
	dialog.title = I18n.text("Attribute sources"); dialog.theme = shell.visual_context.theme
	var list = dialog.get_node("Scroll/Body")
	for stat in controller.attributes.get("attributes", []):
		var group = preload("res://scenes/ui/components/loadout/attribute_source_group.tscn").instantiate()
		group.get_node("Title").text = I18n.text(str(stat.displayName))
		group.get_node("Title").add_theme_color_override("font_color", shell.visual_context.styles.INK)
		group.get_node("Values").text = I18n.text("Base: %s → Current: %s") % [I18n.number(stat.baseValue), I18n.number(stat.effectiveValue)]
		for trace in stat.get("contributions", []):
			var line = preload("res://scenes/ui/components/loadout/attribute_source_trace.tscn").instantiate()
			line.text = I18n.text(str(trace.get("sourceKind", "Source"))) + " · " + I18n.text("Input: %s → Output: %s") % [I18n.number(trace.get("input", 0)), I18n.number(trace.get("output", 0))] + (" · " + I18n.text("Not applied") if not trace.get("applied", true) else "")
			group.get_node("Contributions").add_child(line)
		group.get_node("Contributions").visible = group.get_node("Contributions").get_child_count() > 0
		list.add_child(group)
	add_child(dialog); dialog.confirmed.connect(dialog.queue_free); dialog.canceled.connect(dialog.queue_free); dialog.popup_centered()
func _exit_tree() -> void:
	if controller != null: controller.dispose()

