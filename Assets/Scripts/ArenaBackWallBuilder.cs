using UnityEngine;

/// <summary>
/// Pistin house'un ötesindeki boş ucunu kapatan bir duvar ve
/// üzerine monte edilmiş bir skor tabelası (jumbotron) paneli
/// oluşturur. KickBoardBuilder ile aynı prosedürel desende
/// çalışır — Edit Mode'da anında görünür, elle asset gerekmez.
/// </summary>
[ExecuteAlways]
public class ArenaBackWallBuilder : MonoBehaviour
{
    [Header("Duvar (world units)")]
    [SerializeField] private float wallWidth = 26f;
    [SerializeField] private float wallHeight = 15f;
    [SerializeField] private float wallThickness = 0.5f;

    [Header("Tabela Paneli")]
    [SerializeField] private float screenWidth = 11f;
    [SerializeField] private float screenHeight = 4.5f;
    [SerializeField] private float screenThickness = 0.2f;
    [SerializeField] private float screenBottomOffset = 0.5f;

    [Header("Banner (duvarın ön yüzü)")]
    [SerializeField] private float bannerWidth = 8f;
    [SerializeField] private float bannerHeight = 4.5f;
    [SerializeField] private Material bannerMaterial;

    [Header("Üst Cam Paneller")]
    [SerializeField] private int glassPanelCount = 6;
    [SerializeField] private float glassPanelWidth = 3.4f;
    [SerializeField] private float glassPanelHeight = 4f;
    [SerializeField] private float glassPanelSpacing = 0.4f;
    [SerializeField] private float glassPanelTopMargin = 1f;
    [SerializeField] private Material glassMaterial;

    [Header("Malzemeler")]
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material screenMaterial;

    [Header("Kilit")]
    [Tooltip("Kapatırsan bu script duvarı/camları/banner'ı bir " +
             "daha otomatik yeniden kurmaz — Scene view'da elle " +
             "yaptığın değişiklikler (konum, boyut, döndürme) " +
             "korunur. İlk kurulum için açık kalmalı; sen elle " +
             "düzenlemeye başlayınca kapat.")]
    [SerializeField] private bool autoRebuild = true;

    private const string WallName = "Wall";
    private const string ScreenName = "Screen";
    private const string BannerName = "Banner";
    private const string GlassGroupName = "GlassPanels";

    /// <summary>Tabela panelinin transformu — Scoreboard buna bakar.</summary>
    public Transform ScreenTransform { get; private set; }

    private void Awake()
    {
        Build();
    }

    private void OnValidate()
    {
        Build();
    }

    private void Build()
    {
        bool alreadyBuilt = transform.Find(WallName) != null;

        if (!autoRebuild && alreadyBuilt)
        {
            // Kilitliyiz ve daha önce kurulmuş — elle yapılmış
            // düzenlemelere dokunma, sadece Scoreboard'un ihtiyaç
            // duyduğu referansı tazele.
            ScreenTransform = transform.Find(ScreenName);
            return;
        }

        Transform wall =
            BuildPart(
                WallName,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(wallWidth, wallHeight, wallThickness),
                wallMaterial
            );

        wall.localPosition = new Vector3(0f, wallHeight * 0.5f, 0f);

        Transform screen =
            BuildPart(
                ScreenName,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(screenWidth, screenHeight, screenThickness),
                screenMaterial
            );

        screen.localPosition =
            new Vector3(
                0f,
                wallHeight + screenBottomOffset + screenHeight * 0.5f,
                0f
            );

        ScreenTransform = screen;

        // Banner (sponsor logosu), duvarın ön yüzüne (-Z, kameraya
        // bakan taraf) hafifçe önde asılı düz bir panel.
        Transform banner =
            BuildPart(
                BannerName,
                Vector3.zero,
                Quaternion.Euler(0f, 180f, 0f),
                new Vector3(bannerWidth, bannerHeight, 1f),
                bannerMaterial,
                PrimitiveType.Quad
            );

        banner.localPosition =
            new Vector3(
                0f,
                wallHeight * 0.5f,
                -(wallThickness * 0.5f + 0.02f)
            );

        BuildGlassPanels();
    }

    /// <summary>
    /// Duvarın en üstüne, tavana yakın bir sıra büyük cam panel
    /// dizer — arenanın üst cephesindeki pencereleri andırır.
    /// </summary>
    private void BuildGlassPanels()
    {
        Transform group = transform.Find(GlassGroupName);

        if (group == null)
        {
            group = new GameObject(GlassGroupName).transform;
            group.SetParent(transform, false);
        }

        float totalWidth =
            glassPanelCount * glassPanelWidth +
            (glassPanelCount - 1) * glassPanelSpacing;

        float startX = -totalWidth * 0.5f + glassPanelWidth * 0.5f;

        float panelY =
            wallHeight - glassPanelTopMargin - glassPanelHeight * 0.5f;

        for (int i = 0; i < glassPanelCount; i++)
        {
            Transform panel =
                BuildPart(
                    "Glass_" + i,
                    Vector3.zero,
                    Quaternion.Euler(0f, 180f, 0f),
                    new Vector3(glassPanelWidth, glassPanelHeight, 1f),
                    glassMaterial,
                    PrimitiveType.Quad,
                    group
                );

            panel.localPosition =
                new Vector3(
                    startX + i * (glassPanelWidth + glassPanelSpacing),
                    panelY,
                    -(wallThickness * 0.5f + 0.03f)
                );
        }

        // Fazla kalan eski panel objelerini temizle (sayı azaltılırsa).
        for (int i = glassPanelCount; group.Find("Glass_" + i) != null; i++)
        {
            Transform stale = group.Find("Glass_" + i);
            DestroyPart(stale.gameObject);
        }
    }

    private Transform BuildPart(
        string partName,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        Material material,
        PrimitiveType primitiveType = PrimitiveType.Cube,
        Transform parentOverride = null)
    {
        Transform parent = parentOverride != null ? parentOverride : transform;

        Transform existing = parent.Find(partName);

        GameObject part =
            existing != null
                ? existing.gameObject
                : GameObject.CreatePrimitive(primitiveType);

        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = localScale;

        Collider partCollider = part.GetComponent<Collider>();

        if (partCollider != null)
        {
            DestroyPartCollider(partCollider);
        }

        MeshRenderer renderer = part.GetComponent<MeshRenderer>();

        if (renderer != null && material != null)
        {
            renderer.sharedMaterial = material;
        }

        return part.transform;
    }

    private static void DestroyPart(GameObject part)
    {
        if (Application.isPlaying)
        {
            Destroy(part);
        }
#if UNITY_EDITOR
        else
        {
            UnityEditor.EditorApplication.delayCall +=
                () =>
                {
                    if (part != null)
                    {
                        DestroyImmediate(part);
                    }
                };
        }
#endif
    }

    private static void DestroyPartCollider(Collider partCollider)
    {
        if (Application.isPlaying)
        {
            Destroy(partCollider);
        }
#if UNITY_EDITOR
        else
        {
            // OnValidate içinden anında Destroy çağrısı yasak —
            // Editor'ün bir sonraki güncellemesine erteliyoruz.
            UnityEditor.EditorApplication.delayCall +=
                () =>
                {
                    if (partCollider != null)
                    {
                        DestroyImmediate(partCollider);
                    }
                };
        }
#endif
    }
}
