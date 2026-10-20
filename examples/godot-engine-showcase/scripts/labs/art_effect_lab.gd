extends AcceptDialog
## Uses production ArtSlot and its production-ready executor, not a lab-only shader.
const Slot = preload("res://scripts/ui/art_slot.gd")
var context
var sample
var baseline
var picker: OptionButton
var seconds: SpinBox
var strength: HSlider
var result: Label
var paused: CheckButton
var reduced: CheckButton
var card_sample: CardView
var card_baseline: CardView
var actor_stage: HBoxContainer
var card_stage: ScrollContainer
var _options: Array[String] = ["impact","outline_available","outline_selected","sheen"]
var _keys := {"impact":"Impact flash","outline_available":"Outline · available","outline_selected":"Outline · selected","sheen":"Improvement sheen"}
const TEXT := {
	"Art effects lab":"Laboratório de efeitos",
	"Outline · available":"Contorno · disponível", "Outline · selected":"Contorno · selecionado",
	"Improvement sheen":"Reflexo de melhoria",
	"Static outline — no animation wait":"Contorno estático — sem espera de animação",
	"Impact flash":"Flash de impacto", "Duration (s)":"Duração (s)",
	"Intensity":"Intensidade", "Play effect":"Reproduzir efeito", "Reset":"Limpar",
	"Pause preview":"Pausar prévia", "Reduced motion":"Movimento reduzido",
	"Baseline":"Sem efeito", "Preview":"Prévia", "Close":"Fechar",
	"Shared ArtSlot · ivory palette · no API or gameplay changes":"ArtSlot compartilhado · paleta marfim · sem API ou alterações de gameplay",
	"Ready":"Pronto", "Playing":"Reproduzindo", "Skipped: no animation or wait":"Omitido: sem animação ou espera"}
