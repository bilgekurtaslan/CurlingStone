using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Elmas yeterli olmadığında (Shop'tan) ya da ana menüdeki Gold/
/// Diamond panellerine basılınca açılan mağaza. Elmas gerçek parayla
/// satılır (150 elmas = 500 TL çapasına göre hesaplandı) — gerçek
/// ödeme sistemi henüz bağlanmadığı için basınca "Yakında" gösterir.
/// Gold ise parayla satılmaz; elmas karşılığında (500 gold = 50
/// elmas oranında) gerçekten değiştirilir — bu tamamen oyun içi bir
/// işlem olduğu için anında çalışır.
///
/// YERLEŞİM: Diğer ekranlarla aynı mantık — bütün ölçüler aşağıdaki
/// sabitlerde tanımlı 734x1206'lık "tasarım uzayı" pikselleridir.
/// Panel ekrana AspectRatioFitter ile oturur, çocuklar anchor tabanlı
/// yerleştiği için oranlar her çözünürlükte birebir korunur.
/// </summary>
public class PayUI : MonoBehaviour
{
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
    private const float BarAspect = 1000f / 312f;
    private const float BalanceWidth = 270f;
    private const float BalanceHeight = BalanceWidth / BarAspect;
    private const float BalanceTop = 4f;

    private const float SectionHeight = 30f;
    private const float DiamondSectionTop = 104f;
    private const float GoldSectionTop = 400f;

    private const float RowX = 30f;
    private const float RowWidth = 570f;
    private const float RowHeight = 74f;
    private const float RowPitch = 84f;
    private const float DiamondFirstRowTop = 142f;
    private const float GoldFirstRowTop = 438f;

    private const float BackWidth = 278.5f;
    private const float BackHeight = 100f;
    private const float BackTop = 700f;

    /// Bar görsellerinde para ikonu solda baskılı; yazı sağdaki düz alana oturur.
    private const float BarTextLeft = 0.34f;
    private const float BarTextRight = 0.93f;
    private const float BarTextTop = 0.24f;
    private const float BarTextBottom = 0.76f;

    private static readonly Color DiamondRowFill = new Color(0.02f, 0.102f, 0.275f, 0.88f);
    private static readonly Color DiamondRowEdge = new Color(0.29f, 0.659f, 1f, 1f);
    private static readonly Color GoldRowFill = new Color(0.361f, 0.251f, 0.031f, 0.88f);
    private static readonly Color GoldRowEdge = new Color(0.925f, 0.722f, 0.227f, 1f);
    private static readonly Color DebugButtonColor = new Color(0.298f, 0.173f, 0.408f, 0.8f);

    private GameObject root;
    private GameObject toastRoot;
    private TMP_Text toastText;
    private CanvasGroup toastGroup;
    private Coroutine toastRoutine;

    private TMP_Text goldLabel;
    private TMP_Text goldLabelShadow;
    private TMP_Text diamondLabel;
    private TMP_Text diamondLabelShadow;

    public void Show()
    {
        EnsureBuilt();
        RefreshBalance();

        // Araya sonradan kurulan başka bir ekran girmiş olabilir —
        // panel her açılışta canvas'ın en üstüne alınır.
        root.transform.SetAsLastSibling();
        root.SetActive(true);
    }

    public void Hide()
    {
        if (root != null)
        {
            root.SetActive(false);
        }
    }

    private void EnsureBuilt()
    {
        if (root != null)
            return;

        Canvas canvas = FindAnyObjectByType<Canvas>();

        if (canvas == null)
        {
            Debug.LogError("PayUI: Canvas bulunamadı!");
            return;
        }

        RectTransform rootRect = CreateRect("PayUI", canvas.transform);
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

        CreateLabel(
            Localization.Get("currency_store_title"),
            titleRect,
            TextAlignmentOptions.Center,
            Color.white
        );

        RectTransform contentRect = CreateRect("BodyContent", designRect);
        PlaceInDesign(contentRect, ContentX, ContentTop, ContentWidth, ContentHeight);

        BuildBalanceRow(contentRect);
        BuildDebugAddCurrencyButton(contentRect);

        // 150 elmas = 500 TL çapasına göre: küçük paket daha pahalı
        // birim fiyatlı, büyük paket daha ucuz (standart mobil oyun
        // ekonomisi eğrisi). Gerçek para — henüz bağlanmadı.
        CreateSectionHeader(contentRect, "diamond_unit", DiamondSectionTop);
        CreateDiamondRow(contentRect, 0, 50, "250 TL");
        CreateDiamondRow(contentRect, 1, 150, "500 TL");
        CreateDiamondRow(contentRect, 2, 500, "1500 TL");

        // Gold parayla satılmaz — 500 gold = 50 elmas oranında elmas
        // karşılığında değiştirilir. Tamamen oyun içi, gerçekten çalışır.
        CreateSectionHeader(contentRect, "gold_unit", GoldSectionTop);
        CreateExchangeRow(contentRect, 0, 500, 50);
        CreateExchangeRow(contentRect, 1, 1500, 140);
        CreateExchangeRow(contentRect, 2, 5000, 450);

        BuildBackButton(contentRect);
        BuildCloseButton(designRect);
        BuildToast(canvas.transform);

        root.SetActive(false);
    }

    /// <summary>Alışveriş sırasında cüzdan görünsün diye üstte iki küçük bar.</summary>
    private void BuildBalanceRow(RectTransform parent)
    {
        goldLabel =
            BuildBar(
                parent,
                "GoldBalance",
                "UI/ShopPriceGold",
                RowX,
                BalanceTop,
                out goldLabelShadow
            );

        diamondLabel =
            BuildBar(
                parent,
                "DiamondBalance",
                "UI/ShopPriceDiamond",
                ContentWidth - RowX - BalanceWidth,
                BalanceTop,
                out diamondLabelShadow
            );
    }

    private TMP_Text BuildBar(
        RectTransform parent,
        string name,
        string spritePath,
        float x,
        float y,
        out TMP_Text shadow)
    {
        RectTransform barRect = CreateRect(name, parent);
        PlaceInContent(barRect, x, y, BalanceWidth, BalanceHeight);

        Image image = CreateImage("Bar", barRect, Resources.Load<Sprite>(spritePath));
        Stretch(image.rectTransform);

        RectTransform textRect = CreateRect("Value", barRect);
        textRect.anchorMin = new Vector2(BarTextLeft, 1f - BarTextBottom);
        textRect.anchorMax = new Vector2(BarTextRight, 1f - BarTextTop);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return CreateLabel(string.Empty, textRect, TextAlignmentOptions.Center, Color.white, out shadow);
    }

    private void RefreshBalance()
    {
        SetLabel(goldLabel, goldLabelShadow, Currency.Gold.ToString("N0"));
        SetLabel(diamondLabel, diamondLabelShadow, Currency.Diamonds.ToString("N0"));
    }

    private static void SetLabel(TMP_Text label, TMP_Text shadow, string text)
    {
        if (label != null)
        {
            label.text = text;
        }

        if (shadow != null)
        {
            shadow.text = text;
        }
    }

    private void ShowComingSoon()
    {
        ShowToast(Localization.Get("coming_soon"));
    }

    /// <summary>
    /// Ana menüdeki Gold/Diamond panelleri, menü kapanmadan (bu
    /// ekran üstüne bindiği için) güncellenmiyor — bakiye her
    /// değiştiğinde elle tazelenmesi gerekir.
    /// </summary>
    private static void RefreshMainMenuCurrency()
    {
        MainMenuUI mainMenu = FindAnyObjectByType<MainMenuUI>();

        if (mainMenu != null)
        {
            mainMenu.RefreshCurrencyDisplay();
        }
    }

    /// <summary>Gerçek parayla satılan elmas paketi — ödeme bağlanana kadar "Yakında".</summary>
    private void CreateDiamondRow(RectTransform parent, int index, int diamondAmount, string mockPrice)
    {
        CreatePackageRow(
            parent,
            DiamondFirstRowTop + index * RowPitch,
            DiamondRowFill,
            DiamondRowEdge,
            diamondAmount + " " + Localization.Get("diamond_unit"),
            mockPrice,
            ShowComingSoon
        );
    }

    /// <summary>Gold'u gerçekten elmasla değiştirir (gerçek para değil, oyun içi işlem).</summary>
    private void CreateExchangeRow(RectTransform parent, int index, int goldAmount, int diamondCost)
    {
        CreatePackageRow(
            parent,
            GoldFirstRowTop + index * RowPitch,
            GoldRowFill,
            GoldRowEdge,
            goldAmount + " " + Localization.Get("gold_unit"),
            diamondCost + " " + Localization.Get("diamond_unit"),
            () =>
            {
                if (Currency.TrySpendDiamonds(diamondCost))
                {
                    Currency.AddGold(goldAmount);
                    ShowToast("+" + goldAmount);
                    RefreshBalance();
                    RefreshMainMenuCurrency();
                }
                else
                {
                    ShowToast(Localization.Get("not_enough_diamonds"));
                }
            }
        );
    }

    private void CreatePackageRow(
        RectTransform parent,
        float top,
        Color fill,
        Color edge,
        string amountLabel,
        string priceLabel,
        System.Action onPurchase)
    {
        RectTransform rowRect = CreateRect(amountLabel + "Package", parent);
        PlaceInContent(rowRect, RowX, top, RowWidth, RowHeight);

        Image background =
            CreateImage("Bg", rowRect, CreateRoundedSprite(96, 96, 26, fill), sliced: true);

        background.raycastTarget = true;
        Stretch(background.rectTransform);

        // Satırlar oyunun neon diline uysun diye ince parlak bir çerçeve.
        Image ring =
            CreateImage(
                "Edge",
                rowRect,
                CreateRoundedOutlineSprite(96, 96, 26, edge, 4f),
                sliced: true
            );

        Stretch(ring.rectTransform);

        RectTransform amountRect = CreateRect("Amount", rowRect);
        PlaceInRect(amountRect, RowWidth, RowHeight, 26f, 20f, 300f, 34f);

        CreateLabel(amountLabel, amountRect, TextAlignmentOptions.Left, Color.white);

        RectTransform priceRect = CreateRect("Price", rowRect);
        PlaceInRect(priceRect, RowWidth, RowHeight, 330f, 20f, 214f, 34f);

        CreateLabel(priceLabel, priceRect, TextAlignmentOptions.Right, new Color(1f, 1f, 1f, 0.92f));

        Button button = rowRect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(UISoundManager.PlayClick);

        // Not: buraya ayrıca "Yakında" toast'u bağlanmıyor — gold
        // takası gerçekten çalışan bir işlem, onPurchase kendi
        // sonucunu gösteriyor. (Eskiden ikisi birden tetikleniyordu.)
        button.onClick.AddListener(() => onPurchase());
    }

    private void CreateSectionHeader(RectTransform parent, string localizationKey, float top)
    {
        RectTransform headerRect = CreateRect(localizationKey + "Header", parent);
        PlaceInContent(headerRect, 24f, top, 300f, SectionHeight);

        TMP_Text header =
            CreateLabel(
                Localization.Get(localizationKey),
                headerRect,
                TextAlignmentOptions.Left,
                new Color(1f, 1f, 1f, 0.7f)
            );

        header.characterSpacing = 2f;
    }

    /// <summary>Test/geliştirme amaçlı: bakiyeye anında bol miktarda gold ve elmas ekler.</summary>
    private void BuildDebugAddCurrencyButton(RectTransform parent)
    {
        RectTransform buttonRect = CreateRect("DebugAddCurrencyButton", parent);
        PlaceInContent(buttonRect, 500f, DiamondSectionTop, 110f, 34f);

        Image image =
            CreateImage("Bg", buttonRect, CreateRoundedSprite(64, 64, 18, DebugButtonColor), sliced: true);

        image.raycastTarget = true;
        Stretch(image.rectTransform);

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(UISoundManager.PlayClick);
        button.onClick.AddListener(() =>
        {
            Currency.AddGold(10000);
            Currency.AddDiamonds(5000);
            ShowToast("+10000 / +5000");
            RefreshBalance();
            RefreshMainMenuCurrency();
        });

        RectTransform textRect = CreateRect("Label", buttonRect);
        textRect.anchorMin = new Vector2(0.1f, 0.22f);
        textRect.anchorMax = new Vector2(0.9f, 0.78f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        CreateLabel("TEST +", textRect, TextAlignmentOptions.Center, new Color(0.886f, 0.804f, 1f, 1f));
    }

    private void BuildBackButton(RectTransform parent)
    {
        RectTransform buttonRect = CreateRect("BackButton", parent);
        PlaceInContent(buttonRect, (ContentWidth - BackWidth) * 0.5f, BackTop, BackWidth, BackHeight);

        Image image = CreateImage("Icon", buttonRect, Resources.Load<Sprite>("UI/BackButton"));
        image.raycastTarget = true;
        Stretch(image.rectTransform);

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(UISoundManager.PlayClick);
        button.onClick.AddListener(Hide);

        // Butonun sol tarafındaki ok görselin içinde baskılı — yazı
        // okun sağında kalan düz alana ortalanır.
        RectTransform textRect = CreateRect("Label", buttonRect);
        textRect.anchorMin = new Vector2(0.33f, 0.28f);
        textRect.anchorMax = new Vector2(0.93f, 0.72f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        CreateLabel(Localization.Get("back"), textRect, TextAlignmentOptions.Center, Color.white);
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

    // =========================================
    // TOAST
    // =========================================

    private void BuildToast(Transform canvasTransform)
    {
        RectTransform toastRect = CreateRect("PayToast", canvasTransform);
        toastRoot = toastRect.gameObject;

        toastRect.anchorMin = new Vector2(0.5f, 0.12f);
        toastRect.anchorMax = new Vector2(0.5f, 0.12f);
        toastRect.pivot = new Vector2(0.5f, 0.5f);
        toastRect.sizeDelta = new Vector2(420f, 96f);
        toastRect.anchoredPosition = Vector2.zero;

        toastGroup = toastRoot.AddComponent<CanvasGroup>();
        toastGroup.alpha = 0f;
        toastGroup.blocksRaycasts = false;
        toastGroup.interactable = false;

        Image panel =
            CreateImage(
                "Panel",
                toastRect,
                CreateRoundedSprite(96, 96, 30, new Color(0.008f, 0.031f, 0.078f, 0.94f)),
                sliced: true
            );

        Stretch(panel.rectTransform);

        Image ring =
            CreateImage(
                "Edge",
                toastRect,
                CreateRoundedOutlineSprite(96, 96, 30, new Color(0.29f, 0.659f, 1f, 0.8f), 4f),
                sliced: true
            );

        Stretch(ring.rectTransform);

        RectTransform textRect = CreateRect("Label", toastRect);
        textRect.anchorMin = new Vector2(0.08f, 0.2f);
        textRect.anchorMax = new Vector2(0.92f, 0.8f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        toastText = CreateLabel("", textRect, TextAlignmentOptions.Center, new Color(1f, 0.85f, 0.35f));
    }

    private void ShowToast(string message)
    {
        if (toastText == null)
            return;

        toastText.text = message;

        if (toastRoutine != null)
        {
            StopCoroutine(toastRoutine);
        }

        toastRoutine = StartCoroutine(ToastRoutine());
    }

    private IEnumerator ToastRoutine()
    {
        toastRoot.transform.SetAsLastSibling();

        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime * 6f;
            toastGroup.alpha = Mathf.Clamp01(t);
            yield return null;
        }

        yield return new WaitForSeconds(1.1f);

        t = 1f;

        while (t > 0f)
        {
            t -= Time.deltaTime * 3f;
            toastGroup.alpha = Mathf.Clamp01(t);
            yield return null;
        }

        toastRoutine = null;
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

    private static TMP_Text CreateLabel(
        string text,
        RectTransform parent,
        TextAlignmentOptions alignment,
        Color color)
    {
        return CreateLabel(text, parent, alignment, color, out _);
    }

    /// Verilen rect'i dolduran, otomatik boyutlanan bir yazı üretir
    /// (panel ölçeklenince yazı da ölçeklensin diye sabit punto yok) ve
    /// okunurluk için arkasına koyu bir gölge kopyası yerleştirir.
    private static TMP_Text CreateLabel(
        string text,
        RectTransform parent,
        TextAlignmentOptions alignment,
        Color color,
        out TMP_Text shadow)
    {
        TMP_Text label = CreateText(text, parent, alignment, color);

        // Gölge artık ayrı bir nesne değil, materyalin underlay
        // özelliği — ekrandaki yazı nesnesi (ve draw call) yarıya iner.
        if (UITextStyle.ApplyShadow(label))
        {
            shadow = null;
            return label;
        }

        // Shader underlay desteklemiyorsa eski yönteme dön: arkaya
        // koyu bir kopya koyup onu asıl yazının altına al.
        shadow = CreateText(text, parent, alignment, UITextStyle.ShadowColor);
        shadow.rectTransform.anchorMin = new Vector2(0f, -0.06f);
        shadow.rectTransform.anchorMax = new Vector2(1f, 0.94f);
        shadow.rectTransform.offsetMin = Vector2.zero;
        shadow.rectTransform.offsetMax = Vector2.zero;
        shadow.rectTransform.SetAsFirstSibling();

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
