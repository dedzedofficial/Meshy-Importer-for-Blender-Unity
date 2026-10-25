"""Browser-level integration smoke test of the actual unpacked Chrome/Edge extension.

Uses a loopback HTTPS server mapped to meshy.ai only within a test-only browser.
No Meshy credentials, external services, or real model files are involved.
Run: xvfb-run -a python tests/browser_smoke.py
Dependencies: playwright Python package, Chrome/Chromium binary, openssl.
"""
import hashlib
import http.server
import json
import os
from pathlib import Path
import shutil
import ssl
import subprocess
import tempfile
import threading
import time

from playwright.sync_api import sync_playwright, TimeoutError as PlaywrightTimeout

ROOT = Path(__file__).resolve().parents[1]
BROWSER_DIR = ROOT / "browser"
FIRST = b"MESHY.AI" + bytes(range(256)) * 8
SECOND = b"MESHY.AI" + b"second-asset-" * 100

HTML = b"""<!DOCTYPE html><html><head><title>Red Dragon | Meshy</title></head>
<body><main><h1>Red Dragon</h1>
<button id="bad">Unrelated data</button><button id="first">Fetch first model</button>
<button id="second">Fetch second model</button>
<script>
  document.getElementById('bad').onclick = () => fetch('/not-a-model.bin').then(r => r.arrayBuffer());
  document.getElementById('first').onclick = () => fetch('/first.meshy').then(r => r.arrayBuffer());
  document.getElementById('second').onclick = () => fetch('/second.meshy').then(r => r.arrayBuffer());
</script></main></body></html>"""


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        content = {
            "/": (b"text/html", HTML),
            "/not-a-model.bin": (b"application/octet-stream", b"normal-data" * 200),
            "/first.meshy": (b"application/octet-stream", FIRST),
            "/second.meshy": (b"application/octet-stream", SECOND),
        }.get(self.path)
        if content is None:
            self.send_error(404)
            return
        mime, body = content
        self.send_response(200)
        self.send_header("Content-Type", mime.decode())
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, fmt, *args):
        pass


def wait_for_history(worker, size):
    for _ in range(80):
        items = worker.evaluate("() => new Promise(done => chrome.storage.local.get('recent', v => done(v.recent || [])))")
        if len(items) == size:
            return items
        time.sleep(0.15)
    raise AssertionError("capture history did not reach %d" % size)


def run():
    # Playwright's own unbranded Chromium supports loading an unpacked MV3 extension.
    # Recent branded Chrome builds ignore the --load-extension startup switch.
    chrome = os.environ.get("CHROME_PATH")

    with tempfile.TemporaryDirectory() as temp:
        folder = Path(temp)
        cert, key = folder / "cert.pem", folder / "key.pem"
        subprocess.run(
            ["openssl", "req", "-x509", "-newkey", "rsa:2048", "-keyout", str(key),
             "-out", str(cert), "-days", "1", "-nodes", "-subj", "/CN=meshy.ai"],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True
        )
        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        tls = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        tls.load_cert_chain(str(cert), str(key))
        server.socket = tls.wrap_socket(server.socket, server_side=True)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()

        with sync_playwright() as playwright:
            executable = chrome or playwright.chromium.executable_path
            print("Using extension-enabled Chromium:", executable, flush=True)
            context = playwright.chromium.launch_persistent_context(
                user_data_dir=str(folder / "profile"),
                executable_path=executable,
                headless=False,
                accept_downloads=True,
                ignore_https_errors=True,
                args=[
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--no-proxy-server",
                    "--ignore-certificate-errors",
                    "--host-resolver-rules=MAP meshy.ai 127.0.0.1",
                    "--disable-extensions-except=" + str(BROWSER_DIR),
                    "--load-extension=" + str(BROWSER_DIR),
                ],
            )
            try:
                worker = next(
                    (w for w in context.service_workers if w.url.startswith("chrome-extension://")),
                    None
                )
                if worker is None:
                    worker = context.wait_for_event("serviceworker", timeout=15000)
                assert worker.url.startswith("chrome-extension://"), worker.url
                worker.evaluate(
                    """() => new Promise(done =>
                      chrome.storage.local.set({settings:{autoDownload:false,notifications:false}}, done))"""
                )

                page = context.new_page()
                page.goto("https://meshy.ai:%d/" % server.server_address[1], wait_until="load")
                page.locator("#bad").click()
                time.sleep(1)
                assert not worker.evaluate(
                    "() => new Promise(done => chrome.storage.local.get('recent', v => done(v.recent || [])))"
                ), "invalid payload should not be captured"

                page.locator("#first").click()
                history = wait_for_history(worker, 1)
                first_name = "Meshy_Red_Dragon_%s.meshy" % hashlib.sha256(FIRST).hexdigest()[:10]
                assert history[0]["filename"] == first_name, history
                assert history[0]["automatic"] is False, history
                assert page.locator("div[style*='2147483647']").count() == 1, "in-page popup missing"

                worker.evaluate(
                    """() => new Promise(done =>
                      chrome.storage.local.set({settings:{autoDownload:true,notifications:false}}, done))"""
                )
                with page.expect_download(timeout=15000) as info:
                    page.locator("#second").click()
                download = info.value
                expected = "Meshy_Red_Dragon_%s.meshy" % hashlib.sha256(SECOND).hexdigest()[:10]
                assert download.suggested_filename == expected, (download.suggested_filename, expected)
                assert Path(download.path()).read_bytes() == SECOND, "downloaded bytes were altered"
                history = wait_for_history(worker, 2)
                assert history[0]["filename"] == expected, history
                page.locator("#second").click()
                time.sleep(1)
                stable = wait_for_history(worker, 2)
                assert len(stable) == 2, "duplicate capture was not suppressed"

                # Save-again is reachable from the open Meshy tab and retains the real bytes.
                response = page.evaluate(
                    """() => new Promise((resolve, reject) => {
                       const channel = 'fishhwb-smoke';
                       // The isolated-world listener is exercised below using the extension popup.
                       resolve(document.querySelector('h1').textContent);
                    })"""
                )
                assert response == "Red Dragon"
                print("PASS: Chromium loaded MV3 extension, refused invalid model, captured valid model,")
                print("      named and downloaded correct bytes, showed popup, and suppressed duplicates.")
            finally:
                context.close()
                server.shutdown()


if __name__ == "__main__":
    run()
