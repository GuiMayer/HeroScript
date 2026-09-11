extends VBoxContainer

var router
var presentation: Dictionary
var selected_target := ""
var selected_card := ""
var hand_row: HBoxContainer
var detail_label: RichTextLabel
var queue_label: Label
var next_frame_button: Button
var actor_portraits: Dictionary = {}

func setup(owner, data: Dictionary) -> void:
	router = owner
	presentation = data
	GameSession.auto_present = false
	GameSession.frame_presented.connect(_on_frame)
	add_theme_constant_override("separation", 12)
	_build_header()
	_build_battlefield()
	_build_hand()
	_build_footer()

func _exit_tree() -> void:
	if GameSession.frame_presented.is_connected(_on_frame):
		GameSession.frame_presented.disconnect(_on_frame)

func _build_header() -> void:
	var row := HBoxContainer.new()
	var phase: Dictionary = GameSession.combat.get("phase", {}) if GameSession.combat.get("phase", {}) is Dictionary else {}
	var activation: Dictionary = GameSession.combat.get("activation", {}) if GameSession.combat.get("activation", {}) is Dictionary else {}
	var label := AppTheme.title("COMBATE  %s" % str(GameSession.combat.get("runNodeId", "")).to_upper(), 25)
	row.add_child(label)
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(push)
	var turn := Label.new()
	turn.text = "RODADA %s   •   %s   •   STEP %s" % [
		int(activation.get("round", GameSession.combat.get("currentTurn", 1))),
		str(phase.get("currentPhaseId", phase.get("phaseId", "resolução"))).replace("_", " ").to_upper(),
		int(GameSession.combat.get("step", 0))]
	turn.add_theme_color_override("font_color", AppTheme.GOLD)
	row.add_child(turn)
	row.add_child(_button("TIMELINE  [%s]" % Preferences.action_label("open_timeline"), router.show_timeline, 165))
	row.add_child(_button("PAUSA", router.toggle_pause, 100))
	add_child(row)

func _build_battlefield() -> void:
	var field := HBoxContainer.new()
	field.size_flags_vertical = Control.SIZE_EXPAND_FILL
	field.add_theme_constant_override("separation", 42)
	var player := GameSession.player_actor()
	field.add_child(_actor_card(player, false))
	var center := VBoxContainer.new()
	center.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	center.alignment = BoxContainer.ALIGNMENT_CENTER
	var sigil := Label.new()
	sigil.text = "✦\n╲  ◆  ╱\n✦"
	sigil.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	sigil.add_theme_font_size_override("font_size", 31)
	sigil.add_theme_color_override("font_color", Color("#77576d"))
	center.add_child(sigil)
	var stack_count: int = GameSession.combat.get("pendingActions", []).size()
	if stack_count > 0:
		center.add_child(AppTheme.muted("STACK: %s ação(ões)" % stack_count))
	field.add_child(center)
	var opponents := GameSession.opponents()
	var enemies := HBoxContainer.new()
	enemies.add_theme_constant_override("separation", 12)
	for opponent in opponents:
		enemies.add_child(_actor_card(opponent, true))
	field.add_child(enemies)
	add_child(field)

func _actor_card(actor: Dictionary, hostile: bool) -> Control:
	var content := VBoxContainer.new()
	content.custom_minimum_size = Vector2(300 if not hostile else 280, 355)
	content.add_theme_constant_override("separation", 7)
	var name := Label.new()
	name.text = str(actor.get("name", actor.get("definitionId", "ator"))).to_upper()
	name.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name.add_theme_font_size_override("font_size", 19)
	name.add_theme_color_override("font_color", AppTheme.BLOOD if hostile else AppTheme.TEAL)
	content.add_child(name)
	var portrait := ActorPortrait.new()
	portrait.custom_minimum_size = Vector2(260, 190)
	portrait.configure(hostile, str(actor.get("definitionId", "")).contains("sentinel"))
	actor_portraits[str(actor.get("instanceId", ""))] = portrait
	content.add_child(portrait)
	var resources: Dictionary = actor.get("resources", {})
	for resource_id in ["health", "block", "energy", "mana"]:
		if resources.has(resource_id):
			content.add_child(_resource(resource_id, resources[resource_id]))
	var statuses: Array = actor.get("statuses", [])
	if not statuses.is_empty():
		var status_text := statuses.map(func(status): return "%s ×%s" % [
			str(status.get("statusId", "status")).capitalize(), status.get("stacks", 1)] )
		var status := AppTheme.muted("  •  ".join(status_text), 13)
		status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		status.add_theme_color_override("font_color", AppTheme.EMBER)
		content.add_child(status)
	if hostile:
		var intent := _intent_for(str(actor.get("instanceId", "")))
		var intent_label := Label.new()
		intent_label.text = "INTENÇÃO  •  %s" % intent
		intent_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		intent_label.add_theme_color_override("font_color", AppTheme.GOLD)
		content.add_child(intent_label)
		var select := _button("MIRAR", func(): _select_target(str(actor.get("instanceId", ""))), 0)
		select.button_pressed = selected_target == str(actor.get("instanceId", ""))
		content.add_child(select)
	return AppTheme.panel(content, Color("#261923e8") if hostile else Color("#15252ce8"))

