extends SceneTree
## Repeatable visual fixture. Run with -- --capture=<absolute PNG path> to save
## the field, or -- --header-life to compare the duplicate header variant.

const ActorScene = preload("res://scenes/ui/components/battlefield_actor.tscn")
const Context = preload("res://scripts/ui/visual_context.gd")
const Profile = preload("res://data/themes/mvp_combat_theme.tres")

func _initialize() -> void:
	run.call_deferred()

func run() -> void:
	var options := {}
	for argument in OS.get_cmdline_user_args():
		var parts := argument.trim_prefix("--").split("=", true, 1)
		options[parts[0]] = parts[1] if parts.size() == 2 else true
	var i18n = root.get_node("I18n")
	i18n.set_locale(str(options.get("locale", "en")))
	var context = Context.new(i18n, {"text_scale": float(options.get("scale", 1.0)), "reduced_motion": true, "high_contrast": options.has("high-contrast")}, Profile)
	var background := ColorRect.new()
	background.color = Color("#a4a297")
	root.add_child(background)
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var layout := VBoxContainer.new()
	background.add_child(layout)
	layout.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	layout.add_theme_constant_override("separation", 32)
	var heading := Label.new()
	heading.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	heading.theme = context.theme
	heading.text = "Combat reference" + ("  ·  Hero  62 / 90" if options.has("header-life") else "")
	layout.add_child(heading)
	var scroll := ScrollContainer.new()
	scroll.follow_focus = true
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	layout.add_child(scroll)
	var group := HBoxContainer.new()
	group.alignment = BoxContainer.ALIGNMENT_CENTER
	group.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	group.add_theme_constant_override("separation", 16)
	scroll.add_child(group)
	var effects := []
	for status in ["burn", "focus", "poison", "strength", "vulnerable", "wet"]:
		effects.append({"statusId": status, "stacks": 12 if status == "burn" else 2, "duration": 2})
	var actors := [
		{"name": "Pyromancer", "instanceId": "hero", "isInputActor": true, "hp": 62, "max": 90, "block": 8},
		{"name": "Water Elemental", "instanceId": "support"},
		{"name": "Glass Wisp", "instanceId": "enemy", "hp": 27, "max": 55, "block": 0, "intent": "Attack 11"},
		{"name": "Obsidian Commander", "instanceId": "boss", "hp": 17, "max": 160, "block": 12, "intent": "Guard 14", "boss": true},
	]
	for actor in actors:
		var panel: ActorPanel = ActorScene.instantiate()
		panel.visual_context = context
		group.add_child(panel)
		var hostile: bool = actor.has("intent")
		var resources := {}
		if actor.has("hp"):
			resources = {"health": {"current": actor.hp, "minimum": 0, "maximum": actor.max}, "block": {"current": actor.block}}
		actor["resources"] = resources
		actor["statuses"] = effects if hostile else effects.slice(0, 2) if actor.instanceId == "hero" else []
		panel.setup(actor, hostile, {}, str(actor.get("intent", "")), false, true)
		var silhouette := ColorRect.new()
		silhouette.color = Color("#6d5451") if hostile else Color("#526758")
		silhouette.mouse_filter = Control.MOUSE_FILTER_IGNORE
		panel.portrait_view.add_child(silhouette)
		silhouette.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT, Control.PRESET_MODE_MINSIZE, 20)
	for frame in 5:
		await process_frame
	if options.has("capture"):
		await RenderingServer.frame_post_draw
		var result := root.get_texture().get_image().save_png(str(options.capture))
		quit(result)
	elif DisplayServer.get_name() == "headless":
		quit(0)
