import unittest
from urllib.parse import parse_qs, urlparse

import _path  # noqa: F401
from meshy_core import support
from meshy_core.decode import HELP_WRONG_FILE, MeshyFormatError, decode_meshy_bytes


class SupportTests(unittest.TestCase):
    def test_versions(self):
        self.assertTrue(support.is_newer("1.5.0", "1.4.1"))
        self.assertTrue(support.is_newer("v1.10.0", "1.9.9"))
        self.assertFalse(support.is_newer("1.4.1", "1.4.1"))
        self.assertFalse(support.is_newer("1.4.0", "1.4.1"))
        self.assertFalse(support.is_newer(None, "1.4.1"))

    def test_help_link_round_trips_decoder_errors(self):
        with self.assertRaises(MeshyFormatError) as cm:
            decode_meshy_bytes(b"<html></html>")
        msg = str(cm.exception)
        self.assertEqual(support.help_link(msg), HELP_WRONG_FILE)
        self.assertNotIn("Help:", support.strip_help(msg))
        self.assertIsNone(support.help_link("plain error"))

    def test_bug_report_url_prefills_template_fields(self):
        url = support.bug_report_url("1.5.0 (Blender)", "Blender 4.2 & Linux", "x" * 5000)
        q = parse_qs(urlparse(url).query)
        self.assertEqual(q["template"], ["bug_report.yml"])
        self.assertEqual(q["importer"], ["1.5.0 (Blender)"])
        self.assertEqual(q["host"], ["Blender 4.2 & Linux"])
        self.assertLess(len(q["logs"][0]), 1600)


if __name__ == "__main__":
    unittest.main()
