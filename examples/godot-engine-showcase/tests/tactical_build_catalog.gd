extends SceneTree
var failures := 0
func _init() -> void: call_deferred("run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run() -> void:
	var translator = root.get_node("I18n")
	var previous: String = translator.locale
	for language in ["en", "pt_BR"]:
		translator.set_locale(language)
		var lab = load("res://scenes/unified_loadout_lab.tscn").instantiate(); root.add_child(lab)
		await process_frame; await process_frame
		var composition = lab.composition_view
		var support = lab.support_view
		check(composition.scene_file_path.ends_with("deck_composition.tscn"), "catalog reuses the run composition scene: " + language)
		check(support.scene_file_path.ends_with("support_workbench.tscn"), "catalog reuses the implemented support scene: " + language)
		check(composition._cards.size() == 27, "published card instances remain separate in the visual capture")
		check(not composition._model.canChange and not composition._editable(), "offline capture cannot imitate authoritative build writes")
		check(composition._model.selection.groups.hero.size() == 15 and composition._model.selection.groups.module.size() == 5, "catalog displays captured active composition and reserve")
		var cards: Array = composition._cards.values()
		lab.shell.select("companions"); lab.shell.select("deck"); await process_frame
		check(composition._cards.values() == cards, "tab navigation preserves all shared card controls")
		var groups: OptionButton = composition.get_node("Tools/Groups")
		groups.select(1); groups.item_selected.emit(1); await process_frame
		check(composition.get_node("Tools/Modules").item_count == 2, "same module selector displays both published module alternatives")
		check(support.get_node("Equipment/Routine").item_count == 2, "same companion view displays both published routines")
		check(support.get_node("Equipment/Cadence").text.contains("3"), "cadence is displayed from the captured API value")
		check(not support.get_node("Title").text.contains("Planned"), "implemented support is no longer labeled as a future system")
		lab.queue_free(); await process_frame
	translator.set_locale(previous)
	root.get_node("GameAudio").shutdown()
	quit(1 if failures else 0)
