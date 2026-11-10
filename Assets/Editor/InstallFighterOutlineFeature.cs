using PoSumo;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// *PoSumo → Install Fighter Outline Feature*: adds `Systems_FighterOutlineFeature`
/// to `Assets/Settings/Renderer2D.asset` as a sub-asset, the way the URP inspector's
/// "Add Renderer Feature" button does. Idempotent. Logs `OUTLINE FEATURE RESULT:`.
public static class InstallFighterOutlineFeature
{
    private const string RENDERER_PATH = "Assets/Settings/Renderer2D.asset";

    [MenuItem("PoSumo/Install Fighter Outline Feature")]
    public static void Install()
    {
        var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RENDERER_PATH);
        if (data == null)
        {
            Debug.LogError($"OUTLINE FEATURE RESULT: FAIL — no renderer data at {RENDERER_PATH}");
            return;
        }
        foreach (ScriptableRendererFeature existing in data.rendererFeatures)
        {
            if (existing is Systems_FighterOutlineFeature)
            {
                Debug.Log("OUTLINE FEATURE RESULT: already installed");
                return;
            }
        }

        var feature = ScriptableObject.CreateInstance<Systems_FighterOutlineFeature>();
        feature.name = "FighterOutline";
        AssetDatabase.AddObjectToAsset(feature, data);

        var serialized = new SerializedObject(data);
        SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
        SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
        if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId))
        {
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        }
        serialized.ApplyModifiedProperties();
        data.SetDirty();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        Debug.Log($"OUTLINE FEATURE RESULT: installed on {RENDERER_PATH} ({data.rendererFeatures.Count} feature(s))");
    }
}
