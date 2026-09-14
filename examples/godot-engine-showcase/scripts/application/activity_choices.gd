extends RefCounted
## Maps advertised choices to command payloads. Never invents prices, rules or IDs.

static func build(run: Dictionary, commands: Array) -> Array:
	var choices: Array = []
	for command in commands:
		var type := str(command.get("type", ""))
		var valid: Dictionary = command.get("validPayload", {}) if command.get("validPayload") is Dictionary else {}
		match type:
			"CHOOSE_DIALOGUE_OPTION":
				for id in valid.get("choiceIds", []):
					choices.append(_choice(type, {"dialogueInstanceId": valid.get("dialogueInstanceId"), "nodeId": valid.get("nodeId"), "choiceId": id}, str(id), "dialogue"))
			"ADVANCE_NODE":
				for id in command.get("targetNodeIds", []):
					choices.append(_choice(type, {"targetNodeId": id}, str(id), "node"))
			"PICK_CARD_REWARD", "DECOMPOSE_CARD_REWARD":
				for id in valid.get("cardIds", []):
					var payload := {"selectionInstanceId": valid.get("selectionInstanceId")}
					payload["cardIds" if type == "PICK_CARD_REWARD" else "cardId"] = [id] if type == "PICK_CARD_REWARD" else id
					choices.append(_choice(type, payload, str(id), "card", type == "DECOMPOSE_CARD_REWARD"))
			"BUY_SHOP_ITEM", "APPLY_PREPARATION_OPTION":
				var shop := type == "BUY_SHOP_ITEM"
				for id in valid.get("itemIds" if shop else "optionIds", []):
					var payload := {}
					var instance_key := "shopInstanceId" if shop else "preparationInstanceId"
					payload[instance_key] = valid.get(instance_key)
					payload["itemId" if shop else "optionId"] = id
					choices.append(_choice(type, payload, str(id), "content"))
			"UPGRADE_CARD":
				for upgrade_id in valid.get("upgradeIds", []):
					for card_id in valid.get("cardInstanceIds", []):
						var definition := str(card_id)
						for card in run.get("deck", {}).get("cardInstances", []):
							if str(card.get("cardInstanceId", "")) == str(card_id):
								definition = str(card.get("definitionId", card_id))
						var choice := _choice(type, {"cardInstanceId": card_id, "upgradeId": upgrade_id}, definition, "card")
						choice["variantId"] = upgrade_id
						choices.append(choice)
			"REROLL_CARD_REWARD":
				choices.append(_choice(type, {"selectionInstanceId": valid.get("selectionInstanceId"), "lockedCardIds": []}, "", "", true))
			"REROLL_SHOP":
				choices.append(_choice(type, {"shopInstanceId": valid.get("shopInstanceId")}, "", "", true))
			_:
				choices.append(_choice(type, valid))
	return choices

static func only_forced_advance(choices: Array) -> Dictionary:
	# Abandon is always a pause-menu escape hatch, not a progression decision.
	var gameplay: Array = choices.filter(func(choice): return str(choice.get("type", "")) != "ABANDON_RUN")
	if gameplay.size() != 1 or str(gameplay[0].get("type", "")) != "ADVANCE_NODE":
		return {}
	return gameplay[0].duplicate(true)

static func _choice(type: String, payload: Dictionary, subject := "", subject_type := "", secondary := false) -> Dictionary:
	return {"type": type, "payload": payload.duplicate(true), "subjectId": subject, "subjectType": subject_type, "secondary": secondary}
