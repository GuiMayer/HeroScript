extends VBoxContainer

var router
var presentation: Dictionary
var action_panel: VBoxContainer

func setup(owner, data: Dictionary) -> void:
	router = owner
	presentation = data
	add_theme_constant_override("separation", 16)
	_build_header()
	_build_map()
	var lifecycle := str(GameSession.run.get("lifecycle", "Active"))
	if lifecycle.to_lower() != "active":
		_build_ending(lifecycle)
		return
	var current := _current_node()
	var columns := HBoxContainer.new()
	columns.size_flags_vertical = Control.SIZE_EXPAND_FILL
	columns.add_theme_constant_override("separation", 18)
	add_child(columns)
	columns.add_child(_activity_summary(current))
	action_panel = VBoxContainer.new()
	action_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	action_panel.add_theme_constant_override("separation", 10)
	action_panel.add_child(AppTheme.title(I18n.text("AÇÕES PERMITIDAS"), 22, AppTheme.TEAL))
	_build_actions()
	columns.add_child(AppTheme.panel(action_panel))

func _build_header() -> void:
	var row := HBoxContainer.new()
	var title_box := VBoxContainer.new()
	title_box.add_child(AppTheme.title(I18n.text("THE EMBER PATH"), 30))
	title_box.add_child(AppTheme.muted(I18n.text("Run %s  •  seed %s  •  sequência %s  •  step %s") % [
		str(GameSession.run.get("runId", "")).left(8),
		GameSession.run.get("seed", 0), GameSession.run.get("sequence", 0), GameSession.run.get("step", 0)]))
	row.add_child(title_box)
	var push := Control.new()
	push.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(push)
	for resource_id in ["gold", "power_points"]:
		var resource = GameSession.run.get("resources", {}).get(resource_id, {})
		var badge := Label.new()
		badge.text = "%s  %s" % ["◆" if resource_id == "gold" else "✦", int(resource.get("current", 0))]
		badge.add_theme_color_override("font_color", AppTheme.GOLD if resource_id == "gold" else AppTheme.TEAL)
		badge.add_theme_font_size_override("font_size", 19)
		row.add_child(badge)
	if not GameSession.run.get("relics", []).is_empty():
		var relic_badge := Label.new()
		relic_badge.text = "⬢  %s" % GameSession.run.get("relics", []).size()
		relic_badge.add_theme_color_override("font_color", AppTheme.EMBER)
		row.add_child(relic_badge)
	if not GameSession.run.get("modifiers", []).is_empty():
		var modifier_badge := Label.new()
		modifier_badge.text = "△  %s" % GameSession.run.get("modifiers", []).size()
		modifier_badge.add_theme_color_override("font_color", AppTheme.TEAL)
		row.add_child(modifier_badge)
	row.add_child(_button(I18n.text("VERIFICAR REPLAY"), _verify, 185))
	row.add_child(_button(I18n.text("PAUSA"), router.toggle_pause, 100))
	add_child(row)

func _build_map() -> void:
	var line := HBoxContainer.new()
	line.alignment = BoxContainer.ALIGNMENT_CENTER
	line.add_theme_constant_override("separation", 7)
	var map: Dictionary = GameSession.run.get("map", {})
	var current_id := str(GameSession.run.get("currentNodeId", ""))
	for node in map.get("nodes", []):
		var id := str(node.get("nodeId", ""))
		var info: Dictionary = presentation.get("nodes", {}).get(id, {})
		var marker := VBoxContainer.new()
		marker.custom_minimum_size.x = 145
		var icon := Label.new()
		icon.text = str(info.get("icon", "•"))
		icon.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		icon.add_theme_font_size_override("font_size", 25)
		var resolved := bool(node.get("resolved", false))
		icon.add_theme_color_override("font_color", AppTheme.TEAL if resolved else (AppTheme.EMBER if id == current_id else AppTheme.MUTED))
		marker.add_child(icon)
		var label := AppTheme.muted(str(info.get("name", id)), 12)
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		label.add_theme_color_override("font_color", AppTheme.INK if id == current_id else AppTheme.MUTED)
		marker.add_child(label)
		line.add_child(marker)
		if node != map.get("nodes", [])[-1]:
			var connector := Label.new()
			connector.text = "—"
			connector.add_theme_color_override("font_color", Color("#55566d"))
			line.add_child(connector)
	add_child(AppTheme.panel(line, Color("#151727d9")))

