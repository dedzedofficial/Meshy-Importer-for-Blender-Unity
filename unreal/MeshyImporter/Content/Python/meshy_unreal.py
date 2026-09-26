"""Meshy Importer for Unreal Engine 5.3+ (Editor Python).

Turns each .meshy payload into a plain glTF GLB with meshy_core (decrypt, decode
meshopt, dequantize, bake texture transforms, repair UVs, WebP -> PNG) and imports
that through Unreal's own glTF/Interchange pipeline with an AssetImportTask.

Menu: Tools > Meshy (see register_menus). Must stay Python 3.9 compatible (UE 5.3).
"""

import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tempfile
import threading
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

from meshy_core import support  # noqa: E402
from meshy_core.normalize import NormalizeOptions, normalize_meshy_file  # noqa: E402
from meshy_core.webp_vp8 import has_pillow  # noqa: E402

VERSION = "1.5.0"
DEFAULT_DESTINATION = "/Game/Meshy"
REPO_URL = support.REPO_URL
_MENU_OWNER = "MeshyImporter"
_state = {"last_error": "", "last_import": "", "latest": ""}


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


def import_meshy_files(paths, destination=DEFAULT_DESTINATION, repair_uvs=True, show_options=False):
    """Import .meshy files into `destination`/<Name>. Returns a list of (path, ok, message).

    show_options=True lets Unreal show its own glTF/Interchange import options dialog
    (scale, collision, materials...) instead of importing with the defaults."""
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
                task.set_editor_property("automated", not show_options)
                task.set_editor_property("replace_existing", True)
                task.set_editor_property("save", True)
                tools.import_asset_tasks([task])
                imported = list(task.get_editor_property("imported_object_paths") or [])
                if not imported:
                    raise RuntimeError("Unreal's glTF importer did not create any assets. If you did not "
                                       "cancel the import, enable the Interchange and glTF Importer plugins "
                                       "(Edit > Plugins) and restart. Help: " + support.TROUBLESHOOTING_URL
                                       + "#unreal-engine")
                msg = "%d asset(s) in %s/%s" % (len(imported), destination.rstrip("/"), name)
                unreal.log("Meshy Importer: imported %s -> %s" % (os.path.basename(path), msg))
                results.append((path, True, msg))
            except Exception as exc:
                unreal.log_error("Meshy Importer: %s failed: %s" % (os.path.basename(path), exc))
                _state["last_error"] = "%s: %s" % (os.path.basename(path), exc)
                results.append((path, False, str(exc)))
            finally:
                shutil.rmtree(tmp, ignore_errors=True)
    return results


def _report(results):
    if not results:
        _message("Meshy Importer", "No .meshy files were imported.")
        return
    ok = [r for r in results if r[1]]
    lines = ["%s: %s" % (os.path.basename(p), support.strip_help(m)) for p, _, m in results]
    text = "Imported %d of %d file(s).\n\n%s" % (len(ok), len(results), "\n".join(lines))
    _state["last_import"] = "Imported %d of %d file(s)" % (len(ok), len(results))
    helps = [support.help_link(m) for p, good, m in results if not good and support.help_link(m)]
    if helps:
        text += "\n\nOpen the help page for this error?"
        if _ask("Meshy Importer", text):
            unreal.SystemLibrary.launch_url(helps[0])
        return
    _message("Meshy Importer", text)


def _ask(title, text):
    try:
        return unreal.EditorDialog.show_message(title, text, unreal.AppMsgType.YES_NO) == unreal.AppReturnType.YES
    except Exception:
        _message(title, text)
        return False


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

def import_dialog(show_options=False):
    files = pick_meshy_files()
    if files is None:
        _message("Meshy Importer",
                 "No file dialog is available on this system.\n\nCopy your .meshy files into\n%s\n"
                 "and use Tools > Meshy > Import Inbox Folder Now." % inbox_dir())
        return
    if files:
        _report(import_meshy_files(files, _current_destination(), show_options=show_options))


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


def gltf_importer_available():
    """True when a glTF importer Python can drive is present (Interchange or the legacy plugin)."""
    return any(hasattr(unreal, name) for name in (
        "InterchangeManager", "InterchangeGenericAssetsPipeline", "GLTFImportFactory", "GLTFImportOptions"))


