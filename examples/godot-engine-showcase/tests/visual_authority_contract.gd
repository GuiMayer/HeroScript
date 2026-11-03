extends SceneTree
## Etapa 1: Visual Authority Contract & Observable Editability Verification.
## Verifies that each property has a single authority (.tscn, exported Resource, or responsive layout)
## and that edits to scene nodes or duplicated style resources take effect without cross-overwrites.

const Context = preload("res://scripts/ui/visual_context.gd")
const Fixtures = preload("res://scripts/ui/workbenches/screen_catalog_fixtures.gd")
const MenuStyle = preload("res://scripts/ui/styles/menu_visual_style.gd")
const CardStyle = preload("res://scripts/ui/card_visual_style.gd")
const CombatProfile = preload("res://scripts/ui/styles/combat_layout_profile.gd")
const MapProfile = preload("res://scripts/ui/styles/map_layout_profile.gd")

var failures := 0

func _init() -> void:
	call_deferred("_run")

func check(ok: bool, message: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", message)
	if not ok:
		failures += 1

func settle(frames := 4) -> void:
	for _i in frames:
		await process_frame

func _exercise() -> void:
	root.size = Vector2i(1280, 720)
	var context = Context.new(null, {"text_scale": 1.0, "high_contrast": false, "reduced_motion": true}, load("res://data/themes/ivory_run.tres"))

	# 1. MainMenuView: profile authority vs local .tscn authority (use_profile_metrics = false)
	var menu: Control = load("res://scenes/ui/screens/main_menu.tscn").instantiate()
	menu.use_profile_metrics = false
	var new_btn: Button = menu.get_node("%NewJourneyButton")
	var title_lbl: Label = menu.get_node("%SettingTitle")
	new_btn.custom_minimum_size.y = 77
	title_lbl.add_theme_font_size_override("font_size", 41)
	menu.mount(context)
	root.add_child(menu)
	menu.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	Fixtures.apply(menu, "menu", "populated", context)
	await settle()
	check(is_equal_approx(new_btn.custom_minimum_size.y, 77.0), "MainMenuView preserves authored button height when use_profile_metrics=false")
	check(title_lbl.get_theme_font_size("font_size") == 41, "MainMenuView preserves authored title font_size when use_profile_metrics=false")

	menu.use_profile_metrics = true
	var custom_menu_style := MenuStyle.new()
	custom_menu_style.action_height = 68.0
	custom_menu_style.title_size = 58
	custom_menu_style.compact_width = 2000.0
	custom_menu_style.compact_gap = 42
	menu.visual_style = custom_menu_style
	Fixtures.apply(menu, "menu", "populated", context)
	await settle()
	check(is_equal_approx(new_btn.custom_minimum_size.y, 68.0) and title_lbl.get_theme_font_size("font_size") == 58, "MainMenuView applies exported MenuVisualStyle metrics when use_profile_metrics=true")
	check(menu.get_node("%MenuBody").get_theme_constant("separation") == 42, "MainMenuView uses exported compact_gap from MenuVisualStyle")
	menu.queue_free()
	await settle(2)

	# 2. CardView: local .tscn authority vs exported CardVisualStyle authority
	var card: CardView = load("res://scenes/ui/components/card_view.tscn").instantiate()
	card.animate_entry = false
	card.hand_managed = true
	card.use_profile_metrics = false
	card.custom_minimum_size = Vector2(280, 410)
	card.get_node("%Art").custom_minimum_size.y = 92
	card.get_node("%CardFaceMargin").add_theme_constant_override("margin_top", 21)
	card.visual_context = context
	var custom_card_style := CardStyle.new()
	custom_card_style.requirement_min_width = 195.0
	custom_card_style.inline_font_size = 15
	card.visual_style = custom_card_style
	card.configure({
		"name": "Authority Test",
		"costs": [{"resourceId": "energy", "amount": 1}, {"resourceId": "mana", "amount": 2}],
		"requirements": [{"text": "2 Inner Fire", "passed": true}],
		"effects": ["Deal 10 damage."]
	})
	root.add_child(card)
	await settle(2)
	check(card.custom_minimum_size == Vector2(280, 410) and is_equal_approx(card.get_node("%Art").custom_minimum_size.y, 92.0), "CardView preserves authored dimensions when use_profile_metrics=false")
	check(card.get_node("%CardFaceMargin").get_theme_constant("margin_top") == 21, "CardView preserves authored margin override across configure()")
	var req_line: Label = card.get_node("%Requirements").get_child(0)
	check(is_equal_approx(req_line.custom_minimum_size.x, 195.0) and req_line.get_theme_font_size("font_size") == 15, "CardView uses exported requirement_min_width and inline_font_size from CardVisualStyle")
	card.queue_free()
	await settle(2)

	# 3. CombatView: exported CombatLayoutProfile parameters control derived HUD geometry predictably
	var combat: Control = load("res://scenes/ui/components/combat_view.tscn").instantiate()
	var custom_combat_profile := CombatProfile.new()
	custom_combat_profile.toolbar_min_size = Vector2(188, 52)
	custom_combat_profile.reconnect_button_size = Vector2(176, 50)
	custom_combat_profile.candidate_width = 680.0
	combat.layout_profile = custom_combat_profile
	root.add_child(combat)
	combat.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	Fixtures.apply(combat, "combat", "populated", context)
	await settle(4)
	check(combat.hand.toolbar.custom_minimum_size == Vector2(188, 52), "CombatView applies exported toolbar_min_size from CombatLayoutProfile")
	check(is_equal_approx(combat.reconnect_button.offset_right, 176.0) and is_equal_approx(combat.reconnect_button.offset_bottom, 50.0), "CombatView applies exported reconnect_button_size from CombatLayoutProfile")
	check(is_equal_approx(combat.candidate_panel.offset_right, 680.0), "CombatView applies exported candidate_width from CombatLayoutProfile")
	combat.queue_free()
	await settle(2)

	# 4. MapView & RouteView: exported MapLayoutProfile parameters control node and inspector geometry
	var map_view: Control = load("res://scenes/ui/components/map_view.tscn").instantiate()
	map_view.mount(context)
	root.add_child(map_view)
	map_view.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var custom_map_profile := MapProfile.new()
	custom_map_profile.caption_height = 46.0
	custom_map_profile.node_font_size = 18
	custom_map_profile.inspector_toggle_size = Vector2(56, 56)
	map_view.route.layout_profile = custom_map_profile
	Fixtures.apply(map_view, "map", "populated", context)
	await settle(4)
	var first_stop: Button = map_view.route.buttons["first"]
	var first_caption: Label = first_stop.get_node("Caption")
	check(is_equal_approx(first_caption.size.y, 46.0) and first_stop.get_theme_font_size("font_size") == 18, "RouteView applies exported caption_height and node_font_size from MapLayoutProfile")
	check(map_view._inspector_toggle.size == Vector2(56, 56), "MapView applies exported inspector_toggle_size from MapLayoutProfile")
	map_view.queue_free()
	await settle(2)

	# 5. ActorPanel & PauseMenuView: mode restoration and overlay margin preservation
	var actor: ActorPanel = load("res://scenes/ui/components/actor_panel.tscn").instantiate()
	actor.visual_context = context
	actor.get_node("%ActorIntention").custom_minimum_size.y = 44
	actor.battlefield_intent_height = 30.0
	actor.setup({"instanceId": "hero", "resources": {"health": {"current": 30, "maximum": 50}}}, false, {}, "Defend", false, false)
	root.add_child(actor)
	check(is_equal_approx(actor.get_node("%ActorIntention").custom_minimum_size.y, 44.0), "ActorPanel full mode preserves authored intention height")
	actor.setup({"instanceId": "hero", "resources": {"health": {"current": 30, "maximum": 50}}}, false, {}, "Defend", false, true)
	check(is_equal_approx(actor.get_node("%ActorIntention").custom_minimum_size.y, 30.0), "ActorPanel battlefield mode applies exported battlefield_intent_height")
	actor.setup({"instanceId": "hero", "resources": {"health": {"current": 30, "maximum": 50}}}, false, {}, "Defend", false, false)
	check(is_equal_approx(actor.get_node("%ActorIntention").custom_minimum_size.y, 44.0), "ActorPanel restores authored intention height when returning from battlefield mode")
	actor.queue_free()
	await settle(2)

	var pause_menu = load("res://scenes/ui/overlays/pause_menu.tscn").instantiate()
	var frame_panel: PanelContainer = pause_menu.get_node("%Frame")
	var edited_box: StyleBoxFlat = frame_panel.get_theme_stylebox("panel").duplicate()
	edited_box.content_margin_left = 41
	frame_panel.add_theme_stylebox_override("panel", edited_box)
	pause_menu.setup(context, true, true)
	root.add_child(pause_menu)
	check(is_equal_approx(frame_panel.get_theme_stylebox("panel").content_margin_left, 41.0), "PauseMenuView preserves authored panel content margins when applying theme colors")
	pause_menu.queue_free()
	await settle(2)

func _run() -> void:
	await _exercise()
	root.get_node("GameAudio").shutdown()
	await create_timer(0.05).timeout
	quit(0 if failures == 0 else 1)
