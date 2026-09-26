extends SceneTree
## Checks meshy_support.gd (plugin.gd's helpers) (version compare, diagnostics text); exits 1 on failure.

const Plugin := preload("res://addons/meshy_importer/meshy_support.gd")


func _init() -> void:
	var ok := true
	ok = ok and Plugin.is_newer("1.5.1", "1.5.0") and Plugin.is_newer("v2.0.0", "1.9.9")
	ok = ok and not Plugin.is_newer("1.5.0", "1.5.0") and not Plugin.is_newer("", "1.5.0")
	Engine.set_meta("meshy_last_error", "a.meshy: boom")
	var text := Plugin.diagnostics()
	ok = ok and text.contains("Meshy Importer: " + Plugin.VERSION) and text.contains("a.meshy: boom")
	print("PLUGIN CHECKS " + ("PASSED" if ok else "FAILED") + "\n" + text)
	quit(0 if ok else 1)
