using UnityEditor;
using UnityEngine;

/// <summary>
/// Tek seferlik araç: "Densify Ice Trail" birkaç kez çalıştırıldığı için
/// (her seferinde mevcut değerin üzerine çarpıyordu) yoğunluk aşırıya
/// kaçmış olabilir — çok fazla parçacık üst üste binince, özellikle
/// parlak (spot ışıklı) zeminde beyaza yakınsayıp "soluyormuş" gibi
/// görünüyor. Bu araç, önceki çalıştırmalardan bağımsız olarak tüm
/// değerleri SABİT, ölçülü sayılara resetler (çarpmaz, doğrudan atar).
/// Kullanımı: Tools/Curling/Normalize Ice Trail Density.
/// </summary>
public static class NormalizeIceTrailDensity
{
    private const string PrefabPath = "Assets/Magic VFX/StoneIceTrail.prefab";

    private const float RateOverDistance = 10f;
    private const float RateOverTime = 4f;
    private const float Lifetime = 0.6f;
    private const float SizeMin = 0.08f;
    private const float SizeMax = 0.18f;
    private const int MaxParticles = 400;

    [MenuItem("Tools/Curling/Normalize Ice Trail Density")]
    public static void Normalize()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            Debug.LogError("[NormalizeIceTrailDensity] Prefab bulunamadı: " + PrefabPath);
            return;
        }

        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

        ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
        int adjusted = 0;

        foreach (ParticleSystem ps in systems)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverDistance = new ParticleSystem.MinMaxCurve(RateOverDistance);
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(RateOverTime);

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(Lifetime);
            main.startSize = new ParticleSystem.MinMaxCurve(SizeMin, SizeMax);
            main.maxParticles = MaxParticles;

            adjusted++;
        }

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log("[NormalizeIceTrailDensity] " + adjusted + " parçacık sistemi sabit/ölçülü değerlere resetlendi: " + prefabPath);
    }
}
