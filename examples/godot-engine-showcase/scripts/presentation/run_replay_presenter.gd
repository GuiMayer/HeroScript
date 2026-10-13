extends RefCounted
## Formats facts from the replay read models; no rule evaluation.
static func frames_text(entry: Dictionary, translator) -> String:
	var frames: Array = entry.get("frames", [])
	if frames.is_empty(): return translator.text("No internal frames were recorded for this command.")
	var lines: Array[String] = [translator.text("%s internal frame(s)") % frames.size()]
	for frame in frames:
		var context: Array[String] = []
		if frame.get("actorId") != null: context.append(str(frame.actorId))
		if frame.get("phaseId") != null: context.append(str(frame.phaseId))
		lines.append("• %s  ·  %s%s" % [str(frame.get("scope", "run")).to_upper(),
			str(frame.get("kind", "transition")).replace("_", " ").replace(".", " › ").capitalize(),
			"  ·  " + " / ".join(context) if not context.is_empty() else ""])
	return "\n".join(lines)
static func summary_text(projection: Dictionary, translator) -> String:
	var summary: Dictionary = projection.get("stateSummary", {})
	var resources: Array[String] = []
	for pool in summary.get("resources", []):
		resources.append("%s %s/%s" % [translator.content_name(str(pool.resourceId)), translator.number(float(pool.current)), translator.number(float(pool.maximum))])
	return "%s\n%s\n%s\n%s" % [translator.text("State after command: %s") % str(summary.get("lifecycle", "—")),
		translator.text("Stop: %s  •  Deck: %s cards  •  Relics: %s") % [translator.content_name(str(summary.get("currentNodeId", "")), translator.text("Unknown stop")), summary.get("cardCount", 0), summary.get("relicCount", 0)],
		translator.text("Resources: %s") % (", ".join(resources) if not resources.is_empty() else "—"),
		translator.text("State hash: %s") % projection.get("stateHash", "")]
static func transcript_text(projection: Dictionary, translator) -> String:
	var lines: Array[String] = []
	var dialogue = preload("res://scripts/presentation/dialogue_presenter.gd")
	for line in projection.get("stateSummary", {}).get("transcript", []):
		var text: String = dialogue.localized(line.get("text", {}), translator.locale)
		lines.append("› " + text if line.get("choiceId") != null else dialogue.localized(line.get("speaker", {}), translator.locale) + ": " + text)
	return "" if lines.is_empty() else translator.text("CONVERSATION HISTORY") + "\n\n" + "\n\n".join(lines)
