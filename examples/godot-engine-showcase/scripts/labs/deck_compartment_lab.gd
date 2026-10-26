extends Control
## Dedicated real-API run; never opens or changes the player's active run/bookmark.
var _transport
var _gateway
var _status: Label

func _ready() -> void:
	_status = Label.new(); _status.text = "Connecting to Deck Workshop…"; add_child(_status)
	_transport = preload("res://scripts/engine/http_transport.gd").new(); _transport.base_url = Preferences.api_url; add_child(_transport)
	_gateway = preload("res://scripts/engine/engine_gateway.gd").new(_transport)
	_start.call_deferred()

func _start() -> void:
	var connected: Dictionary = await _gateway.connect_engine()
	if not is_inside_tree(): return
	if not connected.get("ok", false): _status.text = "Start the API and reopen this laboratory."; return
	var setting: Dictionary = {}
	var entries: Array = connected.data.get("items", []) if connected.data is Dictionary else connected.data
	for item in entries:
		if item.get("settingId") == "compartment-lab": setting = item
	if setting.is_empty(): _status.text = "Restart the API to load the Deck Workshop setting."; return
	var created: Dictionary = await _gateway.create_campaign(int(Time.get_unix_time_from_system()), setting)
	if not is_inside_tree(): return
	if not created.get("ok", false): _status.text = str(created.get("error", created)); return
	var id := str(created.data.get("runId", ""))
	var editor = preload("res://scripts/ui/deck_compartment_editor.gd").new()
	editor.service = preload("res://scripts/engine/deck_compartment_gateway.gd").new(_transport, _gateway, id)
	editor.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	editor.back_requested.connect(func(): get_tree().change_scene_to_file("res://main.tscn"))
	_status.hide(); add_child(editor)
