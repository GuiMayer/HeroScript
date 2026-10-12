extends "res://scripts/ui/screen_view.gd"
## Application adapter. Workflows only react to authoritative changes/intents.
const JourneyView = preload("res://scripts/ui/journey_view.gd")
var view: JourneyView
var controller
var router
var presenter
var presentation: Dictionary
var render_epoch := 0
var _projection_key: Array = []
var _workflow_key: Array = []
func setup(owner, data: Dictionary) -> void:
	router = owner; mount(owner.visual_context)
	controller = preload("res://scripts/ui/controllers/activity_controller.gd").new(GameSession)
	view = JourneyView.new()
	view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	view.intent_requested.connect(_on_intent)
	add_child(view)
	_render(data)
func refresh_state(data: Dictionary) -> void: _render(data)
func _render(data: Dictionary) -> void:
	visual_context.update_from(Preferences, I18n, AppTheme.profile)
	var run: Dictionary = GameSession.run
	var choices: Array = GameSession.activity_choices()
	var zones: Dictionary = GameSession.card_zones
	controller.bind(run, choices)
	presenter = preload("res://scripts/presentation/activity_presenter.gd").new(run, choices, data, I18n)
	var key: Array = [run, choices, zones, data, visual_context.presentation_key()]
	if key != _projection_key:
		_projection_key = key.duplicate(true); render_epoch += 1
		presentation = data.duplicate(true)
		view.apply_projection(run, choices, zones, presentation, presenter, visual_context)
	var workflow_key: Array = [run.get("runId", ""), run.get("branchId", ""), run.get("sequence", 0), choices]
	if workflow_key != _workflow_key:
		_workflow_key = workflow_key.duplicate(true)
		if choices.is_empty() and str(run.get("lifecycle", "Active")).to_lower() == "active" and not controller.automatic_recovery_attempted:
			controller.automatic_recovery_attempted = true
			_recover_actions.call_deferred()
		_maybe_auto_advance.call_deferred(render_epoch)
func _on_intent(action: String, arguments: Array) -> void:
	GameAudio.ui()
	match action:
		"map": router.show_map()
		"pause": router.toggle_pause()
		"replay": router.show_run_replay(str(arguments[0]))
		"new_journey": router._new_campaign()
		"main_menu": router.back_to_menu()
		"verify": _verify()
		"recover": _recover_actions()
		"confirm": _confirm_ref(str(arguments[0]))
		"execute":
			var choice: Dictionary = controller.choice_for(str(arguments[0]))
			if not choice.is_empty(): _execute(choice)
func apply_model(model) -> void: refresh_state(model)
func apply_visual_profile(_profile) -> void: refresh_state(presentation)
func dispose() -> void:
	if controller: controller.dispose()
func _confirm_choice(choice: Dictionary, model: Dictionary, label: String) -> void:
	if GameSession.busy: return
	var transformation := str(choice.type) in ["UPGRADE_CARD", "REMOVE_CARD_TRANSFORMATION", "REPLACE_CARD_TRANSFORMATION"]
	if not transformation and str(choice.type) not in ["BUY_SHOP_ITEM", "DECOMPOSE_CARD_REWARD", "REROLL_SHOP", "REROLL_CARD_REWARD"]:
		_execute(choice)
		return
	var dialog := ConfirmationDialog.new()
	dialog.theme = visual_context.theme
	dialog.title = I18n.text("Confirm choice")
	dialog.dialog_text = label + ("\n" + str(model.cost) if not str(model.cost).is_empty() else "")
	if transformation:
		var assessment: Dictionary = await controller.preview_transformation(choice)
		if not is_inside_tree():
			dialog.free()
			return
		if assessment.is_empty():
			dialog.free()
			router.show_toast(I18n.text("Detailed inspection is unavailable."))
			return
		dialog.dialog_text += "\n\n" + presenter.transformation_text(assessment)
		dialog.get_ok_button().disabled = not bool(assessment.get("isCompatible", false))
	dialog.ok_button_text = I18n.text("CONFIRM")
	dialog.cancel_button_text = I18n.text("CANCEL")
	dialog.confirmed.connect(func(): _execute(choice))
	dialog.visibility_changed.connect(func(): if not dialog.visible: dialog.queue_free())
	add_child(dialog)
	dialog.popup_centered(Vector2i(440, 180))


func _execute(choice: Dictionary) -> void:
	if GameSession.busy: return
	for button in view.choice_buttons: button.disabled = true
	router.show_toast(I18n.text("PROCESSING…"))
	var accepted: bool = await controller.execute(choice)
	if not is_inside_tree():
		return
	for button in view.choice_buttons: button.disabled = bool(button.get_meta("engine_disabled", false))
	if accepted:
		if str(choice.type) in ["PICK_CARD_REWARD", "BUY_SHOP_ITEM", "APPLY_PREPARATION_OPTION", "UPGRADE_CARD"]:
			GameAudio.reward()
		router.open_game()


func _recover_actions() -> void:
	if controller.recovering or GameSession.busy or not is_inside_tree():
		return
	for button in view.choice_buttons:
		button.disabled = true
	var accepted: bool = await controller.recover()
	if not is_inside_tree():
		return
	if accepted:
		router.open_game()
	else:
		for button in view.choice_buttons:
			if is_instance_valid(button): button.disabled = false


func _maybe_auto_advance(ticket := -1) -> void:
	if router.dialogue_return_barrier: return
	if (ticket >= 0 and ticket != render_epoch) or controller.auto_advancing or not is_inside_tree() or \
		router.current_screen != "activity" or GameSession.busy or get_tree().paused:
		return
	var choice: Dictionary = controller.forced_advance()
	if choice.is_empty(): return
	controller.auto_advancing = true
	for button in view.choice_buttons: button.disabled = true
	router.show_toast(I18n.text("Moving to the next stop…"))
	var accepted: bool = await controller.execute(choice)
	if not is_inside_tree(): return
	controller.auto_advancing = false
	if accepted: router.open_game()
	elif ticket < 0 or ticket == render_epoch:
		for button in view.choice_buttons: button.disabled = false


func _verify() -> void:
	if controller.verifying: return
	router.show_toast(I18n.text("Verifying replay…"))
	var result: Dictionary = await controller.verify()
	if not is_inside_tree():
		return
	if result.ok:
		var valid := bool(result.data.get("isValid", result.data.get("valid", false)))
		router.show_toast(I18n.text("Deterministic replay verified.") if valid else I18n.text("Replay diverged."), not valid)
	else:
		router.show_error(I18n.error(result))


func _confirm_ref(reference_id: String) -> void:
	var choice: Dictionary = controller.choice_for(reference_id)
	if choice.is_empty(): return
	var model: Dictionary = presenter.offer(choice)
	_confirm_choice(choice, model, view._choice_label(choice, model))
