extends SceneTree
var failures := 0
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _i in 10: await process_frame
func _run() -> void:
	var session = root.get_node("GameSession")
	var router = load("res://main.tscn").instantiate(); root.add_child(router)
	var connected: bool = await session.connect_engine("default")
	check(connected, "connect isolated QA")
	if connected:
		for setting in ["default", "ascendant", "volatile-core"]:
			check(session.select_setting(setting), "select " + setting)
			var opened: bool = await session.start_campaign(int(Time.get_unix_time_from_system()) + randi_range(100000, 999999))
			check(opened, "create QA run " + setting)
			if not opened: continue
			var entry: Dictionary = session.command("START_ENCOUNTER")
			check(not entry.is_empty() and await session.execute_run_command("START_ENCOUNTER", entry.get("validPayload", {})), "enter " + setting)
			router.open_game(); await settle()
			var screen = router.host.get_child(0)
			var zones: Array = session.card_zones.get("zones", []).filter(func(zone): return not zone.get("allowsCardPlay", false) and zone.get("contentsVisible", false) and not zone.get("cards", []).is_empty())
			check(not zones.is_empty(), "visible collection exists " + setting)
			if zones.is_empty(): continue
			var before := JSON.stringify([session.run, session.combat]); var selected: String = screen.selected_card
			screen._open_collection(load("res://scripts/presentation/card_collection_presenter.gd").address(zones[0])); await settle()
			var dialog = screen._collection_dialog
			check(dialog.collection.cards.size() == zones[0].cards.size() and screen.view.find_child("HandOverviewButton", true, false) == null, "faces replace list and overview is absent " + setting)
			var id: String = str(zones[0].cards[0].cardInstanceId)
			dialog.collection.cards[id].pressed.emit()
			for _attempt in 200:
				await create_timer(.025).timeout
				if dialog.detail != null and dialog.detail.find_child("CollectionInspectionStatus", true, false).text != "Loading details…": break
			check(not screen.presenter.inspection_data(id).is_empty(), "single canonical evaluation enriches collection " + setting)
			check(before == JSON.stringify([session.run, session.combat]) and screen.selected_card == selected, "reading neither mutates run nor combat selection " + setting)
			dialog.queue_free(); await settle()
			var replay: Dictionary = await session.verify()
			check(replay.ok and replay.data.get("isValid", false), "inspection leaves replay deterministic " + setting)
	router.queue_free(); await settle(); root.get_node("GameAudio").shutdown(); await create_timer(.08).timeout
	quit(0 if failures == 0 else 1)
