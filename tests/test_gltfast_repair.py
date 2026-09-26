import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("repair", Path(__file__).resolve().parents[1] / "tools/repair_gltfast.py")
repair = importlib.util.module_from_spec(spec)
spec.loader.exec_module(repair)


class GltfFastRepairTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for folder in ("Assets", "ProjectSettings", "Packages"):
            (self.root / folder).mkdir()
        self.manifest = {"dependencies": {repair.UNITY: "6.0.0", repair.LEGACY: "5.0.0", "example": "1.0.0"}, "scopedRegistries": [{"name": "keep me"}]}
        self.lock = {"dependencies": {key: {"version": value, "dependencies": {}} for key, value in self.manifest["dependencies"].items()}}
        self.write()

    def write(self):
        (self.root / "Packages/manifest.json").write_text(json.dumps(self.manifest))
        (self.root / "Packages/packages-lock.json").write_text(json.dumps(self.lock))

    def test_preview_changes_nothing(self):
        original = (self.root / "Packages/manifest.json").read_bytes()
        self.assertEqual(repair.main([str(self.root)]), 0)
        self.assertEqual(original, (self.root / "Packages/manifest.json").read_bytes())
        self.assertFalse((self.root / "MeshyPackageBackups").exists())

    def test_repair_preserves_backup_other_fields_lock_and_versions(self):
        before = (self.root / "Packages/manifest.json").read_bytes()
        locked = (self.root / "Packages/packages-lock.json").read_bytes()
        backup = repair.apply(repair.inspect(self.root))
        expected = json.loads(before)
        del expected["dependencies"][repair.LEGACY]
        self.assertEqual(json.loads((self.root / "Packages/manifest.json").read_bytes()), expected)
        self.assertEqual((backup / "manifest.json").read_bytes(), before)
        self.assertEqual((backup / "packages-lock.json").read_bytes(), locked)
        self.assertEqual((self.root / "Packages/packages-lock.json").read_bytes(), locked)
        self.assertTrue(repair.inspect(self.root)["blockers"])

    def test_keep_legacy(self):
        repair.apply(repair.inspect(self.root, "legacy"))
        result = json.loads((self.root / "Packages/manifest.json").read_bytes())
        self.assertIn(repair.LEGACY, result["dependencies"])
        self.assertNotIn(repair.UNITY, result["dependencies"])

    def test_reverse_dependency_blocks_removal(self):
        self.lock["dependencies"]["example"]["dependencies"][repair.LEGACY] = "5.0.0"
        self.write()
        plan = repair.inspect(self.root)
        self.assertEqual(plan["parents"], ["example"])
        with self.assertRaises(ValueError): repair.apply(plan)

    def test_transitive_parent_detected(self):
        self.lock["dependencies"]["example"]["dependencies"]["indirect"] = "1"
        self.lock["dependencies"]["indirect"] = {"dependencies": {repair.LEGACY: "5.0.0"}}
        self.write()
        self.assertIn("indirect", repair.inspect(self.root)["parents"])

    def test_embedded_variant_never_deleted(self):
        folder = self.root / "Packages/local-gltf"
        folder.mkdir()
        (folder / "package.json").write_text(json.dumps({"name": repair.LEGACY}))
        plan = repair.inspect(self.root)
        self.assertTrue(any("embedded" in b for b in plan["blockers"]))
        with self.assertRaises(ValueError): repair.apply(plan)
        self.assertTrue(folder.exists())

    def test_embedded_parent_dependency(self):
        folder = self.root / "Packages/consumer"
        folder.mkdir()
        (folder / "package.json").write_text(json.dumps({"name": "consumer", "dependencies": {repair.LEGACY: "5.0.0"}}))
        self.assertIn("consumer", repair.inspect(self.root)["parents"])

    def test_local_parent_overrides_stale_lock(self):
        folder = self.root / "LocalConsumer"
        folder.mkdir()
        (folder / "package.json").write_text(json.dumps({"name": "example", "dependencies": {repair.LEGACY: "5.0.0"}}))
        self.manifest["dependencies"]["example"] = "file:../LocalConsumer"
        self.write()
        self.assertIn("example", repair.inspect(self.root)["parents"])

    def test_missing_lock_refuses_apply(self):
        (self.root / "Packages/packages-lock.json").unlink()
        with self.assertRaises(ValueError): repair.apply(repair.inspect(self.root))

    def test_open_editor_refuses_apply(self):
        (self.root / "Temp").mkdir()
        (self.root / "Temp/UnityLockfile").touch()
        with self.assertRaises(ValueError): repair.apply(repair.inspect(self.root))

    def test_stale_preview_refuses_apply(self):
        plan = repair.inspect(self.root)
        self.manifest["dependencies"]["new"] = "1"
        self.write()
        with self.assertRaises(ValueError): repair.apply(plan)

    def test_missing_variant_refuses_apply(self):
        del self.manifest["dependencies"][repair.LEGACY]
        del self.lock["dependencies"][repair.LEGACY]
        self.write()
        with self.assertRaises(ValueError): repair.apply(repair.inspect(self.root))

    def test_transitive_only_variant_refuses_apply(self):
        del self.manifest["dependencies"][repair.LEGACY]
        self.write()
        with self.assertRaises(ValueError): repair.apply(repair.inspect(self.root))

    def test_duplicate_json_keys_rejected(self):
        (self.root / "Packages/manifest.json").write_text('{"dependencies":{}, "dependencies":{}}')
        with self.assertRaises(ValueError): repair.inspect(self.root)

    def test_invalid_project_rejected(self):
        with self.assertRaises(ValueError): repair.inspect(self.root / "Assets")

    def test_malformed_lock_rejected(self):
        (self.root / "Packages/packages-lock.json").write_text('{broken')
        with self.assertRaises(ValueError): repair.inspect(self.root)

if __name__ == "__main__": unittest.main()
