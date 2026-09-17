using UnityEngine;

/// <summary>
/// Verilen tribün objelerinin koltuk alanını (mesh bounds) tarayarak
/// üzerine rastgele seyirci modelleri diziyor.
/// </summary>
public class CrowdManager : MonoBehaviour
{
    [Header("Tribünler")]
    [SerializeField] private GameObject[] leftGrandstands;
    [SerializeField] private GameObject[] rightGrandstands;

    [Header("Koltuk Alanı")]
    [SerializeField] private string seatObjectName = "Paint - Sand Beige";
    [SerializeField] private int rows = 10;
    [SerializeField] private int rowsToSkipFromTop = 3;
    [SerializeField] private float personSpacing = 0.68f;
    [SerializeField] private int extraColumns = 4;
    [SerializeField] private float heightAboveSeat = 0.05f;
    [SerializeField, Range(0.1f, 1f)] private float rowSpanFraction = 0.1f;
    [SerializeField] private Vector3 crowdLocalPositionOffset = new Vector3(-0.42f, -0.1f, 12.445f);
    [SerializeField] private Vector3 crowdLocalRotationOffset = new Vector3(0f, -87.729f, 0f);

    [Header("Seyirciler")]
    [SerializeField] private GameObject[] spectatorPrefabs;
    [SerializeField, Range(0f, 1f)] private float fillProbability = 0.85f;
    [SerializeField] private int clusterMinSize = 3;
    [SerializeField] private int clusterMaxSize = 5;
    [SerializeField] private int gapMinSize = 1;
    [SerializeField] private int gapMaxSize = 1;
    [SerializeField] private float targetHeight = 2.86f;
    [SerializeField] private float heightVarianceMin = 0.92f;
    [SerializeField] private float heightVarianceMax = 1.08f;
    [SerializeField] private float jitter = 0.03f;
    [SerializeField] private float yawJitter = 15f;
    [SerializeField] private float yawOffset = 180f;
    [SerializeField] private float leftYawExtra = 0f;
    [SerializeField] private float rightYawExtra = 180f;

    [Header("Kıyafet Renkleri")]
    [SerializeField] private bool colorizeClothing = true;

    // Uyumlu, gerçekçi kalabalık paleti — kırık/tok tonlar,
    // neon veya çakışan parlak renkler değil.
    [SerializeField]
    private Color[] clothingPalette =
    {
        new Color(0.65f, 0.12f, 0.14f), // bordo/kırmızı
        new Color(0.10f, 0.20f, 0.45f), // lacivert
        new Color(0.12f, 0.32f, 0.18f), // koyu yeşil
        new Color(0.75f, 0.55f, 0.15f), // hardal
        new Color(0.30f, 0.30f, 0.32f), // antrasit gri
        new Color(0.85f, 0.83f, 0.78f), // kırık beyaz
        new Color(0.15f, 0.15f, 0.17f), // siyaha yakın
        new Color(0.45f, 0.25f, 0.15f), // kahverengi
        new Color(0.20f, 0.45f, 0.50f), // petrol mavisi
        new Color(0.55f, 0.20f, 0.30f), // koyu pembe/gül kurusu
        new Color(0.35f, 0.15f, 0.40f), // mor
        new Color(0.60f, 0.60f, 0.62f)  // açık gri
    };

    private static readonly string[] ClothingMaterialNames =
    {
        "Jacket", "LightJacket", "Pants", "Shirt", "Shoes"
    };

    [Header("Animasyon (prosedürel — Mecanim kullanmaz)")]
    [SerializeField, Range(0f, 1f)] private float clapProbability = 0.25f;
    [SerializeField, Range(0f, 1f)] private float cheerProbability = 0.15f;

    [SerializeField] private bool regenerateOnPlay = false;
    [SerializeField] private bool enableAnimations = true;

    private void Start()
    {
        // Edit modunda elle oluşturup ayarladığın kalabalık
        // sahnede zaten duruyor — Play'e her basışta rastgele
        // yeniden üretip üstüne yazmasın diye kapalı.
        if (regenerateOnPlay)
        {
            SpawnAll();
        }
    }

