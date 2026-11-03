@tool
extends RefCounted
## Narrow session read/command adapter around an existing session authority (or fake stub in isolated tests).
## Never owns a second live session state in production; delegates directly to the wrapped target.

var _target
var _fake_state: Dictionary = {
	"run": {},
	"combat": {},
	"legal_actions": [],
	"card_zones": {},
	"available_commands": [],
	"settings": [],
	"selected_setting_id": "default",
	"selected_setting": {"settingId": "default", "displayName": "Ember Archive"},
	"available": false,
	"busy": false,
	"has_pending_command": false,
	"synchronized": true,
	"content": {},
	"run_history": {"ok": true, "data": {"items": [], "availability": {"options": []}}},
	"verify_run": {"ok": true, "data": {"isValid": true}}
}
var recorded_calls: Array[Dictionary] = []

func _init(session_target = null, initial_fake_state: Dictionary = {}) -> void:
	_target = session_target
	if not initial_fake_state.is_empty():
		_fake_state.merge(initial_fake_state.duplicate(true), true)

var target:
	get: return _target

var run: Dictionary:
	get:
		if is_instance_valid(_target) and "run" in _target:
			return _target.run
		return _fake_state.get("run", {}).duplicate(true)

var combat: Dictionary:
	get:
		if is_instance_valid(_target) and "combat" in _target:
			return _target.combat
		return _fake_state.get("combat", {}).duplicate(true)

var legal_actions: Array:
	get:
		if is_instance_valid(_target) and "legal_actions" in _target:
			return _target.legal_actions
		return _fake_state.get("legal_actions", []).duplicate(true)

var card_zones: Dictionary:
	get:
		if is_instance_valid(_target) and "card_zones" in _target:
			return _target.card_zones
		return _fake_state.get("card_zones", {}).duplicate(true)

var available_commands: Array:
	get:
		if is_instance_valid(_target) and "available_commands" in _target:
			return _target.available_commands
		return _fake_state.get("available_commands", []).duplicate(true)

var settings: Array:
	get:
		if is_instance_valid(_target) and "settings" in _target:
			return _target.settings
		return _fake_state.get("settings", []).duplicate(true)

var selected_setting_id: String:
	get:
		if is_instance_valid(_target) and "selected_setting_id" in _target:
			return str(_target.selected_setting_id)
		return str(_fake_state.get("selected_setting_id", "default"))
	set(value):
		_fake_state["selected_setting_id"] = value
		if is_instance_valid(_target) and "selected_setting_id" in _target:
			_target.selected_setting_id = value

var selected_setting: Dictionary:
	get:
		if is_instance_valid(_target) and "selected_setting" in _target:
			return _target.selected_setting
		return _fake_state.get("selected_setting", {}).duplicate(true)

var available: bool:
	get:
		if is_instance_valid(_target) and "available" in _target:
			return bool(_target.available)
		return bool(_fake_state.get("available", false))

var busy: bool:
	get:
		if is_instance_valid(_target) and "busy" in _target:
			return bool(_target.busy)
		return bool(_fake_state.get("busy", false))

var has_pending_command: bool:
	get:
		if is_instance_valid(_target) and "has_pending_command" in _target:
			return bool(_target.has_pending_command)
		return bool(_fake_state.get("has_pending_command", false))

var synchronized: bool:
	get:
		if is_instance_valid(_target) and "synchronized" in _target:
			return bool(_target.synchronized)
		return bool(_fake_state.get("synchronized", true))

func set_fake_content(kind: String, response: Dictionary) -> void:
	if not _fake_state.has("content") or not (_fake_state["content"] is Dictionary):
		_fake_state["content"] = {}
	_fake_state["content"][kind] = response.duplicate(true)

func capture_view_snapshot() -> Dictionary:
	if is_instance_valid(_target) and _target.has_method("capture_view_snapshot"):
		return _target.capture_view_snapshot()
	return {
		"run": run,
		"combat": combat,
		"actions": legal_actions,
		"zones": card_zones,
		"evaluations": _fake_state.get("evaluations", []).duplicate(true),
		"presentations": _fake_state.get("presentations", []).duplicate(true),
		"attributes": _fake_state.get("attributes", {}).duplicate(true)
	}

