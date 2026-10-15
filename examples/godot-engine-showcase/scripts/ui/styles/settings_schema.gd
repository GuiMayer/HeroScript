extends RefCounted
## Local preference schema, separate from the menu composition.
const CATEGORIES := {
	"general": ["GENERAL", "Language and basic gameplay presentation."],
	"audio": ["AUDIO", "Balance music and interface feedback."],
	"display": ["DISPLAY", "Window mode, resolution and presentation."],
	"accessibility": ["ACCESSIBILITY", "Readability and motion preferences."],
	"controls": ["CONTROLS", "Keyboard and gamepad bindings."],
	"connection": ["CONNECTION", "HeroScript service connection."]
}
const OPTIONS := {
	"locale": {"values": ["en", "pt_BR"]},
	"animation_speed": {"min": .25, "max": 2.0, "step": .05, "control": "AnimationSpeedSlider"},
	"auto_animations": {"type": "bool"},
	"animation_preset": {"values": ["normal", "fast", "instant"]},
	"hand_layout_mode": {"values": ["adaptive", "reading"]},
	"hand_emphasis": {"min": 0.0, "max": 1.0, "step": .1, "control": "HandEmphasisSlider"},
	"master_volume": {"min": 0.0, "max": 1.0, "step": .01, "control": "MasterVolumeSlider"},
	"music_volume": {"min": 0.0, "max": 1.0, "step": .01, "control": "MusicVolumeSlider"},
	"sfx_volume": {"min": 0.0, "max": 1.0, "step": .01, "control": "SfxVolumeSlider"},
	"fullscreen": {"type": "bool"},
	"text_scale": {"min": .9, "max": 1.2, "step": .05, "control": "TextScaleSlider"},
	"high_contrast": {"type": "bool"},
	"reveal_text_gradually": {"type": "bool"},
	"reduced_motion": {"type": "bool"}
}
static func option(key: String) -> Dictionary: return OPTIONS.get(key, {}).duplicate(true)
static func for_control(control_id: String) -> Dictionary:
	for value in OPTIONS.values():
		if str(value.get("control", "")) == control_id: return value.duplicate(true)
	return {}
