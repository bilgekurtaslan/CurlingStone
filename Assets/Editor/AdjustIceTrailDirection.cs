using UnityEditor;
using UnityEngine;

/// <summary>
/// Tek seferlik araç: StoneIceTrail.prefab içindeki tüm alt parçacık
/// sistemlerini "aşağı düşen kar" yerine "taşın arkasına doğru akan
/// buz spreyi" gibi görünecek şekilde ayarlar — yerçekimini kapatıp
/// (aksi halde dünya -Y yönüne düşmeye devam ederler), yerel -Z
/// yönünde (taşın hareket yönünün tersi) bir başlangıç hızı ekler.
/// Kullanımı: Tools/Curling/Adjust Ice Trail Direction (Backward).
/// </summary>
public static class AdjustIceTrailDirection
{
    private const string PrefabPath = "Assets/Magic VFX/StoneIceTrail.prefab";

    [MenuItem("Tools/Curling/Adjust Ice Trail Direction (Backward)")]
    public static void AdjustBackward()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            Debug.LogError("[AdjustIceTrailDirection] Prefab bulunamadı: " + PrefabPath);
            return;
        }

        string prefabPath = AssetDatabase.GetAssetPath(prefab);
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

        ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
        int adjusted = 0;

        foreach (ParticleSystem ps in systems)
        {
            ParticleSystem.MainModule main = ps.main;
            main.gravityModifier = 0f;

            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;

            float backSpeed = Mathf.Max(0.6f, Mathf.Abs(main.startSpeed.constant) * 0.8f);

            velocity.z = new ParticleSystem.MinMaxCurve(-backSpeed, -backSpeed * 0.5f);
            velocity.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.2f);

            adjusted++;
        }

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log("[AdjustIceTrailDirection] " + adjusted + " parçacık sistemi geriye akacak şekilde ayarlandı: " + prefabPath);
    }
}
