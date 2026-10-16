extends RefCounted
## Wire-format adaptation only. Never round gameplay values or rewrite a retry envelope.
const COMMAND_PATHS = {
	"REMOVE_CARD_TRANSFORMATION": ["transformationId"],
	"REPLACE_CARD_TRANSFORMATION": ["transformationId"],
	"RESTORE_HEAD_FROM_HISTORY": ["sourceSequence"],
	"INVOKE_CARD_ZONE_GAMEPLAY_FLOW": ["requestedCount"],
	"APPLY_RUN_RESOURCE": ["operation", "field"],
	"PLAY_CARD": ["actionType"], "END_TURN": ["actionType"], "EXECUTE_ACTION": ["actionType"],
	"START_ENCOUNTER": ["participants/*/identityBinding", "participants/*/controllerBinding/kind"]}
const SCENARIO_PATHS = ["schemaVersion", "seed", "initialState/effects/*/stacks", "influences/*/priority", "influences/*/scope", "participants/*/controllerBinding/kind", "relationships/sameSide", "relationships/differentSides", "relationships/rules/*/relationship"]

static func integer_token(value):
	# JSON.parse_string produces doubles. Beyond this range the original integer
	# may already be lost; leave invalid/unsafe inputs for API validation, not truncation.
	if value is float and is_finite(value) and value == floor(value) and absf(value) <= 9007199254740991.0:
		return int(value)
	return value

static func fields(document: Dictionary, paths: Array) -> Dictionary:
	var result := document.duplicate(true)
	for path in paths: _visit(result, str(path).split("/"), 0)
	return result

static func payload(type: String, document: Dictionary) -> Dictionary:
	return fields(document, COMMAND_PATHS.get(type.strip_edges().to_upper(), []))

static func _visit(node, path: PackedStringArray, index: int) -> void:
	if path[index] == "*":
		var values: Array = node.values() if node is Dictionary else node if node is Array else []
		for child in values:
			if index + 1 < path.size(): _visit(child, path, index + 1)
	elif node is Dictionary:
		for key in node:
			if str(key).to_lower() != path[index].to_lower(): continue
			if index + 1 == path.size(): node[key] = integer_token(node[key])
			else: _visit(node[key], path, index + 1)
