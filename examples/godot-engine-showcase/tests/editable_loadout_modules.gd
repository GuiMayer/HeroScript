extends SceneTree
var failures := 0
func _init(): call_deferred("run")
func check(ok: bool, label: String):
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func run():
	var requests = root.get_node("GameServices").diagnostics().size()
	var context = load("res://scripts/ui/visual_context.gd").new()
	var view = load("res://scripts/ui/loadout/deck_module_view.gd").create()
	view.mount(context); root.add_child(view); view.mount(context)
	var model := {"cards":[{"identity":{"definitionId":"basic_attack","cardInstanceId":"one"},"effects":[],"costs":[],"conditions":[],"tags":[]}], "enabled":true, "canChange":true, "context":["run","hero"], "allocations":{}, "profile":{"compartments":[{"compartmentId":"utility","nameKey":"Utility"}]}}
	var untouched := JSON.stringify(model)
	view.present(model)
	var card = view._card_cache.one
	var search = view.get_node("Body/CardBody/Tools/Search")
	view.get_node("Subtabs/Compartments").pressed.emit()
	check(view._card_cache.one == card and view.get_node("Body/CardBody/Tools/Search") == search, "tab switch retains card components and authored tools")
	check(view.get_node("Subtabs/Cards").pressed.get_connections().size() == 1, "remount retains one subtab intent")
	view.get_node("Body/CardBody/Scroll/Cards").add_theme_constant_override("h_separation", 27)
	view._render()
	check(view.get_node("Body/CardBody/Scroll/Cards").get_theme_constant("h_separation") == 27, "model rendering preserves authored card grid spacing")
	check(not view._accepts({"kind":"loadout_compartment","context":["foreign"],"cardInstanceId":"one"}), "compartment drop cannot cross context")
	view._sub_buttons.cards.pressed.emit(); card.pressed.emit()
	var dialogs = view.find_children("*", "AcceptDialog", true, false)
	check(dialogs.size() == 1 and dialogs[0].scene_file_path == "res://scenes/ui/overlays/inspection_dialog.tscn", "card inspection uses canonical details overlay")
	check(JSON.stringify(model) == untouched, "rendering leaves authoritative input untouched")
	var summary = load("res://scripts/ui/loadout/loadout_summary_view.gd").create(); summary.mount(context); root.add_child(summary)
	var button = summary.get_node("Body/Overview/Deck")
	summary.present({"cards":1}); summary.present({"cards":2})
	check(summary.get_node("Body/Overview/Deck") == button and button.text.ends_with("2"), "summary updates values without rebuilding authored navigation")
	check(requests == root.get_node("GameServices").diagnostics().size(), "passive loadout modules do not issue engine requests")
	view.queue_free(); summary.queue_free(); await process_frame
	root.get_node("GameAudio").shutdown(); quit(1 if failures else 0)
