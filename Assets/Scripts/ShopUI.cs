using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ana menüden açılan mağaza: kataloğdaki her eşya için görsel + isim
/// + fiyat barı (kart). Gold ile fiyatlanan eşyalar yeterli bakiye
/// varsa anında açılır; Diamond ile fiyatlananlar için yeterli elmas
/// yoksa (ki gerçek ödeme henüz bağlanmadığı için genelde öyledir)
/// oyuncu Buy Diamonds ekranına yönlendirilir.
///
/// YERLEŞİM: Diğer ekranlarla aynı mantık — bütün ölçüler aşağıdaki
/// sabitlerde tanımlı 734x1206'lık "tasarım uzayı" pikselleridir ve
/// sprite'ların gerçek piksel ölçümlerinden gelir. Panel ekrana
/// AspectRatioFitter ile oturur; kaydırma listesi de dahil her şey
/// anchor tabanlı yerleştiği için oranlar her çözünürlükte korunur
/// (GridLayoutGroup piksel cinsinden çalıştığı ve panelle birlikte
/// ölçeklenmediği için kullanılmıyor, kartlar elle konumlanıyor).
/// </summary>
public class ShopUI : MonoBehaviour
{
    [SerializeField] private CosmeticCatalog catalog;
    [SerializeField] private PayUI payUI;

    private const int RemoveAdsPrice = 200;

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
    private const float IconAspect = 300f / 336f;

    private const float BalanceWidth = 270f;
    private const float BalanceHeight = BalanceWidth / BarAspect;
    private const float BalanceTop = 4f;

    private const float ViewTop = 100f;
    private const float ViewHeight = 600f;

    private const float CardWidth = 290f;
    private const float CardHeight = 330f;
    private const float CardGap = 22f;
    private const float ListPad = 14f;
    private const int Columns = 2;

    private const float CardIconHeight = 190f;
    private const float CardIconWidth = CardIconHeight * IconAspect;
    private const float CardNameTop = 204f;
    private const float CardNameHeight = 32f;
    private const float CardBarWidth = 254f;
    private const float CardBarHeight = CardBarWidth / BarAspect;
    private const float CardBarTop = 240f;

    /// Bar görsellerinde para ikonu solda baskılı; yazı sağdaki düz
    /// alana oturur (bar ölçüsüne oran olarak).
    private const float BarTextLeft = 0.34f;
    private const float BarTextRight = 0.93f;
    private const float BarTextTop = 0.24f;
    private const float BarTextBottom = 0.76f;

    private const float BackWidth = 278.5f;
    private const float BackHeight = 100f;
    private const float BackTop = 730f;

    private GameObject root;
    private RectTransform listRect;
    private TMP_Text goldLabel;
    private TMP_Text goldLabelShadow;
    private TMP_Text diamondLabel;
    private TMP_Text diamondLabelShadow;

    public void Show()
    {
        EnsureBuilt();
        RefreshRows();

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
            Debug.LogError("ShopUI: Canvas bulunamadı!");
            return;
        }

        RectTransform rootRect = CreateRect("ShopUI", canvas.transform);
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

        CreateLabel(Localization.Get("shop_title"), titleRect, TextAlignmentOptions.Center, Color.white);

        RectTransform contentRect = CreateRect("BodyContent", designRect);
        PlaceInDesign(contentRect, ContentX, ContentTop, ContentWidth, ContentHeight);