func _activity_summary(node: Dictionary) -> Control:
	var content := VBoxContainer.new()
	content.custom_minimum_size = Vector2(390, 0)
	content.add_theme_constant_override("separation", 13)
	var id := str(node.get("nodeId", ""))
	var info: Dictionary = presentation.get("nodes", {}).get(id, {})
	var icon := Label.new()
	icon.text = str(info.get("icon", "◈"))
	icon.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	icon.add_theme_font_size_override("font_size", 76)
	icon.add_theme_color_override("font_color", AppTheme.EMBER)
	content.add_child(icon)
	var title := AppTheme.title(str(info.get("name", id)), 27, AppTheme.GOLD)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	content.add_child(title)
	var activity: Dictionary = node.get("activity", {})
	var kind := str(activity.get("type", "Unknown"))
	var description: String = {
		"Encounter": I18n.text("A engine materializa atores, controladores, recursos, deck e regras do encontro a partir do nó."),
		"RelicReward": I18n.text("Relíquias fixam triggers e influences da revisão atual e passam a participar do combate."),
		"CardSelection": I18n.text("As ofertas são sorteadas pelo PRNG da run e persistidas no journal."),
		"Shop": I18n.text("Preços, estoque, rerolls e custos pertencem à definição da loja."),
		"Preparation": I18n.text("Escolha uma preparação capaz de alterar deck, recursos ou modifiers."),
		"CardUpgrade": I18n.text("O upgrade altera os componentes-base da carta; scaling permanece na pipeline.")
	}.get(kind, I18n.text("Atividade governada pela engine."))
	content.add_child(AppTheme.muted(description, 16))
	var deck: Dictionary = GameSession.run.get("deck", {})
	content.add_child(AppTheme.muted(I18n.text("Deck: %s cartas  •  mão: %s  •  descarte: %s") % [
		deck.get("cardInstances", []).size(), deck.get("counts", {}).get("hand", 0),
		deck.get("counts", {}).get("discardPile", 0)], 14))
	return AppTheme.panel(content)

func _build_actions() -> void:
	if GameSession.available_commands.is_empty():
		action_panel.add_child(AppTheme.muted(I18n.text("Aguardando uma transição da engine.")))
		return
	for command in GameSession.available_commands:
		var type := str(command.get("type", ""))
		var valid: Dictionary = command.get("validPayload", {}) if command.get("validPayload", {}) is Dictionary else {}
		match type:
			"ADVANCE_NODE":
				for target in command.get("targetNodeIds", []):
					_add_action(I18n.text("SEGUIR PARA %s") % _node_name(str(target)), type, {"targetNodeId": target})
			"PICK_CARD_REWARD":
				for card_id in valid.get("cardIds", []):
					_add_action(I18n.text("ESCOLHER  •  %s") % _card_name(str(card_id)), type,
						{"selectionInstanceId": valid.get("selectionInstanceId"), "cardIds": [card_id]})
			"DECOMPOSE_CARD_REWARD":
				for card_id in valid.get("cardIds", []):
					_add_action(I18n.text("DECOMPOR  •  %s") % _card_name(str(card_id)), type,
						{"selectionInstanceId": valid.get("selectionInstanceId"), "cardId": card_id}, true)
			"BUY_SHOP_ITEM":
				for item_id in valid.get("itemIds", []):
					_add_action(I18n.text("COMPRAR  •  %s") % I18n.content_name(str(item_id)), type,
						{"shopInstanceId": valid.get("shopInstanceId"), "itemId": item_id})
			"APPLY_PREPARATION_OPTION":
				for option_id in valid.get("optionIds", []):
					_add_action(I18n.text("PREPARAR  •  %s") % I18n.content_name(str(option_id)), type,
						{"preparationInstanceId": valid.get("preparationInstanceId"), "optionId": option_id})
			"UPGRADE_CARD":
				var upgrade_ids: Array = valid.get("upgradeIds", [])
				var upgrade_id := str(upgrade_ids[0]) if not upgrade_ids.is_empty() else "sharpened_edge"
				for card_instance_id in valid.get("cardInstanceIds", []):
					var definition := _card_definition(str(card_instance_id))
					if definition == "basic_attack":
						_add_action(I18n.text("APRIMORAR  •  %s") % _card_name(definition), type,
							{"cardInstanceId": card_instance_id, "upgradeId": upgrade_id})
			"REROLL_CARD_REWARD":
				_add_action(I18n.text("NOVAS OPÇÕES"), type, {
					"selectionInstanceId": valid.get("selectionInstanceId"), "lockedCardIds": []}, true)
			"REROLL_SHOP":
				_add_action(I18n.text("RENOVAR ESTOQUE"), type, {"shopInstanceId": valid.get("shopInstanceId")}, true)
			_:
				_add_action(_friendly_command(type), type, valid)

