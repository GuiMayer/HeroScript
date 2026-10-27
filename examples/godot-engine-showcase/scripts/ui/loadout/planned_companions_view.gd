extends VBoxContainer
## Informational placeholder, never an inventory or a locked progression feature.
func mount(context) -> void:
	add_child(context.styles.title(context.translator.text("Companions · Planned"), 26))
	add_child(context.styles.muted(context.translator.text("Companions are not implemented. There is no equipment or progression to save here."), 17, 640))