func _resource(id: String, state: Dictionary) -> Control:
	var row := HBoxContainer.new()
	var label := Label.new()
	label.text = {"health": "VIDA", "block": "BLOQUEIO", "energy": "ENERGIA", "mana": "MANA"}.get(id, id.to_upper())
	label.custom_minimum_size.x = 78
	label.add_theme_font_size_override("font_size", 12)
	row.add_child(label)
	var bar := ProgressBar.new()
	bar.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bar.custom_minimum_size.y = 22
	bar.min_value = float(state.get("minimum", 0))
	bar.max_value = maxf(float(state.get("maximum", 1)), 1.0)
	bar.value = float(state.get("current", 0))
	bar.show_percentage = false
	var tone: Color = {"health": AppTheme.BLOOD, "block": Color("#7e9fbd"), "energy": AppTheme.GOLD, "mana": Color("#8c75dc")}.get(id, AppTheme.TEAL)
	bar.add_theme_stylebox_override("fill", AppTheme.box(tone, 5))
	row.add_child(bar)
	var amount := Label.new()
	amount.text = "%s/%s" % [int(state.get("current", 0)), int(state.get("maximum", 0))]
	amount.custom_minimum_size.x = 65
	amount.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	row.add_child(amount)
	return row

func _build_hand() -> void:
	var section := VBoxContainer.new()
	section.add_theme_constant_override("separation", 8)
	var heading := HBoxContainer.new()
	heading.add_child(AppTheme.title("MÃO", 18, AppTheme.GOLD))
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	heading.add_child(push)
	var hint := AppTheme.muted("Selecione um alvo e jogue uma carta. Toda prévia veio da engine.")
	hint.custom_minimum_size.x = 500
	hint.autowrap_mode = TextServer.AUTOWRAP_OFF
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	heading.add_child(hint)
	section.add_child(heading)
	hand_row = HBoxContainer.new()
	hand_row.alignment = BoxContainer.ALIGNMENT_CENTER
	hand_row.add_theme_constant_override("separation", 10)
	var hand_ids: Array = GameSession.run.get("deck", {}).get("handInstanceIds", [])
	for id in hand_ids:
		var card := _card_instance(str(id))
		if not card.is_empty():
			hand_row.add_child(_card_button(card))
	if hand_ids.is_empty():
		hand_row.add_child(AppTheme.muted("A mão está vazia."))
	section.add_child(hand_row)
	add_child(AppTheme.panel(section, Color("#151625e8")))

func _card_button(card: Dictionary) -> Button:
	var id := str(card.get("definitionId", ""))
	var info: Dictionary = presentation.get("cards", {}).get(id, {})
	var value := Button.new()
	value.custom_minimum_size = Vector2(188, 146)
	value.text = "%s      %s\n\n%s\n\n%s" % [
		str(info.get("name", id)).to_upper(), int(info.get("cost", 0)),
		str(info.get("text", "Componente data-driven.")),
		"+" if not card.get("upgrades", []).is_empty() else ""]
	value.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	var tone := str(info.get("tone", "skill"))
	var color := AppTheme.BLOOD if tone == "attack" else (Color("#8c75dc") if tone == "power" else AppTheme.TEAL)
	value.add_theme_stylebox_override("normal", AppTheme.box(color.darkened(.62), 10, color.darkened(.12), 2))
	value.add_theme_stylebox_override("hover", AppTheme.box(color.darkened(.42), 10, AppTheme.GOLD, 3))
	var instance_id := str(card.get("cardInstanceId", ""))
	var candidate := _candidate_for_card(instance_id)
	var preview := _preview(candidate)
	if not preview.is_empty():
		value.text += "\n%s" % preview
	value.disabled = candidate.is_empty()
	value.pressed.connect(func(): GameAudio.card(); _play_candidate(instance_id))
	return value

func _build_footer() -> void:
	var row := HBoxContainer.new()
	detail_label = RichTextLabel.new()
	detail_label.bbcode_enabled = true
	detail_label.fit_content = true
	detail_label.custom_minimum_size = Vector2(500, 46)
	detail_label.text = "[color=#a9a0a8]Hash[/color]  %s…" % str(GameSession.combat.get("stateHash", "")).left(20)
	row.add_child(detail_label)
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(push)
	queue_label = Label.new()
	queue_label.text = _queue_text()
	queue_label.add_theme_color_override("font_color", AppTheme.TEAL)
	row.add_child(queue_label)
	next_frame_button = _button("PRÓXIMO FRAME  [%s]" % Preferences.action_label("confirm_action"), _next_frame, 220)
	next_frame_button.visible = GameSession.presentation_index < GameSession.presentation_queue.size()
	row.add_child(next_frame_button)
	var pass_candidate := _system_candidate(["PASS", "PASS_PRIORITY"])
	if not pass_candidate.is_empty():
		row.add_child(_button("PASSAR PRIORIDADE", func(): _execute_system(pass_candidate), 205))
	row.add_child(_button("ENCERRAR TURNO  [%s]" % Preferences.action_label("end_turn"), _end_turn, 220))
	add_child(row)

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("end_turn"):
		_end_turn()
	elif event.is_action_pressed("confirm_action"):
		_next_frame()

