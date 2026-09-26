// Minimal UnityEditor API surface used by the package (signatures from Unity's docs).
// Extend it when the package starts using another editor API.
using System;
using UnityEngine;
namespace UnityEditor
{
    public static class AssetDatabase {
        public static void ImportAsset(string p, ImportAssetOptions o = ImportAssetOptions.Default) {}
        public static void Refresh() {}
        public static UnityEngine.Object[] LoadAllAssetsAtPath(string p) => null;
        public static string GetAssetPath(UnityEngine.Object o) => null;
        public static UnityEngine.Object LoadMainAssetAtPath(string p) => null;
    }
    [Flags] public enum ImportAssetOptions { Default = 0, ForceUpdate = 1 }
    public static class EditorUtility {
        public static bool DisplayDialog(string a, string b, string c) => true;
        public static bool DisplayDialog(string a, string b, string c, string d) => true;
        public static int DisplayDialogComplex(string a, string b, string c, string d, string e) => 0;
        public static string OpenFilePanel(string a, string b, string c) => null;
        public static void RevealInFinder(string p) {}
        public static bool DisplayCancelableProgressBar(string t, string i, float p) => false;
        public static void ClearProgressBar() {}
    }
    public class EditorGUIUtility { public static string systemCopyBuffer { get; set; } }
    public class EditorWindow : ScriptableObject {
        public static T GetWindow<T>(string title) where T : EditorWindow => null;
        public Vector2 minSize { get; set; }
        public void Show() {}
        public void Repaint() {}
    }
    public static class EditorPrefs {
        public static bool GetBool(string k, bool d) => d; public static void SetBool(string k, bool v) {} public static void DeleteKey(string k) {}
        public static string GetString(string k, string d) => d; public static void SetString(string k, string v) {} public static bool HasKey(string k) => false;
    }
    public static class EditorApplication { public delegate void CallbackFunction(); public static CallbackFunction delayCall; public static CallbackFunction update; }
    public static class Selection { public static UnityEngine.Object activeObject; }
    public sealed class MenuItem : Attribute { public MenuItem(string s) {} public MenuItem(string s, bool isValidateFunction, int priority) {} }
    public sealed class InitializeOnLoadMethodAttribute : Attribute {}
    public sealed class CustomEditor : Attribute { public CustomEditor(Type t) {} }
    public class AssetPostprocessor {}
    public static class MeshUtility { public static void Optimize(Mesh m) {} }
    public class SerializedProperty { public float floatValue { get; set; } public bool boolValue { get; set; } }
    public class SerializedObject { public void Update() {} public bool ApplyModifiedProperties() => true; public SerializedProperty FindProperty(string n) => null; }
    public class Editor : ScriptableObject { public UnityEngine.Object target; public SerializedObject serializedObject; public virtual void OnInspectorGUI() {} }
    public static class EditorStyles { public static GUIStyle boldLabel; public static GUIStyle miniLabel; }
    public static class EditorGUILayout {
        public static void LabelField(string a, GUIStyle s) {}
        public static void LabelField(string a, string b) {}
        public static void LabelField(string a, params GUILayoutOption[] o) {}
        public static int Popup(GUIContent label, int selected, GUIContent[] options, params GUILayoutOption[] o) => selected;
        public static bool Foldout(bool foldout, string content, bool toggleOnLabelClick) => foldout;
        public static bool ToggleLeft(string label, bool value, params GUILayoutOption[] o) => value;
        public static Vector2 BeginScrollView(Vector2 scroll, params GUILayoutOption[] o) => scroll;
        public static void EndScrollView() {}
        public static void HelpBox(string m, MessageType t) {}
        public static void Space(float f) {}
        public static bool PropertyField(SerializedProperty p, GUIContent c, params GUILayoutOption[] o) => true;
        public class HorizontalScope : IDisposable { public HorizontalScope(params GUILayoutOption[] o) {} public void Dispose() {} }
    }
    public enum MessageType { None, Info, Warning, Error }
}
namespace UnityEditor.AssetImporters
{
    public abstract class ScriptedImporter : AssetImporter { public abstract void OnImportAsset(AssetImportContext ctx); }
    public class AssetImporter : UnityEngine.Object { public string assetPath; }
    public sealed class ScriptedImporterAttribute : Attribute { public ScriptedImporterAttribute(int v, string[] exts) {} public bool AllowCaching { get; set; } }
    public class AssetImportContext { public string assetPath; public void AddObjectToAsset(string id, UnityEngine.Object o) {} public void SetMainObject(UnityEngine.Object o) {} }
    public abstract class AssetImporterEditor : UnityEditor.Editor { public virtual void OnEnable() {} protected bool ApplyRevertGUI() => true; }
    public abstract class ScriptedImporterEditor : AssetImporterEditor {}
}
namespace UnityEditor.PackageManager
{
    public enum StatusCode { InProgress, Success, Failure }
    public class Error { public string message; }
    public class PackageInfo { public string version; public static PackageInfo FindForAssembly(System.Reflection.Assembly a) => null; }
    public static class Client { public static Requests.AddRequest Add(string id) => null; }
}
namespace UnityEditor.PackageManager.Requests
{
    public class Request { public bool IsCompleted; public StatusCode Status; public Error Error; }
    public class AddRequest : Request {}
}
