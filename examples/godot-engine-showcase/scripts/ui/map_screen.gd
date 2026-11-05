extends "res://scripts/ui/screen_view.gd"
## Navigation adapter: inspection is local; travel uses current published choices.
const ScreenContext = preload("res://scripts/ui/context/screen_context.gd")
var _router
var _screen_context
var router:
	get: return _router
	set(value):
		_router = value
		_screen_context = ScreenContext.ensure(value)
var screen_context:
	get:
		if _screen_context == null:
			_screen_context = ScreenContext.ensure(_router)
		return _screen_context
	set(value):
		_screen_context = value
		if value != null and value.router != null:
			_router = value.router
var controller
var view
var presentation: Dictionary
var _scope := ""
var _workflow_key: Array = []
var _alive := true
func setup(owner, data: Dictionary) -> void:
	screen_context = ScreenContext.ensure(owner)
	if _router == null: _router = owner
	mount(screen_context.visual_context)
	controller = preload("res://scripts/ui/controllers/activity_controller.gd").new(GameSession)
	view = get_node("MapView")
	view.size_flags_vertical = Control.SIZE_EXPAND_FILL
	view.intent_requested.connect(_intent)
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
	view.set_resume_target(screen_context.navigation.gameplay_route())
	if changed_scope:
		var saved: Dictionary = screen_context.navigation.map_reading_state(_scope)
		if saved.is_empty():
			saved = {"selected": run.get("currentNodeId", ""), "overview": false, "filter": ""}
		view.restore_local_state(saved)
	view.set_read_only(GameSession.busy or not GameSession.synchronized)
	view.set_compartments_available(not GameSession.command("APPLY_DECK_COMPARTMENTS").is_empty())
	var workflow: Array = [run.get("runId"), run.get("branchId"), run.get("sequence"), choices]
	if workflow != _workflow_key:
		_workflow_key = workflow.duplicate(true)
		_auto_advance.call_deferred(workflow)
func _intent(action: String, arguments: Array) -> void:
	if not _alive or not is_inside_tree(): return
	match action:
		"loadout": screen_context.navigation.show_run_loadout()
		"inscriptions": screen_context.navigation.show_run_loadout("inscriptions")
		"deck_compartments": screen_context.navigation.show_run_deck_compartments()
		"pause": screen_context.navigation.toggle_pause()
		"modifier_items":
			screen_context.navigation.show_run_loadout("deck", "modifiers")
		"resume_dialogue": screen_context.navigation.show_dialogue()
		"resume_run":
			if not get_tree().paused: screen_context.open_game()
		"command":
			if get_tree().paused or GameSession.busy or not GameSession.synchronized: return
			# Revalidate the opaque reference against the latest snapshot, not a
			# retained button closure from a previous sequence or branch.
			controller.bind(GameSession.run, GameSession.activity_choices())
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
				if _alive: screen_context.open_game()
			elif is_inside_tree(): view.set_read_only(not GameSession.synchronized)
func capture_local_state() -> Dictionary: return view.capture_local_state()
func apply_interaction(_state) -> void: view.set_read_only(GameSession.busy or not GameSession.synchronized)
func apply_visual_profile(_profile) -> void: refresh_state(presentation)
func dispose() -> void:
	if not _alive: return
	_alive = false
	if is_instance_valid(view): view.cancel_gesture()
	_store_reading()
	if controller: controller.dispose()
func _store_reading() -> void:
	if not is_instance_valid(view): return
	screen_context.navigation.save_map_reading_state(_scope, view.capture_local_state())
func _auto_advance(ticket: Array) -> void:
	if not _alive or ticket != _workflow_key or screen_context.dialogue_return_barrier or screen_context.current_screen != "map" or get_tree().paused or GameSession.busy or not GameSession.synchronized: return
	var choice: Dictionary = controller.only_completion()
	if not choice.is_empty() and str(choice.get("payload", {}).get("currentNodeId", "")) != str(GameSession.run.get("currentNodeId", "")): return
	if choice.is_empty(): choice = controller.forced_advance()
	if choice.is_empty(): return
	_intent("command", [controller.option_reference(choice)])

func _ready() -> void:
	_preview_if_isolated.call_deferred()
func _preview_if_isolated() -> void:
	if controller == null and get_tree().current_scene == self:
		get_node("MapView").show_editor_preview()
