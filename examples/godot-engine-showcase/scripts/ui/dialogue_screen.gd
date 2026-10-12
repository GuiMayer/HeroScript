extends "res://scripts/ui/screen_view.gd"
## Application adapter for the isolated narrative presentation.
var router
var controller
var view
var presentation: Dictionary
var _model: Dictionary = {}
var _start_attempted := false
var _alive := true
func setup(owner, data: Dictionary) -> void:
	router = owner; mount(owner.visual_context); presentation = data.duplicate(true)
	controller = preload("res://scripts/ui/controllers/dialogue_controller.gd").new(GameSession)
	controller.changed.connect(_interaction)
	view = preload("res://scenes/ui/dialogue_scene.tscn").instantiate()
	view.intent_requested.connect(_intent)
	view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(view)
	refresh_state(data)
	_start_if_needed.call_deferred()
func refresh_state(data: Dictionary) -> void:
	if not _alive: return
	presentation = data.duplicate(true)
	visual_context.update_from(Preferences, I18n, AppTheme.profile)
	var choices: Array = GameSession.activity_choices()
	_model = preload("res://scripts/presentation/dialogue_presenter.gd").build(GameSession.run, choices, I18n)
	controller.bind(GameSession.run, choices, _model)
	view.apply_projection(_model, visual_context)
	_interaction()
func _start_if_needed() -> void:
	if not _alive or _start_attempted or not _model.is_empty(): return
	_start_attempted = true
	if await controller.execute_published("START_DIALOGUE") and _alive: router.open_game()
func _intent(action: String, arguments: Array) -> void:
	match action:
		"pause": router.toggle_pause()
		"map": router.show_activity(true)
		"recover":
			if await controller.recover() and _alive: router.open_game()
		"choice":
			if get_tree().paused or controller.locked(): return
			if await controller.submit(str(arguments[0])) and _alive:
				refresh_state(presentation)
				if _model.get("completed", false):
					if not controller.command("RESOLVE_NODE").is_empty():
						if not await controller.execute_published("RESOLVE_NODE"): return
					if _alive:
						router.dialogue_return_barrier = true
						router.show_activity(true)
				else: router.open_game()
func _interaction() -> void:
	if is_instance_valid(view): view.set_read_only(controller.locked())
func apply_interaction(_state) -> void: _interaction()
func apply_visual_profile(_profile) -> void: refresh_state(presentation)
func dispose() -> void:
	_alive = false
	if controller: controller.dispose()
