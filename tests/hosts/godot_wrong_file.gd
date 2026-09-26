extends SceneTree
## Prints meshy_decrypt.gd describe_wrong_file for each file argument as a JSON array.

const Decrypt := preload("res://addons/meshy_importer/meshy_decrypt.gd")


func _init() -> void:
	var out := []
	for path in OS.get_cmdline_user_args():
		out.append(Decrypt.describe_wrong_file(FileAccess.get_file_as_bytes(path)))
	print(JSON.stringify(out))
	quit(0)
