"""Headless Blender smoke test.

    blender -b --factory-startup --python tests/hosts/blender_smoke.py -- <addon_parent_dir> <fixture.meshy> [...]

<addon_parent_dir> is a folder containing meshy_blender_importer (the repo's
blender/ folder, or an unpacked release ZIP). Exits non-zero on failure.
"""

import sys
import traceback

import bpy


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    addon_parent, fixtures = args[0], args[1:]
    sys.path.insert(0, addon_parent)
    import meshy_blender_importer as addon
    addon.register()
    assert hasattr(bpy.ops.import_scene, "meshy"), "operator not registered"
    if hasattr(bpy.types, "FileHandler"):
        assert "IO_FH_meshy" in dir(bpy.types), "FileHandler not registered"

    for path in fixtures:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        res = bpy.ops.import_scene.meshy(filepath=path)
        assert res == {'FINISHED'}, res
        meshes = [o for o in bpy.data.objects if o.type == 'MESH']
        assert meshes, "no mesh imported"
        obj = meshes[0]
        mesh = obj.data
        assert obj.select_get(), "imported object should stay selected"
        assert bpy.context.view_layer.objects.active == obj
        uv = mesh.uv_layers.active
        assert uv is not None
        us = [d.uv[0] for d in uv.data]
        vs = [d.uv[1] for d in uv.data]
        mats = [m for m in bpy.data.materials if m.users]
        imgs = [i for i in bpy.data.images if i.size[0] > 0]
        print("OK %s: %d verts, %d polys, uv u[%.4f..%.4f] v[%.4f..%.4f], materials=%d images=%s, bad=%s repaired=%s regen=%s"
              % (path, len(mesh.vertices), len(mesh.polygons), min(us), max(us), min(vs), max(vs), len(mats),
                 [(i.name, tuple(i.size)) for i in imgs], obj.get("FISHHWB_Meshy_UV_BadCorners"),
                 obj.get("FISHHWB_Meshy_UV_RepairedCorners"), obj.get("FISHHWB_Meshy_UV_Regenerated")))

    # Scale option scales the imported root about the origin.
    def root_scales(**kw):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        assert bpy.ops.import_scene.meshy(filepath=fixtures[0], **kw) == {'FINISHED'}
        return [o.scale[0] for o in bpy.data.objects if o.parent is None]
    plain, doubled = root_scales(), root_scales(scale=2.0)
    assert plain and all(abs(d - 2.0 * p) < 1e-6 for p, d in zip(plain, doubled)), (plain, doubled)

    # The "Keep Original Data" preset turns UV repair off.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    assert bpy.ops.import_scene.meshy(filepath=fixtures[-1], preset='ORIGINAL') == {'FINISHED'}
    obj = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    assert "FISHHWB_Meshy_UV_BadCorners" not in obj, dict(obj)

    # A wrong file fails cleanly with the plain-language message.
    import os
    import tempfile
    bad = os.path.join(tempfile.mkdtemp(), "page.meshy")
    with open(bad, "wb") as f:
        f.write(b"<!DOCTYPE html><html></html>")
    try:
        res = bpy.ops.import_scene.meshy(filepath=bad)
    except RuntimeError as exc:  # operator errors are raised in background mode
        assert "web page" in str(exc), exc
    else:
        assert res == {'CANCELLED'}, res
    assert "web page" in addon._state["last_error"], addon._state

    # Help/diagnostics operators and the Help submenu are registered.
    text = addon.diagnostics()
    assert "Meshy Importer: " + addon.VERSION in text and "web page" in text, text
    assert hasattr(bpy.types, "TOPBAR_MT_meshy_help")
    assert bpy.ops.wm.meshy_copy_diagnostics.poll()

    addon.unregister()
    print("BLENDER SMOKE TEST PASSED")


try:
    main()
except Exception:
    traceback.print_exc()
    sys.exit(1)
