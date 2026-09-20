extends RefCounted
## Detects wrapped text controls that were compressed into an unreadable column.

static func vertical_text_issues(root: Node) -> Array[String]:
	var issues: Array[String] = []
	_collect(root, issues)
	return issues

static func _collect(node: Node, issues: Array[String]) -> void:
	if node is CanvasItem and not node.is_visible_in_tree(): return
	if node is Label:
		var label := node as Label
		var text: String = label.text.strip_edges()
		var font_size: int = label.get_theme_font_size("font_size")
		var narrow: bool = label.size.x < maxf(72.0, font_size * 3.2)
		if text.length() > 3 and label.autowrap_mode != TextServer.AUTOWRAP_OFF and label.get_line_count() > 1 and narrow:
			issues.append("%s (%sx%s): %s" % [label.get_path(), roundi(label.size.x), roundi(label.size.y), text.left(48)])
	elif node is Button:
		var button := node as Button
		var text: String = button.text.strip_edges()
		if text.length() > 3 and button.autowrap_mode != TextServer.AUTOWRAP_OFF and button.size.x < 72:
			issues.append("%s (%sx%s): %s" % [button.get_path(), roundi(button.size.x), roundi(button.size.y), text.left(48)])
	for child in node.get_children():
		_collect(child, issues)
