extends VBoxContainer
## Small view contract. Context contains only visual services, never a session.
const ContextType = preload("res://scripts/ui/visual_context.gd")
var visual_context: ContextType
func mount(context) -> void: visual_context = context
func apply_model(_model) -> void: pass
func apply_interaction(_state) -> void: pass
func apply_visual_profile(_profile) -> void: pass
func capture_local_state() -> Dictionary: return {}
func restore_local_state(_state: Dictionary) -> void: pass
func dispose() -> void: pass
func _exit_tree() -> void: dispose()
