using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Ana menüden açılan Ayarlar paneli: dil seçimi (seçilince sahneyi
/// yeniden yükleyip her ekranı yeni dilde tazeler) ve müzik ses
/// seviyesi (MusicManager'a bağlı, kalıcı). Görsel olarak Resources/UI
/// altındaki header/panel/ikon/bayrak/radyo sprite'larını kullanır.
/// Her Show() çağrısında (yoksa) sıfırdan, kod içinde inşa edilir —
/// böylece sahnede elle bozulabilecek kalıcı bir kopyaya bağımlı değil.
///
/// YERLEŞİM: Tüm ölçüler aşağıdaki sabitlerde tanımlı "tasarım uzayı"
/// pikselleridir (panelin kendi 629x1243'lük koordinat sistemi). Panel
/// ekrana AspectRatioFitter ile oturur, çocuklar da anchor tabanlı
/// yerleştiği için oranlar her cihaz çözünürlüğünde birebir korunur.
/// Sayılar sprite'ların gerçek piksel ölçümlerinden çıkarıldı: PanelBody
/// ve Header aynı genişlikte (629), neon çerçevenin iç boşluğu 15..614.
/// </summary>
public class SettingsUI : MonoBehaviour
{
    // --- Panel (tasarım uzayı: soldan/üstten piksel) ---
    private const float PanelWidth = 629f;
    private const float HeaderHeight = 314f;
    private const float BodyHeight = 981f;

    /// Başlık bandının panel gövdesinin üstüne bindiği miktar — band
    /// alt kenarı tam olarak gövdenin üst neon çizgisine otursun diye.
    private const float HeaderOverlap = 52f;

    private const float PanelHeight = HeaderHeight + BodyHeight - HeaderOverlap;
    private const float BodyTop = HeaderHeight - HeaderOverlap;

    // --- Gövdenin neon çerçeve içi (içerik) alanı ---
    private const float ContentLeft = 15f;
    private const float ContentTopInset = 58f;
    private const float ContentWidth = 599f;
    private const float ContentHeight = 904f;

    // --- İçerik ızgarası (ContentWidth x ContentHeight uzayında) ---
    private const float Pad = 40f;
    private const float RowIconSize = 96f;
    private const float LabelHeight = 60f;

    private const float FlagCellWidth = 232f;
    private const float FlagCellHeight = 148f;
    private const float FlagColLeft = Pad;
    private const float FlagColRight = 327f;
    private const float FlagRowTop = 378f;
    private const float FlagRowBottom = 561f;
    private const float RadioSize = 58f;

    private const float BackWidth = 340f;
    private const float BackHeight = 122f;
    private const float BackTop = 748f;

    /// Slider tutamağının yarıçapı, slider genişliğine oran olarak —
    /// dolgu ve tutamak alanları bu kadar içeri alınır ki tutamak
    /// uçlarda dışarı taşmasın.
    private const float SliderHandleInset = 0.045f;

    private GameObject root;
    private TMP_Text musicValueLabel;
    private TMP_Text musicValueShadow;

    public void Show()
    {
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
            Debug.LogError("SettingsUI: Canvas bulunamadı!");
            return;
        }

        RectTransform rootRect = CreateRect("SettingsUIRuntime", canvas.transform);
        root = rootRect.gameObject;
        Stretch(rootRect);

        Image scrim =
            CreateImage(
                "Scrim",
                rootRect,
                CreateRoundedSprite(4, 4, 0, new Color(0.01f, 0.02f, 0.05f, 0.92f))
            );

        Stretch(scrim.rectTransform);
        scrim.raycastTarget = true;

        // Panel, ekranın %92x%90'lık alanına oranı bozulmadan oturur.
        RectTransform panelArea = CreateRect("PanelArea", rootRect);
        panelArea.anchorMin = new Vector2(0.04f, 0.05f);
        panelArea.anchorMax = new Vector2(0.96f, 0.95f);
        panelArea.offsetMin = Vector2.zero;
        panelArea.offsetMax = Vector2.zero;

        RectTransform panelRect = CreateRect("Panel", panelArea);
        Stretch(panelRect);

        AspectRatioFitter fitter = panelRect.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = PanelWidth / PanelHeight;

        Image body = CreateImage("Body", panelRect, Resources.Load<Sprite>("UI/PanelBody"));
        PlaceInPanel(body.rectTransform, 0f, BodyTop, PanelWidth, BodyHeight);

        // Header gövdeden sonra eklenir: band, gövdenin üst neon
        // çizgisinin ve "tab" çıkıntısının önünde kalsın.
        Image header = CreateImage("Header", panelRect, Resources.Load<Sprite>("UI/Header"));
        PlaceInPanel(header.rectTransform, 0f, 0f, PanelWidth, HeaderHeight);

        BuildTitle(panelRect);

        // Gövdenin çerçeve içi alanı — bütün içerik bunun içine,
        // ContentWidth x ContentHeight uzayında yerleşir.
        RectTransform contentRect = CreateRect("BodyContent", panelRect);
        PlaceInPanel(contentRect, ContentLeft, BodyTop + ContentTopInset, ContentWidth, ContentHeight);

        BuildMusicRow(contentRect);
        BuildDivider(contentRect);
        BuildLanguageGrid(contentRect);
        BuildBackButton(contentRect);

        // Kapatma rozeti en son: panelin sağ üst köşesine biner.
        BuildCloseButton(panelRect);

        root.SetActive(false);
    }

    private void BuildTitle(RectTransform panelRect)
    {
        // Başlık bandının düz iç yüzü (ölçüm: panel uzayında x 25..604,
        // y 193..299) — yazı tam ortasına, bandın kendi eğimine değmeden.
        RectTransform titleRect = CreateRect("Title", panelRect);
        PlaceInPanel(titleRect, 40f, 199f, 549f, 92f);

        CreateLabel(
            Localization.Get("settings_title"),
            titleRect,
            TextAlignmentOptions.Center,
            Color.white
        );
    }

    private void BuildCloseButton(RectTransform panelRect)
    {
        RectTransform buttonRect = CreateRect("CloseButton", panelRect);
        PlaceInPanel(buttonRect, 566f, 268f, 104f, 101f);

        Image image = CreateImage("Icon", buttonRect, Resources.Load<Sprite>("UI/CloseButton"));
        image.raycastTarget = true;
        Stretch(image.rectTransform);

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(UISoundManager.PlayClick);
        button.onClick.AddListener(Hide);
    }

    private void BuildMusicRow(RectTransform parent)
    {
        Image musicIcon = CreateImage("MusicIcon", parent, Resources.Load<Sprite>("UI/MusicIcon"));
        PlaceInContent(musicIcon.rectTransform, Pad, 26f, RowIconSize, RowIconSize);

        RectTransform labelRect = CreateRect("MusicLabel", parent);
        PlaceInContent(labelRect, 150f, 44f, 250f, LabelHeight);

        CreateLabel(
            Localization.Get("music_label"),
            labelRect,
            TextAlignmentOptions.Left,
            Color.white
        );

        RectTransform valueRect = CreateRect("MusicValue", parent);
        PlaceInContent(valueRect, 400f, 44f, ContentWidth - Pad - 400f, LabelHeight);

        musicValueLabel =
            CreateLabel(
                string.Empty,
                valueRect,
                TextAlignmentOptions.Right,
                new Color(0.59f, 0.84f, 1f, 1f),
                out musicValueShadow
            );

        BuildMusicSlider(parent);
    }

    private void BuildMusicSlider(RectTransform parent)
    {
        RectTransform sliderRect = CreateRect("MusicSlider", parent);
        PlaceInContent(sliderRect, 44f, 138f, 511f, 52f);

        // Slider'ın kendi üzerinde görünmez ama tıklanabilir bir alan:
        // barın ince şeridine değil, 511x52'lik tüm satıra basılabilsin.
        Image touchArea = sliderRect.gameObject.AddComponent<Image>();
        touchArea.color = Color.clear;
        touchArea.raycastTarget = true;

        // Bar, slider yüksekliğinin ortasında ince bir şerit.
        const float barHalf = 22f / 52f * 0.5f;
        float barMinY = 0.5f - barHalf;
        float barMaxY = 0.5f + barHalf;

        Image background =
            CreateImage(
                "Background",
                sliderRect,
                CreateRoundedSprite(48, 16, 8, new Color(0.03f, 0.09f, 0.22f, 1f)),
                sliced: true
            );

        background.rectTransform.anchorMin = new Vector2(0f, barMinY);
        background.rectTransform.anchorMax = new Vector2(1f, barMaxY);
        background.rectTransform.offsetMin = Vector2.zero;
        background.rectTransform.offsetMax = Vector2.zero;

        RectTransform fillArea = CreateRect("FillArea", sliderRect);
        fillArea.anchorMin = new Vector2(SliderHandleInset, barMinY);
        fillArea.anchorMax = new Vector2(1f - SliderHandleInset, barMaxY);
        fillArea.offsetMin = Vector2.zero;
        fillArea.offsetMax = Vector2.zero;

        Image fill =
            CreateImage(
                "Fill",
                fillArea,
                CreateRoundedSprite(48, 16, 8, new Color(0.24f, 0.67f, 1f, 1f)),
                sliced: true
            );

        Stretch(fill.rectTransform);

        RectTransform handleArea = CreateRect("HandleArea", sliderRect);
        handleArea.anchorMin = new Vector2(SliderHandleInset, 0f);
        handleArea.anchorMax = new Vector2(1f - SliderHandleInset, 1f);
        handleArea.offsetMin = Vector2.zero;
        handleArea.offsetMax = Vector2.zero;

        Image handle =
            CreateImage("Handle", handleArea, CreateRoundedSprite(64, 64, 32, Color.white));

        handle.raycastTarget = true;

        // Slider tutamağın X anchor'ını sürer; yüksekliği ebeveynden
        // gelsin, genişliği de yüksekliğe eşitlensin ki panel
        // ölçeklenirken tutamak da birlikte büyüsün.
        handle.rectTransform.anchorMin = new Vector2(0f, 0f);
        handle.rectTransform.anchorMax = new Vector2(0f, 1f);
        handle.rectTransform.sizeDelta = Vector2.zero;

        AspectRatioFitter handleFitter =
            handle.gameObject.AddComponent<AspectRatioFitter>();

        handleFitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
        handleFitter.aspectRatio = 1f;

        Slider slider = sliderRect.gameObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;

        float initialVolume =
            MusicManager.Instance != null
                ? MusicManager.Instance.CurrentVolume
                : PlayerPrefs.GetFloat("curling_music_volume", 0.6f);

        slider.SetValueWithoutNotify(initialVolume);
        SetMusicValueText(initialVolume);

        slider.onValueChanged.AddListener(volume =>
        {
            if (MusicManager.Instance != null)
            {
                MusicManager.Instance.SetVolume(volume);
            }

            SetMusicValueText(volume);
        });
    }

    private void SetMusicValueText(float volume)
    {
        string text = Mathf.RoundToInt(volume * 100f) + "%";

        if (musicValueLabel != null)
        {
            musicValueLabel.text = text;
        }

        if (musicValueShadow != null)
        {
            musicValueShadow.text = text;
        }
    }

    private void BuildDivider(RectTransform parent)
    {
        Image divider =
            CreateImage(
                "Divider",
                parent,
                CreateRoundedSprite(64, 4, 2, new Color(1f, 1f, 1f, 0.18f)),
                sliced: true
            );

        PlaceInContent(divider.rectTransform, Pad, 222f, ContentWidth - Pad * 2f, 4f);
    }

    private void BuildLanguageGrid(RectTransform parent)
    {
        Image globeIcon = CreateImage("GlobeIcon", parent, Resources.Load<Sprite>("UI/GlobeIcon"));
        PlaceInContent(globeIcon.rectTransform, Pad, 254f, RowIconSize, RowIconSize);

        RectTransform labelRect = CreateRect("LanguageLabel", parent);
        PlaceInContent(labelRect, 150f, 272f, 300f, LabelHeight);

        CreateLabel(
            Localization.Get("language_label"),
            labelRect,
            TextAlignmentOptions.Left,
            Color.white
        );

        // 2x2 bayrak ızgarası: TR | EN üstte, ES | FR altta — isim
        // yazmıyoruz, bayrak + sağ alt köşedeki radyo işareti yeterli.
        CreateFlagButton(parent, Language.TR, FlagColLeft, FlagRowTop);
        CreateFlagButton(parent, Language.EN, FlagColRight, FlagRowTop);
        CreateFlagButton(parent, Language.ES, FlagColLeft, FlagRowBottom);
        CreateFlagButton(parent, Language.FR, FlagColRight, FlagRowBottom);
    }

    private void CreateFlagButton(RectTransform parent, Language language, float x, float y)
    {
        RectTransform buttonRect = CreateRect(language + "Flag", parent);
        PlaceInContent(buttonRect, x, y, FlagCellWidth, FlagCellHeight);

        bool isActive = Localization.Current == language;

        // Bayrak görselinin kendi çerçevesi/parlayan kenarlığı zaten
        // görselde baskılı — üstüne ayrıca kod tarafında çerçeve
        // çizmiyoruz, iki çerçeve üst üste binip çirkin duruyordu.
        Image flagImage = CreateImage("Flag", buttonRect, Resources.Load<Sprite>("UI/Flag" + language));
        flagImage.raycastTarget = true;
        Stretch(flagImage.rectTransform);

        flagImage.color = isActive ? Color.white : new Color(0.72f, 0.76f, 0.82f, 1f);

        // Sağ alt köşede dolu/boş radyo ikonu — hangisi seçili net görünsün.
        Image radio =
            CreateImage(
                "Radio",
                buttonRect,
                Resources.Load<Sprite>(isActive ? "UI/RadioOn" : "UI/RadioOff")
            );

        PlaceInRect(
            radio.rectTransform,
            FlagCellWidth,
            FlagCellHeight,
            FlagCellWidth - RadioSize - 4f,
            FlagCellHeight - RadioSize + 8f,
            RadioSize,
            RadioSize
        );

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = flagImage;
        button.onClick.AddListener(UISoundManager.PlayClick);
        button.onClick.AddListener(() => SelectLanguage(language));
    }

    private void SelectLanguage(Language language)
    {
        Localization.SetLanguage(language);

        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.buildIndex);
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

        CreateLabel(
            Localization.Get("back"),
            textRect,
            TextAlignmentOptions.Center,
            Color.white
        );
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

    private static void PlaceInPanel(RectTransform rect, float x, float y, float width, float height)
    {
        PlaceInRect(rect, PanelWidth, PanelHeight, x, y, width, height);
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

        // Sprite'ların 9-slice kenarlığı yok; Sliced çizim modu
        // preserveAspect'i de yok saydığı için hazır görseller Simple
        // çizilir. Sliced yalnızca kod içinde üretilen yuvarlak
        // köşeli (kenarlıklı) sprite'lar için kullanılır.
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

    /// Verilen rect'i tamamen dolduran, otomatik boyutlanan bir yazı
    /// üretir (panel ölçeklenince yazı da ölçeklensin diye sabit punto
    /// kullanılmıyor) ve okunurluk için arkasına koyu bir gölge kopyası
    /// yerleştirir.
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
