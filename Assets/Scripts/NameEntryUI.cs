using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Play with a Friend" sonrası açılan, iki oyuncunun ismini
/// sorduğumuz ara ekran. İsimler onaylanınca callback ile
/// MainMenuUI'a bildirilir, o da TurnManager'a aktarır; iptal
/// rozetiyle isim girmeden ana menüye dönülebilir.
///
/// YERLEŞİM: SettingsUI / DifficultySelectUI ile aynı mantık — bütün
/// ölçüler aşağıdaki sabitlerde tanımlı 512x931'lik "tasarım uzayı"
/// pikselleridir ve sprite'ların gerçek piksel ölçümlerinden gelir.
/// Panel ekrana AspectRatioFitter ile oturur, çocuklar anchor tabanlı
/// yerleştiği için oranlar her çözünürlükte birebir korunur.
///
/// İsim kutularının çerçevesi, avatar ikonu ve içteki koyu yazı alanı
/// görselin kendisinde baskılı; TMP_InputField'ın yazı alanı tam o
/// içteki kutuya (aşağıdaki Inset* oranları) oturtuluyor.
/// </summary>
public class NameEntryUI : MonoBehaviour
{
    // --- Tasarım uzayı (soldan/üstten piksel) ---
    private const float DesignWidth = 512f;
    private const float DesignHeight = 931f;

    // --- Panelin neon çerçeve içi (içerik) alanı ---
    private const float ContentX = 16f;
    private const float ContentTop = 283f;
    private const float ContentWidth = 480f;
    private const float ContentHeight = 617f;

    // --- İçerik ızgarası (ContentWidth x ContentHeight uzayında) ---
    private const float FieldAspect = 763f / 180f;
    private const float FieldWidth = 460f;
    private const float FieldHeight = FieldWidth / FieldAspect;
    private const float FieldX = (ContentWidth - FieldWidth) * 0.5f;
    private const float Field1Top = 73f;
    private const float Field2Top = 255f;

    private const float StartAspect = 521f / 174f;
    private const float StartWidth = 320f;
    private const float StartHeight = StartWidth / StartAspect;
    private const float StartX = (ContentWidth - StartWidth) * 0.5f;
    private const float StartTop = 437f;

    /// İsim kutusu görselinin içindeki koyu yazı alanının, kutunun
    /// kendi ölçüsüne oranla sınırları (soldan/üstten).
    private const float InsetLeft = 0.28f;
    private const float InsetRight = 0.875f;
    private const float InsetTop = 0.345f;
    private const float InsetBottom = 0.715f;

    private const float NameFontSize = 40f;

    private GameObject root;
    private TMP_InputField player1Field;
    private TMP_InputField player2Field;
    private Action<string, string> onConfirmed;

    public void Show(Action<string, string> confirmedCallback)
    {
        onConfirmed = confirmedCallback;

        EnsureBuilt();

        player1Field.text = string.Empty;
        player2Field.text = string.Empty;

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
            Debug.LogError("NameEntryUI: Canvas bulunamadı!");
            return;
        }

        RectTransform rootRect = CreateRect("NameEntry", canvas.transform);
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

        // Bu ekranda bant ve gövde tek bir görselde birlikte geliyor.
        Image panel = CreateImage("Body", designRect, Resources.Load<Sprite>("UI/NamePanel"));
        PlaceInDesign(panel.rectTransform, 0f, 0f, DesignWidth, DesignHeight);

        RectTransform titleRect = CreateRect("Title", designRect);
        PlaceInDesign(titleRect, 60f, 176f, 392f, 68f);

        CreateLabel(Localization.Get("team_names"), titleRect, TextAlignmentOptions.Center);

        RectTransform contentRect = CreateRect("BodyContent", designRect);
        PlaceInDesign(contentRect, ContentX, ContentTop, ContentWidth, ContentHeight);

        player1Field =
            CreateNameField(
                contentRect,
                "Player1Field",
                "UI/NameFieldP1",
                Field1Top,
                Localization.Get("player1_label")
            );

        player2Field =
            CreateNameField(
                contentRect,
                "Player2Field",
                "UI/NameFieldP2",
                Field2Top,
                Localization.Get("player2_label")
            );

        BuildStartButton(contentRect);
        BuildCancelButton(designRect);

