bl_info = {
    "name": "Meshy Importer for Blender & Unity",
    "author": "FISHHWB",
    "version": (1, 4, 1),
    "blender": (3, 6, 0),
    "location": "File > Import > Meshy Model (.meshy)",
    "description": "Imports Meshy .meshy containers locally through Blender's native GLB importer.",
    "category": "Import-Export",
}

import os
import sys
import tempfile

import bpy
from bpy.props import BoolProperty, CollectionProperty, StringProperty
from bpy.types import Operator, OperatorFileListElement
from bpy_extras.io_utils import ImportHelper

try:
    # The release ZIP bundles the shared decoder as a sub-package.
    from .meshy_core.decode import decode_meshy_file
    from .meshy_core.normalize import NormalizeOptions, normalize_glb
    from .meshy_core.uv_repair import repair_uvs
except ImportError:  # running straight from a repository checkout
    _core = os.path.join(os.path.dirname(os.path.abspath(__file__)), os.pardir, os.pardir, "core", "python")
    if os.path.isdir(_core) and _core not in sys.path:
        sys.path.append(_core)
    from meshy_core.decode import decode_meshy_file
    from meshy_core.normalize import NormalizeOptions, normalize_glb
    from meshy_core.uv_repair import repair_uvs


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

    auto_repair_uvs: BoolProperty(
        name="Auto-repair UVs",
        description="Fix broken UVs (NaN, wild outliers, collapsed triangles) while leaving valid Meshy "
                    "UVs untouched. Meshes without any UVs get a Smart UV Project",
        default=True,
    )
    remove_unused_slots: BoolProperty(
        name="Remove Unused Material Slots",
        description="Drop material slots no face uses (the materials themselves are kept)",
        default=True,
    )
    save_decoded_glb: BoolProperty(
        name="Save Decoded .glb",
        description="Also write the decoded model next to the .meshy file as a plain .glb",
        default=False,
    )

    def invoke(self, context, event):
        if self.directory and len(self.files):  # drag-and-drop through the FileHandler
            return context.window_manager.invoke_props_dialog(self)
        return ImportHelper.invoke(self, context, event)

    def _paths(self):
        if self.directory and len(self.files):
            return [os.path.join(self.directory, f.name) for f in self.files if f.name]
        return [self.filepath]

    def execute(self, context):
        paths = [p for p in self._paths() if p.lower().endswith(".meshy")]
        if not paths:
            self.report({'ERROR'}, "No .meshy file selected.")
            return {'CANCELLED'}

        imported_all = []
        failures = []
        for path in paths:
            try:
                imported_all.extend(self._import_one(context, path))
            except Exception as exc:
                failures.append("%s: %s" % (os.path.basename(path), exc))

        _select(context, imported_all)
        if failures:
            for msg in failures:
                self.report({'ERROR'}, "Meshy import failed: " + msg)
            if not imported_all:
                return {'CANCELLED'}
        self.report({'INFO'}, "Imported %d Meshy model(s)." % (len(paths) - len(failures)))
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
            _prepare_imported_objects(context, objects, self.auto_repair_uvs, self.remove_unused_slots)
            return objects
        finally:
            try:
                os.remove(temp_path)
            except OSError:
                pass


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

    scene = context.scene
    scene["FISHHWB_Meshy_LastImport_Meshes"] = mesh_count
    scene["FISHHWB_Meshy_LastImport_Materials"] = material_count
    scene["FISHHWB_Meshy_LastImport_Vertices"] = vertex_count
    scene["FISHHWB_Meshy_LastImport_Polygons"] = polygon_count
    scene["FISHHWB_Meshy_LastImport_GeneratedUVs"] = generated_uvs
    scene["FISHHWB_Meshy_LastImport_RepairedUVMeshes"] = repaired_uv_meshes
    scene["FISHHWB_Meshy_LastImport_Rigged"] = skinned > 0


def menu_func_import(self, context):
    self.layout.operator(IMPORT_OT_meshy.bl_idname, text="Meshy Model (.meshy)")


def menu_func_help(self, context):
    self.layout.separator()
    self.layout.operator("wm.meshy_support", text="Meshy Importer Support / Patreon")
    self.layout.operator("wm.meshy_docs", text="Meshy Importer Documentation")


class WM_OT_meshy_support(Operator):
    bl_idname = "wm.meshy_support"
    bl_label = "Meshy Importer Support"

    def execute(self, context):
        bpy.ops.wm.url_open(url="https://www.patreon.com/cw/DedZed")
        return {'FINISHED'}


class WM_OT_meshy_docs(Operator):
    bl_idname = "wm.meshy_docs"
    bl_label = "Meshy Importer Documentation"

    def execute(self, context):
        bpy.ops.wm.url_open(url="https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity")
        return {'FINISHED'}


classes = [IMPORT_OT_meshy, WM_OT_meshy_support, WM_OT_meshy_docs]

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


def unregister():
    bpy.types.TOPBAR_MT_file_import.remove(menu_func_import)
    bpy.types.TOPBAR_MT_help.remove(menu_func_help)
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
