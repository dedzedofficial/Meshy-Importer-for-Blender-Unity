@tool
extends EditorScenePostImportPlugin
## Adds the Meshy options to the Import dock for .meshy files. (Godot 4.2/4.3 scene
## format importers cannot declare options themselves; a post-import plugin can, and
## the values reach meshy_scene_importer.gd through the options dictionary.)


func _get_import_options(path: String) -> void:
	if not path.to_lower().ends_with(".meshy"):
		return
	add_import_option("meshy/auto_repair_uvs", true)
	add_import_option("meshy/save_decoded_glb", false)
