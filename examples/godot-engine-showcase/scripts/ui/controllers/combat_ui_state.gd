extends RefCounted
enum InputState { READY, SELECTING, SUBMITTING, ANIMATING, RECOVERING, PAUSED }
var selected_card := ""
var selected_target := ""
var submitting := false
var scope := ""
static func phase(submitting_now: bool, synchronized: bool, animating: bool, paused: bool, selected: String) -> InputState:
	if paused: return InputState.PAUSED
	if submitting_now: return InputState.SUBMITTING
	if not synchronized: return InputState.RECOVERING
	if animating: return InputState.ANIMATING
	return InputState.READY if selected.is_empty() else InputState.SELECTING
func clear() -> void:
	selected_card = ""
	selected_target = ""
