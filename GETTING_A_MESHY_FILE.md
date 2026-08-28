# How to Get a `.meshy` File

> **Important:** Meshy's normal Download button provides standard formats such as GLB/FBX/OBJ. The `.meshy` payload used by this importer is obtained from the model request in the web application.

## Chrome DevTools workflow

1. Open the Meshy model in **Google Chrome**.
2. Press **F12** or **Ctrl+Shift+I**.
3. Open **Network**.
4. Reload the model page so the requests populate.
5. In the Network filter/search box, enter **`model`**.
6. Locate the request that contains the actual model payload. Request names and URLs can change as Meshy updates its website.
7. Save the response/body to disk.
8. Give it the `.meshy` extension only if it is the actual Meshy payload.
9. Verify it starts with the `MESHY.AI` signature.

### Do not do this

Do **not** rename `model.glb`, `model.fbx`, `model.obj`, an HTML page, or a JSON API response to `model.meshy`. The importer validates the `MESHY.AI` container signature before decoding.

## Unity

Put the real file anywhere under the project's `Assets` folder:

```text
Assets/Models/MyMeshyModel.meshy
```

The Meshy Importer registers `.meshy` with Unity's Asset Pipeline, creates a native Meshy source asset, and automatically reconstructs a local `.glb` companion for UnityGLTF.

## Blender

Use **File > Import > Meshy Model (.meshy)**. The bundled Blender add-on reconstructs the GLB in memory/temp storage and sends it to Blender's native glTF importer.
