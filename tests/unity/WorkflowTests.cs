using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using FISHHWB.MeshyImporter;
using FISHHWB.MeshyImporter.Editor;

// Copy tests/unity to Assets/MeshyWorkflowTests in a disposable test project.
public sealed class MeshyWorkflowTests
{
    private string folder, path, guid;
    [SetUp] public void SetUp()
    {
        folder = "Assets/MeshyWorkflowRun_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        path = folder + "/Model.meshy";
        File.Copy("Assets/MeshyWorkflowTests/Fixtures/workflow.bytes", path);
        Reimport();
        guid = AssetDatabase.AssetPathToGUID(path);
    }
    [TearDown] public void TearDown()
    {
        AssetDatabase.DeleteAsset(folder);
        string memory = "ProjectSettings/MeshyImporter/LastSuccessful/" + guid + ".json";
        if (File.Exists(memory)) File.Delete(memory);
    }
    private void Reimport() => AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
    private MeshySourceAsset Source() => AssetDatabase.LoadAllAssetsAtPath(path).OfType<MeshySourceAsset>().Single();
    private MeshyScriptedImporter Importer() => (MeshyScriptedImporter)AssetImporter.GetAtPath(path);

    [Test] public void NativeSummaryAndSettingsSurviveReimport()
    {
        var source = Source();
        Assert.IsTrue(source.NativeSuccess, source.Status);
        Assert.AreEqual(81, source.VertexCount);
        Assert.AreEqual(128, source.TriangleCount);
        Assert.Greater(source.EstimatedTextureBytes, 0);
        Assert.IsNotEmpty(source.PreflightReport);
        var settings = new SerializedObject(Importer());
        settings.FindProperty("scaleFactor").floatValue = 2f;
        settings.FindProperty("generateColliders").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Importer().SaveAndReimport();
        Reimport();
        settings = new SerializedObject(Importer());
        Assert.AreEqual(2f, settings.FindProperty("scaleFactor").floatValue);
        Assert.IsTrue(settings.FindProperty("generateColliders").boolValue);
        Assert.IsTrue(Source().NativeSuccess, Source().Status);
    }

    [Test] public void ExternalMaterialSurvivesSourceReimport()
    {
        var material = new Material(Source().GeneratedMaterials[0]);
        foreach (string property in material.GetTexturePropertyNames()) material.SetTexture(property, null);
        string materialPath = folder + "/Custom.mat";
        AssetDatabase.CreateAsset(material, materialPath);
        var settings = new SerializedObject(Importer());
        var maps = settings.FindProperty("materialRemaps"); maps.arraySize = 1;
        var map = maps.GetArrayElementAtIndex(0);
        map.FindPropertyRelative("key").stringValue = Source().MaterialKeys[0];
        map.FindPropertyRelative("material").objectReferenceValue = material;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string matGuid, out long matId);
        map.FindPropertyRelative("materialGuid").stringValue = matGuid;
        map.FindPropertyRelative("materialFileId").longValue = matId;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Importer().SaveAndReimport(); Reimport();
        var model = (GameObject)AssetDatabase.LoadMainAssetAtPath(path);
        Assert.IsTrue(model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Contains(material));
        Assert.AreEqual(materialPath, AssetDatabase.GetAssetPath(material));
    }

    [UnityTest] public IEnumerator FailedImportKeepsSuccessfulSettingsSnapshot()
    {
        yield return null;
        string memory = "ProjectSettings/MeshyImporter/LastSuccessful/" + guid + ".json";
        Assert.IsTrue(File.Exists(memory));
        string saved = File.ReadAllText(memory);
        LogAssert.Expect(LogType.Error, new Regex("Meshy Importer: failed to import"));
        File.WriteAllText(path, "broken download");
        Reimport(); yield return null;
        StringAssert.StartsWith("Import failed", Source().Status);
        Assert.AreEqual(saved, File.ReadAllText(memory));
    }
}