def diagnostics():
    """Everything a bug report needs, as plain text."""
    lines = [
        "Meshy Importer: %s (Unreal plugin)" % VERSION,
        "Engine: %s" % unreal.SystemLibrary.get_engine_version(),
        "OS: %s" % platform.platform(),
        "Python: %s" % sys.version.split()[0],
        "meshy_core: %s" % meshy_core.__version__,
        "glTF importer: %s" % ("available" if gltf_importer_available() else "NOT FOUND (enable Interchange/glTF Importer)"),
        "WebP decoding: %s" % ("Pillow (fast)" if has_pillow() else "built-in pure-Python decoder"),
        "Inbox watcher: %s" % ("on" if _watch["handle"] is not None else "off"),
    ]
    if _state["latest"]:
        lines.append("Latest release seen: %s" % _state["latest"])
    if _state["last_import"]:
        lines.append("Last import: %s" % _state["last_import"])
    if _state["last_error"]:
        lines.append("Last error: %s" % _state["last_error"])
    return "\n".join(lines)


def validate_installation():
    lines = [
        "Meshy Importer %s: %s" % (VERSION, "OK" if gltf_importer_available() else "glTF importer missing"),
        "Engine: %s" % unreal.SystemLibrary.get_engine_version(),
        "Python: %s" % sys.version.split()[0],
        "meshy_core: %s (%s)" % (meshy_core.__version__, os.path.dirname(meshy_core.__file__)),
        "glTF importer: %s" % ("available" if gltf_importer_available()
                               else "not found. Enable Interchange and glTF Importer in Edit > Plugins, then restart"),
        "WebP decoding: %s" % ("Pillow (fast)" if has_pillow() else "built-in pure-Python decoder"),
        "Inbox folder: %s" % inbox_dir(),
        "Default destination: %s" % DEFAULT_DESTINATION,
    ]
    _message("Meshy Importer Diagnostics", "\n".join(lines))


def _copy_to_clipboard(text):
    """Unreal's Python API has no clipboard; use the OS tool. Returns True on success."""
    try:
        if sys.platform.startswith("win"):
            proc = subprocess.run(["clip"], input=text.encode("utf-16-le"), check=False)
            return proc.returncode == 0
        if sys.platform == "darwin":
            return subprocess.run(["pbcopy"], input=text.encode("utf-8"), check=False).returncode == 0
        for cmd in (["wl-copy"], ["xclip", "-selection", "clipboard"], ["xsel", "--clipboard", "--input"]):
            if shutil.which(cmd[0]):
                return subprocess.run(cmd, input=text.encode("utf-8"), check=False).returncode == 0
    except Exception:
        pass
    return False


def copy_diagnostics():
    text = diagnostics()
    unreal.log("Meshy Importer diagnostics:\n" + text)
    if _copy_to_clipboard(text):
        _message("Meshy Importer", "Diagnostics copied to the clipboard. Paste them into your bug report or Discord message.")
        return
    path = os.path.join(_saved_dir(), "diagnostics.txt")
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)
    _message("Meshy Importer", "Diagnostics were written to\n%s\n(and to the Output Log)." % path)


def report_bug():
    text = diagnostics()
    _copy_to_clipboard(text)
    host = "Unreal %s, %s" % (unreal.SystemLibrary.get_engine_version(), platform.system())
    unreal.SystemLibrary.launch_url(support.bug_report_url(VERSION + " (Unreal)", host, text))


def open_docs():
    unreal.SystemLibrary.launch_url(REPO_URL + "/tree/main/unreal")


def open_url(url):
    unreal.SystemLibrary.launch_url(url)


# ---------------------------------------------------------------------------
# Settings, inbox watcher and update check
# ---------------------------------------------------------------------------

def _saved_dir():
    path = os.path.join(project_dir(), "Saved", "MeshyImporter")
    if not os.path.isdir(path):
        os.makedirs(path)
    return path


