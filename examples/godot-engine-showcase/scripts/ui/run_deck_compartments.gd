extends "res://scripts/ui/deck_compartment_editor.gd"
## No offline fallback or character selector in a run.
var session
func _ready() -> void:
	if session == null: session = GameSession
	service = preload("res://scripts/application/deck_compartment_session_service.gd").new(session)
	super._ready()
