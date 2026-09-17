using UnityEditor;
using UnityEngine;

/// <summary>
/// Tek seferlik araç: StoneIceTrail.prefab'daki tüm alt parçacık
/// sistemlerine "Rate over Distance" ekler — taş ne kadar hızlı
/// giderse gitsin, metre başına parçacık yoğunluğu sabit kalır
/// (aksi halde "Rate over Time" ile hızlı giderken iz seyrekleşiyordu).
/// Ayrıca ömrü ve boyutu hafifçe artırıp izin daha belirgin
/// görünmesini sağlar.
/// Kullanımı: Tools/Curling/Densify Ice Trail.
/// </summary>
public static class DensifyIceTrail
{
    private const string PrefabPath = "Assets/Magic VFX/StoneIceTrail.prefab";
    private const float RateOverDistance = 60f;
    private const float RateOverTimeMultiplier = 3f;
    private const float LifetimeMultiplier = 1.6f;
    private const float SizeMultiplier = 1.5f;
    private const int MinMaxParticles = 300;

    [MenuItem("Tools/Curling/Densify Ice Trail")]
    public static void Densify()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            Debug.LogError("[DensifyIceTrail] Prefab bulunamadı: " + PrefabPath);
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

            if (emission.rateOverTime.mode == ParticleSystemCurveMode.Constant)
            {
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(
                    Mathf.Max(emission.rateOverTime.constant, 1f) * RateOverTimeMultiplier
                );
            }

            ParticleSystem.MainModule main = ps.main;

            if (main.maxParticles < MinMaxParticles)
            {
                main.maxParticles = MinMaxParticles;
            }

            if (main.startLifetime.mode == ParticleSystemCurveMode.Constant)
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(
                    main.startLifetime.constant * LifetimeMultiplier
                );
            }
            else if (main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants)
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(
                    main.startLifetime.constantMin * LifetimeMultiplier,
                    main.startLifetime.constantMax * LifetimeMultiplier
                );
            }

            if (main.startSize.mode == ParticleSystemCurveMode.Constant)
            {
                main.startSize = new ParticleSystem.MinMaxCurve(
                    main.startSize.constant * SizeMultiplier
                );
            }
            else if (main.startSize.mode == ParticleSystemCurveMode.TwoConstants)
            {
                main.startSize = new ParticleSystem.MinMaxCurve(
                    main.startSize.constantMin * SizeMultiplier,
                    main.startSize.constantMax * SizeMultiplier
                );
            }

            adjusted++;
        }

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log("[DensifyIceTrail] " + adjusted + " parçacık sistemi yoğunlaştırıldı: " + prefabPath);
    }
}
