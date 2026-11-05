extends Node
var previous: WeakRef
func _ready() -> void:
	var window := get_parent() as Window
	window.about_to_popup.connect(_capture)
	window.visibility_changed.connect(func(): if not window.visible: _restore.call_deferred())
	window.tree_exiting.connect(_restore)
func _capture() -> void:
	var viewport := get_parent().get_parent().get_viewport()
	var focused := viewport.gui_get_focus_owner()
	previous = weakref(focused) if focused != null else null
func _restore() -> void:
	if previous == null: return
	var focused = previous.get_ref()
	previous = null
	if is_instance_valid(focused) and focused.is_inside_tree() and focused.is_visible_in_tree(): focused.grab_focus()