        BuildBalanceRow(contentRect);
        BuildScrollList(contentRect);
        BuildResetButton(contentRect);
        BuildBackButton(contentRect);
        BuildCloseButton(designRect);

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
                30f,
                BalanceTop,
                BalanceWidth,
                BalanceHeight,
                string.Empty,
                out goldLabelShadow
            );

        diamondLabel =
            BuildBar(
                parent,
                "DiamondBalance",
                "UI/ShopPriceDiamond",
                ContentWidth - 30f - BalanceWidth,
                BalanceTop,
                BalanceWidth,
                BalanceHeight,
                string.Empty,
                out diamondLabelShadow
            );
    }

    /// <summary>Para/sahiplik barı + üstündeki yazı. Kart butonları da bunu kullanır.</summary>
    private TMP_Text BuildBar(
        RectTransform parent,
        string name,
        string spritePath,
        float x,
        float y,
        float width,
        float height,
        string text,
        out TMP_Text shadow)
    {
        RectTransform barRect = CreateRect(name, parent);
        PlaceInRect(barRect, ContentWidth, ContentHeight, x, y, width, height);

        Image image = CreateImage("Bar", barRect, Resources.Load<Sprite>(spritePath));
        Stretch(image.rectTransform);

        RectTransform textRect = CreateRect("Value", barRect);
        textRect.anchorMin = new Vector2(BarTextLeft, 1f - BarTextBottom);
        textRect.anchorMax = new Vector2(BarTextRight, 1f - BarTextTop);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return CreateLabel(text, textRect, TextAlignmentOptions.Center, Color.white, out shadow);
    }

    private void BuildScrollList(RectTransform parent)
    {
        RectTransform viewport = CreateRect("Viewport", parent);
        PlaceInContent(viewport, 0f, ViewTop, ContentWidth, ViewHeight);

        // Kartların arasındaki boşluktan da sürüklenebilsin diye
        // görünmez ama raycast alan bir zemin.
        Image catcher = viewport.gameObject.AddComponent<Image>();
        catcher.color = Color.clear;
        catcher.raycastTarget = true;

        viewport.gameObject.AddComponent<RectMask2D>();

        listRect = CreateRect("List", viewport);
        listRect.anchorMin = new Vector2(0f, 0f);
        listRect.anchorMax = new Vector2(1f, 1f);
        listRect.offsetMin = Vector2.zero;
        listRect.offsetMax = Vector2.zero;

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = listRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
    }

    private void RefreshRows()
    {
        if (listRect == null)
            return;

        foreach (Transform child in listRect)
        {
            Destroy(child.gameObject);
        }

        List<CosmeticItem> items = new List<CosmeticItem>();

        if (catalog != null)
        {
            foreach (CosmeticItem item in catalog.Items)
            {
                items.Add(item);
            }
        }

        // Reklam kaldırma kozmetik değil, kataloğa dahil değil — ama
        // mağazada satıldığı için listenin sonuna kendi kartıyla eklenir.
        int cardCount = items.Count + 1;
        int rows = Mathf.CeilToInt(cardCount / (float)Columns);

        // Liste yüksekliği viewport'a oran olarak verilir; böylece
        // panel büyüyüp küçülünce kartlar da birlikte ölçeklenir.
        float listHeight = ListPad * 2f + rows * CardHeight + Mathf.Max(0, rows - 1) * CardGap;
        float heightFraction = listHeight / ViewHeight;

        listRect.anchorMin = new Vector2(0f, 1f - heightFraction);
        listRect.anchorMax = new Vector2(1f, 1f);
        listRect.offsetMin = Vector2.zero;
        listRect.offsetMax = Vector2.zero;

        for (int i = 0; i < items.Count; i++)
        {
            BuildItemCard(items[i], i, listHeight);
        }

        BuildRemoveAdsCard(items.Count, listHeight);

        RefreshBalance();
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

    /// <summary>Kart zemini + adı; fiyat/sahiplik barı çağırana bırakılır.</summary>
    private RectTransform BuildCard(int index, float listHeight, string name, Sprite icon, string label)
    {
        int column = index % Columns;
        int row = index / Columns;

        float x = ListPad + column * (CardWidth + CardGap);
        float y = ListPad + row * (CardHeight + CardGap);

        RectTransform cardRect = CreateRect(name, listRect);
        PlaceInRect(cardRect, ContentWidth, listHeight, x, y, CardWidth, CardHeight);

        Image background =
            CreateImage(
                "Bg",
                cardRect,
                CreateRoundedSprite(96, 96, 26, new Color(0.004f, 0.05f, 0.15f, 0.55f)),
                sliced: true
            );

        Stretch(background.rectTransform);

        if (icon != null)
        {
            Image picture = CreateImage("Picture", cardRect, icon);
            picture.preserveAspect = true;

            PlaceInRect(
                picture.rectTransform,
                CardWidth,
                CardHeight,
                (CardWidth - CardIconWidth) * 0.5f,
                10f,
                CardIconWidth,
                CardIconHeight
            );

            RectTransform nameRect = CreateRect("Name", cardRect);
            PlaceInRect(nameRect, CardWidth, CardHeight, 12f, CardNameTop, CardWidth - 24f, CardNameHeight);

            CreateLabel(label, nameRect, TextAlignmentOptions.Center, Color.white);
        }
        else
        {
            // Görseli olmayan eşya (reklam kaldırma) için isim, görselin
            // yerini kaplayacak şekilde ortada durur.
            RectTransform nameRect = CreateRect("Name", cardRect);
            PlaceInRect(nameRect, CardWidth, CardHeight, 20f, 60f, CardWidth - 40f, 110f);

            TMP_Text text = CreateLabel(label, nameRect, TextAlignmentOptions.Center, Color.white);
            text.textWrappingMode = TextWrappingModes.Normal;
        }

        return cardRect;
    }

    /// <summary>Kartın alt barı: satın alınabiliyorsa fiyat butonu, alınmışsa yeşil "sahipsin" barı.</summary>
    private void BuildCardBar(
        RectTransform cardRect,
        bool owned,
        CurrencyType currency,
        int price,
        UnityEngine.Events.UnityAction onClick)
    {
        RectTransform barRect = CreateRect("Bar", cardRect);

        PlaceInRect(
            barRect,
            CardWidth,
            CardHeight,
            (CardWidth - CardBarWidth) * 0.5f,
            CardBarTop,
            CardBarWidth,
            CardBarHeight
        );

        string spritePath =
            owned
                ? "UI/ShopOwned"
                : currency == CurrencyType.Gold
                    ? "UI/ShopPriceGold"
                    : "UI/ShopPriceDiamond";

        Image image = CreateImage("Bg", barRect, Resources.Load<Sprite>(spritePath));
        image.raycastTarget = !owned;
        Stretch(image.rectTransform);

        RectTransform textRect = CreateRect("Value", barRect);
        textRect.anchorMin = new Vector2(BarTextLeft, 1f - BarTextBottom);
        textRect.anchorMax = new Vector2(BarTextRight, 1f - BarTextTop);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        CreateLabel(
            owned ? Localization.Get("owned") : price.ToString(),
            textRect,
            TextAlignmentOptions.Center,
            Color.white
        );

        if (owned)
            return;

        Button button = barRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(UISoundManager.PlayClick);
        button.onClick.AddListener(onClick);
    }

    private void BuildItemCard(CosmeticItem item, int index, float listHeight)
    {
        RectTransform cardRect =
            BuildCard(index, listHeight, item.id + "Card", item.icon, item.displayName);

        BuildCardBar(
            cardRect,
            CosmeticLoadout.IsOwned(item.id),
            item.currency,
            item.price,
            () => TryUnlock(item)
        );
    }

    private void BuildRemoveAdsCard(int index, float listHeight)
    {
        RectTransform cardRect =
            BuildCard(index, listHeight, "RemoveAdsCard", null, Localization.Get("remove_ads"));

        BuildCardBar(
            cardRect,
            Currency.AdsRemoved,
            CurrencyType.Diamond,
            RemoveAdsPrice,
            TryRemoveAds
        );
    }

    private void TryRemoveAds()
    {
        if (Currency.TrySpendDiamonds(RemoveAdsPrice))
        {
            Currency.MarkAdsRemoved();
            RefreshRows();
            RefreshMainMenuCurrency();
        }
        else if (payUI != null)
        {
            payUI.Show();
        }
    }

    private void TryUnlock(CosmeticItem item)
    {
        bool purchased =
            item.currency == CurrencyType.Gold
                ? Currency.TrySpendGold(item.price)
                : Currency.TrySpendDiamonds(item.price);

        if (purchased)
        {
            CosmeticLoadout.Unlock(item.id);
            RefreshRows();
            RefreshMainMenuCurrency();
            return;
        }

        // Elmas yetmiyorsa (gerçek ödeme henüz bağlanmadığı için
        // genelde öyledir) oyuncuyu elmas satın alma ekranına yönlendir.
        if (item.currency == CurrencyType.Diamond && payUI != null)
        {
            payUI.Show();
        }
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

    /// <summary>Test/geliştirme amaçlı: tüm satın alınan eşyaları sıfırlar (bakiyeye dokunmaz).</summary>
    private void BuildResetButton(RectTransform parent)
    {
        RectTransform buttonRect = CreateRect("ResetButton", parent);
        PlaceInContent(buttonRect, 8f, BackTop + 26f, 118f, 48f);

        Image image =
            CreateImage(
                "Bg",
                buttonRect,
                CreateRoundedSprite(64, 64, 18, new Color(0.35f, 0.12f, 0.14f, 0.8f)),
                sliced: true
            );

        image.raycastTarget = true;
        Stretch(image.rectTransform);

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(UISoundManager.PlayClick);
        button.onClick.AddListener(() =>
        {
            CosmeticLoadout.ResetAll(catalog);
            RefreshRows();
        });

        RectTransform textRect = CreateRect("Label", buttonRect);
        textRect.anchorMin = new Vector2(0.12f, 0.24f);
        textRect.anchorMax = new Vector2(0.88f, 0.76f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        CreateLabel("RESET", textRect, TextAlignmentOptions.Center, new Color(1f, 0.82f, 0.82f, 1f));
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
}
