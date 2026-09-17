using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "PLAY" (tek oyunculu, AI'a karşı) sonrası açılan zorluk seçim
/// ekranı. Seçim yapılınca callback ile MainMenuUI'a bildirilir;
/// GERİ ile seçim yapılmadan kapatılabilir.
///
/// YERLEŞİM: SettingsUI ile aynı mantık — bütün ölçüler aşağıdaki
/// sabitlerde tanımlı 734x1206'lık "tasarım uzayı" pikselleridir ve
/// sprite'ların gerçek piksel ölçümlerinden çıkarılmıştır. Panel ekrana
/// AspectRatioFitter ile oturur, çocuklar anchor tabanlı yerleştiği için
/// oranlar her çözünürlükte birebir korunur.
///
/// Zorluk butonlarının üzerinde yazı yok: görselin kendisinde taş +
/// yıldız (1/2/3) + renkli ok baskılı ve araya yazı sığacak boşluk
/// bırakmıyor — seviye yıldız sayısıyla anlatılıyor.
/// </summary>
public class DifficultySelectUI : MonoBehaviour
{
    // --- Tasarım uzayı (soldan/üstten piksel) ---
    private const float HeaderWidth = 734f;
    private const float HeaderHeight = 331f;
    private const float PanelWidth = 671f;
    private const float BodyHeight = 895f;

    /// Başlık bandı panelden geniş; panel yatayda ortalanır.
    private const float PanelX = (HeaderWidth - PanelWidth) * 0.5f;

    /// Bandın alt kenarı gövdenin üst neon çizgisine otursun diye
    /// gövde bu kadar aşağıdan başlar.
    private const float BodyTop = 311f;

    private const float DesignWidth = HeaderWidth;
    private const float DesignHeight = BodyTop + BodyHeight;

    // --- Gövdenin neon çerçeve içi (içerik) alanı ---
    private const float ContentX = PanelX + 20f;
    private const float ContentTop = BodyTop + 26f;
    private const float ContentWidth = 630f;
    private const float ContentHeight = 848f;

    // --- İçerik ızgarası (ContentWidth x ContentHeight uzayında) ---
    private const float ButtonWidth = 519f;
    private const float ButtonHeight = 190f;
    private const float ButtonX = (ContentWidth - ButtonWidth) * 0.5f;
    private const float ButtonGap = 28f;
    private const float FirstButtonTop = 40f;

    private const float BackWidth = 278.5f;
    private const float BackHeight = 100f;
    private const float BackX = (ContentWidth - BackWidth) * 0.5f;
    private const float BackTop = 716f;

    private GameObject root;
    private Action<AIOpponent.AIDifficulty> onConfirmed;

    public void Show(Action<AIOpponent.AIDifficulty> confirmedCallback)
    {
        onConfirmed = confirmedCallback;

        EnsureBuilt();

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
            Debug.LogError("DifficultySelectUI: Canvas bulunamadı!");
            return;
        }

        RectTransform rootRect = CreateRect("DifficultySelect", canvas.transform);
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

        // Header gövdeden sonra: band, gövdenin üst çizgisinin önünde kalsın.
        Image header = CreateImage("Header", designRect, Resources.Load<Sprite>("UI/PanelHeader"));
        PlaceInDesign(header.rectTransform, 0f, 0f, HeaderWidth, HeaderHeight);

        RectTransform titleRect = CreateRect("Title", designRect);
        PlaceInDesign(titleRect, 55f, 226f, 624f, 82f);

        CreateLabel(
            Localization.Get("select_difficulty"),
            titleRect,
            TextAlignmentOptions.Center
        );

        RectTransform contentRect = CreateRect("BodyContent", designRect);
        PlaceInDesign(contentRect, ContentX, ContentTop, ContentWidth, ContentHeight);

        CreateDifficultyButton(contentRect, 0, "UI/DifficultyEasy", AIOpponent.AIDifficulty.Easy);
        CreateDifficultyButton(contentRect, 1, "UI/DifficultyMedium", AIOpponent.AIDifficulty.Medium);
        CreateDifficultyButton(contentRect, 2, "UI/DifficultyHard", AIOpponent.AIDifficulty.Hard);

        BuildBackButton(contentRect);

        root.SetActive(false);
    }

    private void CreateDifficultyButton(
        RectTransform parent,
        int row,
        string spritePath,
        AIOpponent.AIDifficulty difficulty)
    {
        RectTransform buttonRect = CreateRect(difficulty + "Button", parent);

        PlaceInContent(
            buttonRect,
            ButtonX,
            FirstButtonTop + row * (ButtonHeight + ButtonGap),
            ButtonWidth,
            ButtonHeight
        );

        Image image = CreateImage("Icon", buttonRect, Resources.Load<Sprite>(spritePath));
        image.raycastTarget = true;
        Stretch(image.rectTransform);

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(UISoundManager.PlayClick);
        button.onClick.AddListener(() => Confirm(difficulty));
    }

    private void BuildBackButton(RectTransform parent)
    {
        RectTransform buttonRect = CreateRect("BackButton", parent);
        PlaceInContent(buttonRect, BackX, BackTop, BackWidth, BackHeight);

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

        CreateLabel(Localization.Get("back"), textRect, TextAlignmentOptions.Center);
    }

    private void Confirm(AIOpponent.AIDifficulty difficulty)
    {
        Hide();

        onConfirmed?.Invoke(difficulty);
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
        GameObject obj =
            new GameObject(name, typeof(RectTransform), typeof(Image));

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
        TextAlignmentOptions alignment)
    {
        TMP_Text label = CreateText(text, parent, alignment, Color.white);

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
        GameObject obj =
            new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(TextMeshProUGUI)
            );

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
