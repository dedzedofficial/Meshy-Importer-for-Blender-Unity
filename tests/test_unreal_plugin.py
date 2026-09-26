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

        def add_sub_menu(self, owner, section, name, label, tip=""):
            sub = ToolMenu()
            ToolMenus.menus["Sub." + name] = sub
            return sub

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
    def show_message(title, text, kind):
        state["messages"].append(text)
        return state.get("answer", 0)

    def register_tick(fn):
        state["ticks"].append(fn)
        return fn

    u.EditorDialog = _Props(show_message=show_message)
    u.AppMsgType = _Props(OK=0, YES_NO=1)
    u.AppReturnType = _Props(YES=1, NO=0)
    u.register_slate_post_tick_callback = register_tick
    u.unregister_slate_post_tick_callback = lambda h: state["ticks"].remove(h)
    if state.get("gltf", True):
        u.InterchangeManager = object
    u.EditorUtilityLibrary = _Props(get_current_content_browser_path=lambda: "/Game/Props")
    u.SystemLibrary = _Props(get_engine_version=lambda: "5.3.2", launch_url=lambda url: state["urls"].append(url))
    u.log = lambda m: state["log"].append(m)
    u.log_warning = u.log
    u.log_error = lambda m: state["errors"].append(m)
    return u


class UnrealPluginTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.state = {"imported": [], "messages": [], "log": [], "errors": [], "project": self.tmp.name,
                      "ticks": [], "urls": []}
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
        self.assertIn("MeshyWatchInbox", tools.entries)
        self.assertIn("MeshyReportBug", self.mod.unreal.ToolMenus.menus["Sub.MeshyHelp"].entries)
        self.mod.validate_installation()
        self.assertIn(self.mod.VERSION, self.state["messages"][-1])

    def _tick(self, n=1):
        for _ in range(n):
            self.mod._watch["last_poll"] = 0.0
            for fn in list(self.state["ticks"]):
                fn(0.1)

    def test_inbox_watcher_imports_new_files_and_moves_them(self):
        self.mod.open_inbox = lambda: None
        self.mod.toggle_inbox_watch()
        self.assertEqual(len(self.state["ticks"]), 1)
        inbox = self.mod.inbox_dir()
        with open(os.path.join(inbox, "a.meshy"), "wb") as f:
            f.write(build_meshy())
        with open(os.path.join(inbox, "bad.meshy"), "wb") as f:
            f.write(b"<html></html>")
        self._tick()  # first sight: waits for the size to settle
        self.assertEqual(self.state["imported"], [])
        self._tick()
        self.assertEqual(len(self.state["imported"]), 1)
        self.assertTrue(os.path.exists(os.path.join(inbox, "Imported", "a.meshy")))
        self.assertTrue(os.path.exists(os.path.join(inbox, "Failed", "bad.meshy")))
        self.assertTrue(self.mod._load_settings()["watch_inbox"])
        self.mod.toggle_inbox_watch()
        self.assertEqual(self.state["ticks"], [])
        self.assertFalse(self.mod._load_settings()["watch_inbox"])

    def test_wrong_file_error_offers_help_link(self):
        bad = os.path.join(self.tmp.name, "page.meshy")
        with open(bad, "wb") as f:
            f.write(b"<!DOCTYPE html>")
        self.state["answer"] = 1  # "Yes, open the help page"
        self.mod._report(self.mod.import_meshy_files([bad]))
        self.assertIn("web page", self.state["messages"][-1])
        self.assertNotIn("Help: http", self.state["messages"][-1])
        self.assertTrue(self.state["urls"][-1].endswith("#wrong-file-errors"))
        self.assertIn("web page", self.mod.diagnostics())

    def test_report_bug_opens_prefilled_issue(self):
        self.mod._copy_to_clipboard = lambda text: True
        self.mod.report_bug()
        self.assertIn("issues/new?template=bug_report.yml", self.state["urls"][-1])
        self.assertIn("Unreal", self.state["urls"][-1])

    def test_update_check_runs_once_a_day_and_reports_new_versions(self):
        original = self.mod.support.fetch_latest_version
        self.addCleanup(setattr, self.mod.support, "fetch_latest_version", original)
        self.mod.support.fetch_latest_version = lambda ua, timeout=10.0: "99.0.0"
        self.mod.on_editor_start()
        for _ in range(100):
            if not self.state["ticks"]:
                break
            self._tick()
            import time
            time.sleep(0.01)
        self.assertEqual(self.mod._state["latest"], "99.0.0")
        self.assertTrue(any("99.0.0 is available" in m for m in self.state["log"]))
        self.state["log"].clear()
        self.mod.on_editor_start()  # checked less than a day ago: no second request
        self.assertEqual(self.state["ticks"], [])


class UnrealNoGltfTests(unittest.TestCase):
    def test_missing_gltf_importer_is_explained_once(self):
        with tempfile.TemporaryDirectory() as tmp:
            state = {"imported": [], "messages": [], "log": [], "errors": [], "project": tmp,
                     "ticks": [], "urls": [], "gltf": False}
            sys.modules["unreal"] = _make_unreal_stub(state)
            sys.path.insert(0, PLUGIN_PY)
            sys.modules.pop("meshy_unreal", None)
            try:
                import meshy_unreal
                meshy_unreal._save_settings(check_updates=False)
                meshy_unreal.on_editor_start()
                meshy_unreal.on_editor_start()
                self.assertEqual(len([m for m in state["messages"] if "Edit > Plugins" in m]), 1)
                self.assertIn("NOT FOUND", meshy_unreal.diagnostics())
            finally:
                sys.path.remove(PLUGIN_PY)
                sys.modules.pop("unreal", None)
                sys.modules.pop("meshy_unreal", None)


if __name__ == "__main__":
    unittest.main()
