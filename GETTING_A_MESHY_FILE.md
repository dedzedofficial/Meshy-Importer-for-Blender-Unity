# How to Get a `.meshy` File

> **First check you need one.** Meshy's normal **Download** button gives you a `.glb`, `.fbx` or `.obj` file. Every engine imports those directly, without this importer. The `.meshy` file is what the Meshy website itself loads to show the model in your browser. This guide shows how to save it.

## Save it with Chrome (about a minute)

1. Open the model's page on the Meshy website in **Google Chrome** (Edge and other Chromium browsers work the same way).
2. Press **F12** (or **Ctrl+Shift+I**, or **Cmd+Option+I** on a Mac) to open DevTools.
3. Click the **Network** tab.
4. Reload the page (**F5**) with DevTools still open, so the requests are recorded.
5. Type **`model`** in the Network filter box.
6. Find the request whose response is the model. It is usually the largest one: click the **Size** column header to sort by size. Request names and URLs change when Meshy updates its website.
7. Right-click that request and choose **Open in new tab**. The browser downloads the file. Alternatively, open the request's **Response** tab and save it from there.
8. Rename the downloaded file so it ends in **`.meshy`**, for example `dragon.meshy`.

### Check you saved the right thing

A real `.meshy` file:

- starts with the text **`MESHY.AI`** (open it in a text editor to check; the rest looks like gibberish), and
- is usually **several megabytes** in size.

If you picked the wrong request, the importer tells you what you saved instead, for example "This is a web page (HTML), not the model" or "This is a JSON API response". Go back to step 6 and try the next-largest request.

### Don't do this

Don't rename a `.glb`, `.fbx`, `.obj`, an HTML page or a JSON response to `.meshy`. It won't work: the importer checks for the `MESHY.AI` signature before decoding.

## Import it

| Engine | How |
|---|---|
| **Unity** | Drop it anywhere under `Assets/`, for example `Assets/Models/dragon.meshy`. Unity builds the model right away. Select the file to see its settings. |
| **Blender** | **File > Import > Meshy Model (.meshy)**, or drag the file into the 3D viewport (Blender 4.1+). |
| **Godot** | Drop it anywhere in the FileSystem dock. It imports like any 3D scene; double-click to open it. |
| **Unreal** | **Tools > Meshy > Import .meshy Files...**, or turn on **Tools > Meshy > Auto-Import Inbox Folder** and drop it into `<Project>/MeshyInbox/`. |
| **Anything else** | `python -m meshy_core dragon.meshy` writes a normal `dragon.glb` (see the [README](README.md#-command-line-converter)). |

Having trouble? See [TROUBLESHOOTING.md](TROUBLESHOOTING.md#wrong-file-errors).
