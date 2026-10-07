extends Node
## Composition root: the only bridge between local preferences and engine services.
const Transport = preload("res://scripts/engine/http_transport.gd")
const Gateway = preload("res://scripts/engine/engine_gateway.gd")
var _transport

func _ready() -> void:
	_transport = Transport.new()
	add_child(_transport)
	_apply_connection()
	GameSession.configure(Gateway.new(_transport))
	_transport.availability_changed.connect(GameSession.set_available)
	Preferences.changed.connect(_apply_connection)
	GameSession.run_opened.connect(_remember_run)
	GameSession.changed.connect(_remember_progress)
	GameSession.run_opened.connect(Playback.clear)
	GameSession.receipt_received.connect(Playback.load_receipt)

func _apply_connection() -> void:
	if _transport.base_url == Preferences.api_url:
		return
	if GameSession.busy or GameSession.has_pending_command:
		Preferences.api_url = _transport.base_url
		return
	GameSession.invalidate()
	Playback.clear()
	_transport.base_url = Preferences.api_url

func _remember_run(run_id: String) -> void:
	if run_id != str(GameSession.run.get("runId", "")): return
	var updated: bool = Preferences.resume_index.remember(Preferences.api_url, GameSession.run)
	updated = updated or Preferences.selected_setting_id != GameSession.selected_setting_id
	Preferences.selected_setting_id = GameSession.selected_setting_id
	if updated: Preferences.save_session()

func _remember_progress() -> void:
	if not GameSession.synchronized or GameSession.run.is_empty(): return
	if Preferences.resume_index.remember(Preferences.api_url, GameSession.run): Preferences.save_session()

func diagnostics() -> Array:
	return _transport.timings.duplicate(true)
