"""Meshy Importer for Unreal Engine 5.3+ (Editor Python).

Turns each .meshy payload into a plain glTF GLB with meshy_core (decrypt, decode
meshopt, dequantize, bake texture transforms, repair UVs, WebP -> PNG) and imports
that through Unreal's own glTF/Interchange pipeline with an AssetImportTask.

Menu: Tools > Meshy (see register_menus). Must stay Python 3.9 compatible (UE 5.3).
"""

import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

import unreal

_HERE = os.path.dirname(os.path.abspath(__file__))
try:
    import meshy_core  # bundled next to this file in the release ZIP
except ImportError:  # repository checkout: use core/python directly
    _core = os.path.normpath(os.path.join(_HERE, "..", "..", "..", "..", "core", "python"))
    if os.path.isdir(_core) and _core not in sys.path:
        sys.path.append(_core)
    import meshy_core

from meshy_core.normalize import NormalizeOptions, normalize_meshy_file  # noqa: E402
from meshy_core.webp_vp8 import has_pillow  # noqa: E402

VERSION = "1.4.1"
DEFAULT_DESTINATION = "/Game/Meshy"
REPO_URL = "https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity"
_MENU_OWNER = "MeshyImporter"


# ---------------------------------------------------------------------------
# Import
# ---------------------------------------------------------------------------

def project_dir():
    return os.path.abspath(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir()))


def inbox_dir():
    """<Project>/MeshyInbox: drop .meshy files here and use Tools > Meshy > Import Inbox."""
    path = os.path.join(project_dir(), "MeshyInbox")
    if not os.path.isdir(path):
        os.makedirs(path)
    return path


def _asset_name(path):
    name = re.sub(r"[^A-Za-z0-9_]", "_", os.path.splitext(os.path.basename(path))[0]).strip("_")
    return name or "MeshyModel"


def import_meshy_files(paths, destination=DEFAULT_DESTINATION, repair_uvs=True):
    """Import .meshy files into `destination`/<Name>. Returns a list of (path, ok, message)."""
    paths = [p for p in paths if p.lower().endswith(".meshy")]
    results = []
    if not paths:
        return results
    if not has_pillow():
        unreal.log("Meshy Importer: Pillow not installed; using the built-in WebP decoder "
                   "(large textures take a little longer).")
    opts = NormalizeOptions.for_host("full", repair_uvs=repair_uvs,
                                     log=lambda msg: unreal.log("Meshy Importer: " + msg))
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    with unreal.ScopedSlowTask(len(paths) * 2, "Importing Meshy models") as slow:
        slow.make_dialog(True)
        for path in paths:
            if slow.should_cancel():
                break
            name = _asset_name(path)
            slow.enter_progress_frame(1, "Decoding %s" % os.path.basename(path))
            tmp = tempfile.mkdtemp(prefix="meshy_")
            try:
                start = time.time()
                glb, report = normalize_meshy_file(path, opts)
                glb_path = os.path.join(tmp, name + ".glb")
                with open(glb_path, "wb") as f:
                    f.write(glb)
                unreal.log("Meshy Importer: %s decoded in %.1f s (%s)"
                           % (os.path.basename(path), time.time() - start, report.summary()))

                slow.enter_progress_frame(1, "Importing %s" % name)
                task = unreal.AssetImportTask()
                task.set_editor_property("filename", glb_path)
                task.set_editor_property("destination_path", destination.rstrip("/") + "/" + name)
                task.set_editor_property("automated", True)
                task.set_editor_property("replace_existing", True)
                task.set_editor_property("save", True)
                tools.import_asset_tasks([task])
                imported = list(task.get_editor_property("imported_object_paths") or [])
                if not imported:
                    raise RuntimeError("Unreal's glTF importer did not create any assets "
                                       "(is the Interchange/glTF importer enabled?)")
                msg = "%d asset(s) in %s/%s" % (len(imported), destination.rstrip("/"), name)
                unreal.log("Meshy Importer: imported %s -> %s" % (os.path.basename(path), msg))
                results.append((path, True, msg))
            except Exception as exc:
                unreal.log_error("Meshy Importer: %s failed: %s" % (os.path.basename(path), exc))
                results.append((path, False, str(exc)))
            finally:
                shutil.rmtree(tmp, ignore_errors=True)
    return results


