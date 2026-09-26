@tool
extends EditorPlugin
## Registers the .meshy scene importer, its import options, and the
## Project > Tools > Meshy Importer menu (help, diagnostics, bug report, update check).

const SceneImporter := preload("meshy_scene_importer.gd")
const OptionsPlugin := preload("meshy_import_options.gd")
const Decrypt := preload("meshy_decrypt.gd")
const Support := preload("meshy_support.gd")

const VERSION := Support.VERSION
const REPO_URL := "https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity"
const GETTING_A_FILE_URL := REPO_URL + "/blob/main/GETTING_A_MESHY_FILE.md"
const RELEASES_URL := REPO_URL + "/releases/latest"
const API_LATEST := "https://api.github.com/repos/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/latest"
const DISCORD_URL := "https://discord.gg/vCcsnX4HQP"
const SETTING_CHECK := "meshy_importer/check_for_updates"
const SETTING_LAST := "meshy_importer/last_update_check"
const SETTING_LATEST := "meshy_importer/latest_release"
const MENU_NAME := "Meshy Importer"

enum { ID_UPDATE, ID_GET_FILE, ID_TROUBLESHOOTING, ID_DOCS, ID_COPY, ID_BUG, ID_CHECK, ID_DISCORD }

var _importer: EditorSceneFormatImporter
var _options: EditorScenePostImportPlugin
var _menu: PopupMenu
var _http: HTTPRequest
var _manual_check := false


func _enter_tree() -> void:
	_importer = SceneImporter.new()
	_options = OptionsPlugin.new()
	add_scene_format_importer_plugin(_importer)
	add_scene_post_import_plugin(_options)

	var settings := EditorInterface.get_editor_settings()
	if not settings.has_setting(SETTING_CHECK):
		settings.set_setting(SETTING_CHECK, true)
	settings.set_initial_value(SETTING_CHECK, true, false)

	_menu = PopupMenu.new()
	_menu.id_pressed.connect(_on_menu)
	_rebuild_menu()
	add_tool_submenu_item(MENU_NAME, _menu)

	_http = HTTPRequest.new()
	_http.timeout = 15.0
	_http.request_completed.connect(_on_release_info)
	add_child(_http)
	if bool(settings.get_setting(SETTING_CHECK)) and not DisplayServer.get_name() == "headless":
		var last := float(settings.get_setting(SETTING_LAST)) if settings.has_setting(SETTING_LAST) else 0.0
		if Time.get_unix_time_from_system() - last >= 24 * 3600:
			_check_updates(false)


func _exit_tree() -> void:
	remove_tool_menu_item(MENU_NAME)
	remove_scene_post_import_plugin(_options)
	remove_scene_format_importer_plugin(_importer)
	if _http:
		_http.queue_free()
	_options = null
	_importer = null
	_menu = null
	_http = null


func _latest_known() -> String:
	var settings := EditorInterface.get_editor_settings()
	return str(settings.get_setting(SETTING_LATEST)) if settings.has_setting(SETTING_LATEST) else ""


func _rebuild_menu() -> void:
	_menu.clear()
	var latest := _latest_known()
	if Support.is_newer(latest, VERSION):
		_menu.add_item("Update Available: %s" % latest, ID_UPDATE)
		_menu.add_separator()
	_menu.add_item("How Do I Get a .meshy File?", ID_GET_FILE)
	_menu.add_item("Troubleshooting", ID_TROUBLESHOOTING)
	_menu.add_item("Documentation", ID_DOCS)
	_menu.add_separator()
	_menu.add_item("Copy Diagnostics", ID_COPY)
	_menu.add_item("Report a Bug...", ID_BUG)
	_menu.add_item("Check for Updates", ID_CHECK)
	_menu.add_separator()
	_menu.add_item("Discord", ID_DISCORD)


func _on_menu(id: int) -> void:
	match id:
		ID_UPDATE: OS.shell_open(RELEASES_URL)
		ID_GET_FILE: OS.shell_open(GETTING_A_FILE_URL)
		ID_TROUBLESHOOTING: OS.shell_open(Decrypt.TROUBLESHOOTING)
		ID_DOCS: OS.shell_open(REPO_URL + "/tree/main/godot")
		ID_COPY:
			DisplayServer.clipboard_set(Support.diagnostics())
			print("Meshy Importer: diagnostics copied to the clipboard.\n" + Support.diagnostics())
		ID_BUG: _report_bug()
		ID_CHECK: _check_updates(true)
		ID_DISCORD: OS.shell_open(DISCORD_URL)


func _report_bug() -> void:
	var text := Support.diagnostics()
	DisplayServer.clipboard_set(text)
	if text.length() > 1500:
		text = text.substr(0, 1500) + "\n..."
	var url := REPO_URL + "/issues/new?template=bug_report.yml" \
		+ "&importer=" + (VERSION + " (Godot)").uri_encode() \
		+ "&host=" + ("Godot %s, %s" % [Engine.get_version_info().get("string", "?"), OS.get_name()]).uri_encode() \
		+ "&logs=" + text.uri_encode()
	OS.shell_open(url)


# Update check: downloads only the public "latest release" record from GitHub, at most once
# a day. Turn it off with Editor Settings > Meshy Importer > Check For Updates.
func _check_updates(manual: bool) -> void:
	_manual_check = manual
	var err := _http.request(API_LATEST, PackedStringArray([
		"User-Agent: MeshyImporter-Godot/" + VERSION, "Accept: application/vnd.github+json"]))
	if err != OK and manual:
		push_warning("Meshy Importer: could not check for updates (error %d)." % err)


func _on_release_info(result: int, code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
	var settings := EditorInterface.get_editor_settings()
	settings.set_setting(SETTING_LAST, Time.get_unix_time_from_system())
	var latest := ""
	if result == HTTPRequest.RESULT_SUCCESS and code == 200:
		var parsed = JSON.parse_string(body.get_string_from_utf8())
		if parsed is Dictionary:
			latest = str(parsed.get("tag_name", "")).trim_prefix("v")
	if latest != "":
		settings.set_setting(SETTING_LATEST, latest)
		_rebuild_menu()
	if Support.is_newer(latest, VERSION):
		print_rich("[color=yellow]Meshy Importer %s is available[/color] (you have %s): %s" % [latest, VERSION, RELEASES_URL])
	elif _manual_check:
		print("Meshy Importer: " + ("you have the latest version (%s)." % VERSION if latest != "" else "could not reach GitHub to check for updates."))
