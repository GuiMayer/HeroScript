extends Control
## Run-only composition root. Modules stay alive while tabs change, including pending saves.
signal back_requested
var session
var controller
var shell
var inscriptions_view
var _pending_item := ""
var _clear_dialog: ConfirmationDialog
var initial_tab := "overview"

func _ready() -> void:
	if session == null: session = GameSession
	controller = preload("res://scripts/application/unified_loadout_controller.gd").new(session)
	var context = preload("res://scripts/ui/visual_context.gd").from_nodes(Preferences, I18n).derive(preload("res://data/themes/ivory_theme.tres"))
	shell = preload("res://scripts/ui/loadout/loadout_page_view.gd").new(); add_child(shell); shell.mount(context)
	shell.heading.text = str(session.run.get("playerEntityId", ""))
	shell.tab_requested.connect(_select_tab); shell.back_requested.connect(_back); shell.recovery_requested.connect(controller.recover)
	inscriptions_view = preload("res://scripts/ui/loadout/loadout_view.gd").new()
	inscriptions_view.editorial_layout = true; inscriptions_view.shared_attributes = true; inscriptions_view.mount(context)
	var scroll := ScrollContainer.new(); scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED; scroll.follow_focus = true
	inscriptions_view.size_flags_horizontal = Control.SIZE_EXPAND_FILL; scroll.add_child(inscriptions_view)
	shell.install("inscriptions", scroll)
	inscriptions_view.item_requested.connect(_select_item)
	inscriptions_view.slot_requested.connect(_select_slot)
	inscriptions_view.drop_requested.connect(func(item, slot, origin): _pending_item = ""; controller.edit_inscription("stage_drop", [item, slot, origin]))
	inscriptions_view.equip_requested.connect(func(): controller.edit_inscription("stage_selected"))
	inscriptions_view.remove_requested.connect(func(): controller.edit_inscription("remove_selected"))
	inscriptions_view.clear_requested.connect(func(): _clear_dialog.popup_centered())
	inscriptions_view.search_requested.connect(func(value):
		if controller.inscriptions != null: controller.inscriptions.search = value; _render())
	_clear_dialog = ConfirmationDialog.new(); _clear_dialog.dialog_text = I18n.text("Unequip all inscriptions? Changes are saved automatically.")
	_clear_dialog.confirmed.connect(func(): controller.edit_inscription("clear")); add_child(_clear_dialog)
	for entry in preload("res://scripts/ui/loadout/module_catalog.gd").entries():
		if entry.id == "inscriptions": continue
		var page := VBoxContainer.new(); page.add_child(context.styles.title(I18n.text(entry.label), 24)); shell.install(entry.id, page)
	controller.changed.connect(_render)
	_select_tab(initial_tab); _render(); controller.refresh()
func _select_tab(id: String) -> void:
	shell.select(id); _render()
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
	if not usable: shell.present_attributes({"name": "", "attributes": []})
	shell.set_feedback(controller.message if usable else "Run or character changed. Reopen loadout.", controller.uncertain)
	inscriptions_view.visible = controller.inscriptions != null and shell.active_tab == "inscriptions" and usable
	if controller.inscriptions != null and usable:
		var model: Dictionary = controller.inscriptions.snapshot(); model.liveEquipment = true
		model.canEdit = controller.editable() and model.canEdit
		inscriptions_view.present(model)
		inscriptions_view.find_child("LoadoutClearButton", true, false).text = I18n.text("Unequip all")
		shell.present_attributes(model.character)
func _back() -> void:
	if controller.can_leave(): back_requested.emit()
	else: shell.set_feedback("Wait for equipment to finish saving, or recover the pending operation.", controller.uncertain)
func _exit_tree() -> void:
	if controller != null: controller.dispose()