def _report(results):
    if not results:
        _message("Meshy Importer", "No .meshy files were imported.")
        return
    ok = [r for r in results if r[1]]
    lines = ["%s: %s" % (os.path.basename(p), m) for p, _, m in results]
    _message("Meshy Importer", "Imported %d of %d file(s).\n\n%s" % (len(ok), len(results), "\n".join(lines)))


def _message(title, text):
    unreal.EditorDialog.show_message(title, text, unreal.AppMsgType.OK)


def _current_destination():
    try:
        path = unreal.EditorUtilityLibrary.get_current_content_browser_path()
        if path:
            return path if path.startswith("/") else "/" + path
    except Exception:
        pass
    return DEFAULT_DESTINATION


# ---------------------------------------------------------------------------
# File picker (Unreal's Python API has none)
# ---------------------------------------------------------------------------

def pick_meshy_files():
    """Native multi-select open dialog. Returns [] when cancelled; None when no dialog is available."""
    try:
        if sys.platform.startswith("win"):
            return _pick_windows()
        if sys.platform == "darwin":
            script = ('set f to choose file with prompt "Select Meshy .meshy files" with multiple selections allowed\n'
                      'set out to ""\nrepeat with x in f\nset out to out & POSIX path of x & linefeed\nend repeat\nreturn out')
            res = subprocess.run(["osascript", "-e", script], capture_output=True, text=True)
            return [l for l in res.stdout.splitlines() if l.strip()]
        for cmd in (["zenity", "--file-selection", "--multiple", "--separator=\n", "--file-filter=*.meshy"],
                    ["kdialog", "--getopenfilename", "--multiple", "--separate-output", ".", "*.meshy"]):
            if shutil.which(cmd[0]):
                res = subprocess.run(cmd, capture_output=True, text=True)
                return [l for l in res.stdout.splitlines() if l.strip()]
    except Exception as exc:
        unreal.log_warning("Meshy Importer: file dialog failed: %s" % exc)
    return None


def _pick_windows():
    import ctypes
    from ctypes import wintypes

    class OPENFILENAMEW(ctypes.Structure):
        _fields_ = [("lStructSize", wintypes.DWORD), ("hwndOwner", wintypes.HWND),
                    ("hInstance", wintypes.HINSTANCE), ("lpstrFilter", wintypes.LPCWSTR),
                    ("lpstrCustomFilter", wintypes.LPWSTR), ("nMaxCustFilter", wintypes.DWORD),
                    ("nFilterIndex", wintypes.DWORD), ("lpstrFile", wintypes.LPWSTR),
                    ("nMaxFile", wintypes.DWORD), ("lpstrFileTitle", wintypes.LPWSTR),
                    ("nMaxFileTitle", wintypes.DWORD), ("lpstrInitialDir", wintypes.LPCWSTR),
                    ("lpstrTitle", wintypes.LPCWSTR), ("Flags", wintypes.DWORD),
                    ("nFileOffset", wintypes.WORD), ("nFileExtension", wintypes.WORD),
                    ("lpstrDefExt", wintypes.LPCWSTR), ("lCustData", wintypes.LPARAM),
                    ("lpfnHook", ctypes.c_void_p), ("lpTemplateName", wintypes.LPCWSTR),
                    ("pvReserved", ctypes.c_void_p), ("dwReserved", wintypes.DWORD),
                    ("FlagsEx", wintypes.DWORD)]

    size = 65536
    buf = ctypes.create_unicode_buffer(size)
    ofn = OPENFILENAMEW()
    ofn.lStructSize = ctypes.sizeof(OPENFILENAMEW)
    ofn.lpstrFilter = "Meshy model (*.meshy)\0*.meshy\0All files (*.*)\0*.*\0\0"
    ofn.lpstrFile = ctypes.cast(buf, wintypes.LPWSTR)
    ofn.nMaxFile = size
    ofn.lpstrTitle = "Select Meshy .meshy files"
    # OFN_EXPLORER | OFN_ALLOWMULTISELECT | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
    ofn.Flags = 0x00080000 | 0x00000200 | 0x00001000 | 0x00000800 | 0x00000008
    if not ctypes.windll.comdlg32.GetOpenFileNameW(ctypes.byref(ofn)):
        return []
    parts = buf[:].split("\0")
    parts = parts[:parts.index("")] if "" in parts else parts
    if len(parts) == 1:
        return parts
    return [os.path.join(parts[0], name) for name in parts[1:]]


