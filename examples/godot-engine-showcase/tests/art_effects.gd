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
	check(art.texture.resource_path.begins_with("res://assets/ivory/"),"actor preview resolves the actual ivory combat scenery catalog")
	if not output.is_empty() and DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
		var before_image: Image = panel.get_texture().get_image()
		player._bind(); await settle(); await RenderingServer.frame_post_draw
		var image: Image = panel.get_texture().get_image()
		var first: Rect2 = art.get_global_rect()
		var maximum := 0.
		for y in range(20,220,10):
			for x in range(20,220,10):
				var a := before_image.get_pixel(int(first.position.x)+x,int(first.position.y)+y)
				var b := image.get_pixel(int(first.position.x)+x,int(first.position.y)+y)
				maximum = maxf(maximum,absf(a.r-b.r)+absf(a.g-b.g)+absf(a.b-b.b))
		check(maximum < .03,"neutral shader preserves original artwork instead of multiplying its texture twice")
		player.clear()
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
	art.set_art_outline("available")
	var outline_material: ShaderMaterial = art.material
	check(float(outline_material.get_shader_parameter("outline_width")) == player.style.available_width and player.tween == null,"available outline is static, no animation wait")
	art.set_art_outline("selected")
	check(art.material == outline_material and float(art.material.get_shader_parameter("outline_width")) == player.style.selected_width,"selected outline changes color/width without allocating another material")
	var outlined: Rect2 = art.get_global_rect()
	art.size += Vector2(40,20); await settle()
	check(art.material.get_shader_parameter("quad_size").x > 0 and art.mouse_filter == Control.MOUSE_FILTER_IGNORE,"outline geometry updates on resize without capturing input")
	art.size = outlined.size; await settle()
	if not output.is_empty() and DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
		check(panel.get_texture().get_image().save_png(output.path_join("outline.png")) == OK,"rendered outline capture saved")
	art.play_art_effect("impact",.05); await create_timer(.1).timeout
	check(art.material == outline_material and player.tween == null,"finishing an impact preserves a persistent target outline")
	art.set_art_outline("")
	check(art.material == previous and art.get_global_rect() == rect,"clearing outline restores original material and preserves original input rect")
	art.set_art_outline("selected"); panel.reduced.set_pressed_no_signal(true); panel.picker.select(1); panel.play_effect()
	check(player.tween == null and art.material is ShaderMaterial,"reduced motion preserves static outline rather than blinking")
	panel.reset()
	panel.picker.select(3); panel.picker.item_selected.emit(3); await settle()
	var card: CardView = panel.card_sample
	var card_art = card.artwork()
	var model_json := JSON.stringify(card.model)
	var effect_text: String = card.find_child("Rules",true,false).text
	var border: Color = card.get_meta("semantic_border_color")
	check(panel.card_stage.visible and not panel.actor_stage.visible and card is CardView,"improvement selector shows shared production CardViews, not a separate card drawing")
	panel.reduced.set_pressed_no_signal(false)
	panel.play_effect()
	var sheen: Tween = card_art.art_effects.tween
	check(sheen != null and card_art.material is ShaderMaterial and card.material == null and panel.card_baseline.artwork().material == null,"improvement affects only preview artwork, never cost/text/rarity or baseline")
	sheen.pause(); sheen.custom_step(panel.seconds.value * .5)
	check(is_equal_approx(float(card_art.material.get_shader_parameter("sheen_progress")),.5),"sheen uses explicit progress and does not depend on TIME")
	card.configure(card.model); await settle()
	check(card.artwork() == card_art and is_equal_approx(float(card_art.material.get_shader_parameter("sheen_progress")),.5),"reconciliation does not restart or detach an active cosmetic shader")
	if not output.is_empty() and DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
		check(panel.get_texture().get_image().save_png(output.path_join("sheen.png")) == OK,"rendered improvement capture saved")
	panel.reset()
	check(card_art.material == null and JSON.stringify(card.model) == model_json and card.find_child("Rules",true,false).text == effect_text and card.get_meta("semantic_border_color") == border,"reset preserves card data, rules text, cost and rarity color")
	panel.reduced.set_pressed_no_signal(true); panel.play_effect()
	check(card_art.art_effects.tween == null and card_art.material == null,"reduced motion improvement is skipped without material or wait")
	panel.reduced.set_pressed_no_signal(false); panel.seconds.value = 0; panel.play_effect()
	check(card_art.art_effects.tween == null,"zero-second improvement is instant")
	panel.seconds.value = .2; panel.play_effect()
	var paused_tween: Tween = card_art.art_effects.tween
	paused_tween.pause(); paused_tween.custom_step(.05); paused_tween.play()
	var frozen: float = card_art.material.get_shader_parameter("sheen_progress")
	paused = true; await create_timer(.06).timeout
	check(is_equal_approx(float(card_art.material.get_shader_parameter("sheen_progress")),frozen),"real SceneTree pause freezes the shader's bound Tween")
	paused = false; await create_timer(.3).timeout
	check(card_art.material == null,"resume completes sheen and restores material")
	art.play_art_effect("impact",.2); card_art.play_art_effect("sheen",.2)
	check(art.material != card_art.material and art.material.shader == card_art.material.shader,"concurrent artwork shares shader code but never mutable material state")
	panel.hide(); await settle()
	check(art.material == previous and card_art.material == null,"closing preview cancels all active shared executors")
	var empty = load("res://scripts/ui/art_slot.gd").new(context); root.add_child(empty)
	check(empty.play_art_effect("impact",.2) == null,"missing texture never creates an empty animation")
	empty.queue_free()
	check(JSON.stringify([session.run,session.combat,services.diagnostics()]) == original,"laboratory does not change run/combat or call REST")
	panel.queue_free(); await settle()
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
