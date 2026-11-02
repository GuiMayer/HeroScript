extends "res://tests/visual_baseline.gd"
func _run() -> void:
	session = root.get_node("GameSession"); gateway = FixtureGateway.new(); session.configure(gateway); session.available = false
	load_combat()
	var untouched := JSON.stringify([session.run,session.combat,session.legal_actions])
	router = load("res://main.tscn").instantiate(); root.add_child(router); await process_frame
	router.show_combat(); await settle()
	var screen = router.host.get_child(0); var view = screen.view
	var battlefield = view.battlefield_view
	var actor = battlefield.panels.hero; var art = view.scenery.portraits.hero
	var effects = view.feedback.effects; var floating = view.floating_hud.host
	battlefield.get_node("BattlefieldActors/Field").add_theme_constant_override("separation",31)
	screen.refresh_state(router.presentation); await settle()
	check(battlefield.panels.hero == actor and view.scenery.portraits.hero == art, "snapshot refresh retains actor panels and artwork anchors")
	check(view.feedback.effects == effects and view.floating_hud.host == floating, "refresh retains effect and floating HUD hosts")
	check(battlefield.get_node("BattlefieldActors/Field").get_theme_constant("separation") == 31, "published actors do not override authored field spacing")
	check(battlefield.scene_file_path == "res://scenes/ui/components/battlefield.tscn" and actor.scene_file_path == "res://scenes/ui/components/actor_panel.tscn", "battlefield and actors use canonical scenes")
	check(view.scenery.scene_file_path == "res://scenes/ui/components/combat_scenery.tscn" and view.scenery.get_node("Backdrop").mouse_filter == Control.MOUSE_FILTER_IGNORE, "artwork uses separate passive scenery scene")
	var context = load("res://scripts/ui/visual_context.gd").new(null,{"animation_preset":"instant"})
	context.capture_motion()
	var feedback = load("res://scripts/ui/components/combat_feedback.gd").create(context); root.add_child(feedback)
	feedback._float_application({"targetEntityId":"hero","resourceId":"health","previousValue":5,"currentValue":3},art)
	check(feedback.effects.get_child_count() == 0 and feedback._tails.is_empty(), "zero duration creates no decorative node or animation tail")
	check(JSON.stringify([session.run,session.combat,session.legal_actions]) == untouched, "visual binding leaves authoritative snapshots untouched")
	check(root.get_node("GameServices").diagnostics().is_empty(), "visual host binding performs no real engine request")
	feedback.dispose(); feedback.queue_free(); router.queue_free(); await settle()
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