func _add_action(label: String, type: String, payload: Dictionary, secondary := false) -> void:
	var button := _button(label, func(): _execute(type, payload), 0)
	button.alignment = HORIZONTAL_ALIGNMENT_LEFT
	if secondary:
		button.modulate = Color("#c9bdca")
	action_panel.add_child(button)

func _execute(type: String, payload: Dictionary) -> void:
	router.show_toast(I18n.text("HeroScript processando %s…") % type)
	if await GameSession.execute_run_command(type, payload, _friendly_command(type)):
		if type in ["PICK_CARD_REWARD", "BUY_SHOP_ITEM", "APPLY_PREPARATION_OPTION", "UPGRADE_CARD"]:
			GameAudio.reward()
		router.open_game()

func _verify() -> void:
	var result := await GameSession.verify()
	if result.ok:
		var valid := bool(result.data.get("isValid", result.data.get("valid", false)))
		router.show_toast(I18n.text("Replay determinístico verificado.") if valid else I18n.text("Replay divergiu."), not valid)
	else:
		router.show_error(result.error)

func _build_ending(lifecycle: String) -> void:
	var center := CenterContainer.new()
	center.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(center)
	var content := VBoxContainer.new()
	content.custom_minimum_size.x = 560
	var won := lifecycle.to_lower() == "completed"
	content.add_child(AppTheme.title(I18n.text("ARQUIVO CONCLUÍDO") if won else I18n.text("A CHAMA SE APAGOU"), 40,
		AppTheme.GOLD if won else AppTheme.BLOOD))
	content.add_child(AppTheme.muted(I18n.text("O journal preserva cada comando, frame e hash desta jornada.")))
	content.add_child(_button(I18n.text("VERIFICAR REPLAY"), _verify, 420))
	content.add_child(_button(I18n.text("NOVA JORNADA"), router._new_campaign, 420))
	content.add_child(_button(I18n.text("MENU PRINCIPAL"), router.back_to_menu, 420))
	center.add_child(AppTheme.panel(content))

func _current_node() -> Dictionary:
	var id := str(GameSession.run.get("currentNodeId", ""))
	for node in GameSession.run.get("map", {}).get("nodes", []):
		if str(node.get("nodeId", "")) == id:
			return node
	return {}

func _card_definition(instance_id: String) -> String:
	for card in GameSession.run.get("deck", {}).get("cardInstances", []):
		if str(card.get("cardInstanceId", "")) == instance_id:
			return str(card.get("definitionId", ""))
	return ""

func _card_name(id: String) -> String:
	return str(presentation.get("cards", {}).get(id, {}).get("name", id.replace("_", " ").capitalize()))

func _node_name(id: String) -> String:
	return str(presentation.get("nodes", {}).get(id, {}).get("name", id.replace("_", " ").capitalize()))

func _friendly_command(type: String) -> String:
	return {
		"START_ENCOUNTER": I18n.text("ENTRAR EM COMBATE"),
		"RESOLVE_COMBAT": I18n.text("CONFIRMAR RESULTADO"),
		"RESOLVE_NODE": I18n.text("CONCLUIR ETAPA"),
			"ACQUIRE_RELIC": I18n.text("RECOLHER RELÍQUIA"),
			"CREATE_CARD_SELECTION": I18n.text("REVELAR RECOMPENSA"),
		"CREATE_SHOP": I18n.text("ENTRAR NA LOJA"),
		"CREATE_PREPARATION": I18n.text("ACENDER FOGUEIRA")
	}.get(type, type.replace("_", " "))

func _button(text: String, action: Callable, width := 0) -> Button:
	var value := AppTheme.button(text, width)
	value.pressed.connect(func(): GameAudio.ui(); action.call())
	return value
