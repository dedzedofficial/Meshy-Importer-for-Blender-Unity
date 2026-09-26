bl_info = {
    "name": "Meshy Importer for Blender & Unity",
    "author": "FISHHWB",
    "version": (1, 5, 0),
    "blender": (3, 6, 0),
    "location": "File > Import > Meshy Model (.meshy)",
    "description": "Imports Meshy .meshy containers locally through Blender's native GLB importer.",
    "category": "Import-Export",
}

import json
import os
import platform
import sys
import tempfile
import threading
import time

import bpy
from bpy.props import BoolProperty, CollectionProperty, EnumProperty, FloatProperty, StringProperty
from bpy.types import AddonPreferences, Menu, Operator, OperatorFileListElement
from bpy_extras.io_utils import ImportHelper

try:
    # The release ZIP bundles the shared decoder as a sub-package.
    from .meshy_core.decode import decode_meshy_file
    from .meshy_core.normalize import NormalizeOptions, normalize_glb
    from .meshy_core.uv_repair import repair_uvs
    from .meshy_core import support
except ImportError:  # running straight from a repository checkout
    _core = os.path.join(os.path.dirname(os.path.abspath(__file__)), os.pardir, os.pardir, "core", "python")
    if os.path.isdir(_core) and _core not in sys.path:
        sys.path.append(_core)
    from meshy_core.decode import decode_meshy_file
    from meshy_core.normalize import NormalizeOptions, normalize_glb
    from meshy_core.uv_repair import repair_uvs
    from meshy_core import support

VERSION = "%d.%d.%d" % bl_info["version"]

# What the Help > Meshy Importer menu and "Copy Diagnostics" report.
_state = {"last_import": "", "last_error": "", "latest": "", "checking": False}


def _prepare_glb(path, save_decoded_glb):
    """Decrypt a .meshy file and make it something Blender's glTF importer accepts.

    Blender's importer does not implement EXT_meshopt_compression (which Meshy
    uses), so that is decoded here. WebP textures are only converted for Blender
    versions whose importer predates EXT_texture_webp.
    """
    glb = decode_meshy_file(path)
    glb, _report = normalize_glb(glb, NormalizeOptions.for_host(
        "blender", webp_to_png=bpy.app.version < (4, 0, 0)))
    if save_decoded_glb:
        out = os.path.splitext(path)[0] + ".glb"
        if os.path.exists(out):
            raise FileExistsError("%s already exists; not overwriting it" % os.path.basename(out))
        with open(out, "wb") as f:
            f.write(glb)
    return glb