# ---------------------------------------------------------------------------
# Menu actions
# ---------------------------------------------------------------------------

def import_dialog():
    files = pick_meshy_files()
    if files is None:
        _message("Meshy Importer",
                 "No file dialog is available on this system.\n\nCopy your .meshy files into\n%s\n"
                 "and use Tools > Meshy > Import Inbox Folder." % inbox_dir())
        return
    if files:
        _report(import_meshy_files(files, _current_destination()))


def import_inbox():
    folder = inbox_dir()
    files = sorted(os.path.join(folder, f) for f in os.listdir(folder) if f.lower().endswith(".meshy"))
    if not files:
        _message("Meshy Importer", "The inbox folder has no .meshy files:\n%s" % folder)
        return
    _report(import_meshy_files(files, _current_destination()))


def open_inbox():
    folder = inbox_dir()
    if sys.platform.startswith("win"):
        os.startfile(folder)  # noqa: S606 (Windows only)
    elif sys.platform == "darwin":
        subprocess.Popen(["open", folder])
    else:
        subprocess.Popen(["xdg-open", folder])


def validate_installation():
    lines = [
        "Meshy Importer %s: OK" % VERSION,
        "Engine: %s" % unreal.SystemLibrary.get_engine_version(),
        "Python: %s" % sys.version.split()[0],
        "meshy_core: %s (%s)" % (meshy_core.__version__, os.path.dirname(meshy_core.__file__)),
        "WebP decoding: %s" % ("Pillow (fast)" if has_pillow() else "built-in pure-Python decoder"),
        "Inbox folder: %s" % inbox_dir(),
        "Default destination: %s" % DEFAULT_DESTINATION,
    ]
    _message("Meshy Importer Diagnostics", "\n".join(lines))


def open_docs():
    unreal.SystemLibrary.launch_url(REPO_URL + "/tree/main/unreal")


# ---------------------------------------------------------------------------
# Menus
# ---------------------------------------------------------------------------

def _entry(name, label, tooltip, command):
    entry = unreal.ToolMenuEntry(name=name, type=unreal.MultiBlockType.MENU_ENTRY)
    entry.set_label(label)
    entry.set_tool_tip(tooltip)
    entry.set_string_command(unreal.ToolMenuStringCommandType.PYTHON, "", command)
    return entry


def register_menus():
    menus = unreal.ToolMenus.get()
    tools = menus.find_menu("LevelEditor.MainMenu.Tools")
    if tools is None:
        unreal.log_warning("Meshy Importer: Tools menu not found; use `import meshy_unreal` from the Python console.")
        return
    tools.add_section("Meshy", "Meshy")
    entries = [
        ("MeshyImportFiles", "Import .meshy Files...",
         "Pick .meshy files and import them into the current Content Browser folder (default /Game/Meshy).",
         "import meshy_unreal; meshy_unreal.import_dialog()"),
        ("MeshyImportInbox", "Import Inbox Folder",
         "Import every .meshy file in <Project>/MeshyInbox.", "import meshy_unreal; meshy_unreal.import_inbox()"),
        ("MeshyOpenInbox", "Open Inbox Folder", "Open <Project>/MeshyInbox in the file browser.",
         "import meshy_unreal; meshy_unreal.open_inbox()"),
        ("MeshyValidate", "Validate Meshy Importer", "Show version and decoder diagnostics.",
         "import meshy_unreal; meshy_unreal.validate_installation()"),
        ("MeshyDocs", "Meshy Importer Documentation", "Open the documentation in a browser.",
         "import meshy_unreal; meshy_unreal.open_docs()"),
    ]
    for name, label, tip, cmd in entries:
        tools.add_menu_entry("Meshy", _entry(name, label, tip, cmd))

    folder_menu = menus.find_menu("ContentBrowser.FolderContextMenu")
    if folder_menu is not None:
        folder_menu.add_section("Meshy", "Meshy")
        folder_menu.add_menu_entry("Meshy", _entry(
            "MeshyImportHere", "Import .meshy Files Here...",
            "Pick .meshy files and import them into this folder.", "import meshy_unreal; meshy_unreal.import_dialog()"))
    menus.refresh_all_widgets()
    unreal.log("Meshy Importer %s ready: Tools > Meshy" % VERSION)
