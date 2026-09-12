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
	Preferences.last_run_id = run_id
	Preferences.save()

func diagnostics() -> Array:
	return _transport.timings.duplicate(true)
