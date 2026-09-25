@tool
extends EditorPlugin
## Registers the .meshy scene importer and its import options.

const SceneImporter := preload("meshy_scene_importer.gd")
const OptionsPlugin := preload("meshy_import_options.gd")

var _importer: EditorSceneFormatImporter
var _options: EditorScenePostImportPlugin


func _enter_tree() -> void:
	_importer = SceneImporter.new()
	_options = OptionsPlugin.new()
	add_scene_format_importer_plugin(_importer)
	add_scene_post_import_plugin(_options)


func _exit_tree() -> void:
	remove_scene_post_import_plugin(_options)
	remove_scene_format_importer_plugin(_importer)
	_options = null
	_importer = null
