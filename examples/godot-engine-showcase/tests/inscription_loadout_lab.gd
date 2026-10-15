extends SceneTree
## Offline draft/view certification. No engine access, inventory changes or preference writes.
var failures := 0
var output_dir := ""
func _init() -> void: call_deferred("_run")
func check(ok: bool, label: String) -> void:
	print("[PASS] " if ok else "[FAIL] ", label)
	if not ok: failures += 1
func settle() -> void:
	for _frame in 10: await process_frame
func attribute(model, id: String) -> Dictionary:
	for stat in model.snapshot().character.attributes:
		if stat.id == id: return stat
	return {}
func _run() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--lab-output="): output_dir = argument.trim_prefix("--lab-output=")
	var prefs = root.get_node("Preferences")
	var translator = root.get_node("I18n")
	var session = root.get_node("GameSession")
	var services = root.get_node("GameServices")
	var before_run := JSON.stringify([session.run, session.combat])
	var before_requests: int = services.diagnostics().size()
	var before_config := FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""
	var prior := {"scale": prefs.text_scale, "locale": translator.locale, "profile": AppTheme.profile}
	var fixture: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/inscription_loadout_lab.json"))
	var model = load("res://scripts/labs/inscription_loadout_draft.gd").new()
	check(model.configure(fixture).is_empty(), "fixture has separate identities and valid visual references")
	var original: Dictionary = model.snapshot()
	check(original.slots.size() == 6 and original.items.size() == fixture.inscriptions.size() and original.usedCapacity == 10,
		"six data-driven positions and varied inscription examples")
	check(attribute(model, "max_health").value == 100 and attribute(model, "max_energy").value == 3 and attribute(model, "dodge").value == 0, "sample survival stats include zero default dodge")
	check(attribute(model, "critical_chance").base == 25 and attribute(model, "critical_chance").value == 50 and attribute(model, "critical_damage").base == 100, "character critical chance uses explicit points; critical bonus defaults to 100 percent")
	model.select_item("sample_force"); model.select_slot("trace_2"); model.stage_selected()
	check(attribute(model, "direct_bonus").value == 35 and attribute(model, "critical_chance").value == 35, "tradeoff modifies character attributes without editing card values")
	model.restore()
	model.select_item("sample_focus")
	check(attribute(model, "critical_chance").value == 50, "selecting an inscription does not equip or add its bonus")
	model.clear()
	for pair in [["sample_vitality","trace_1"],["sample_energy","trace_2"],["sample_dodge","trace_3"],["sample_critical_damage","trace_4"],["sample_fire","trace_5"],["sample_poison","trace_6"]]:
		model.select_item(pair[0]); model.select_slot(pair[1]); model.stage_selected()
	check(attribute(model, "max_health").value == 125 and attribute(model, "max_energy").value == 4 and attribute(model, "dodge").value == 5 and attribute(model, "critical_damage").value == 150, "sample gear updates health, energy, dodge and critical bonus independently")
	check(attribute(model, "fire_bonus").value == 25 and attribute(model, "poison_bonus").value == 25 and attribute(model, "cold_bonus").value == 0, "tag affinities are separate character attributes")
	model.clear()
	check(attribute(model, "max_health").value == 100 and attribute(model, "fire_bonus").value == 0, "removing gear returns sample attributes to base without drift")
	model.select_item("sample_armor"); model.select_slot("trace_1"); model.stage_selected()
	check(attribute(model, "armor").value == 20 and attribute(model, "dodge").value == 0, "armor is an attribute, not a fabricated damage reduction or dodge chance")
	model.restore()
	check(original.selectedItem.is_empty() and original.selectedSlot.is_empty(), "no default inscription or slot selection")
	model.select_item("sample_focus"); model.select_slot("trace_6"); model.stage_selected()
	var moved: Dictionary = model.snapshot()
	check(moved.assignments.get("trace_6") == "sample_focus" and not moved.assignments.has("trace_1") and moved.assignments.size() == original.assignments.size(), "moving an inscription never duplicates its UI instance")
	var external: Dictionary = model.snapshot()
	external.items.clear(); external.assignments.clear()
	check(model.snapshot().items.size() == fixture.inscriptions.size() and model.snapshot().assignments.size() == 4, "projection copies do not expose mutable draft internals")
	model.clear()
	for index in 6:
		model.select_item(fixture.inscriptions[index].instanceId)
		model.select_slot(fixture.slots[index]); model.stage_selected()
	check(model.snapshot().overBudget and model.snapshot().assignments.size() == 6, "over-budget visual draft stays editable; no fake engine legality")
	var selected: String = model.selected_item
	model.select_item("Crimson covenant")
	check(model.selected_item == selected and model.snapshot().items.size() == fixture.inscriptions.size(), "unrelated item identities cannot enter inscription inventory")
	model.restore()
	check(model.snapshot().assignments == original.assignments, "restoring an example only restores local draft")
	var invalid: Dictionary = fixture.duplicate(true)
	invalid.inscriptions.append(invalid.inscriptions[0].duplicate(true))
	check(not model.configure(invalid).is_empty() and model.snapshot().assignments == original.assignments, "invalid fixture fails without partially replacing a draft")
	invalid = fixture.duplicate(true)
	invalid.inscriptions[0].attributeBonuses[0].attributeId = "unknown_stat"
	check(not model.configure(invalid).is_empty() and model.snapshot().assignments == original.assignments, "unknown attribute bonus is rejected without replacing the draft")
	var character_copy: Dictionary = model.snapshot().character
	character_copy.attributes[0].value = 9999
	check(attribute(model, "max_health").value == 100 and fixture.character.attributes[0].base == 100, "sample projections cannot mutate authored bases or future snapshots")
	var high_crit: Dictionary = fixture.duplicate(true)
	for stat in high_crit.character.attributes:
		if stat.id == "critical_chance": stat.base = 175
	var high_model = load("res://scripts/labs/inscription_loadout_draft.gd").new()
	check(high_model.configure(high_crit).is_empty() and attribute(high_model, "critical_chance").value == 200, "display preserves chances above 100 percent without adding a gameplay cap")
	check(model.stage_drop("sample_focus", "trace_2", "trace_1"), "drop moves a local inscription")
	check(model.snapshot().assignments.trace_1 == "sample_edge" and model.snapshot().assignments.trace_2 == "sample_focus", "occupied positions swap without losing either inscription")
	var before_invalid: Dictionary = model.snapshot().assignments
	check(not model.stage_drop("sample_focus", "trace_6", "trace_1") and model.snapshot().assignments == before_invalid, "stale source rejected atomically")
	check(model.stage_drop("sample_renewal", "trace_2", "") and not "sample_focus" in model.snapshot().assignments.values(), "inventory replacement returns displaced item to unequipped inventory")
	model.restore()
	for resolution in [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1080)]:
		root.size = resolution
		root.content_scale_size = resolution
		for language in ["en", "pt_BR"]:
			translator.set_locale(language)
			prefs.text_scale = 1.2 if language == "pt_BR" else 1.0
			var lab = load("res://scenes/inscription_loadout_lab.tscn").instantiate()
			root.add_child(lab)
			await settle()
			check(lab.fixture_error.is_empty() and lab.view._inventory.size() == fixture.inscriptions.size() and lab.view._positions.size() == 6, "scene builds from JSON: %s / %s" % [resolution, language])
			check(lab.find_child("LoadoutRelicReferences", true, false) == null and lab.find_child("LoadoutModifierReferences", true, false) == null, "right panel is dedicated to character attributes, not other systems")
			check(lab.view.attributes_view._rows.critical_chance.base.text == "25%" and lab.view.attributes_view._rows.critical_chance.equipped.text == "50%", "character panel shows base and equipped values separately")
			lab.draft.clear(); lab.draft.select_item("sample_force"); lab.draft.select_slot("trace_1"); lab.draft.stage_selected(); lab._refresh()
			check(lab.view.attributes_view._rows.critical_chance.equipped.text == "10%" and lab.view.attributes_view._rows.critical_chance.equipped.get_theme_color("font_color") == lab.visual_context.styles.color("accent.danger"), "reduced character attributes show the actual result with a penalty color")
			lab.draft.restore(); lab._refresh()
			check(lab.view.equip_button.disabled and not lab.view.details.text.is_empty(), "initial detail prompt has no implicit equip action")
			var source = lab.view._inventory.sample_renewal
			var target = lab.view._positions.trace_6
			var payload: Dictionary = source.drag_payload()
			var board_position: Vector2 = target.global_position
			lab.view.inventory_scroll.scroll_vertical = 10000
			await settle()
			check(target.global_position == board_position and lab.view.inventory_scroll.get_global_rect().intersects(lab.view._inventory.sample_poison.get_global_rect()), "inventory scroll exposes lower items without moving the sockets")
			lab.view.inventory_scroll.scroll_vertical = 0
			await settle()
			check(target._can_drop_data(Vector2.ZERO, payload) and not source._can_drop_data(Vector2.ZERO, payload), "only positions accept native inscription payloads")
			var foreign: Dictionary = payload.duplicate(); foreign.scope = -1
			check(not target._can_drop_data(Vector2.ZERO, foreign) and not target._can_drop_data(Vector2.ZERO, {"kind": "relic"}), "foreign views and separate item systems cannot be dropped")
			check(target.get_theme_stylebox("normal").bg_color != lab.view._positions.trace_1.get_theme_stylebox("normal").bg_color, "empty sockets have a distinct fill as well as a plus label")
			# Exercise native viewport drag/drop dispatch with real pointer events.
			var scroll_for_drag: ScrollContainer = lab.find_child("LoadoutPageScroll", true, false)
			scroll_for_drag.scroll_vertical = maxi(0, int(target.global_position.y + target.size.y - root.size.y + 40))
			lab.view.inventory_scroll.scroll_vertical = maxi(0, int(source.position.y - 30))
			await settle()
			if resolution == Vector2i(1920, 1080) and language == "en":
				var start := InputEventMouseMotion.new()
				start.position = source.get_global_rect().get_center()
				root.push_input(start)
				await settle()
				var press := InputEventMouseButton.new()
				press.button_index = MOUSE_BUTTON_LEFT
				press.position = start.position
				press.pressed = true
				root.push_input(press)
				await settle()
				var drag := InputEventMouseMotion.new()
				drag.position = start.position + Vector2(30, 0)
				drag.relative = Vector2(30, 0)
				drag.button_mask = MOUSE_BUTTON_MASK_LEFT
				root.push_input(drag)
				await settle()
				check(root.gui_is_dragging(), "mouse press and motion start drag on the actual inventory tile")
			else:
				source.force_drag(payload, null)
			var pointer := InputEventMouseMotion.new()
			pointer.position = target.get_global_rect().get_center()
			root.push_input(pointer)
			await settle()
			if not output_dir.is_empty() and DisplayServer.get_name() != "headless" and resolution == Vector2i(1920, 1080) and language == "en":
				await RenderingServer.frame_post_draw
				check(root.get_texture().get_image().save_png(output_dir.path_join("loadout-drag-preview.png")) == OK, "saved native drag preview and target feedback")
			var release := InputEventMouseButton.new()
			release.button_index = MOUSE_BUTTON_LEFT
			release.position = pointer.position
			release.pressed = false
			root.push_input(release)
			await settle()
			check(not root.gui_is_dragging() and lab.draft.snapshot().assignments.get("trace_6") == "sample_renewal", "viewport delivers native drag/drop into the draft")
			check(not target._can_drop_data(Vector2.ZERO, payload), "payload cannot be replayed after draft mutation")
			var before_cancel: Dictionary = lab.draft.snapshot().assignments
			source.force_drag(source.drag_payload(), null)
			var outside := InputEventMouseMotion.new()
			outside.position = Vector2(2, 2)
			root.push_input(outside)
			await settle()
			release.position = outside.position
			root.push_input(release)
			await settle()
			check(not root.gui_is_dragging() and lab.draft.snapshot().assignments == before_cancel, "dropping outside cancels without losing an inscription")
			lab.draft.restore(); lab._refresh()
			scroll_for_drag.scroll_vertical = 0
			lab.view.inventory_scroll.scroll_vertical = 0
			await settle()
			if not output_dir.is_empty() and DisplayServer.get_name() != "headless":
				lab.view._inventory.sample_focus.pressed.emit()
				await settle()
				await RenderingServer.frame_post_draw
				check(root.get_texture().get_image().save_png(output_dir.path_join("loadout-%s-%sx%s.png" % [language, resolution.x, resolution.y])) == OK, "saved rendered loadout capture")
			var item_button: Button = lab.view._inventory.sample_renewal
			item_button.grab_focus(); item_button.pressed.emit()
			lab.view._positions.trace_6.pressed.emit()
			lab.view.equip_button.pressed.emit()
			await settle()
			check(lab.draft.snapshot().assignments.get("trace_6") == "sample_renewal", "real view intents stage one local assignment")
			check(lab.view._inventory.sample_renewal == item_button and item_button.has_focus(), "draft changes preserve controls and focus")
			check(lab.view.details.text.contains(translator.text("Renewal script")) and lab.view.details.text.contains(translator.text("Character attribute bonuses")) and lab.view.attributes_view._rows.healing_bonus.equipped.text == "30%", "inscription details explain character bonuses and equipment updates the panel")
			check(preload("res://tests/layout_inspector.gd").vertical_text_issues(lab).is_empty(), "no compressed vertical labels: %s / %s" % [resolution, language])
			var scroll: ScrollContainer = lab.find_child("LoadoutPageScroll", true, false)
			check(scroll.get_h_scroll_bar().max_value <= scroll.get_h_scroll_bar().page + 1, "layout fits width; only vertical scrolling is needed")
			lab.view.search_field.text = translator.text("Dense script")
			lab.view.search_field.text_changed.emit(lab.view.search_field.text)
			await settle()
			var visible_count := 0
			for button in lab.view._inventory.values():
				if button.visible: visible_count += 1
			check(visible_count == 1, "search uses localized labels without changing ownership")
			lab.view.remove_button.pressed.emit()
			check(not lab.draft.snapshot().assignments.has("trace_6"), "remove changes only the selected draft position")
			lab.queue_free(); await settle()
	translator.set_locale(prior.locale); prefs.text_scale = prior.scale
	var router = load("res://main.tscn").instantiate()
	root.add_child(router); await settle()
	var open_button: Button = router.host.find_child("LoadoutLabButton", true, false)
	check(open_button != null and not open_button.disabled, "loadout laboratory is accessible from the main menu offline")
	open_button.pressed.emit(); await settle()
	check(router.current_screen == "loadout_lab" and router.host.get_child(0).view != null, "main menu mounts the separate laboratory scene")
	router.host.find_child("LoadoutBackButton", true, false).pressed.emit(); await settle()
	check(router.current_screen == "menu", "Back returns to menu without entering a run")
	router.queue_free(); await settle()
	check(JSON.stringify([session.run, session.combat]) == before_run and services.diagnostics().size() == before_requests, "no run mutation or engine request during the entire laboratory test")
	check(before_config == (FileAccess.get_file_as_string(prefs.SAVE_PATH) if FileAccess.file_exists(prefs.SAVE_PATH) else ""), "laboratory never saves player preferences")
	check(AppTheme.profile == prior.profile, "laboratory never leaks a global theme change")
	root.get_node("GameAudio").shutdown()
	print("LOADOUT LAB: ", failures, " failure(s)")
	quit(0 if failures == 0 else 1)
