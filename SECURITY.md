# Local processing and privacy

All FISHHWB importers and the browser extension process model bytes on the user's own machine. The extension does not upload model bytes, make its own network requests, or access account tokens/cookies. Its only persistent data is local preferences, model filename/hash history and timestamps; the model bytes remain in the Meshy tab's memory and the user's Downloads folder. The extension only runs on Meshy domains and observes existing browser-loaded binary responses. The four engine integrations and CLI convert and import locally.

**Scope of this promise:** Meshy's original website naturally contacts Meshy's services when you use it. The engine plugins' optional update checks access public GitHub release data (and installations/downloads use GitHub). These are distinct from model uploads; do not imply all internet activity stops when using Meshy. Browser extensions can only capture resources actually fetched by an open tab; no private Meshy API polling occurs.

The browser extension can request a download but cannot guarantee the browser or operating system completed it; verify in Downloads. Treat any downloaded asset as untrusted input and respect model owners' rights and service terms. No secrets or full models should be pasted into public issue reports.

---

# Security Policy

## Reporting a security issue

Please do not publish security-sensitive details in a public issue.

Contact the project maintainer through the contact method listed on the GitHub profile and include enough information to reproduce the problem without sharing credentials, tokens, or other private data.

## Scope

The importer is designed to decode `.meshy` files locally. Be especially careful with model files from untrusted sources, because imported assets are processed by Blender or Unity and may exercise bugs in those applications or their native import systems.
