extends RefCounted
## Immutable timeline metadata; the cursor does not belong to the live session.
var _entries: Array = []
var next_cursor := 0

func append_page(page: Dictionary) -> void:
	for item in page.get("items", []):
		if not _entries.any(func(previous): return previous.runSequence == item.runSequence):
			_entries.append(item.duplicate(true))
	_entries.sort_custom(func(a, b): return int(a.runSequence) < int(b.runSequence))
	next_cursor = int(page.get("nextCursor", next_cursor))

func entries() -> Array:
	return _entries.duplicate(true)

static func lineage(root: Dictionary, depth := 0) -> Array:
	var rows: Array = [{"runId": str(root.get("runId", "")), "parentRunId": root.get("parentRunId"),
		"depth": depth, "branchKey": root.get("branchKey"), "sourceSequence": root.get("sourceSequence")}]
	for child in root.get("children", []): rows.append_array(lineage(child, depth + 1))
	return rows
