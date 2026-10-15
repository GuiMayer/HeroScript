extends "res://scripts/ui/screen_view.gd"
## Navigation adapter: inspection is local; travel uses current published choices.
var router
var controller
var view
var presentation: Dictionary
var _scope := ""
var _workflow_key: Array = []
var _alive := true
func setup(owner, data: Dictionary) -> void:
	router = owner; mount(owner.visual_context)
	controller = preload("res://scripts/ui/controllers/activity_controller.gd").new(GameSession)
	view = preload("res://scripts/ui/map_view.gd").new()
	view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	view.intent_requested.connect(_intent)
	add_child(view)
	refresh_state(data)
func refresh_state(data: Dictionary) -> void:
	presentation = data.duplicate(true)
	visual_context.update_from(Preferences, I18n, AppTheme.profile)
	var run: Dictionary = GameSession.run
	var choices: Array = GameSession.activity_choices()
	var next_scope := JSON.stringify([visual_context.setting_id, run.get("runId", ""), run.get("branchId", "")])
	var changed_scope := next_scope != _scope
	if changed_scope and not _scope.is_empty(): _store_reading()
	_scope = next_scope
	controller.bind(run, choices)
	var presenter = preload("res://scripts/presentation/activity_presenter.gd").new(run, choices, data, I18n, GameSession.card_presentations, GameSession.modifier_items)
	view.apply_projection(run, choices, presenter, visual_context)
	if changed_scope:
		view.restore_local_state(router.map_reading_states.get(_scope, {"selected": run.get("currentNodeId", ""), "horizontal": 0, "vertical": 0, "overview": false, "filter": ""}))
	view.set_read_only(GameSession.busy or not GameSession.synchronized)
	var workflow: Array = [run.get("runId"), run.get("branchId"), run.get("sequence"), choices]
	if workflow != _workflow_key:
		_workflow_key = workflow.duplicate(true)
		_auto_advance.call_deferred(workflow)
func _intent(action: String, arguments: Array) -> void:
	if not _alive or not is_inside_tree(): return
	match action:
		"inscriptions": router.show_loadout_lab(true)
		"pause": router.toggle_pause()
		"modifier_items":
			router.show_activity()
			if router.host.get_child_count() > 0: router.host.get_child(0)._open_items()
		"resume_dialogue": router.show_dialogue()
		"command":
			if get_tree().paused or GameSession.busy: return
			var choice: Dictionary = controller.choice_for(str(arguments[0]))
			if choice.is_empty(): return
			view.set_read_only(true)
			if await controller.execute(choice) and _alive:
				if str(choice.get("type", "")) == "ADVANCE_NODE" and not get_tree().paused:
					var choices: Array = GameSession.activity_choices()
					controller.bind(GameSession.run, choices)
					var entry: Dictionary = preload("res://scripts/application/activity_choices.gd").single_entry(choices)
					if not entry.is_empty():
						await controller.execute(entry)
				if _alive: router.open_game()
			elif is_inside_tree(): view.set_read_only(not GameSession.synchronized)
func capture_local_state() -> Dictionary: return view.capture_local_state()
func apply_interaction(_state) -> void: view.set_read_only(GameSession.busy or not GameSession.synchronized)
func apply_visual_profile(_profile) -> void: refresh_state(presentation)
func dispose() -> void:
	if not _alive: return
	_alive = false
	_store_reading()
	if controller: controller.dispose()
func _store_reading() -> void:
	if not is_instance_valid(view): return
	if router.map_reading_states.size() >= 16 and not router.map_reading_states.has(_scope): router.map_reading_states.erase(router.map_reading_states.keys()[0])
	router.map_reading_states[_scope] = view.capture_local_state()
func _auto_advance(ticket: Array) -> void:
	if not _alive or ticket != _workflow_key or router.dialogue_return_barrier or router.current_screen != "map" or get_tree().paused or GameSession.busy: return
	var choice: Dictionary = controller.forced_advance()
	if choice.is_empty(): return
	_intent("command", [controller.option_reference(choice)])