        root.SetActive(false);
    }

    private TMP_InputField CreateNameField(
        RectTransform parent,
        string name,
        string spritePath,
        float top,
        string placeholderText)
    {
        RectTransform fieldRect = CreateRect(name, parent);
        PlaceInContent(fieldRect, FieldX, top, FieldWidth, FieldHeight);

        Image background = CreateImage("Frame", fieldRect, Resources.Load<Sprite>(spritePath));
        background.raycastTarget = true;
        Stretch(background.rectTransform);

        // Yazı alanı, görselin içindeki koyu kutunun tam üstüne
        // oturur; RectMask2D uzun isimlerin çerçeveye taşmasını keser.
        RectTransform viewport = CreateRect("TextArea", fieldRect);
        viewport.anchorMin = new Vector2(InsetLeft, 1f - InsetBottom);
        viewport.anchorMax = new Vector2(InsetRight, 1f - InsetTop);
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        viewport.gameObject.AddComponent<RectMask2D>();

        TMP_Text textComponent =
            CreateText("", viewport, TextAlignmentOptions.Left, Color.white);

        textComponent.fontStyle = FontStyles.Bold;
        textComponent.fontSize = NameFontSize;

        // Placeholder: kutunun içinde soluk şekilde duran, yazmaya
        // başlayınca otomatik kaybolan "hayalet" yazı — kullanıcı
        // önce mevcut bir metni silmek zorunda kalmasın diye.
        TMP_Text placeholder =
            CreateText(
                placeholderText,
                viewport,
                TextAlignmentOptions.Left,
                new Color(1f, 1f, 1f, 0.45f)
            );

        placeholder.fontStyle = FontStyles.Bold;
        placeholder.fontSize = NameFontSize;

        TMP_InputField inputField =
            fieldRect.gameObject.AddComponent<TMP_InputField>();

        inputField.targetGraphic = background;
        inputField.textViewport = viewport;
        inputField.textComponent = textComponent;
        inputField.placeholder = placeholder;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.characterLimit = 16;
        inputField.customCaretColor = true;
        inputField.caretColor = Color.white;
        inputField.caretWidth = 3;
        inputField.selectionColor = new Color(0.24f, 0.67f, 1f, 0.55f);

        return inputField;
    }

    private void BuildStartButton(RectTransform parent)
    {
        RectTransform buttonRect = CreateRect("StartButton", parent);
        PlaceInContent(buttonRect, StartX, StartTop, StartWidth, StartHeight);

        // Görselin ortasında zaten büyük bir "play" üçgeni baskılı,
        // üstüne yazı koyacak boşluk yok — buton görseliyle konuşuyor.
        Image image = CreateImage("Icon", buttonRect, Resources.Load<Sprite>("UI/StartButton"));
        image.raycastTarget = true;
        Stretch(image.rectTransform);

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(UISoundManager.PlayClick);
        button.onClick.AddListener(Confirm);
    }

    private void BuildCancelButton(RectTransform parent)
    {
        RectTransform buttonRect = CreateRect("CancelButton", parent);
        PlaceInDesign(buttonRect, 430f, 215f, 82f, 79f);

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
        string name1 =
            string.IsNullOrWhiteSpace(player1Field.text)
                ? Localization.Get("player1_label")
                : player1Field.text.Trim();

        string name2 =
            string.IsNullOrWhiteSpace(player2Field.text)
                ? Localization.Get("player2_label")
                : player2Field.text.Trim();

        Hide();

        onConfirmed?.Invoke(name1, name2);
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

            shadow.enableAutoSizing = true;
            shadow.fontSizeMin = 6f;
            shadow.fontSizeMax = 220f;
            shadow.rectTransform.anchorMin = new Vector2(0f, -0.06f);
            shadow.rectTransform.anchorMax = new Vector2(1f, 0.94f);
            shadow.rectTransform.offsetMin = Vector2.zero;
            shadow.rectTransform.offsetMax = Vector2.zero;
            shadow.rectTransform.SetAsFirstSibling();
        }

        label.enableAutoSizing = true;
        label.fontSizeMin = 6f;
        label.fontSizeMax = 220f;

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

        return tmp;
    }

    // Yuvarlak köşeli sprite'lar UISpriteCache'te önbellekleniyor —
    // aynı parametre her çağrıda yeni doku üretip sızdırıyordu.
    private static Sprite CreateRoundedSprite(int width, int height, int radius, Color color)
    {
        return UISpriteCache.Rounded(width, height, radius, color);
    }
}