static func label_for(locale: String, key: String) -> String: return str(TEXT.get(key,key)) if locale == "pt_BR" else key
func _text(key: String) -> String: return label_for(context.translator.locale,key)
func setup(visual_context, card_model: Dictionary = {}) -> void:
	context = visual_context
	name = "ArtEffectLab"; title = _text("Art effects lab"); ok_button_text = _text("Close"); unresizable = false
	theme = context.theme
	var body := VBoxContainer.new(); body.add_theme_constant_override("separation",14); add_child(body)
	var intro: Label = context.styles.muted(_text("Shared ArtSlot · ivory palette · no API or gameplay changes"),14)
	intro.custom_minimum_size.x = 620
	intro.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART; body.add_child(intro)
	var tools := HFlowContainer.new(); tools.add_theme_constant_override("h_separation",12); body.add_child(tools)
	picker = OptionButton.new(); picker.name = "ArtEffectPicker"
	for id in _options: picker.add_item(_text(_keys[id]))
	tools.add_child(picker)
	picker.item_selected.connect(_choose)
	tools.add_child(context.styles.title(_text("Duration (s)"),14))
	seconds = SpinBox.new(); seconds.min_value = 0; seconds.max_value = 2; seconds.step = .01; seconds.value = .16; seconds.custom_minimum_size.x = 110; tools.add_child(seconds)
	tools.add_child(context.styles.title(_text("Intensity"),14))
	strength = HSlider.new(); strength.min_value = 0; strength.max_value = 1; strength.step = .05; strength.value = .7; strength.custom_minimum_size.x = 140; tools.add_child(strength)
	actor_stage = HBoxContainer.new(); actor_stage.add_theme_constant_override("separation",24); body.add_child(actor_stage)
	var actor_context = preload("res://scripts/ui/visual_context.gd").new(context.translator,{},preload("res://data/themes/ivory_run.tres"))
	actor_context.set_scenery_profile(preload("res://data/themes/ivory_scenery.tres"))
	for is_preview in [false,true]:
		var column := VBoxContainer.new(); column.size_flags_horizontal = Control.SIZE_EXPAND_FILL; actor_stage.add_child(column)
		column.add_child(context.styles.title(_text("Preview" if is_preview else "Baseline"),18))
		var art = Slot.new(actor_context); art.custom_minimum_size = Vector2(240,240); art.size_flags_horizontal = Control.SIZE_EXPAND_FILL; art.setup("actors","opponent")
		column.add_child(art)
		if is_preview: sample = art
		else: baseline = art
	card_stage = ScrollContainer.new(); card_stage.custom_minimum_size.y = 340
	card_stage.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	body.add_child(card_stage)
	var cards := HBoxContainer.new(); cards.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	cards.add_theme_constant_override("separation",24); card_stage.add_child(cards)
	var model := card_model.duplicate(true)
	if model.is_empty():
		model = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_hand_lab.json")).cards.strike.duplicate(true)
		if context.translator.locale == "pt_BR": model.merge(model.get("pt_BR",{}),true)
		model.erase("pt_BR"); model["rarityId"] = str(model.get("rarity","common")).to_lower()
	for is_preview in [false,true]:
		var column := VBoxContainer.new(); column.size_flags_horizontal = Control.SIZE_EXPAND_FILL; cards.add_child(column)
		column.add_child(context.styles.title(_text("Preview" if is_preview else "Baseline"),18))
		var center := CenterContainer.new(); column.add_child(center)
		var card := CardView.new(context); card.hand_managed = true; card.animate_entry = false; card.configure(model)
		card.mouse_filter = Control.MOUSE_FILTER_IGNORE; card.focus_mode = Control.FOCUS_NONE; center.add_child(card)
		if is_preview: card_sample = card
		else: card_baseline = card
	card_stage.hide()
	var actions := HFlowContainer.new(); actions.add_theme_constant_override("h_separation",12); body.add_child(actions)
	for item in [["Play effect",play_effect],["Reset",reset]]:
		var button: Button = context.styles.button(_text(item[0])); button.pressed.connect(item[1]); actions.add_child(button)
	paused = CheckButton.new(); paused.text = _text("Pause preview"); actions.add_child(paused)
	paused.toggled.connect(func(value):
		var effect = active_art().art_effects
		if effect != null and effect.tween != null and effect.tween.is_valid():
			if value: effect.tween.pause()
			else: effect.tween.play())
	reduced = CheckButton.new(); reduced.text = _text("Reduced motion"); reduced.button_pressed = context.reduced_motion; actions.add_child(reduced)
	reduced.toggled.connect(func(_value): reset())
	result = context.styles.muted(_text("Ready"),14); body.add_child(result)
	about_to_popup.connect(func(): sample.effect_player().warmup())
	visibility_changed.connect(func(): if not visible: reset())
func play_effect() -> void:
	reset()
	if _options[picker.selected].begins_with("outline_"):
		sample.set_art_outline(_options[picker.selected].trim_prefix("outline_"))
		result.text = _text("Static outline — no animation wait"); return
	var tween: Tween = active_art().effect_player().play(_options[picker.selected],seconds.value,strength.value,reduced.button_pressed)
	result.text = _text("Skipped: no animation or wait") if tween == null else _text("Playing")
	if tween != null: tween.finished.connect(func(): result.text = _text("Ready"),CONNECT_ONE_SHOT)
func active_art(): return card_sample.artwork() if _options[picker.selected] == "sheen" else sample
func _choose(_index: int) -> void:
	reset()
	var is_sheen := _options[picker.selected] == "sheen"
	actor_stage.visible = not is_sheen; card_stage.visible = is_sheen
	var style = sample.effect_player().style
	seconds.value = style.sheen_seconds if is_sheen else style.impact_seconds
	strength.value = style.sheen_strength if is_sheen else style.impact_strength
	seconds.editable = not _options[picker.selected].begins_with("outline_")
	strength.editable = seconds.editable
func reset() -> void:
	if is_instance_valid(sample) and sample.art_effects != null: sample.art_effects.clear()
	if is_instance_valid(card_sample):
		var art = card_sample.artwork()
		if art != null and art.art_effects != null: art.art_effects.clear()
	if paused != null: paused.set_pressed_no_signal(false)
	if result != null: result.text = _text("Ready")
