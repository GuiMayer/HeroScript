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
var _bound := false
static func create(): return load("res://scenes/ui/overlays/art_effect_lab.tscn").instantiate()
func setup(visual_context, card_model: Dictionary = {}) -> void:
	context = visual_context
	name = "ArtEffectLab"; title = _text("Art effects lab"); ok_button_text = _text("Close"); unresizable = false
	theme = context.theme
	actor_stage = get_node("Content/ActorStage")
	baseline = get_node("Content/ActorStage/BaselineActor/Baseline")
	card_baseline = get_node("Content/CardStage/Cards/BaselineCard/Center/CardBaseline")
	card_sample = get_node("Content/CardStage/Cards/PreviewCard/Center/CardSample")
	card_stage = get_node("Content/CardStage")
	paused = get_node("Content/Actions/Paused")
	picker = get_node("Content/Tools/ArtEffectPicker")
	reduced = get_node("Content/Actions/Reduced")
	result = get_node("Content/Result")
	sample = get_node("Content/ActorStage/PreviewActor/Sample")
	seconds = get_node("Content/Tools/Seconds")
	strength = get_node("Content/Tools/Strength")
	get_node("Content/ActorStage/BaselineActor/Title").text = _text("Baseline")
	get_node("Content/ActorStage/PreviewActor/Title").text = _text("Preview")
	get_node("Content/CardStage/Cards/BaselineCard/Title").text = _text("Baseline")
	get_node("Content/CardStage/Cards/PreviewCard/Title").text = _text("Preview")
	get_node("Content/Actions/Play").text = _text("Play effect")
	get_node("Content/Actions/Reset").text = _text("Reset")
	get_node("Content/Actions/Paused").text = _text("Pause preview")
	get_node("Content/Actions/Reduced").text = _text("Reduced motion")
	get_node("Content/Tools/DurationLabel").text = _text("Duration (s)")
	get_node("Content/Tools/IntensityLabel").text = _text("Intensity")
	get_node("Content/Introduction").text = _text("Shared ArtSlot · ivory palette · no API or gameplay changes")
	get_node("Content/Result").text = _text("Ready")
	picker.clear()
	for id in _options: picker.add_item(_text(_keys[id]))
	var actor_context = preload("res://scripts/ui/visual_context.gd").new(context.translator,{},preload("res://data/themes/ivory_run.tres"))
	actor_context.set_scenery_profile(preload("res://data/themes/ivory_scenery.tres"))
	for art in [baseline,sample]:
		art.visual_context = actor_context; art.setup("actors","opponent")
	var model := card_model.duplicate(true)
	if model.is_empty():
		model = JSON.parse_string(FileAccess.get_file_as_string("res://data/labs/card_hand_lab.json")).cards.strike.duplicate(true)
		if context.translator.locale == "pt_BR": model.merge(model.get("pt_BR",{}),true)
		model.erase("pt_BR"); model["rarityId"] = str(model.get("rarity","common")).to_lower()
	for card in [card_baseline,card_sample]:
		card.visual_context = context; card.hand_managed = true; card.animate_entry = false; card.configure(model)
	card_stage.hide(); actor_stage.show()
	reduced.set_pressed_no_signal(context.reduced_motion)
	if _bound: return
	_bound = true
	picker.item_selected.connect(_choose)
	get_node("Content/Actions/Play").pressed.connect(play_effect)
	get_node("Content/Actions/Reset").pressed.connect(reset)
	paused.toggled.connect(func(value):
		var effect = active_art().art_effects
		if effect != null and effect.tween != null and effect.tween.is_valid():
			if value: effect.tween.pause()
			else: effect.tween.play())
	reduced.toggled.connect(func(_value): reset())
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