class IMPORT_OT_meshy(Operator, ImportHelper):
    """Import one or more Meshy .meshy model payloads"""
    bl_idname = "import_scene.meshy"
    bl_label = "Import Meshy Model"
    bl_options = {'REGISTER', 'UNDO', 'PRESET'}

    filename_ext = ".meshy"
    filter_glob: StringProperty(default="*.meshy", options={'HIDDEN'})
    files: CollectionProperty(type=OperatorFileListElement, options={'HIDDEN', 'SKIP_SAVE'})
    directory: StringProperty(subtype='DIR_PATH', options={'HIDDEN', 'SKIP_SAVE'})

    preset: EnumProperty(
        name="Preset",
        description="Set the options below in one go",
        items=[
            ('DEFAULT', "Default", "Repair broken UVs and remove unused material slots"),
            ('ORIGINAL', "Keep Original Data", "Import exactly what the file contains: no UV repair or cleanup"),
            ('CUSTOM', "Custom", "Your own combination of the options below"),
        ],
        default='DEFAULT',
        update=lambda self, context: _apply_preset(self),
    )
    scale: FloatProperty(
        name="Scale",
        description="Uniform scale applied to the imported model (1.0 keeps Meshy's size)",
        default=1.0, min=0.0001, soft_min=0.001, soft_max=100.0,
    )
    auto_repair_uvs: BoolProperty(
        name="Auto-repair UVs",
        description="Fix broken UVs (NaN, wild outliers, collapsed triangles) while leaving valid Meshy "
                    "UVs untouched. Meshes without any UVs get a Smart UV Project",
        default=True,
        update=lambda self, context: _match_preset(self),
    )
    remove_unused_slots: BoolProperty(
        name="Remove Unused Material Slots",
        description="Drop material slots no face uses (the materials themselves are kept)",
        default=True,
        update=lambda self, context: _match_preset(self),
    )
    save_decoded_glb: BoolProperty(
        name="Save Decoded .glb",
        description="Also write the decoded model next to the .meshy file as a plain .glb",
        default=False,
    )

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False
        layout.prop(self, "preset")
        layout.prop(self, "scale")
        col = layout.column(heading="Options")
        col.prop(self, "auto_repair_uvs")
        col.prop(self, "remove_unused_slots")
        col.prop(self, "save_decoded_glb")
        layout.separator()
        layout.operator("wm.url_open", text="How Do I Get a .meshy File?", icon='QUESTION').url = support.GETTING_A_FILE_URL

    def invoke(self, context, event):
        if self.directory and len(self.files):  # drag-and-drop through the FileHandler
            return context.window_manager.invoke_props_dialog(self)
        return ImportHelper.invoke(self, context, event)

    def _paths(self):
        if self.directory and len(self.files):
            return [os.path.join(self.directory, f.name) for f in self.files if f.name]
        return [self.filepath]

    def execute(self, context):
        # Scripts may pass preset=... without the UI's update callbacks running.
        if self.properties.is_property_set("preset"):
            _apply_preset(self)
        paths = [p for p in self._paths() if p.lower().endswith(".meshy")]
        if not paths:
            self.report({'ERROR'}, "No .meshy file selected.")
            return {'CANCELLED'}

        imported_all = []
        failures = []
        totals = {}
        for path in paths:
            try:
                objects, stats = self._import_one(context, path)
                imported_all.extend(objects)
                for k, v in stats.items():
                    totals[k] = totals.get(k, 0) + v
            except Exception as exc:
                failures.append((os.path.basename(path), str(exc)))

        _select(context, imported_all)
        if failures:
            _state["last_error"] = "; ".join("%s: %s" % f for f in failures)
            for name, msg in failures:
                self.report({'ERROR'}, "Meshy import failed: %s: %s" % (name, support.strip_help(msg)))
            _show_failure_popup(context, failures)
            if not imported_all:
                return {'CANCELLED'}
        ok = len(paths) - len(failures)
        summary = _summary(ok, paths[0] if ok == 1 and not failures else None, totals)
        _state["last_import"] = summary
        print("Meshy Importer: " + summary)
        self.report({'INFO'}, summary)
        return {'FINISHED'}

    def _import_one(self, context, path):
        glb = _prepare_glb(path, self.save_decoded_glb)
        fd, temp_path = tempfile.mkstemp(suffix=".glb", prefix="meshy_")
        os.close(fd)
        try:
            with open(temp_path, "wb") as f:
                f.write(glb)
            before = set(bpy.data.objects)
            result = bpy.ops.import_scene.gltf(filepath=temp_path)
            if 'FINISHED' not in result:
                raise RuntimeError("Blender's GLB importer did not finish.")
            objects = [obj for obj in bpy.data.objects if obj not in before]
            stats = _prepare_imported_objects(context, objects, self.auto_repair_uvs, self.remove_unused_slots)
            if abs(self.scale - 1.0) > 1e-9:
                _apply_scale(objects, self.scale)
            return objects, stats
        finally:
            try:
                os.remove(temp_path)
            except OSError:
                pass


_PRESETS = {
    'DEFAULT': {"auto_repair_uvs": True, "remove_unused_slots": True},
    'ORIGINAL': {"auto_repair_uvs": False, "remove_unused_slots": False},
}
_applying_preset = [False]


def _apply_preset(op):
    values = _PRESETS.get(op.preset)
    if not values or _applying_preset[0]:
        return
    _applying_preset[0] = True
    try:
        for k, v in values.items():
            setattr(op, k, v)
    finally:
        _applying_preset[0] = False


def _match_preset(op):
    """Show which preset the current options correspond to (or Custom)."""
    if _applying_preset[0]:
        return
    current = {k: getattr(op, k) for k in _PRESETS['DEFAULT']}
    name = next((n for n, v in _PRESETS.items() if v == current), 'CUSTOM')
    if op.preset != name:
        _applying_preset[0] = True
        try:
            op.preset = name
        finally:
            _applying_preset[0] = False


