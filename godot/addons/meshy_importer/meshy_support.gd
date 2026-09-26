@tool
extends RefCounted
## Plain helpers for plugin.gd (version compare, diagnostics text). Kept out of the
## EditorPlugin script so tests can load them without the editor.

const VERSION := "1.5.0"


static func diagnostics() -> String:
	var lines := PackedStringArray([
		"Meshy Importer: %s (Godot plugin)" % VERSION,
		"Godot: %s" % Engine.get_version_info().get("string", "?"),
		"OS: %s" % OS.get_name(),
		"Renderer: %s" % str(ProjectSettings.get_setting("rendering/renderer/rendering_method", "?")),
	])
	var last_error := str(Engine.get_meta("meshy_last_error", ""))
	if last_error != "":
		lines.append("Last error: " + last_error)
	return "\n".join(lines)


static func is_newer(candidate: String, current: String) -> bool:
	if candidate == "" or current == "":
		return false
	var a := candidate.trim_prefix("v").replace("-", ".").split(".")
	var b := current.trim_prefix("v").replace("-", ".").split(".")
	for i in 3:
		var x := int(a[i]) if i < a.size() else 0
		var y := int(b[i]) if i < b.size() else 0
		if x != y:
			return x > y
	return false
