extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ",label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 10: await process_frame
func _run() -> void:
	var preferences = root.get_node("Preferences")
	var config_before := FileAccess.get_file_as_string(preferences.SAVE_PATH) if FileAccess.file_exists(preferences.SAVE_PATH) else ""
	var session = root.get_node("GameSession")
	var services = root.get_node("GameServices")
	var original := JSON.stringify([session.run,session.combat,services.diagnostics()])
	var lab = load("res://scenes/card_hand_lab.tscn").instantiate(); root.add_child(lab); await settle()
	root.size = Vector2i(1280,720)
	for locale in ["en","pt_BR"]:
		lab.set_language(locale); lab.select_card(str(lab.models[1].cardInstanceId)); await settle()
		var button: Button = lab.find_child("OpenArtEffectLab",true,false)
		check(button.text == ("Laboratório de efeitos" if locale == "pt_BR" else "Art effects lab"),"laboratory entry is localized: " + locale)
		button.pressed.emit(); await settle()
		var panel = lab.get_node("ArtEffectLab")
		check(panel.card_sample.model.definitionId == lab.selected_model().definitionId,"preview uses selected real card model: " + locale)
		check(panel.visible and panel.picker.item_count == 4,"three effects and two outline states are selectable: " + locale)
		for index in range(panel.picker.item_count):
			panel.picker.select(index); panel.picker.item_selected.emit(index); panel.play_effect(); await settle()
			check(panel.size.x <= root.size.x and panel.size.y <= root.size.y,"preview controls remain within 1280x720: %s/%s" % [locale,index])
			panel.reset()
		check(panel.seconds.editable and panel.card_stage.visible,"sheen has editable seconds and actual card comparison: " + locale)
		panel.hide(); await settle()
		button.pressed.emit(); await settle()
		check(lab.get_children().filter(func(child): return child.name == "ArtEffectLab").size() == 1,"reopening replaces the old preview without leaking windows: " + locale)
		lab.get_node("ArtEffectLab").hide()
	check(JSON.stringify([session.run,session.combat,services.diagnostics()]) == original,"real lab flow has no REST calls or run mutations")
	lab.queue_free(); await settle()
	var config_after := FileAccess.get_file_as_string(preferences.SAVE_PATH) if FileAccess.file_exists(preferences.SAVE_PATH) else ""
	check(config_before == config_after,"lab effect controls do not save user preferences")
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