def _apply_scale(objects, factor):
    """Scale the imported hierarchy about the world origin (only its root objects)."""
    imported = set(objects)
    for obj in objects:
        if obj.parent is None or obj.parent not in imported:
            obj.scale = obj.scale * factor
            obj.location = obj.location * factor


def _summary(count, single_path, totals):
    what = os.path.basename(single_path) if single_path else "%d Meshy model(s)" % count
    text = "Imported %s: %d mesh(es), %s faces, %d material(s)" % (
        what, totals.get("meshes", 0), format(totals.get("polygons", 0), ","), totals.get("materials", 0))
    fixed = totals.get("repaired_uv_meshes", 0) + totals.get("generated_uvs", 0)
    if fixed:
        text += "; UVs fixed on %d mesh(es)" % fixed
    if totals.get("rigged", 0):
        text += "; rigged"
    return text + "."


def _show_failure_popup(context, failures):
    if bpy.app.background or context.window_manager is None:
        return
    name, message = failures[0]
    help_url = support.help_link(message)

    def draw(menu, _context):
        layout = menu.layout
        for fname, msg in failures[:5]:
            layout.label(text="%s: %s" % (fname, support.strip_help(msg)))
        if len(failures) > 5:
            layout.label(text="...and %d more (see the Info editor)" % (len(failures) - 5))
        layout.separator()
        if help_url:
            layout.operator("wm.url_open", text="Open Help", icon='HELP').url = help_url
        layout.operator("wm.meshy_copy_diagnostics", icon='COPYDOWN')
        layout.operator("wm.meshy_report_bug", icon='URL')

    try:
        context.window_manager.popup_menu(draw, title="Meshy import failed", icon='ERROR')
    except Exception:
        pass  # no window (e.g. called from a script)


def _select(context, objects):
    """Leave the imported objects selected with one active, like Blender's own importers."""
    alive = [o for o in objects if o.name in bpy.data.objects]
    if not alive:
        return
    for o in context.view_layer.objects:
        o.select_set(False)
    for o in alive:
        try:
            o.select_set(True)
        except RuntimeError:
            pass  # not in the active view layer
    meshes = [o for o in alive if o.type == 'MESH']
    context.view_layer.objects.active = meshes[0] if meshes else alive[0]