func activity_choices() -> Array:
	if is_instance_valid(_target) and _target.has_method("activity_choices"):
		return _target.activity_choices()
	return _fake_state.get("activity_choices", []).duplicate(true)

func command(command_type: String) -> Dictionary:
	if is_instance_valid(_target) and _target.has_method("command"):
		return _target.command(command_type)
	for cmd in available_commands:
		if str(cmd.get("type", "")) == command_type:
			return cmd.duplicate(true)
	return {}

func content(kind: String, limit := 200) -> Dictionary:
	recorded_calls.append({"method": "content", "kind": kind, "limit": limit})
	if is_instance_valid(_target) and _target.has_method("content"):
		return await _target.content(kind, limit)
	var table: Dictionary = _fake_state.get("content", {})
	if table.has(kind):
		return table[kind].duplicate(true)
	return {"ok": true, "data": {"items": []}}

func run_history() -> Dictionary:
	recorded_calls.append({"method": "run_history"})
	if is_instance_valid(_target) and _target.has_method("run_history"):
		return await _target.run_history()
	return _fake_state.get("run_history", {"ok": true, "data": {"items": [], "availability": {"options": []}}}).duplicate(true)

func verify_run(run_id: String) -> Dictionary:
	recorded_calls.append({"method": "verify_run", "run_id": run_id})
	if is_instance_valid(_target) and _target.has_method("verify_run"):
		return await _target.verify_run(run_id)
	return _fake_state.get("verify_run", {"ok": true, "data": {"isValid": true}}).duplicate(true)

func continue_run(run_id: String, fallback_mode := "") -> bool:
	recorded_calls.append({"method": "continue_run", "run_id": run_id, "fallback_mode": fallback_mode})
	if is_instance_valid(_target) and _target.has_method("continue_run"):
		return await _target.continue_run(run_id, fallback_mode)
	return bool(_fake_state.get("continue_run_ok", true))

func start_sandbox(mode_id: String, scenario: Dictionary, seed_val: int) -> bool:
	recorded_calls.append({"method": "start_sandbox", "mode_id": mode_id, "scenario": scenario.duplicate(true), "seed": seed_val})
	if is_instance_valid(_target) and _target.has_method("start_sandbox"):
		return await _target.start_sandbox(mode_id, scenario, seed_val)
	return bool(_fake_state.get("start_sandbox_ok", true))

func start_campaign(seed_val: int, mode_id := "") -> bool:
	recorded_calls.append({"method": "start_campaign", "seed": seed_val, "mode_id": mode_id})
	if is_instance_valid(_target) and _target.has_method("start_campaign"):
		return await _target.start_campaign(seed_val, mode_id)
	return bool(_fake_state.get("start_campaign_ok", true))

func submit_activity(choice: Dictionary) -> bool:
	recorded_calls.append({"method": "submit_activity", "choice": choice.duplicate(true)})
	if is_instance_valid(_target) and _target.has_method("submit_activity"):
		return await _target.submit_activity(choice)
	return bool(_fake_state.get("submit_activity_ok", true))

func execute_run_command(command_type: String, payload: Dictionary = {}) -> bool:
	recorded_calls.append({"method": "execute_run_command", "type": command_type, "payload": payload.duplicate(true)})
	if is_instance_valid(_target) and _target.has_method("execute_run_command"):
		return await _target.execute_run_command(command_type, payload)
	return bool(_fake_state.get("execute_run_command_ok", true))

func refresh() -> bool:
	recorded_calls.append({"method": "refresh"})
	if is_instance_valid(_target) and _target.has_method("refresh"):
		return await _target.refresh()
	return bool(_fake_state.get("refresh_ok", true))

func connect_signal(signal_name: String, callable: Callable) -> void:
	if is_instance_valid(_target) and _target.has_signal(signal_name) and not _target.is_connected(signal_name, callable):
		_target.connect(signal_name, callable)

func disconnect_signal(signal_name: String, callable: Callable) -> void:
	if is_instance_valid(_target) and _target.has_signal(signal_name) and _target.is_connected(signal_name, callable):
		_target.disconnect(signal_name, callable)