    [ContextMenu("Tümünü Yeniden Oluştur (sol + sağ)")]
    private void SpawnAll()
    {
        SpawnGroup(leftGrandstands, leftYawExtra);
        SpawnGroup(rightGrandstands, rightYawExtra);
    }

    [ContextMenu("Sadece Animasyonları Yenile (pozisyon değişmez)")]
    private void RefreshAnimationsOnly()
    {
        RefreshAnimationsFor(leftGrandstands);
        RefreshAnimationsFor(rightGrandstands);
    }

    private void RefreshAnimationsFor(GameObject[] stands)
    {
        if (stands == null)
            return;

        foreach (GameObject stand in stands)
        {
            if (stand == null)
                continue;

            Transform crowd = stand.transform.Find("Crowd");

            if (crowd == null)
                continue;

            foreach (Transform spectator in crowd)
            {
                PlayRandomAnimation(spectator.gameObject);
            }
        }
    }

    [ContextMenu("Sadece Sol Tribünleri Yeniden Oluştur")]
    private void SpawnLeftOnly()
    {
        SpawnGroup(leftGrandstands, leftYawExtra);
    }

    [ContextMenu("Sadece Sağ Tribünleri Yeniden Oluştur")]
    private void SpawnRightOnly()
    {
        SpawnGroup(rightGrandstands, rightYawExtra);
    }

    private void SpawnGroup(GameObject[] stands, float extraYaw)
    {
        if (stands == null)
            return;

        foreach (GameObject stand in stands)
        {
            SpawnCrowdFor(stand, extraYaw);
        }
    }

    private void SpawnCrowdFor(GameObject stand, float extraYaw)
    {
        if (stand == null)
            return;

        if (spectatorPrefabs == null || spectatorPrefabs.Length == 0)
            return;

        Transform seat = FindDeepChild(stand.transform, seatObjectName);

        if (seat == null)
        {
            Debug.LogWarning(
                "[CrowdManager] '" + seatObjectName +
                "' bulunamadı: " + stand.name
            );

            return;
        }

        Renderer seatRenderer = seat.GetComponent<Renderer>();

        if (seatRenderer == null)
            return;

        Bounds bounds = seatRenderer.bounds;

        // Daha önce oluşturulmuş bir kalabalık varsa
        // (Edit modunda tekrar tekrar tetiklenebilir) önce sil.
        Transform existingCrowd = stand.transform.Find("Crowd");

        if (existingCrowd != null)
        {
            if (Application.isPlaying)
            {
                Destroy(existingCrowd.gameObject);
            }
            else
            {
                DestroyImmediate(existingCrowd.gameObject);
            }
        }

        GameObject holder = new GameObject("Crowd");
        holder.transform.SetParent(stand.transform, false);
        holder.transform.localPosition = crowdLocalPositionOffset;
        holder.transform.localRotation = Quaternion.Euler(crowdLocalRotationOffset);

        float baseYaw = stand.transform.eulerAngles.y + yawOffset + extraYaw;

        // Sıra genişliğine göre kaç kişinin tam yan yana
        // (omuz omuza) sığacağını otomatik hesapla.
        float rowWidth = bounds.max.x - bounds.min.x;

        int columns =
            Mathf.Max(1, Mathf.FloorToInt(rowWidth / personSpacing))
            + extraColumns;

        int rowsToSpawn =
            Mathf.Max(0, rows - rowsToSkipFromTop);

        for (int r = 0; r < rowsToSpawn; r++)
        {
            int c = 0;

            while (c < columns)
            {
                bool isCluster = Random.value < fillProbability;

                int runLength =
                    isCluster
                        ? Random.Range(clusterMinSize, clusterMaxSize + 1)
                        : Random.Range(gapMinSize, gapMaxSize + 1);

                for (int k = 0;
                     k < runLength && c < columns;
                     k++, c++)
                {
                    if (!isCluster)
                        continue;

                float tRow =
                    (rows <= 1 ? 0.5f : r / (float)(rows - 1)) *
                    rowSpanFraction;

                float tCol =
                    columns <= 1 ? 0.5f : c / (float)(columns - 1);

                Vector3 position = new Vector3(
                    Mathf.Lerp(bounds.min.x, bounds.max.x, tCol),
                    Mathf.Lerp(bounds.min.y, bounds.max.y, tRow) + heightAboveSeat,
                    Mathf.Lerp(bounds.min.z, bounds.max.z, tRow)
                );

                position.x += Random.Range(-jitter, jitter);
                position.z += Random.Range(-jitter, jitter);

                GameObject prefab =
                    spectatorPrefabs[
                        Random.Range(0, spectatorPrefabs.Length)
                    ];

                GameObject instance =
                    Instantiate(
                        prefab,
                        position,
                        Quaternion.identity,
                        holder.transform
                    );

                float yaw =
                    baseYaw + Random.Range(-yawJitter, yawJitter);

                instance.transform.rotation =
                    Quaternion.Euler(0f, yaw, 0f);

                NormalizeHeight(instance, position);
                PlayRandomAnimation(instance);

                    if (colorizeClothing)
                    {
                        ColorizeClothing(instance);
                    }
                }
            }
        }
    }

