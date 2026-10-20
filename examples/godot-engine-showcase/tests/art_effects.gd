extends SceneTree
var failures := 0
var output := ""
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ",label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 8: await process_frame
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output = argument.trim_prefix("--lab-output=")
	var services = root.get_node("GameServices")
	var session = root.get_node("GameSession")
	var original := JSON.stringify([session.run,session.combat,services.diagnostics()])
	var context = load("res://scripts/ui/visual_context.gd").new(root.get_node("I18n"),{},load("res://data/themes/ivory_theme.tres"))
	var panel = load("res://scripts/labs/art_effect_lab.gd").new(); panel.setup(context)
	root.add_child(panel); panel.popup_centered_clamped(Vector2i(900,540),.9); await settle()
	var art = panel.sample
	var previous := CanvasItemMaterial.new(); art.material = previous
	var rect: Rect2 = art.get_global_rect()
	var player = art.effect_player()
	check(art.get_script() == load("res://scripts/ui/art_slot.gd"),"lab uses the production actor art component")
	check(player.play("impact",0,.7) == null and art.material == previous,"zero duration has no tween, material or wait")
	check(player.play("impact",.2,.7,true) == null and player.play("impact",.2,.7,false,true) == null,"reduced motion and instant produce no animation")
	check(player.play("impact",.2,0) == null and player.play("unknown",.2,.7) == null,"disabled/unknown effect is an immediate no-op")
	var tween: Tween = art.play_art_effect("impact",.2)
	check(tween != null and art.material is ShaderMaterial and panel.baseline.material == null,"only preview receives its own feedback material")
	tween.pause(); tween.custom_step(.1)
	var value: float = art.material.get_shader_parameter("impact_amount")
	check(value > .6 and art.get_global_rect() == rect and art.mouse_filter == Control.MOUSE_FILTER_IGNORE,"midpoint flash preserves input and geometry")
	await create_timer(.05).timeout
	check(is_equal_approx(float(art.material.get_shader_parameter("impact_amount")),value),"paused preview freezes explicit shader progress")
	if not output.is_empty() and DisplayServer.get_name() != "headless":
		DirAccess.make_dir_recursive_absolute(output); await RenderingServer.frame_post_draw
		check(panel.get_texture().get_image().save_png(output.path_join("impact.png")) == OK,"rendered impact capture saved")
	tween.play(); await create_timer(.2).timeout
	check(art.material == previous and player.tween == null,"completion restores original material and releases tween")
	art.play_art_effect("impact",.2); player.clear(); player.clear()
	check(art.material == previous,"cancel/reset is idempotent and restores previous material")
	art.play_art_effect("impact",.2); panel.hide(); await settle()
	check(art.material == previous,"closing laboratory clears a running effect")
	panel.popup_centered(); art.play_art_effect("impact",.2); art.apply_model({"category":"actors","contentId":"another"})
	check(art.material == previous,"changing artwork identity clears stale effects")
	var empty = load("res://scripts/ui/art_slot.gd").new(context); root.add_child(empty)
	check(empty.play_art_effect("impact",.2) == null,"missing texture never creates an empty animation")
	empty.queue_free()
	check(JSON.stringify([session.run,session.combat,services.diagnostics()]) == original,"laboratory does not change run/combat or call REST")
	panel.queue_free(); await settle()
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
