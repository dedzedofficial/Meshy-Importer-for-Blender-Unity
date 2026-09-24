"""Headless Godot smoke test: import synthetic .meshy fixtures through the plugin.

    python tests/hosts/godot_smoke.py /path/to/godot4
"""

import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, "tests"))
sys.path.insert(0, os.path.join(ROOT, "core", "python"))

from meshy_fixtures import build_meshy  # noqa: E402


def main():
    godot = sys.argv[1]
    with tempfile.TemporaryDirectory() as tmp:
        project = os.path.join(tmp, "project")
        shutil.copytree(os.path.join(HERE, "godot_project"), project)
        shutil.copytree(os.path.join(ROOT, "godot", "addons"), os.path.join(project, "addons"))
        shutil.copy(os.path.join(HERE, "godot_inspect.gd"), project)
        shutil.copy(os.path.join(HERE, "godot_meshopt_vectors.gd"), project)
        with open(os.path.join(project, "clean.meshy"), "wb") as f:
            f.write(build_meshy())
        with open(os.path.join(project, "broken.meshy"), "wb") as f:
            f.write(build_meshy(broken_uvs=True))
        imp = subprocess.run([godot, "--headless", "--editor", "--import", "--path", project],
                             capture_output=True, text=True, timeout=600)
        log = imp.stdout + imp.stderr
        errors = [l for l in log.splitlines() if "ERROR" in l or "SCRIPT ERROR" in l or "Parse Error" in l]
        print("\n".join(errors) or "import: no errors")
        run = subprocess.run([godot, "--headless", "--path", project, "--script", "res://godot_inspect.gd", "--",
                              "res://clean.meshy", "res://broken.meshy"], capture_output=True, text=True, timeout=600)
        print(run.stdout.strip())
        if run.returncode != 0 or errors:
            print(run.stderr[-3000:])
            return 1
        vec = subprocess.run([godot, "--headless", "--path", project, "--script", "res://godot_meshopt_vectors.gd"],
                             capture_output=True, text=True, timeout=600)
        print("\n".join(l for l in vec.stdout.splitlines() if "MESHOPT" in l or "FAIL" in l))
        if vec.returncode != 0:
            print(vec.stderr[-3000:])
            return 1
    print("GODOT SMOKE TEST PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
