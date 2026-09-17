using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tek seferlik araç: "Magic VFX - Ice (FREE)" paketindeki büyük
/// Ef_IceMagicGlowFree01 prefab'ı (17 alt efekt: kar taneleri, buz
/// dumanı, parlayan büyü halkaları vs.) içinden sadece kar/buz temalı,
/// döngüsel (looping) alt efektleri (parlayan büyü halkalarını hariç
/// tutarak) ayıklayıp taşın arkasında sürüklenecek yeni, hafif bir
/// "StoneIceTrail" prefab'ı olarak kaydeder.
/// Kullanımı: Unity menüsünden Tools/Curling/Extract Ice Trail Effect.
/// </summary>
public static class ExtractIceTrailEffect
{
    private const string SourcePrefabPath =
        "Assets/Magic VFX/Magic VFX - Ice (FREE)/Prefabs/Ef_IceMagicGlowFree01.prefab";

    private const string TargetPrefabPath =
        "Assets/Magic VFX/StoneIceTrail.prefab";

    private static readonly string[] KeepChildNames =
    {
        "Ef_SnowFlakes01",
        "Ef_SnowFlakes02",
        "Ef_SnowFlakes03",
        "Ef_SnowFlakes04",
        "Ef_SnowFlakes05",
        "FrostDrops_01",
    };

    [MenuItem("Tools/Curling/Extract Ice Trail Effect")]
    public static void Extract()
    {
        GameObject source =
            AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);

        if (source == null)
        {
            Debug.LogError(
                "[ExtractIceTrailEffect] Kaynak prefab bulunamadı: " +
                SourcePrefabPath
            );
            return;
        }

        GameObject instance =
            (GameObject)PrefabUtility.InstantiatePrefab(source);

        PrefabUtility.UnpackPrefabInstance(
            instance,
            PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction
        );

        var childrenToRemove =
            instance.transform
                .Cast<Transform>()
                .Where(child => !KeepChildNames.Contains(child.name))
                .ToList();

        foreach (Transform child in childrenToRemove)
        {
            Object.DestroyImmediate(child.gameObject);
        }

        var remainingNames =
            instance.transform.Cast<Transform>().Select(t => t.name).ToList();

        instance.name = "StoneIceTrail";

        GameObject saved =
            PrefabUtility.SaveAsPrefabAsset(instance, TargetPrefabPath);

        Object.DestroyImmediate(instance);

        if (saved != null)
        {
            Debug.Log(
                "[ExtractIceTrailEffect] Kaydedildi: " + TargetPrefabPath +
                "  |  Kalan alt efektler: " + string.Join(", ", remainingNames)
            );

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
        }
        else
        {
            Debug.LogError("[ExtractIceTrailEffect] Prefab kaydedilemedi.");
        }
    }
}
