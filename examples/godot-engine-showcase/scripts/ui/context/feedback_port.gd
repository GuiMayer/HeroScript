@tool
extends RefCounted
## Narrow feedback and cosmetic audio contract for screens and coordinators.

var _router
var _audio
var toasts: Array[Dictionary] = []
var audio_events: Array[String] = []

func _init(router_target = null, audio_target = null) -> void:
	_router = router_target
	_audio = audio_target

func show_toast(message: String, error := false) -> void:
	toasts.append({"message": message, "error": error})
	if is_instance_valid(_router) and _router.has_method("show_toast"):
		if error:
			_router.show_toast(message, true)
		else:
			_router.show_toast(message)

func show_error(message: String) -> void:
	toasts.append({"message": message, "error": true})
	if is_instance_valid(_router):
		if _router.has_method("show_error"):
			_router.show_error(message)
		elif _router.has_method("show_toast"):
			_router.show_toast(message, true)

func ui() -> void:
	audio_events.append("ui")
	if is_instance_valid(_audio) and _audio.has_method("ui"):
		_audio.ui()

func cancel() -> void:
	audio_events.append("cancel")
	if is_instance_valid(_audio) and _audio.has_method("cancel"):
		_audio.cancel()

func card() -> void:
	audio_events.append("card_play")
	if is_instance_valid(_audio) and _audio.has_method("card"):
		_audio.card()

func select_card() -> void:
	audio_events.append("card_select")
	if is_instance_valid(_audio) and _audio.has_method("select_card"):
		_audio.select_card()

func hit() -> void:
	audio_events.append("impact")
	if is_instance_valid(_audio) and _audio.has_method("hit"):
		_audio.hit()

func reward() -> void:
	audio_events.append("reward")
	if is_instance_valid(_audio) and _audio.has_method("reward"):
		_audio.reward()

func turn() -> void:
	audio_events.append("turn")
	if is_instance_valid(_audio) and _audio.has_method("turn"):
		_audio.turn()

func error() -> void:
	audio_events.append("error")
	if is_instance_valid(_audio) and _audio.has_method("error"):
		_audio.error()

func play_event(event_id: String, combat_cue := false) -> bool:
	audio_events.append(event_id)
	if is_instance_valid(_audio) and _audio.has_method("play_event"):
		return bool(_audio.play_event(event_id, combat_cue))
	return false

func frame(frame_data: Dictionary) -> void:
	if is_instance_valid(_audio) and _audio.has_method("frame"):
		_audio.frame(frame_data)

func play_beat(frame_data: Dictionary, policy) -> void:
	if is_instance_valid(_audio) and _audio.has_method("play_beat"):
		_audio.play_beat(frame_data, policy)
