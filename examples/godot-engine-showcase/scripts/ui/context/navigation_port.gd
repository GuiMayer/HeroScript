@tool
extends RefCounted
## Narrow navigation contract for screens. Prevents direct access to router.shell or private router methods.

var _router
var _current_screen := "menu"
var _navigation_epoch := 0
var _presentation: Dictionary = {}
var _dialogue_return_barrier := false
var _map_reading_states: Dictionary = {}
var _dialogue_reading_states: Dictionary = {}
var _combat_scenery_leases := 0
var history: Array[Dictionary] = []

func _init(router_target = null, initial_presentation: Dictionary = {}) -> void:
	_router = router_target
	_presentation = initial_presentation.duplicate(true)

var router:
	get: return _router

var current_screen: String:
	get:
		if is_instance_valid(_router) and "current_screen" in _router:
			return str(_router.current_screen)
		return _current_screen
	set(value):
		_current_screen = value
		if is_instance_valid(_router) and "current_screen" in _router:
			_router.current_screen = value

var navigation_epoch: int:
	get:
		if is_instance_valid(_router) and "navigation_epoch" in _router:
			return int(_router.navigation_epoch)
		return _navigation_epoch
	set(value):
		_navigation_epoch = value
		if is_instance_valid(_router) and "navigation_epoch" in _router:
			_router.navigation_epoch = value

var presentation: Dictionary:
	get:
		if is_instance_valid(_router) and "presentation" in _router:
			return _router.presentation
		return _presentation
	set(value):
		_presentation = value.duplicate(true)

var dialogue_return_barrier: bool:
	get:
		if is_instance_valid(_router) and "dialogue_return_barrier" in _router:
			return bool(_router.dialogue_return_barrier)
		return _dialogue_return_barrier
	set(value):
		_dialogue_return_barrier = value
		if is_instance_valid(_router) and "dialogue_return_barrier" in _router:
			_router.dialogue_return_barrier = value

func is_tree_paused() -> bool:
	if is_instance_valid(_router) and _router is Node and _router.is_inside_tree():
		return _router.get_tree().paused
	return false

func back_to_menu() -> void:
	history.append({"route": "menu"})
	_current_screen = "menu"
	if is_instance_valid(_router) and _router.has_method("back_to_menu"):
		_router.back_to_menu()

func open_game() -> void:
	history.append({"route": "open_game"})
	if is_instance_valid(_router) and _router.has_method("open_game"):
		_router.open_game()

func start_new_campaign() -> void:
	history.append({"route": "new_campaign"})
	if is_instance_valid(_router):
		if _router.has_method("start_new_campaign"):
			_router.start_new_campaign()
		elif _router.has_method("_new_campaign"):
			_router._new_campaign()

func gameplay_route() -> String:
	if is_instance_valid(_router) and _router.has_method("gameplay_route"):
		return str(_router.gameplay_route())
	return _current_screen

func show_map() -> void:
	history.append({"route": "map"})
	_current_screen = "map"
	if is_instance_valid(_router) and _router.has_method("show_map"):
		_router.show_map()

func show_activity(inspect_map := false) -> void:
	history.append({"route": "activity", "inspect_map": inspect_map})
	_current_screen = "activity"
	if is_instance_valid(_router) and _router.has_method("show_activity"):
		_router.show_activity(inspect_map)

func show_dialogue() -> void:
	history.append({"route": "dialogue"})
	_current_screen = "dialogue"
	if is_instance_valid(_router) and _router.has_method("show_dialogue"):
		_router.show_dialogue()

func show_combat() -> void:
	history.append({"route": "combat"})
	_current_screen = "combat"
	if is_instance_valid(_router) and _router.has_method("show_combat"):
		_router.show_combat()

func show_timeline() -> void:
	history.append({"route": "timeline"})
	_current_screen = "timeline"
	if is_instance_valid(_router) and _router.has_method("show_timeline"):
		_router.show_timeline()

func show_run_loadout(initial_tab := "overview", deck_tab := "cards") -> void:
	history.append({"route": "run_loadout", "initial_tab": initial_tab, "deck_tab": deck_tab})
	_current_screen = "run_loadout"
	if is_instance_valid(_router) and _router.has_method("show_run_loadout"):
		_router.show_run_loadout(initial_tab, deck_tab)

func show_run_deck_compartments() -> void:
	history.append({"route": "run_deck_compartments"})
	if is_instance_valid(_router) and _router.has_method("show_run_deck_compartments"):
		_router.show_run_deck_compartments()

