# Browser-aware installer

This static installer detects the desktop browser and operating system locally, offers the supported Meshy Importer 1.5.2 ZIP for Chrome and Edge, and displays honest compatibility information for Firefox, Opera, Safari and mobile browsers.

The intended GitHub Pages URL is:

https://dedzedofficial.github.io/Meshy-Importer-for-Blender-Unity/

## Enable hosting

1. Visit the repository's **Settings > Pages**.
2. Set **Build and deployment > Source** to **GitHub Actions** and save.
3. Go to **Actions > Publish browser-aware installer > Run workflow** on the main branch, or push a change under `site/`.
4. Verify the live page opens and correctly detects Chrome and Edge.
5. Only after successful deployment should the public repository README button be changed from the existing ZIP download to this page.

The deployment workflow automatically checks whether GitHub Pages is enabled. If it is not enabled, the workflow explains the setup step without publishing a misleading URL.

## Future official browser stores

The `stores` object in `site/installer.js` holds empty store URL fields. Insert a real, verified Edge, Chrome, Firefox or Opera store listing only after the corresponding extension is tested, published and approved. For an approved store URL, the installer will offer a direct store installation. A GitHub-hosted page cannot silently install an unpublished browser extension.

The installer does not perform network requests, log browsing data or upload models, and downloads only when the user clicks its button. On mobile devices, it shows a compatibility warning rather than providing an unusable desktop extension.

Run tests: `node --test tests/installer-routing.test.cjs`.