    private void PlayRandomAnimation(GameObject instance)
    {
        // Var olan bir component'ten temiz başla — Refresh
        // tekrar çağrıldığında eski davranış kalmasın.
        SpectatorIdleAnimator existing =
            instance.GetComponent<SpectatorIdleAnimator>();

        if (existing != null)
        {
            if (Application.isPlaying)
                Destroy(existing);
            else
                DestroyImmediate(existing);
        }

        if (!enableAnimations)
            return;

        float roll = Random.value;

        SpectatorIdleAnimator.ArmBehavior behavior;

        if (roll < clapProbability)
        {
            behavior = SpectatorIdleAnimator.ArmBehavior.Clap;
        }
        else if (roll < clapProbability + cheerProbability)
        {
            behavior = SpectatorIdleAnimator.ArmBehavior.Cheer;
        }
        else
        {
            // Alkışlamayan/tezahürat yapmayan seyirci hiç
            // hareket etmesin — düz otursun/dursun.
            return;
        }

        instance
            .AddComponent<SpectatorIdleAnimator>()
            .SetBehavior(behavior);
    }

    private void ColorizeClothing(GameObject instance)
    {
        Renderer[] renderers =
            instance.GetComponentsInChildren<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;
            bool changedAny = false;

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null)
                    continue;

                string materialName =
                    materials[i].name.Replace(" (Instance)", "");

                if (!IsClothingMaterial(materialName))
                    continue;

                materials[i].color = RandomVividColor();
                changedAny = true;
            }

            if (changedAny)
            {
                renderer.materials = materials;
            }
        }
    }

    private static bool IsClothingMaterial(string materialName)
    {
        foreach (string keyword in ClothingMaterialNames)
        {
            if (materialName.IndexOf(
                    keyword,
                    System.StringComparison.OrdinalIgnoreCase
                ) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private Color RandomVividColor()
    {
        if (clothingPalette == null || clothingPalette.Length == 0)
            return Color.gray;

        return clothingPalette[Random.Range(0, clothingPalette.Length)];
    }

    private void NormalizeHeight(
        GameObject instance,
        Vector3 groundPosition)
    {
        Renderer[] renderers =
            instance.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return;

        Bounds combined = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }

        float currentHeight = combined.size.y;

        if (currentHeight <= 0.0001f)
            return;

        float desiredHeight =
            targetHeight *
            Random.Range(heightVarianceMin, heightVarianceMax);

        float scaleFactor = desiredHeight / currentHeight;

        instance.transform.localScale *= scaleFactor;

        // Ölçeklendirdikten sonra ayakları zemin
        // seviyesine (spawn noktasına) tekrar oturt.
        renderers = instance.GetComponentsInChildren<Renderer>();
        combined = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            combined.Encapsulate(renderers[i].bounds);
        }

        float feetOffset = groundPosition.y - combined.min.y;

        instance.transform.position +=
            new Vector3(0f, feetOffset, 0f);
    }

    private static Transform FindDeepChild(
        Transform parent,
        string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;

            Transform found = FindDeepChild(child, name);

            if (found != null)
                return found;
        }

        return null;
    }
}