func show_run_replay(run_id: String, from_history := false) -> void:
	history.append({"route": "run_replay", "run_id": run_id, "from_history": from_history})
	_current_screen = "run_replay"
	if is_instance_valid(_router) and _router.has_method("show_run_replay"):
		_router.show_run_replay(run_id, from_history)

func show_history() -> void:
	history.append({"route": "history"})
	_current_screen = "history"
	if is_instance_valid(_router) and _router.has_method("show_history"):
		_router.show_history()

func show_sandbox() -> void:
	history.append({"route": "sandbox"})
	_current_screen = "sandbox"
	if is_instance_valid(_router) and _router.has_method("show_sandbox"):
		_router.show_sandbox()

func show_codex() -> void:
	history.append({"route": "codex"})
	_current_screen = "codex"
	if is_instance_valid(_router) and _router.has_method("show_codex"):
		_router.show_codex()

func show_settings(return_to_gameplay := false) -> void:
	history.append({"route": "settings", "return_to_gameplay": return_to_gameplay})
	_current_screen = "settings"
	if is_instance_valid(_router) and _router.has_method("show_settings"):
		_router.show_settings(return_to_gameplay)

func toggle_pause() -> void:
	history.append({"route": "toggle_pause"})
	if is_instance_valid(_router) and _router.has_method("toggle_pause"):
		_router.toggle_pause()

func map_reading_state(scope_key: String) -> Dictionary:
	if is_instance_valid(_router) and "map_reading_states" in _router:
		return _router.map_reading_states.get(scope_key, {}).duplicate(true)
	return _map_reading_states.get(scope_key, {}).duplicate(true)

func save_map_reading_state(scope_key: String, state: Dictionary) -> void:
	if scope_key.is_empty():
		return
	if is_instance_valid(_router) and "map_reading_states" in _router:
		if _router.map_reading_states.size() >= 16 and not _router.map_reading_states.has(scope_key):
			_router.map_reading_states.erase(_router.map_reading_states.keys()[0])
		_router.map_reading_states[scope_key] = state.duplicate(true)
	else:
		if _map_reading_states.size() >= 16 and not _map_reading_states.has(scope_key):
			_map_reading_states.erase(_map_reading_states.keys()[0])
		_map_reading_states[scope_key] = state.duplicate(true)

func clear_map_reading_state(scope_key: String) -> void:
	if is_instance_valid(_router) and "map_reading_states" in _router:
		_router.map_reading_states.erase(scope_key)
	_map_reading_states.erase(scope_key)

func dialogue_reading_state(scope_key: String) -> Dictionary:
	if is_instance_valid(_router) and "dialogue_reading_states" in _router:
		return _router.dialogue_reading_states.get(scope_key, {}).duplicate(true)
	return _dialogue_reading_states.get(scope_key, {}).duplicate(true)

func save_dialogue_reading_state(scope_key: String, state: Dictionary) -> void:
	if scope_key.is_empty():
		return
	if is_instance_valid(_router) and "dialogue_reading_states" in _router:
		if _router.dialogue_reading_states.size() >= 16 and not _router.dialogue_reading_states.has(scope_key):
			_router.dialogue_reading_states.erase(_router.dialogue_reading_states.keys()[0])
		_router.dialogue_reading_states[scope_key] = state.duplicate(true)
	else:
		if _dialogue_reading_states.size() >= 16 and not _dialogue_reading_states.has(scope_key):
			_dialogue_reading_states.erase(_dialogue_reading_states.keys()[0])
		_dialogue_reading_states[scope_key] = state.duplicate(true)

func clear_dialogue_reading_state(scope_key: String) -> void:
	if is_instance_valid(_router) and "dialogue_reading_states" in _router:
		_router.dialogue_reading_states.erase(scope_key)
	_dialogue_reading_states.erase(scope_key)

func acquire_combat_scenery():
	_combat_scenery_leases += 1
	if is_instance_valid(_router):
		if _router.has_method("acquire_combat_scenery"):
			return _router.acquire_combat_scenery()
		elif "shell" in _router and _router.shell != null and "run_scenery" in _router.shell and _router.shell.run_scenery != null:
			return _router.shell.run_scenery.acquire_combat()
	return null

func release_combat_scenery(scenery = null) -> void:
	_combat_scenery_leases = maxi(0, _combat_scenery_leases - 1)
	if is_instance_valid(_router):
		if _router.has_method("release_combat_scenery"):
			_router.release_combat_scenery(scenery)
		elif "shell" in _router and _router.shell != null and "run_scenery" in _router.shell and _router.shell.run_scenery != null:
			_router.shell.run_scenery.release_combat(scenery)
