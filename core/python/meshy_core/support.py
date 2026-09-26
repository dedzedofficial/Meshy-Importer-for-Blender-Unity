"""Help links, bug-report URLs and the release check shared by the Python hosts
(Blender, Unreal). Unity (MeshySupport.cs / MeshyUpdateCheck.cs) and Godot
(plugin.gd) carry the same logic.

The update check downloads only the public "latest release" record from GitHub;
nothing about the user or their project is sent.
"""

import json

from urllib.parse import quote
from urllib.request import Request, urlopen

REPO_URL = "https://github.com/dedzedofficial/Meshy-Importer-for-Blender-Unity"
GETTING_A_FILE_URL = REPO_URL + "/blob/main/GETTING_A_MESHY_FILE.md"
TROUBLESHOOTING_URL = REPO_URL + "/blob/main/TROUBLESHOOTING.md"
CHANGELOG_URL = REPO_URL + "/blob/main/CHANGELOG.md"
RELEASES_URL = REPO_URL + "/releases/latest"
DISCORD_URL = "https://discord.gg/vCcsnX4HQP"
PATREON_URL = "https://www.patreon.com/cw/DedZed"
_API_LATEST = "https://api.github.com/repos/dedzedofficial/Meshy-Importer-for-Blender-Unity/releases/latest"
_MAX_LOG = 1500


def help_link(message):
    """The "Help: https://..." link at the end of an importer error message, or None."""
    if not message:
        return None
    i = str(message).find("Help: http")
    if i < 0:
        return None
    return str(message)[i + len("Help: "):].split()[0]


def strip_help(message):
    """The error message without its trailing "Help: <url>" part."""
    i = str(message).find("Help: http")
    return str(message)[:i].rstrip() if i >= 0 else str(message)


def bug_report_url(importer, host, logs=""):
    """A GitHub "new issue" link pre-filled for .github/ISSUE_TEMPLATE/bug_report.yml."""
    if len(logs) > _MAX_LOG:
        logs = logs[:_MAX_LOG] + "\n..."
    return (REPO_URL + "/issues/new?template=bug_report.yml"
            + "&importer=" + quote(importer, safe="")
            + "&host=" + quote(host, safe="")
            + "&logs=" + quote(logs, safe=""))


def parse_version(text):
    parts = []
    for piece in str(text or "").strip().lstrip("vV").replace("-", ".").split(".")[:3]:
        try:
            parts.append(int(piece))
        except ValueError:
            parts.append(0)
    while len(parts) < 3:
        parts.append(0)
    return tuple(parts)


def is_newer(candidate, current):
    """True when dotted version `candidate` is newer than `current`."""
    if not candidate or not current:
        return False
    return parse_version(candidate) > parse_version(current)


def fetch_latest_version(user_agent, timeout=10.0):
    """The latest release's version ("1.5.0"), or None if GitHub can't be reached."""
    try:
        req = Request(_API_LATEST, headers={"User-Agent": user_agent, "Accept": "application/vnd.github+json"})
        with urlopen(req, timeout=timeout) as resp:  # noqa: S310 (fixed https URL)
            tag = json.loads(resp.read().decode("utf-8")).get("tag_name", "")
        return str(tag).lstrip("vV") or None
    except Exception:
        return None
