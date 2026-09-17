using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Ana menüden açılan kişiselleştirme ekranı: üstte karakterin canlı
/// 3D önizlemesi, altta sahip olunan baş eşyaları.
///
/// ÖNİZLEME: Sahnedeki atletin bir kopyası, dünyanın çok uzağındaki
/// bir "sahnecikte" (PreviewStagePosition) tutulur; ona bakan ayrı
/// bir kamera bir RenderTexture'a render eder, o da UI'da RawImage
/// olarak gösterilir. Şapka seçilince kopyada oyunun kendi
/// <see cref="AthleteController.EquipCosmetic"/> API'si çağrıldığı
/// için önizleme maçtaki görünümle birebir aynı — eşya başına hazır
/// görsel üretmeye gerek yok. Kamera, karakterin renderer sınırlarından
/// otomatik çerçevelenir; karakter sürüklenerek döndürülebilir.
///
/// SEÇİM: Seçim anında kaydedilmez — ONAYLA'ya basılınca
/// CosmeticLoadout'a yazılır, GERİ/X ile çıkılırsa değişiklik atılır.
///
/// YERLEŞİM: Diğer ekranlarla aynı mantık — bütün ölçüler aşağıdaki
/// sabitlerde tanımlı 734x1206'lık "tasarım uzayı" pikselleridir.
/// Panel ekrana AspectRatioFitter ile oturur, çocuklar anchor tabanlı
/// yerleştiği için oranlar her çözünürlükte birebir korunur.
/// </summary>
public class CustomizeUI : MonoBehaviour
{
    [SerializeField] private CosmeticCatalog catalog;

    [Header("3D Önizleme")]
    [Tooltip("Boş bırakılırsa sahnedeki atlet bulunup klonlanır.")]
    [SerializeField] private GameObject previewAthletePrefab;

    [Tooltip("Önizlemede oynatılacak animasyon state'i. Animator'de yoksa dokunulmaz.")]
    [SerializeField] private string previewIdleState = "Idle";

    [SerializeField] private float previewFieldOfView = 28f;

    [Tooltip("1 = karakter kareyi tam doldurur; büyütmek kenarlarda pay bırakır.")]
    [Range(0.6f, 2f)]
    [SerializeField] private float previewZoom = 1.18f;

    [Tooltip("Kameranın baktığı nokta, karakterin boyuna oran olarak (0 = ayak, 1 = baş).")]
    [Range(0f, 1f)]
    [SerializeField] private float previewFocusHeight = 0.58f;

    [Tooltip("Sürüklerken piksel başına dönüş açısı.")]
    [SerializeField] private float previewRotateSpeed = 0.55f;

    /// Oyun dünyasının çok altında, ana kameranın asla göremeyeceği
    /// bir nokta — önizleme için ayrı bir layer ayırmaya gerek kalmıyor.
    private static readonly Vector3 PreviewStagePosition = new Vector3(0f, -500f, 0f);

    private const int PreviewTextureWidth = 720;
    private const int PreviewTextureHeight = 480;

    // --- Tasarım uzayı (soldan/üstten piksel) ---
    private const float HeaderWidth = 734f;
    private const float HeaderHeight = 331f;
    private const float PanelWidth = 671f;
    private const float BodyHeight = 895f;
    private const float PanelX = (HeaderWidth - PanelWidth) * 0.5f;
    private const float BodyTop = 311f;
    private const float DesignWidth = HeaderWidth;
    private const float DesignHeight = BodyTop + BodyHeight;

    // --- Gövdenin neon çerçeve içi (içerik) alanı ---
    private const float ContentX = PanelX + 20f;
    private const float ContentTop = BodyTop + 26f;
    private const float ContentWidth = 630f;
    private const float ContentHeight = 848f;

    // --- İçerik ızgarası ---
    private const float PreviewHeight = 420f;

