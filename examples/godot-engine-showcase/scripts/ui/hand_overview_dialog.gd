extends AcceptDialog
signal card_chosen(instance_id: String)
var hand
func setup(models: Array, scope: String, selected: String) -> void:
	title = I18n.text("Hand overview")
	theme = AppTheme.build(Preferences.high_contrast)
	theme.set_stylebox("panel", "AcceptDialog", AppTheme.box(AppTheme.NIGHT, 10, AppTheme.GOLD, 1))
	var border := AppTheme.box(AppTheme.PANEL, 10, AppTheme.GOLD, 1)
	border.expand_margin_top = 30
	theme.set_stylebox("embedded_border", "Window", border)
	theme.set_color("title_color", "Window", AppTheme.INK)
	theme.set_constant("title_height", "Window", 30)
	min_size = Vector2i(850, 560)
	hand = load("res://scripts/ui/card_hand_view.gd").new()
	hand.mode_override = "reading"
	hand.drag_override = false
	add_child(hand)
	hand.sync_models(models, scope)
	hand.set_selection(selected)
	hand.card_activated.connect(func(id): card_chosen.emit(id); hide(); queue_free())
	hand.inspection_requested.connect(_inspect)
	hand.find_child("HandOverviewButton", true, false).hide()
	confirmed.connect(queue_free)
	canceled.connect(queue_free)

func _inspect(id: String) -> void:
	if hand.reading_modal: return
	var card = hand.card_for(id)
	if not is_instance_valid(card): return
	var detail = preload("res://scripts/ui/inspection_dialog.gd").new()
	detail.setup(I18n.text("Card inspection"), str(card.model.get("summary", "")), {}, card.model, card.visual_style)
	add_child(detail)
	hand.track_reading_modal(detail)
	detail.tree_exiting.connect(func(): if is_instance_valid(card): card.grab_focus())
	detail.popup_centered()

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("inspect_card") and not event.is_echo():
		_inspect(hand.active_card_id())
		set_input_as_handled()
