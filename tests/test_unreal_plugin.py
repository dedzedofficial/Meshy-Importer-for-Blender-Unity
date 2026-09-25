"""Exercises unreal/MeshyImporter/Content/Python/meshy_unreal.py against a recording
stub of the `unreal` module (the real editor cannot run in CI)."""

import os
import sys
import tempfile
import types
import unittest

import _path
from meshy_core.glb import read_chunks
from meshy_fixtures import build_meshy

PLUGIN_PY = os.path.join(_path.ROOT, "unreal", "MeshyImporter", "Content", "Python")


def _make_unreal_stub(state):
    u = types.ModuleType("unreal")

    class _Props(object):
        def __init__(self, **kw):
            self.__dict__.update(kw)

        def set_editor_property(self, k, v):
            setattr(self, k, v)

        def get_editor_property(self, k):
            return getattr(self, k, None)

    class AssetImportTask(_Props):
        pass

    class AssetTools(object):
        def import_asset_tasks(self, tasks):
            for t in tasks:
                with open(t.filename, "rb") as f:  # the temp GLB must exist during import
                    state["imported"].append((t.destination_path, f.read(), t.automated, t.save))
                t.imported_object_paths = [t.destination_path + "/Mesh"] if state.get("succeed", True) else []

    class ScopedSlowTask(object):
        def __init__(self, work, desc):
            pass

        def __enter__(self):
            return self

        def __exit__(self, *a):
            return False

        def make_dialog(self, cancel):
            pass

        def should_cancel(self):
            return False

        def enter_progress_frame(self, work, desc=""):
            pass

    class ToolMenu(object):
        def __init__(self):
            self.entries = []

        def add_section(self, name, label):
            pass

        def add_menu_entry(self, section, entry):
            self.entries.append(entry.name)

    class ToolMenus(object):
        menus = {"LevelEditor.MainMenu.Tools": ToolMenu(), "ContentBrowser.FolderContextMenu": ToolMenu()}

        @classmethod
        def get(cls):
            return cls()

        def find_menu(self, name):
            return self.menus.get(name)

        def refresh_all_widgets(self):
            pass

    class ToolMenuEntry(_Props):
        def set_label(self, v):
            self.label = v

        def set_tool_tip(self, v):
            self.tip = v

        def set_string_command(self, t, c, s):
            self.command = s

    u.AssetImportTask = AssetImportTask
    u.AssetToolsHelpers = _Props(get_asset_tools=lambda: AssetTools())
    u.ScopedSlowTask = ScopedSlowTask
    u.ToolMenus = ToolMenus
    u.ToolMenuEntry = ToolMenuEntry
    u.MultiBlockType = _Props(MENU_ENTRY=0)
    u.ToolMenuStringCommandType = _Props(PYTHON=0)
    u.Paths = _Props(project_dir=lambda: state["project"], convert_relative_path_to_full=lambda p: p)
    u.EditorDialog = _Props(show_message=lambda *a: state["messages"].append(a[1]))
    u.AppMsgType = _Props(OK=0)
    u.EditorUtilityLibrary = _Props(get_current_content_browser_path=lambda: "/Game/Props")
    u.SystemLibrary = _Props(get_engine_version=lambda: "5.3.2", launch_url=lambda u: None)
    u.log = lambda m: state["log"].append(m)
    u.log_warning = u.log
    u.log_error = lambda m: state["errors"].append(m)
    return u


class UnrealPluginTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.state = {"imported": [], "messages": [], "log": [], "errors": [], "project": self.tmp.name}
        sys.modules["unreal"] = _make_unreal_stub(self.state)
        sys.path.insert(0, PLUGIN_PY)
        sys.modules.pop("meshy_unreal", None)
        import meshy_unreal
        self.mod = meshy_unreal

    def tearDown(self):
        sys.path.remove(PLUGIN_PY)
        sys.modules.pop("unreal", None)
        sys.modules.pop("meshy_unreal", None)
        self.tmp.cleanup()

    def _fixture(self, name="My Model.v2.meshy", **kw):
        path = os.path.join(self.tmp.name, name)
        with open(path, "wb") as f:
            f.write(build_meshy(**kw))
        return path

    def test_import_hands_plain_gltf_to_interchange(self):
        results = self.mod.import_meshy_files([self._fixture()], "/Game/Meshy")
        self.assertEqual([r[1] for r in results], [True])
        dest, glb, automated, save = self.state["imported"][0]
        self.assertEqual(dest, "/Game/Meshy/My_Model_v2")
        self.assertTrue(automated and save)
        gltf, _ = read_chunks(glb)
        self.assertNotIn("extensionsRequired", gltf)
        self.assertEqual(gltf["images"][0]["mimeType"], "image/png")

    def test_failures_are_reported_not_raised(self):
        bad = os.path.join(self.tmp.name, "bad.meshy")
        with open(bad, "wb") as f:
            f.write(b"not a meshy file")
        self.state["succeed"] = True
        results = self.mod.import_meshy_files([bad, self._fixture()], "/Game/Meshy")
        self.assertEqual([r[1] for r in results], [False, True])
        self.assertTrue(self.state["errors"])

    def test_empty_import_result_is_an_error(self):
        self.state["succeed"] = False
        results = self.mod.import_meshy_files([self._fixture()], "/Game/Meshy")
        self.assertFalse(results[0][1])

    def test_inbox_and_menus(self):
        inbox = self.mod.inbox_dir()
        self.assertTrue(os.path.isdir(inbox))
        with open(os.path.join(inbox, "a.meshy"), "wb") as f:
            f.write(build_meshy())
        self.mod.import_inbox()
        self.assertEqual(self.state["imported"][0][0], "/Game/Props/a")
        self.mod.register_menus()
        tools = self.mod.unreal.ToolMenus.menus["LevelEditor.MainMenu.Tools"]
        self.assertIn("MeshyImportFiles", tools.entries)
        self.mod.validate_installation()
        self.assertIn("1.4.1", self.state["messages"][-1])


if __name__ == "__main__":
    unittest.main()