func _select_target(actor_id: String) -> void:
	selected_target = actor_id
	router.show_toast("Alvo: %s" % actor_id)
	router.show_combat()

func _play_candidate(instance_id: String) -> void:
	var candidate := _candidate_for_card(instance_id)
	if candidate.is_empty():
		router.show_error("A engine não considera esta carta legal agora.")
		return
	var command: Dictionary = candidate.get("command", {})
	if not selected_target.is_empty():
		var matching := _candidate_for_card(instance_id, selected_target)
		if not matching.is_empty():
			candidate = matching
			command = candidate.get("command", {})
	var payload := _command_payload(command)
	if await GameSession.execute_combat_command("PLAY_CARD", payload,
		str(candidate.get("cardDefinitionId", "Carta"))):
		GameAudio.hit()
		router.show_combat()

func _end_turn() -> void:
	var actor := GameSession.input_actor_id()
	if actor.is_empty():
		return
	var candidate := _system_candidate(["END_TURN"])
	var payload := _command_payload(candidate.get("command", {})) if not candidate.is_empty() else {"actorId": actor}
	if await GameSession.execute_combat_command("END_TURN", payload, "Encerrar turno"):
		router.open_game()

func _execute_system(candidate: Dictionary) -> void:
	var command: Dictionary = candidate.get("command", {})
	if await GameSession.execute_combat_command("EXECUTE_ACTION", _command_payload(command), "Passar prioridade"):
		router.open_game()

func _next_frame() -> void:
	if not GameSession.present_next_frame():
		next_frame_button.visible = false
	queue_label.text = _queue_text()
	next_frame_button.visible = GameSession.presentation_index < GameSession.presentation_queue.size()

func _on_frame(frame: Dictionary, index: int, total: int) -> void:
	var label := str(frame.get("kind", frame.get("type", frame.get("label", "resolução"))))
	detail_label.text = "[color=#ee8653]FRAME %s/%s[/color]  %s" % [index + 1, total, label]
	var target := str(frame.get("targetId", frame.get("actorId", "")))
	if actor_portraits.has(target):
		actor_portraits[target].flash_hit()

func _queue_text() -> String:
	var remaining := GameSession.presentation_queue.size() - GameSession.presentation_index
	return "%s frame(s) na apresentação" % maxi(remaining, 0)

func _candidate_for_card(instance_id: String, target_id := "") -> Dictionary:
	for candidate in GameSession.legal_actions:
		var command: Dictionary = candidate.get("command", {})
		if str(command.get("cardInstanceId", "")) != instance_id:
			continue
		var targets: Array = command.get("targetIds", [])
		if target_id.is_empty() or target_id in targets or str(command.get("targetId", "")) == target_id:
			return candidate
	return {}

func _system_candidate(action_types: Array) -> Dictionary:
	for candidate in GameSession.legal_actions:
		if str(candidate.get("source", "")) == "System" and \
			str(candidate.get("command", {}).get("actionType", "")) in action_types:
			return candidate
	return {}

func _preview(candidate: Dictionary) -> String:
	if candidate.is_empty():
		return ""
	for application in candidate.get("applications", []):
		var before := float(application.get("previousValue", 0))
		var after := float(application.get("currentValue", before))
		var delta := after - before
		if absf(delta) > .001 and str(application.get("targetEntityId", "")) != GameSession.input_actor_id():
			return "Prévia: %s%s %s" % ["+" if delta > 0 else "", int(delta), application.get("resourceId", "resource")]
	return ""

func _command_payload(command: Dictionary) -> Dictionary:
	var payload := {}
	for key in ["actorId", "actionType", "powerId", "targetId", "targetIds", "costOptionId", "cardInstanceId"]:
		if command.has(key) and command[key] != null:
			payload[key] = command[key]
	return payload

func _card_instance(instance_id: String) -> Dictionary:
	for card in GameSession.run.get("deck", {}).get("cardInstances", []):
		if str(card.get("cardInstanceId", "")) == instance_id:
			return card
	return {}

func _intent_for(actor_id: String) -> String:
	for intent in GameSession.combat.get("activation", {}).get("intents", []):
		if str(intent.get("actorId", "")) == actor_id:
			return str(intent.get("actionId", intent.get("actionType", "ação"))).replace("_", " ").capitalize()
	return "observando"

func _button(text: String, action: Callable, width := 0) -> Button:
	var value := AppTheme.button(text, width)
	value.custom_minimum_size.y = 44
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