def _repair_mesh_uvs(mesh):
    """Run the shared UV repair on the active UV layer. Returns the stats dict."""
    uv_layer = mesh.uv_layers.active or mesh.uv_layers[0]
    mesh.calc_loop_triangles()
    loop_count = len(mesh.loops)

    vert_index = [0] * loop_count
    mesh.loops.foreach_get("vertex_index", vert_index)
    co = [0.0] * (len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    flat_uv = [0.0] * (loop_count * 2)
    uv_layer.data.foreach_get("uv", flat_uv)
    tri_loops = [0] * (len(mesh.loop_triangles) * 3)
    mesh.loop_triangles.foreach_get("loops", tri_loops)

    # Treat every loop as a vertex: Blender stores UVs per face corner.
    positions = [(co[3 * v], co[3 * v + 1], co[3 * v + 2]) for v in vert_index]
    uvs = [(flat_uv[2 * i], flat_uv[2 * i + 1]) for i in range(loop_count)]
    new_uvs, stats = repair_uvs(positions, uvs, tri_loops)
    if new_uvs is not None and not stats["regenerated"]:
        uv_layer.data.foreach_set("uv", [c for uv in new_uvs for c in uv])
        mesh.update()
    return stats


def _smart_project(context, obj):
    try:
        with context.temp_override(active_object=obj, object=obj, selected_objects=[obj],
                                   selected_editable_objects=[obj]):
            context.view_layer.objects.active = obj
            bpy.ops.object.mode_set(mode='EDIT')
            bpy.ops.mesh.select_all(action='SELECT')
            bpy.ops.uv.smart_project(island_margin=0.02)
            bpy.ops.object.mode_set(mode='OBJECT')
        return True
    except Exception:
        if obj.mode != 'OBJECT':
            try:
                bpy.ops.object.mode_set(mode='OBJECT')
            except Exception:
                pass
        return False


def _prepare_imported_objects(context, objects, auto_repair_uvs=True, remove_unused_slots=True):
    """Apply lightweight post-import cleanup without changing valid Meshy data."""
    mesh_count = material_count = vertex_count = polygon_count = 0
    generated_uvs = repaired_uv_meshes = 0
    skinned = 0
    seen_meshes = set()

    for obj in objects:
        if obj.type == 'ARMATURE':
            skinned += 1
            continue
        if obj.type != 'MESH' or obj.data is None:
            continue

        mesh = obj.data
        mesh_count += 1
        vertex_count += len(mesh.vertices)
        polygon_count += len(mesh.polygons)
        material_count += len([slot for slot in obj.material_slots if slot.material])

        if auto_repair_uvs and mesh.name not in seen_meshes and len(mesh.polygons) > 0:
            seen_meshes.add(mesh.name)
            if len(mesh.uv_layers) == 0:
                mesh.uv_layers.new(name="UVMap")
                if _smart_project(context, obj):
                    generated_uvs += 1
                obj["FISHHWB_Meshy_UV_Regenerated"] = True
            else:
                stats = _repair_mesh_uvs(mesh)
                if stats["regenerated"] and _smart_project(context, obj):
                    generated_uvs += 1
                elif stats["bad_vertices"]:
                    repaired_uv_meshes += 1
                obj["FISHHWB_Meshy_UV_BadCorners"] = stats["bad_vertices"]
                obj["FISHHWB_Meshy_UV_RepairedCorners"] = stats["repaired_vertices"]
                obj["FISHHWB_Meshy_UV_Regenerated"] = stats["regenerated"]

        if remove_unused_slots and len(obj.material_slots) > 1:
            try:
                with context.temp_override(active_object=obj, object=obj, selected_objects=[obj]):
                    bpy.ops.object.material_slot_remove_unused()
            except Exception:
                pass

        obj["FISHHWB_Meshy_VertexCount"] = len(mesh.vertices)
        obj["FISHHWB_Meshy_PolygonCount"] = len(mesh.polygons)
        obj["FISHHWB_Meshy_HasUV"] = len(mesh.uv_layers) > 0

    stats = {"meshes": mesh_count, "materials": material_count, "vertices": vertex_count,
             "polygons": polygon_count, "generated_uvs": generated_uvs,
             "repaired_uv_meshes": repaired_uv_meshes, "rigged": 1 if skinned else 0}
    scene = context.scene
    scene["FISHHWB_Meshy_LastImport_Meshes"] = mesh_count
    scene["FISHHWB_Meshy_LastImport_Materials"] = material_count
    scene["FISHHWB_Meshy_LastImport_Vertices"] = vertex_count
    scene["FISHHWB_Meshy_LastImport_Polygons"] = polygon_count
    scene["FISHHWB_Meshy_LastImport_GeneratedUVs"] = generated_uvs
    scene["FISHHWB_Meshy_LastImport_RepairedUVMeshes"] = repaired_uv_meshes
    scene["FISHHWB_Meshy_LastImport_Rigged"] = skinned > 0
    return stats


def menu_func_import(self, context):
    self.layout.operator(IMPORT_OT_meshy.bl_idname, text="Meshy Model (.meshy)")


def menu_func_help(self, context):
    self.layout.separator()
    self.layout.menu(TOPBAR_MT_meshy_help.bl_idname, icon='IMPORT')


class TOPBAR_MT_meshy_help(Menu):
    bl_idname = "TOPBAR_MT_meshy_help"
    bl_label = "Meshy Importer"

    def draw(self, context):
        layout = self.layout
        if support.is_newer(_state["latest"], VERSION):
            layout.operator("wm.url_open", text="Update Available: %s" % _state["latest"],
                            icon='ERROR').url = support.RELEASES_URL
            layout.separator()
        layout.operator("wm.url_open", text="How Do I Get a .meshy File?").url = support.GETTING_A_FILE_URL
        layout.operator("wm.url_open", text="Troubleshooting").url = support.TROUBLESHOOTING_URL
        layout.operator("wm.meshy_docs", text="Documentation")
        layout.separator()
        layout.operator("wm.meshy_copy_diagnostics", icon='COPYDOWN')
        layout.operator("wm.meshy_report_bug", icon='URL')
        layout.operator("wm.meshy_check_updates")
        layout.separator()
        layout.operator("wm.url_open", text="Discord").url = support.DISCORD_URL
        layout.operator("wm.meshy_support", text="Support on Patreon")


class WM_OT_meshy_support(Operator):
    bl_idname = "wm.meshy_support"
    bl_label = "Meshy Importer Support"

    def execute(self, context):
        bpy.ops.wm.url_open(url=support.PATREON_URL)
        return {'FINISHED'}


class WM_OT_meshy_docs(Operator):
    bl_idname = "wm.meshy_docs"
    bl_label = "Meshy Importer Documentation"

    def execute(self, context):
        bpy.ops.wm.url_open(url=support.REPO_URL + "/tree/main/blender")
        return {'FINISHED'}


def diagnostics():
    """Everything a bug report needs, as plain text."""
    gltf = "unknown"
    try:
        import io_scene_gltf2
        gltf = "%d.%d.%d" % tuple(io_scene_gltf2.bl_info["version"][:3])
    except Exception:
        pass
    try:
        from .meshy_core.webp_vp8 import has_pillow
    except ImportError:
        from meshy_core.webp_vp8 import has_pillow
    lines = [
        "Meshy Importer: %s (Blender add-on)" % VERSION,
        "Blender: %s" % bpy.app.version_string,
        "OS: %s" % platform.platform(),
        "Python: %s" % sys.version.split()[0],
        "glTF importer: %s" % gltf,
        "WebP: %s" % ("Blender's importer" if bpy.app.version >= (4, 0, 0)
                      else ("Pillow" if has_pillow() else "built-in decoder")),
    ]
    if _state["latest"]:
        lines.append("Latest release seen: %s" % _state["latest"])
    if _state["last_import"]:
        lines.append("Last import: %s" % _state["last_import"])
    if _state["last_error"]:
        lines.append("Last error: %s" % _state["last_error"])
    return "\n".join(lines)


class WM_OT_meshy_copy_diagnostics(Operator):
    """Copy version and error details to the clipboard for a bug report or Discord message"""
    bl_idname = "wm.meshy_copy_diagnostics"
    bl_label = "Copy Diagnostics"

    def execute(self, context):
        text = diagnostics()
        context.window_manager.clipboard = text
        print("Meshy Importer diagnostics:\n" + text)
        self.report({'INFO'}, "Meshy Importer diagnostics copied to the clipboard.")
        return {'FINISHED'}


class WM_OT_meshy_report_bug(Operator):
    """Open a pre-filled bug report on GitHub (diagnostics are also copied to the clipboard)"""
    bl_idname = "wm.meshy_report_bug"
    bl_label = "Report a Bug..."

    def execute(self, context):
        text = diagnostics()
        context.window_manager.clipboard = text
        host = "Blender %s, %s" % (bpy.app.version_string, platform.system())
        bpy.ops.wm.url_open(url=support.bug_report_url(VERSION + " (Blender)", host, text))
        return {'FINISHED'}


# ---- update check ---------------------------------------------------------------
# Only runs when Blender's "Allow Online Access" (4.2+) is on and the add-on preference
# is enabled; at most once a day, in a background thread. Nothing about you is sent.

def _online_allowed():
    return getattr(bpy.app, "online_access", True)


def _prefs():
    addon = bpy.context.preferences.addons.get(__name__)
    return addon.preferences if addon else None


def _stamp_path():
    try:
        folder = bpy.utils.user_resource('CONFIG', path="meshy_importer", create=True)
    except Exception:
        folder = tempfile.gettempdir()
    return os.path.join(folder, "update_check.json")


def _read_stamp():
    try:
        with open(_stamp_path(), encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


def _start_update_check(manual):
    if _state["checking"]:
        return
    _state["checking"] = True
    result = {}

    def worker():
        result["latest"] = support.fetch_latest_version("MeshyImporter-Blender/" + VERSION)

    thread = threading.Thread(target=worker, daemon=True)
    thread.start()

    def poll():
        if thread.is_alive():
            return 0.5
        _state["checking"] = False
        latest = result.get("latest")
        if latest:
            _state["latest"] = latest
            try:
                with open(_stamp_path(), "w", encoding="utf-8") as f:
                    json.dump({"checked": time.time(), "latest": latest}, f)
            except Exception:
                pass
        newer = support.is_newer(latest, VERSION)
        if newer:
            print("Meshy Importer %s is available (you have %s): %s" % (latest, VERSION, support.RELEASES_URL))
        if manual:
            _notify("Version %s is available (you have %s). See Help > Meshy Importer." % (latest, VERSION) if newer
                    else ("You have the latest version (%s)." % VERSION if latest
                          else "Could not reach GitHub to check for updates."))
        return None

    bpy.app.timers.register(poll, first_interval=0.5)


def _notify(text):
    def draw(menu, _context):
        menu.layout.label(text=text)
    try:
        bpy.context.window_manager.popup_menu(draw, title="Meshy Importer", icon='INFO')
    except Exception:
        print("Meshy Importer: " + text)


def _auto_update_check():
    stamp = _read_stamp()
    _state["latest"] = stamp.get("latest", "") or ""
    prefs = _prefs()
    if bpy.app.background or not _online_allowed() or (prefs is not None and not prefs.check_updates):
        return None
    if time.time() - float(stamp.get("checked", 0)) >= 24 * 3600:
        _start_update_check(False)
    return None


class WM_OT_meshy_check_updates(Operator):
    """Check GitHub for a newer version of the Meshy Importer"""
    bl_idname = "wm.meshy_check_updates"
    bl_label = "Check for Updates"

    def execute(self, context):
        if not _online_allowed():
            self.report({'WARNING'}, "Online access is off. Turn on Preferences > System > Network > "
                                     "Allow Online Access to check for updates.")
            return {'CANCELLED'}
        _start_update_check(True)
        return {'FINISHED'}


class MeshyImporterPreferences(AddonPreferences):
    bl_idname = __name__

    check_updates: BoolProperty(
        name="Check for updates once a day",
        description="Look up the latest release on GitHub (needs Blender's online access to be allowed)",
        default=True,
    )

    def draw(self, context):
        layout = self.layout
        row = layout.row()
        row.prop(self, "check_updates")
        row.operator("wm.meshy_check_updates")
        if not _online_allowed():
            layout.label(text="Online access is off in Preferences > System, so no update checks run.", icon='INFO')
        row = layout.row()
        row.operator("wm.meshy_copy_diagnostics", icon='COPYDOWN')
        row.operator("wm.meshy_report_bug", icon='URL')
        row = layout.row()
        row.operator("wm.url_open", text="How Do I Get a .meshy File?").url = support.GETTING_A_FILE_URL
        row.operator("wm.url_open", text="Troubleshooting").url = support.TROUBLESHOOTING_URL


classes = [IMPORT_OT_meshy, WM_OT_meshy_support, WM_OT_meshy_docs, WM_OT_meshy_copy_diagnostics,
           WM_OT_meshy_report_bug, WM_OT_meshy_check_updates, TOPBAR_MT_meshy_help, MeshyImporterPreferences]

if hasattr(bpy.types, "FileHandler"):  # Blender 4.1+: drag .meshy files into the viewport
    class IO_FH_meshy(bpy.types.FileHandler):
        bl_idname = "IO_FH_meshy"
        bl_label = "Meshy Model"
        bl_import_operator = "import_scene.meshy"
        bl_file_extensions = ".meshy"

        @classmethod
        def poll_drop(cls, context):
            return context.area is not None and context.area.type in {'VIEW_3D', 'OUTLINER'}

    classes.append(IO_FH_meshy)


def register():
    for cls in classes:
        bpy.utils.register_class(cls)
    bpy.types.TOPBAR_MT_file_import.append(menu_func_import)
    bpy.types.TOPBAR_MT_help.append(menu_func_help)
    bpy.app.timers.register(_auto_update_check, first_interval=10.0)


def unregister():
    if bpy.app.timers.is_registered(_auto_update_check):
        bpy.app.timers.unregister(_auto_update_check)
    bpy.types.TOPBAR_MT_file_import.remove(menu_func_import)
    bpy.types.TOPBAR_MT_help.remove(menu_func_help)
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
