extends Control
## Isolated presentation fixture. No session, API, gameplay write or preference change.
signal back_requested
var shell
func _ready() -> void:
	var context = preload("res://scripts/ui/visual_context.gd").from_nodes(Preferences, I18n).derive(preload("res://data/themes/ivory_theme.tres"))
	shell = preload("res://scripts/ui/loadout/loadout_page_view.gd").new(); add_child(shell); shell.mount(context)
	shell.heading.text = I18n.text("Visual laboratory — no run changes")
	shell.back_requested.connect(back_requested.emit)
	shell.tab_requested.connect(shell.select)
	for entry in preload("res://scripts/ui/loadout/module_catalog.gd").entries():
		var page := VBoxContainer.new()
		page.add_child(context.styles.title(I18n.text(entry.label), 24))
		page.add_child(context.styles.muted(I18n.text("Presentation study. No equipment is saved here.")))
		shell.install(entry.id, page)
	shell.present_attributes({"name": "Example character", "attributes": []})
	shell.set_feedback("Visual laboratory — no run changes"); shell.select("overview")

