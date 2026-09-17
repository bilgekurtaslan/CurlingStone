using UnityEditor;
using UnityEngine;

/// <summary>
/// Tek seferlik araç: StoneIceTrail.prefab'ın (Magic VFX paketinden
/// gelen) alt objelerindeki AudioSource'ları kaldırır — parçacık
/// efekti artık sessiz.
/// Kullanımı: Tools/Curling/Remove Ice Trail Audio.
/// </summary>
public static class RemoveIceTrailAudio
{
    private const string PrefabPath = "Assets/Magic VFX/StoneIceTrail.prefab";

    [MenuItem("Tools/Curling/Remove Ice Trail Audio")]
    public static void RemoveAudio()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            Debug.LogError("[RemoveIceTrailAudio] Prefab bulunamadı: " + PrefabPath);
            return;
        }

        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

        AudioSource[] sources = root.GetComponentsInChildren<AudioSource>(true);
        int removed = sources.Length;

        foreach (AudioSource source in sources)
        {
            Object.DestroyImmediate(source, true);
        }

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log("[RemoveIceTrailAudio] " + removed + " AudioSource kaldırıldı: " + prefabPath);
    }
}