    private const float GridTop = 436f;
    private const float GridHeight = 270f;
    private const float GridPad = 12f;
    private const float CellSize = 141f;
    private const float CellGap = 14f;
    private const int Columns = 4;
    private const float CellIconPad = 14f;
    private const float IconAspect = 300f / 336f;

    private const float ButtonTop = 732f;
    private const float ButtonHeight = 96f;
    private const float ButtonGap = 30f;
    private const float BackWidth = ButtonHeight * (440f / 158f);
    private const float ConfirmWidth = ButtonHeight * (1000f / 312f);

    private GameObject root;
    private RectTransform gridRect;
    private RectTransform emptyHintRect;
    private RawImage previewImage;

    private GameObject previewRoot;
    private GameObject previewAthlete;
    private AthleteController previewAthleteController;
    private Camera previewCamera;
    private RenderTexture previewTexture;

    /// Onaylanana kadar sadece burada tutulur — CosmeticLoadout'a yazılmaz.
    private string pendingItemId;

    public void Show()
    {
        EnsureBuilt();
        EnsurePreviewRig();

        pendingItemId = CosmeticLoadout.GetEquipped(CosmeticSlot.Head);
        ApplyPreview(pendingItemId);
        RefreshCells();

        SetPreviewActive(true);

        // Araya sonradan kurulan başka bir ekran girmiş olabilir —
        // panel her açılışta canvas'ın en üstüne alınır.
        root.transform.SetAsLastSibling();
        root.SetActive(true);
    }

