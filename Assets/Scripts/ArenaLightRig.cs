using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Arenanın tavanına, pistin iki yanına simetrik spotlight
/// rigleri kurar. Görsel armatürler çok sayıda olabilir ama
/// gerçek (realtime) ışık sayısı bilinçli olarak azdır —
/// mobil performans için.
///
/// Kurulum Edit Mode'da context menu ile yapılır, sonuç
/// sahneye kaydedilir (Play sırasında hiçbir şey üretilmez).
/// </summary>
[ExecuteAlways]
public class ArenaLightRig : MonoBehaviour
{
    [Header("Yerleşim")]
    [Tooltip("Tavandaki traversların yüksekliği. 8, ana " +
             "kameranın portre kadrajında armatürlerin üst " +
             "bölgede görünmesini sağlayan değer.")]
    [SerializeField] private float ceilingHeight = 8f;

    [Tooltip("Traversların pist merkezinden yanal uzaklığı (±X).")]
    [SerializeField] private float sideOffsetX = 6.5f;

    [Tooltip("Her iki yanda traverslerin duracağı Z noktaları.")]
    [SerializeField] private float[] trussZPositions =
        { -18f, -6f, 6f, 18f };

    [Header("Armatürler (sadece görsel)")]
    [SerializeField] private int lampsPerTruss = 3;
    [SerializeField] private float lampSpacing = 1.6f;
    [SerializeField] private float lampDropBelowTruss = 0.32f;

    [Tooltip("Modelin lensi -Z'ye bakıyorsa işaretli kalsın. " +
             "Armatür ters duruyorsa bu kutuyu değiştir.")]
    [SerializeField] private bool lampLensFacesMinusZ = true;

    [Header("Gerçek Işıklar (az sayıda!)")]
    [Tooltip("Gerçek Spot Light konulacak Z noktaları. " +
             "Her biri iki yanda birer ışık üretir.")]
    [SerializeField] private float[] lightZPositions = { -12f, 12f };

    [SerializeField] private Color lightColor =
        new Color(0.94f, 0.96f, 1f);

    [Tooltip("URP spot ışığı mesafenin karesiyle sönümlenir; " +
             "~10 m yükseklikten buzu aydınlatmak için yüksek " +
             "değer gerekir. Parlaklığı ayarlamak için asıl knob bu.")]
    [SerializeField] private float lightIntensity = 100f;

    [SerializeField] private float lightRange = 30f;
    [SerializeField] private float spotAngle = 72f;
    [SerializeField] private float innerSpotAngle = 34f;

    [Tooltip("Mobilde ek ışık gölgeleri zaten kapalı; " +
             "burada da kapalı tutmak en ucuzu.")]
    [SerializeField] private bool spotShadows = false;

    [Header("Hedef")]
    [Tooltip("Işıkların baktığı yükseklik (buz yüzeyi).")]
    [SerializeField] private float aimHeight = 0.1f;

    private const string RigRootName = "Rigs";

    /// <summary>
    /// Inspector'da parlaklık/renk/açı değiştirilince rig'i
    /// yeniden kurmaya gerek kalmadan mevcut ışıklara uygular.
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        foreach (Light light in GetComponentsInChildren<Light>(true))
        {
            if (light.type != LightType.Spot)
                continue;

            light.color = lightColor;
            light.intensity = lightIntensity;
            light.range = lightRange;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = innerSpotAngle;
            light.shadows =
                spotShadows ? LightShadows.Soft : LightShadows.None;
        }
    }

