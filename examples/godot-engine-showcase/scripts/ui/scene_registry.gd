extends RefCounted
## One production scene per route. Add entries only when adopting that scene.
## Routing, sessions and gameplay remain with the existing application adapters.
const ROUTES := {
	"combat_mvp": preload("res://scenes/ui/screens/combat_mvp.tscn"),
	"codex": preload("res://scenes/ui/screens/codex.tscn"),
	"sandbox": preload("res://scenes/ui/screens/sandbox.tscn"),
	"run_replay": preload("res://scenes/ui/screens/run_replay.tscn"),
	"timeline": preload("res://scenes/ui/screens/timeline.tscn"),
	"run_history": preload("res://scenes/ui/screens/run_history.tscn"),
	"dialogue": preload("res://scenes/ui/screens/dialogue_screen.tscn"),
	"activity": preload("res://scenes/ui/screens/activity_screen.tscn"),
	"map": preload("res://scenes/ui/screens/map_screen.tscn"),
	"combat": preload("res://scenes/ui/screens/combat_screen.tscn"),
	"run_loadout": preload("res://scenes/ui/screens/unified_loadout.tscn"),
	"settings": preload("res://scenes/ui/screens/settings.tscn"),
	"menu": preload("res://scenes/ui/screens/main_menu.tscn"),
	"loadout_lab": preload("res://scenes/unified_loadout_lab.tscn"),
}
static func combat_route(run: Dictionary, fallback: String) -> String:
	return "combat_mvp" if str(run.get("settingId", fallback)) == "prototype-test" else "combat"

static func instantiate(route: String) -> Node:
	assert(ROUTES.has(route), "Unregistered UI scene: " + route)
	return (ROUTES[route] as PackedScene).instantiate()