    public void Hide()
    {
        SetPreviewActive(false);

        if (root != null)
        {
            root.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (previewCamera != null)
        {
            previewCamera.targetTexture = null;
        }

        if (previewTexture != null)
        {
            previewTexture.Release();
            Destroy(previewTexture);
            previewTexture = null;
        }
    }

    private void SetPreviewActive(bool active)
    {
        if (previewRoot != null)
        {
            previewRoot.SetActive(active);
        }

        // Kamera yalnızca ekran açıkken render etsin.
        if (previewCamera != null)
        {
            previewCamera.enabled = active;
        }
    }

    // =========================================
    // 3D ÖNİZLEME
    // =========================================

    private void EnsurePreviewRig()
    {
        if (previewRoot != null)
            return;

        GameObject source = previewAthletePrefab;

        if (source == null)
        {
            AthleteController sceneAthlete =
                FindAnyObjectByType<AthleteController>(FindObjectsInactive.Include);

            if (sceneAthlete != null)
            {
                source = sceneAthlete.gameObject;
            }
        }

        if (source == null)
        {
            Debug.LogWarning(
                "CustomizeUI: önizleme için atlet bulunamadı — " +
                "Inspector'daki 'Preview Athlete Prefab' alanını doldurun."
            );

            return;
        }

        previewRoot = new GameObject("CustomizePreviewStage");
        previewRoot.transform.position = PreviewStagePosition;

        previewAthlete = Instantiate(source, previewRoot.transform);
        previewAthlete.name = "PreviewAthlete";
        previewAthlete.transform.localPosition = Vector3.zero;
        previewAthlete.transform.localRotation = Quaternion.identity;
        previewAthlete.SetActive(true);

        // Klon sadece poz verecek: taş takibi, süpürme, ses gibi oyun
        // mantığı önizlemede çalışmasın. (Devre dışı bir bileşenin
        // public metodu hâlâ çağrılabilir — EquipCosmetic çalışır.)
        foreach (MonoBehaviour behaviour in previewAthlete.GetComponentsInChildren<MonoBehaviour>(true))
        {
            behaviour.enabled = false;
        }

        foreach (AudioSource audioSource in previewAthlete.GetComponentsInChildren<AudioSource>(true))
        {
            audioSource.playOnAwake = false;
            audioSource.Stop();
            audioSource.enabled = false;
        }

        previewAthleteController = previewAthlete.GetComponentInChildren<AthleteController>(true);

        Animator animator = previewAthlete.GetComponentInChildren<Animator>(true);

        if (animator != null && !string.IsNullOrEmpty(previewIdleState))
        {
            int stateHash = Animator.StringToHash(previewIdleState);

            if (animator.HasState(0, stateHash))
            {
                animator.Play(stateHash, 0, 0f);
                animator.Update(0f);
            }
        }

        SetupPreviewCamera();

        if (previewImage != null)
        {
            previewImage.texture = previewTexture;
            previewImage.color = previewTexture != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }
    }

    private void SetupPreviewCamera()
    {
        Bounds bounds = CalculateRendererBounds(previewAthlete);

        Vector3 focus = bounds.center;
        focus.y = bounds.min.y + bounds.size.y * previewFocusHeight;

        float halfHeight = Mathf.Max(0.05f, bounds.size.y * 0.5f * previewZoom);

        float distance =
            Mathf.Max(0.2f, halfHeight / Mathf.Tan(previewFieldOfView * 0.5f * Mathf.Deg2Rad));

        // Kamera karakterin önüne konur — modelin hangi yöne baktığını
        // varsaymak yerine kendi forward'ı kullanılıyor.
        Vector3 forward = previewAthlete.transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();

        GameObject cameraObject = new GameObject("CustomizePreviewCamera");
        cameraObject.transform.SetParent(previewRoot.transform, true);
        cameraObject.transform.position = focus + forward * distance;
        cameraObject.transform.LookAt(focus);

        previewTexture =
            new RenderTexture(PreviewTextureWidth, PreviewTextureHeight, 24, RenderTextureFormat.ARGB32);

        previewTexture.antiAliasing = 2;
        previewTexture.Create();

        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        previewCamera.fieldOfView = previewFieldOfView;
        previewCamera.nearClipPlane = Mathf.Max(0.01f, distance * 0.05f);
        previewCamera.farClipPlane = distance * 4f;
        previewCamera.targetTexture = previewTexture;
        previewCamera.allowHDR = false;
        previewCamera.useOcclusionCulling = false;

        // Sahne ışığından bağımsız kalmasın diye menzilli (yalnızca bu
        // sahneciği aydınlatan) bir anahtar ışık — directional olsaydı
        // bütün oyun dünyasını da aydınlatırdı.
        GameObject lightObject = new GameObject("CustomizePreviewLight");
        lightObject.transform.SetParent(cameraObject.transform, false);
        lightObject.transform.localPosition = new Vector3(-0.35f, 0.45f, -0.15f) * distance;

        Light keyLight = lightObject.AddComponent<Light>();
        keyLight.type = LightType.Point;
        keyLight.range = distance * 8f;
        keyLight.intensity = 2.4f;
        keyLight.color = new Color(1f, 0.98f, 0.93f);
        keyLight.shadows = LightShadows.None;
    }

    private static Bounds CalculateRendererBounds(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
        {
            return new Bounds(target.transform.position, Vector3.one);
        }

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private void ApplyPreview(string itemId)
    {
        if (previewAthleteController == null)
            return;

        CosmeticItem item =
            string.IsNullOrEmpty(itemId) || catalog == null
                ? null
                : catalog.FindById(itemId);

        previewAthleteController.EquipCosmetic(CosmeticSlot.Head, item);
    }

    private void RotatePreview(float deltaX)
    {
        if (previewAthlete != null)
        {
            previewAthlete.transform.Rotate(0f, -deltaX * previewRotateSpeed, 0f, Space.World);
        }
    }

    // =========================================
    // EKRAN KURULUMU
    // =========================================

    private void EnsureBuilt()
    {
        if (root != null)
            return;

        Canvas canvas = FindAnyObjectByType<Canvas>();

        if (canvas == null)
        {
            Debug.LogError("CustomizeUI: Canvas bulunamadı!");
            return;
        }

        RectTransform rootRect = CreateRect("CustomizeUI", canvas.transform);
        root = rootRect.gameObject;
        Stretch(rootRect);

        Image scrim =
            CreateImage(
                "Scrim",
                rootRect,
                CreateRoundedSprite(4, 4, 0, new Color(0.01f, 0.03f, 0.06f, 0.92f))
            );

        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;

        // Panel, ekranın %92x%90'lık alanına oranı bozulmadan oturur.
        RectTransform panelArea = CreateRect("PanelArea", rootRect);
        panelArea.anchorMin = new Vector2(0.04f, 0.05f);
        panelArea.anchorMax = new Vector2(0.96f, 0.95f);
        panelArea.offsetMin = Vector2.zero;
        panelArea.offsetMax = Vector2.zero;

        RectTransform designRect = CreateRect("Panel", panelArea);
        Stretch(designRect);

        AspectRatioFitter fitter = designRect.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = DesignWidth / DesignHeight;

        Image body = CreateImage("Body", designRect, Resources.Load<Sprite>("UI/PanelMain"));
        PlaceInDesign(body.rectTransform, PanelX, BodyTop, PanelWidth, BodyHeight);

        Image header = CreateImage("Header", designRect, Resources.Load<Sprite>("UI/PanelHeader"));
        PlaceInDesign(header.rectTransform, 0f, 0f, HeaderWidth, HeaderHeight);

        RectTransform titleRect = CreateRect("Title", designRect);
        PlaceInDesign(titleRect, 55f, 226f, 624f, 82f);

        CreateLabel(Localization.Get("customize_title"), titleRect, TextAlignmentOptions.Center, Color.white);

        RectTransform contentRect = CreateRect("BodyContent", designRect);
        PlaceInDesign(contentRect, ContentX, ContentTop, ContentWidth, ContentHeight);

        BuildPreviewView(contentRect);
        BuildGrid(contentRect);
        BuildButtons(contentRect);
        BuildCloseButton(designRect);

        root.SetActive(false);
    }

    private void BuildPreviewView(RectTransform parent)
    {
        RectTransform viewRect = CreateRect("Preview", parent);
        PlaceInContent(viewRect, 0f, 0f, ContentWidth, PreviewHeight);

        previewImage = viewRect.gameObject.AddComponent<RawImage>();
        previewImage.texture = previewTexture;
        // Doku rig kurulunca atanir; o ana kadar (ve rig kurulamazsa)
        // beyaz bir blok gibi gorunmesin diye saydam baslar.
        previewImage.color = previewTexture != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        previewImage.raycastTarget = true;

        // Sürükleyince karakter dönsün — ayrı sağ/sol ok görselimiz yok,
        // dokunmatikte sürükleme zaten daha doğal.
        EventTrigger trigger = viewRect.gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };

        dragEntry.callback.AddListener(data =>
        {
            if (data is PointerEventData pointer)
            {
                RotatePreview(pointer.delta.x);
            }
        });

        trigger.triggers.Add(dragEntry);
    }

    private void BuildGrid(RectTransform parent)
    {
        RectTransform viewport = CreateRect("GridViewport", parent);
        PlaceInContent(viewport, 0f, GridTop, ContentWidth, GridHeight);

        // Hücrelerin arasındaki boşluktan da sürüklenebilsin diye
        // görünmez ama raycast alan bir zemin.
        Image catcher = viewport.gameObject.AddComponent<Image>();
        catcher.color = Color.clear;
        catcher.raycastTarget = true;

        viewport.gameObject.AddComponent<RectMask2D>();

        gridRect = CreateRect("Cells", viewport);
        gridRect.anchorMin = Vector2.zero;
        gridRect.anchorMax = Vector2.one;
        gridRect.offsetMin = Vector2.zero;
        gridRect.offsetMax = Vector2.zero;

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = gridRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        emptyHintRect = CreateRect("EmptyHint", parent);
        PlaceInContent(emptyHintRect, 40f, GridTop + GridHeight * 0.5f - 30f, ContentWidth - 80f, 60f);

        TMP_Text hint =
            CreateLabel(
                Localization.Get("customize_hint"),
                emptyHintRect,
                TextAlignmentOptions.Center,
                new Color(1f, 1f, 1f, 0.6f)
            );

        hint.fontSizeMax = 34f;
        emptyHintRect.gameObject.SetActive(false);
    }

    private void RefreshCells()
    {
        if (gridRect == null)
            return;

        foreach (Transform child in gridRect)
        {
            Destroy(child.gameObject);
        }

        // "Yok" seçeneği her zaman ilk sırada — şapkayı çıkarmak için.
        int cellCount = 1;

        if (catalog != null)
        {
            foreach (CosmeticItem item in catalog.Items)
            {
                if (item.slot == CosmeticSlot.Head && CosmeticLoadout.IsOwned(item.id))
                {
                    cellCount++;
                }
            }
        }

        int rows = Mathf.CeilToInt(cellCount / (float)Columns);

        // Izgara yüksekliği viewport'a oran olarak verilir; böylece
        // panel büyüyüp küçülünce hücreler de birlikte ölçeklenir.
        float gridDesignHeight =
            GridPad * 2f + rows * CellSize + Mathf.Max(0, rows - 1) * CellGap;

        float heightFraction = gridDesignHeight / GridHeight;

        gridRect.anchorMin = new Vector2(0f, 1f - heightFraction);
        gridRect.anchorMax = new Vector2(1f, 1f);
        gridRect.offsetMin = Vector2.zero;
        gridRect.offsetMax = Vector2.zero;

        BuildCell(0, gridDesignHeight, null, null, Localization.Get("none"));

        int index = 1;

        if (catalog != null)
        {
            foreach (CosmeticItem item in catalog.Items)
            {
                if (item.slot != CosmeticSlot.Head || !CosmeticLoadout.IsOwned(item.id))
                    continue;

                BuildCell(index, gridDesignHeight, item.id, item.icon, item.displayName);
                index++;
            }
        }

        if (emptyHintRect != null)
        {
            emptyHintRect.gameObject.SetActive(cellCount <= 1);
        }
    }

    private void BuildCell(int index, float gridDesignHeight, string itemId, Sprite icon, string label)
    {
        int column = index % Columns;
        int row = index / Columns;

        float x = GridPad + column * (CellSize + CellGap);
        float y = GridPad + row * (CellSize + CellGap);

        RectTransform cellRect = CreateRect((itemId ?? "none") + "Cell", gridRect);
        PlaceInRect(cellRect, ContentWidth, gridDesignHeight, x, y, CellSize, CellSize);

        Image background =
            CreateImage(
                "Bg",
                cellRect,
                CreateRoundedSprite(96, 96, 26, new Color(0.004f, 0.05f, 0.15f, 0.62f)),
                sliced: true
            );

        background.raycastTarget = true;
        Stretch(background.rectTransform);

        bool selected = string.Equals(itemId ?? string.Empty, pendingItemId ?? string.Empty);

        if (icon != null)
        {
            Image picture = CreateImage("Picture", cellRect, icon);
            picture.preserveAspect = true;

            float iconHeight = CellSize - CellIconPad * 2f;
            float iconWidth = iconHeight * IconAspect;

            PlaceInRect(
                picture.rectTransform,
                CellSize,
                CellSize,
                (CellSize - iconWidth) * 0.5f,
                CellIconPad,
                iconWidth,
                iconHeight
            );
        }
        else
        {
            RectTransform labelRect = CreateRect("Label", cellRect);
            PlaceInRect(labelRect, CellSize, CellSize, 14f, CellSize * 0.5f - 20f, CellSize - 28f, 40f);

            CreateLabel(label, labelRect, TextAlignmentOptions.Center, Color.white);
        }

        if (selected)
        {
            // Seçili hücre, oyunun genel neon diline uygun parlak bir
            // çerçeveyle işaretlenir. İçeriğin üstünde çizilsin diye
            // en son ekleniyor.
            Image ring =
                CreateImage(
                    "SelectedRing",
                    cellRect,
                    CreateRoundedOutlineSprite(96, 96, 26, new Color(0.42f, 0.86f, 1f, 1f), 5f),
                    sliced: true
                );

            Stretch(ring.rectTransform);
        }

        Button button = cellRect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(UISoundManager.PlayClick);
        button.onClick.AddListener(() => SelectItem(itemId));
    }

    private void SelectItem(string itemId)
    {
        if (string.Equals(itemId ?? string.Empty, pendingItemId ?? string.Empty))
            return;

        pendingItemId = itemId;

        ApplyPreview(pendingItemId);
        RefreshCells();
    }

    private void BuildButtons(RectTransform parent)
    {
        float total = BackWidth + ButtonGap + ConfirmWidth;
        float startX = (ContentWidth - total) * 0.5f;

        RectTransform backRect = CreateRect("BackButton", parent);
        PlaceInContent(backRect, startX, ButtonTop, BackWidth, ButtonHeight);

        Image backImage = CreateImage("Icon", backRect, Resources.Load<Sprite>("UI/BackButton"));
        backImage.raycastTarget = true;
        Stretch(backImage.rectTransform);

        Button backButton = backRect.gameObject.AddComponent<Button>();
        backButton.targetGraphic = backImage;
        backButton.onClick.AddListener(UISoundManager.PlayClick);
        backButton.onClick.AddListener(Hide);

        // Butonun sol tarafındaki ok görselin içinde baskılı — yazı
        // okun sağında kalan düz alana ortalanır.
        RectTransform backLabel = CreateRect("Label", backRect);
        backLabel.anchorMin = new Vector2(0.33f, 0.28f);
        backLabel.anchorMax = new Vector2(0.93f, 0.72f);
        backLabel.offsetMin = Vector2.zero;
        backLabel.offsetMax = Vector2.zero;

        CreateLabel(Localization.Get("back"), backLabel, TextAlignmentOptions.Center, Color.white);

        RectTransform confirmRect = CreateRect("ConfirmButton", parent);
        PlaceInContent(confirmRect, startX + BackWidth + ButtonGap, ButtonTop, ConfirmWidth, ButtonHeight);

        // Yeşil tikli bar mağazadaki "sahipsin" barıyla aynı görsel —
        // onay butonu olarak da aynı dili konuşuyor.
        Image confirmImage = CreateImage("Icon", confirmRect, Resources.Load<Sprite>("UI/ShopOwned"));
        confirmImage.raycastTarget = true;
        Stretch(confirmImage.rectTransform);

        Button confirmButton = confirmRect.gameObject.AddComponent<Button>();
        confirmButton.targetGraphic = confirmImage;
        confirmButton.onClick.AddListener(UISoundManager.PlayClick);
        confirmButton.onClick.AddListener(Confirm);

        RectTransform confirmLabel = CreateRect("Label", confirmRect);
        confirmLabel.anchorMin = new Vector2(0.34f, 0.24f);
        confirmLabel.anchorMax = new Vector2(0.93f, 0.76f);
        confirmLabel.offsetMin = Vector2.zero;
        confirmLabel.offsetMax = Vector2.zero;

        CreateLabel(Localization.Get("confirm"), confirmLabel, TextAlignmentOptions.Center, Color.white);
    }

    private void BuildCloseButton(RectTransform parent)
    {
        RectTransform buttonRect = CreateRect("CloseButton", parent);
        PlaceInDesign(buttonRect, 656f, 292f, 84f, 81f);

        Image image = CreateImage("Icon", buttonRect, Resources.Load<Sprite>("UI/CloseButton"));
        image.raycastTarget = true;
        Stretch(image.rectTransform);

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(UISoundManager.PlayClick);
        button.onClick.AddListener(Hide);
    }

    private void Confirm()
    {
        CosmeticLoadout.SetEquipped(CosmeticSlot.Head, pendingItemId);

        Hide();
    }

    // =========================================
    // YARDIMCI (UI KURULUM) METOTLARI
    // =========================================

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// Tasarım uzayındaki (soldan/üstten piksel) bir dikdörtgeni,
    /// ebeveyni designWidth x designHeight kabul ederek anchor'a çevirir.
    private static void PlaceInRect(
        RectTransform rect,
        float designWidth,
        float designHeight,
        float x,
        float y,
        float width,
        float height)
    {
        rect.anchorMin = new Vector2(x / designWidth, 1f - (y + height) / designHeight);
        rect.anchorMax = new Vector2((x + width) / designWidth, 1f - y / designHeight);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void PlaceInDesign(RectTransform rect, float x, float y, float width, float height)
    {
        PlaceInRect(rect, DesignWidth, DesignHeight, x, y, width, height);
    }

    private static void PlaceInContent(RectTransform rect, float x, float y, float width, float height)
    {
        PlaceInRect(rect, ContentWidth, ContentHeight, x, y, width, height);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image CreateImage(
        string name,
        Transform parent,
        Sprite sprite,
        bool sliced = false)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        Image image = obj.GetComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.raycastTarget = false;

        // Hazır görsellerin 9-slice kenarlığı yok; Sliced yalnızca kod
        // içinde üretilen yuvarlak köşeli sprite'lar için.
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;

        return image;
    }

    /// Verilen rect'i dolduran, otomatik boyutlanan bir yazı üretir
    /// (panel ölçeklenince yazı da ölçeklensin diye sabit punto yok) ve
    /// okunurluk için arkasına koyu bir gölge kopyası yerleştirir.
    private static TMP_Text CreateLabel(
        string text,
        RectTransform parent,
        TextAlignmentOptions alignment,
        Color color)
    {
        TMP_Text label = CreateText(text, parent, alignment, color);

        // Gölge artık ayrı bir nesne değil, materyalin underlay
        // özelliği — ekrandaki yazı nesnesi (ve draw call) yarıya iner.
        if (!UITextStyle.ApplyShadow(label))
        {
            // Shader underlay desteklemiyorsa eski yönteme dön.
            TMP_Text shadow = CreateText(text, parent, alignment, UITextStyle.ShadowColor);
            shadow.rectTransform.anchorMin = new Vector2(0f, -0.06f);
            shadow.rectTransform.anchorMax = new Vector2(1f, 0.94f);
            shadow.rectTransform.offsetMin = Vector2.zero;
            shadow.rectTransform.offsetMax = Vector2.zero;
            shadow.rectTransform.SetAsFirstSibling();
        }

        return label;
    }

    private static TMP_Text CreateText(
        string text,
        Transform parent,
        TextAlignmentOptions alignment,
        Color color)
    {
        GameObject obj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Stretch(rect);

        TMP_Text tmp = obj.GetComponent<TMP_Text>();
        tmp.text = text;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 6f;
        tmp.fontSizeMax = 220f;

        return tmp;
    }

    // Yuvarlak köşeli sprite'lar UISpriteCache'te önbellekleniyor —
    // aynı parametre her çağrıda yeni doku üretip sızdırıyordu.
    private static Sprite CreateRoundedSprite(int width, int height, int radius, Color color)
    {
        return UISpriteCache.Rounded(width, height, radius, color);
    }

    private static Sprite CreateRoundedOutlineSprite(
        int width,
        int height,
        int radius,
        Color color,
        float thickness)
    {
        return UISpriteCache.RoundedOutline(width, height, radius, color, thickness);
    }
}