def _load_settings():
    try:
        with open(os.path.join(_saved_dir(), "settings.json"), encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


def _save_settings(**changes):
    settings = _load_settings()
    settings.update(changes)
    try:
        with open(os.path.join(_saved_dir(), "settings.json"), "w", encoding="utf-8") as f:
            json.dump(settings, f, indent=2)
    except Exception as exc:
        unreal.log_warning("Meshy Importer: could not save settings: %s" % exc)


_watch = {"handle": None, "last_poll": 0.0, "sizes": {}}
WATCH_INTERVAL = 2.0


def _watch_tick(_delta):
    now = time.time()
    if now - _watch["last_poll"] < WATCH_INTERVAL:
        return
    _watch["last_poll"] = now
    try:
        folder = inbox_dir()
        ready, sizes = [], {}
        for name in sorted(os.listdir(folder)):
            path = os.path.join(folder, name)
            if not name.lower().endswith(".meshy") or not os.path.isfile(path):
                continue
            size = os.path.getsize(path)
            sizes[path] = size
            if _watch["sizes"].get(path) == size and size > 0:  # unchanged since last poll: fully copied
                ready.append(path)
        _watch["sizes"] = {p: s for p, s in sizes.items() if p not in ready}
        if not ready:
            return
        done_dir = os.path.join(folder, "Imported")
        if not os.path.isdir(done_dir):
            os.makedirs(done_dir)
        for path, ok, msg in import_meshy_files(ready, _current_destination()):
            target = os.path.join(done_dir if ok else os.path.join(folder, "Failed"), os.path.basename(path))
            if not os.path.isdir(os.path.dirname(target)):
                os.makedirs(os.path.dirname(target))
            if os.path.exists(target):
                os.remove(target)
            shutil.move(path, target)
            if not ok:
                unreal.log_warning("Meshy Importer: moved %s to MeshyInbox/Failed: %s" % (os.path.basename(path), msg))
    except Exception as exc:
        unreal.log_error("Meshy Importer: inbox watcher error: %s" % exc)


def start_inbox_watch(save=True):
    if _watch["handle"] is None:
        _watch["handle"] = unreal.register_slate_post_tick_callback(_watch_tick)
        unreal.log("Meshy Importer: watching %s; .meshy files dropped there are imported automatically." % inbox_dir())
    if save:
        _save_settings(watch_inbox=True)


def stop_inbox_watch(save=True):
    if _watch["handle"] is not None:
        unreal.unregister_slate_post_tick_callback(_watch["handle"])
        _watch["handle"] = None
        unreal.log("Meshy Importer: stopped watching the inbox folder.")
    if save:
        _save_settings(watch_inbox=False)


def toggle_inbox_watch():
    if _watch["handle"] is None:
        start_inbox_watch()
        _message("Meshy Importer", "Auto-import is on.\n\nDrop .meshy files into\n%s\nand they are imported into the "
                                   "current Content Browser folder within a few seconds. Imported files move to "
                                   "MeshyInbox/Imported." % inbox_dir())
        open_inbox()
    else:
        stop_inbox_watch()
        _message("Meshy Importer", "Auto-import is off.")


# Update check: downloads only the public "latest release" record from GitHub, at most once a
# day, in a background thread. Turn it off with Tools > Meshy > Help > Check for Updates Daily.

def _start_update_check(manual):
    result = {}
    thread = threading.Thread(target=lambda: result.update(
        latest=support.fetch_latest_version("MeshyImporter-Unreal/" + VERSION)), daemon=True)
    thread.start()
    holder = {}

    def tick(_delta):
        if thread.is_alive():
            return
        unreal.unregister_slate_post_tick_callback(holder["handle"])
        latest = result.get("latest")
        _save_settings(last_update_check=time.time(), **({"latest": latest} if latest else {}))
        if latest:
            _state["latest"] = latest
        if support.is_newer(latest, VERSION):
            unreal.log_warning("Meshy Importer %s is available (you have %s): %s" % (latest, VERSION, support.RELEASES_URL))
            if manual and _ask("Meshy Importer", "Version %s is available (you have %s).\n\nOpen the release page?"
                               % (latest, VERSION)):
                unreal.SystemLibrary.launch_url(support.RELEASES_URL)
        elif manual:
            _message("Meshy Importer", "You have the latest version (%s)." % VERSION if latest
                     else "Could not reach GitHub to check for updates.")

    holder["handle"] = unreal.register_slate_post_tick_callback(tick)


def check_for_updates():
    _start_update_check(True)


def toggle_daily_update_check():
    enabled = not _load_settings().get("check_updates", True)
    _save_settings(check_updates=enabled)
    _message("Meshy Importer", "Daily update check is %s." % ("on" if enabled else "off"))


def on_editor_start():
    """Called by init_unreal.py: menus, startup checks, inbox watcher, daily update check."""
    register_menus()
    settings = _load_settings()
    _state["latest"] = settings.get("latest", "") or ""
    if not gltf_importer_available():
        unreal.log_warning("Meshy Importer: no glTF importer found. Enable the Interchange and glTF Importer "
                           "plugins in Edit > Plugins, then restart the editor.")
        if not settings.get("warned_no_gltf"):
            _save_settings(warned_no_gltf=True)
            _message("Meshy Importer", "Unreal's glTF importer is not enabled, so .meshy files cannot be imported yet.\n\n"
                                       "Enable the Interchange and glTF Importer plugins in Edit > Plugins, then restart.")
    if settings.get("watch_inbox"):
        start_inbox_watch(save=False)
    if settings.get("check_updates", True) and time.time() - float(settings.get("last_update_check", 0)) >= 24 * 3600:
        _start_update_check(False)


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
    m = "import meshy_unreal; meshy_unreal."
    entries = [
        ("MeshyImportFiles", "Import .meshy Files...",
         "Pick .meshy files and import them into the current Content Browser folder (default /Game/Meshy).",
         m + "import_dialog()"),
        ("MeshyImportFilesOptions", "Import .meshy Files (Show Options)...",
         "Like Import .meshy Files, but shows Unreal's glTF import options (scale, collision, materials).",
         m + "import_dialog(show_options=True)"),
        ("MeshyWatchInbox", "Auto-Import Inbox Folder (On/Off)",
         "Watch <Project>/MeshyInbox and import any .meshy file dropped there automatically.",
         m + "toggle_inbox_watch()"),
        ("MeshyImportInbox", "Import Inbox Folder Now",
         "Import every .meshy file in <Project>/MeshyInbox.", m + "import_inbox()"),
        ("MeshyOpenInbox", "Open Inbox Folder", "Open <Project>/MeshyInbox in the file browser.",
         m + "open_inbox()"),
    ]
    help_entries = [
        ("MeshyGetFile", "How Do I Get a .meshy File?", "Open the step-by-step guide.",
         m + "open_url(meshy_unreal.support.GETTING_A_FILE_URL)"),
        ("MeshyTroubleshooting", "Troubleshooting", "Open the troubleshooting guide.",
         m + "open_url(meshy_unreal.support.TROUBLESHOOTING_URL)"),
        ("MeshyDocs", "Documentation", "Open the documentation in a browser.", m + "open_docs()"),
        ("MeshyValidate", "Validate Installation", "Show version and decoder diagnostics.",
         m + "validate_installation()"),
        ("MeshyCopyDiagnostics", "Copy Diagnostics", "Copy version and error details for a bug report.",
         m + "copy_diagnostics()"),
        ("MeshyReportBug", "Report a Bug...", "Open a pre-filled bug report on GitHub.", m + "report_bug()"),
        ("MeshyCheckUpdates", "Check for Updates", "Look up the latest release on GitHub.",
         m + "check_for_updates()"),
        ("MeshyToggleUpdateCheck", "Check for Updates Daily (On/Off)",
         "Turn the once-a-day update check on or off.", m + "toggle_daily_update_check()"),
        ("MeshyDiscord", "Discord", "Ask for help on Discord.", m + "open_url(meshy_unreal.support.DISCORD_URL)"),
    ]
    for name, label, tip, cmd in entries:
        tools.add_menu_entry("Meshy", _entry(name, label, tip, cmd))
    help_menu = tools.add_sub_menu(_MENU_OWNER, "Meshy", "MeshyHelp", "Meshy Importer Help",
                                   "Guides, diagnostics, bug reports and updates") \
        if hasattr(tools, "add_sub_menu") else None
    for name, label, tip, cmd in help_entries:
        if help_menu is not None:
            help_menu.add_menu_entry("Help", _entry(name, label, tip, cmd))
        else:
            tools.add_menu_entry("Meshy", _entry(name, label, tip, cmd))

    folder_menu = menus.find_menu("ContentBrowser.FolderContextMenu")
    if folder_menu is not None:
        folder_menu.add_section("Meshy", "Meshy")
        folder_menu.add_menu_entry("Meshy", _entry(
            "MeshyImportHere", "Import .meshy Files Here...",
            "Pick .meshy files and import them into this folder.", "import meshy_unreal; meshy_unreal.import_dialog()"))
    menus.refresh_all_widgets()
    unreal.log("Meshy Importer %s ready: Tools > Meshy" % VERSION)
