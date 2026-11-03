@tool
extends RefCounted
## Narrow composition context passed to application screen coordinators.
## Wraps navigation, session read/command, feedback/audio, playback, and i18n without creating duplicate global services.

const VisualContextType = preload("res://scripts/ui/visual_context.gd")
const NavigationPort = preload("res://scripts/ui/context/navigation_port.gd")
const SessionPort = preload("res://scripts/ui/context/session_port.gd")
const FeedbackPort = preload("res://scripts/ui/context/feedback_port.gd")
const PlaybackPort = preload("res://scripts/ui/context/playback_port.gd")

var visual_context
var navigation: NavigationPort
var session: SessionPort
var feedback: FeedbackPort
var playback: PlaybackPort
var i18n
var preferences
var services
var router

func _init(
	p_visual_context = null,
	p_navigation: NavigationPort = null,
	p_session: SessionPort = null,
	p_feedback: FeedbackPort = null,
	p_playback: PlaybackPort = null,
	p_i18n = null,
	p_preferences = null,
	p_services = null,
	p_router = null
) -> void:
	visual_context = p_visual_context if p_visual_context != null else VisualContextType.new()
	navigation = p_navigation if p_navigation != null else NavigationPort.new(p_router)
	session = p_session if p_session != null else SessionPort.new()
	feedback = p_feedback if p_feedback != null else FeedbackPort.new(p_router)
	playback = p_playback if p_playback != null else PlaybackPort.new()
	i18n = p_i18n if p_i18n != null else visual_context.translator
	preferences = p_preferences
	services = p_services
	router = p_router if p_router != null else navigation.router

var presentation: Dictionary:
	get: return navigation.presentation
	set(value): navigation.presentation = value

var current_screen: String:
	get: return navigation.current_screen
	set(value): navigation.current_screen = value

var navigation_epoch: int:
	get: return navigation.navigation_epoch
	set(value): navigation.navigation_epoch = value

var dialogue_return_barrier: bool:
	get: return navigation.dialogue_return_barrier
	set(value): navigation.dialogue_return_barrier = value

func back_to_menu() -> void:
	navigation.back_to_menu()

func open_game() -> void:
	navigation.open_game()

func show_toast(message: String, error := false) -> void:
	feedback.show_toast(message, error)

func show_error(message: String) -> void:
	feedback.show_error(message)

static func _autoload(name: String):
	if Engine.is_editor_hint():
		return null
	var loop := Engine.get_main_loop()
	if loop is SceneTree and loop.root != null:
		return loop.root.get_node_or_null(name)
	return null

static func ensure(owner_or_context, overrides: Dictionary = {}):
	var SelfScript = load("res://scripts/ui/context/screen_context.gd")
	if owner_or_context != null and is_instance_of(owner_or_context, SelfScript):
		return owner_or_context
	if owner_or_context != null and "screen_context" in owner_or_context and owner_or_context.screen_context != null and overrides.is_empty():
		return owner_or_context.screen_context
	return from_router(owner_or_context, overrides)

static func from_router(router_node = null, overrides: Dictionary = {}):
	var SelfScript = load("res://scripts/ui/context/screen_context.gd")
	var v_ctx = overrides.get("visual_context", null)
	if v_ctx == null and is_instance_valid(router_node) and "visual_context" in router_node and router_node.visual_context != null:
		v_ctx = router_node.visual_context
	if v_ctx == null:
		v_ctx = VisualContextType.new()

	var i18n_target = overrides.get("i18n", _autoload("I18n"))
	if i18n_target == null:
		i18n_target = v_ctx.translator

	var session_target = overrides.get("session_target", _autoload("GameSession"))
	var audio_target = overrides.get("audio_target", _autoload("GameAudio"))
	var playback_target = overrides.get("playback_target", _autoload("Playback"))
	var prefs_target = overrides.get("preferences", _autoload("Preferences"))
	var services_target = overrides.get("services", _autoload("GameServices"))

	var nav_port: NavigationPort = overrides.get("navigation", NavigationPort.new(router_node))
	var sess_port: SessionPort = overrides.get("session", SessionPort.new(session_target, overrides.get("fake_session", {})))
	var fb_port: FeedbackPort = overrides.get("feedback", FeedbackPort.new(router_node, audio_target))
	var pb_port: PlaybackPort = overrides.get("playback", PlaybackPort.new(playback_target))

	return SelfScript.new(v_ctx, nav_port, sess_port, fb_port, pb_port, i18n_target, prefs_target, services_target, router_node)

static func isolated(overrides: Dictionary = {}):
	var SelfScript = load("res://scripts/ui/context/screen_context.gd")
	var i18n_target = overrides.get("i18n", VisualContextType.English.new())
	var v_ctx = overrides.get("visual_context", VisualContextType.new(i18n_target, overrides.get("options", {}), overrides.get("profile", null)))
	var nav_port: NavigationPort = overrides.get("navigation", NavigationPort.new(null, overrides.get("presentation", {})))
	var sess_port: SessionPort = overrides.get("session", SessionPort.new(overrides.get("session_target", null), overrides.get("fake_session", {})))
	var fb_port: FeedbackPort = overrides.get("feedback", FeedbackPort.new(null, overrides.get("audio_target", null)))
	var pb_port: PlaybackPort = overrides.get("playback", PlaybackPort.new(overrides.get("playback_target", null)))
	return SelfScript.new(v_ctx, nav_port, sess_port, fb_port, pb_port, i18n_target, overrides.get("preferences", null), overrides.get("services", null), null)