#if UNITY_EDITOR
    private const string SpotlightModelPath =
        "Assets/Art/Spotlight/Spotlight.fbx";

    private const string TrussModelPath =
        "Assets/Art/Spotlight/SpotlightStructureCeiling.fbx";

    private const string HolderModelPath =
        "Assets/Art/Spotlight/SpotlightHolder.obj";

    [ContextMenu("Build Lighting Rig")]
    public void BuildRig()
    {
        ClearRig();

        GameObject spotlightModel =
            AssetDatabase.LoadAssetAtPath<GameObject>(SpotlightModelPath);

        GameObject trussModel =
            AssetDatabase.LoadAssetAtPath<GameObject>(TrussModelPath);

        GameObject holderModel =
            AssetDatabase.LoadAssetAtPath<GameObject>(HolderModelPath);

        if (spotlightModel == null || trussModel == null)
        {
            Debug.LogError(
                "[ArenaLightRig] Spotlight modelleri bulunamadı. " +
                "Assets/Art/Spotlight/ içindeki FBX'lerin import " +
                "edildiğinden emin ol."
            );

            return;
        }

        Transform rigRoot = new GameObject(RigRootName).transform;
        rigRoot.SetParent(transform, false);

        // Her iki yan: -1 sol, +1 sağ.
        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            float side = sideIndex == 0 ? -1f : 1f;

            foreach (float z in trussZPositions)
            {
                BuildTruss(
                    rigRoot,
                    trussModel,
                    spotlightModel,
                    holderModel,
                    side,
                    z
                );
            }

            foreach (float z in lightZPositions)
            {
                BuildSpotLight(rigRoot, side, z);
            }
        }

        Debug.Log(
            "[ArenaLightRig] Rig kuruldu: " +
            (trussZPositions.Length * 2) + " travers, " +
            (trussZPositions.Length * 2 * lampsPerTruss) + " armatür, " +
            (lightZPositions.Length * 2) + " gerçek ışık."
        );
    }

    [ContextMenu("Clear Lighting Rig")]
    public void ClearRig()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);

            if (child.name != RigRootName)
                continue;

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    private void BuildTruss(
        Transform rigRoot,
        GameObject trussModel,
        GameObject spotlightModel,
        GameObject holderModel,
        float side,
        float z)
    {
        Vector3 trussPosition =
            new Vector3(side * sideOffsetX, ceilingHeight, z);

        GameObject truss =
            (GameObject)PrefabUtility.InstantiatePrefab(trussModel);

        truss.name =
            "Truss_" + (side < 0f ? "L" : "R") + "_" + z;

        truss.transform.SetParent(rigRoot, false);
        truss.transform.position = trussPosition;

        // Model 5 m boyunca kendi X ekseninde uzanıyor;
        // 90° çevirince pist boyunca (Z) uzanır.
        truss.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        // Armatürler traversin altına, Z boyunca dizilir.
        float span = (lampsPerTruss - 1) * lampSpacing;

        for (int i = 0; i < lampsPerTruss; i++)
        {
            float offset =
                lampsPerTruss == 1
                    ? 0f
                    : -span * 0.5f + i * lampSpacing;

            Vector3 lampPosition =
                trussPosition +
                new Vector3(0f, -lampDropBelowTruss, offset);

            if (holderModel != null)
            {
                GameObject holder =
                    (GameObject)PrefabUtility.InstantiatePrefab(holderModel);

                holder.name = "Holder_" + i;
                holder.transform.SetParent(truss.transform, true);
                holder.transform.position =
                    trussPosition + new Vector3(0f, 0f, offset);

                holder.transform.rotation = Quaternion.identity;
            }

            GameObject lamp =
                (GameObject)PrefabUtility.InstantiatePrefab(spotlightModel);

            lamp.name = "Lamp_" + i;
            lamp.transform.SetParent(truss.transform, true);
            lamp.transform.position = lampPosition;

            // Armatür buzu göstersin.
            Vector3 aimPoint = new Vector3(0f, aimHeight, z + offset);
            Vector3 toTarget = (aimPoint - lampPosition).normalized;

            lamp.transform.rotation =
                Quaternion.LookRotation(
                    lampLensFacesMinusZ ? -toTarget : toTarget
                );
        }
    }

    private void BuildSpotLight(
        Transform rigRoot,
        float side,
        float z)
    {
        Vector3 position =
            new Vector3(
                side * sideOffsetX,
                ceilingHeight - lampDropBelowTruss,
                z
            );

        GameObject lightObject =
            new GameObject(
                "ArenaSpot_" + (side < 0f ? "L" : "R") + "_" + z
            );

        lightObject.transform.SetParent(rigRoot, false);
        lightObject.transform.position = position;

        Vector3 aimPoint = new Vector3(0f, aimHeight, z);

        lightObject.transform.rotation =
            Quaternion.LookRotation(
                (aimPoint - position).normalized
            );

        Light light = lightObject.AddComponent<Light>();

        light.type = LightType.Spot;
        light.color = lightColor;
        light.intensity = lightIntensity;
        light.range = lightRange;
        light.spotAngle = spotAngle;
        light.innerSpotAngle = innerSpotAngle;
        light.shadows =
            spotShadows ? LightShadows.Soft : LightShadows.None;

        // Realtime: lightmap'e pişirilmesin.
        light.lightmapBakeType = LightmapBakeType.Realtime;
        light.useColorTemperature = false;
    }
#endif
}
