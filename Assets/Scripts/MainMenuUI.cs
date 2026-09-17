using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Oyun açılışında görünen giriş ekranı. Butonların görselleri
/// hazır sanat dosyaları (Assets/Art/UI/Buttons); sadece gerçekten
/// var olan özelliklere (AI'a karşı tek oyunculu, aynı cihazda 2
/// oyunculu yerel mod, sınırsız atışlı Chill Mode, Ayarlar, Mağaza,
/// Özelleştir) bağlı butonlar çalışır. Henüz yapılmamış özellikler
/// "Yakında" rozetiyle görünüp pasif kalır — olmayan bir sistemi
/// varmış gibi göstermemek için.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("Arka Plan")]
    [SerializeField] private Sprite backgroundSprite;

    [Header("Buton Görselleri (dile göre değişir)")]
    [SerializeField] private LocalizedSpriteSet playSprite;
    [SerializeField] private LocalizedSpriteSet localSprite;
    [SerializeField] private LocalizedSpriteSet chillSprite;
    [SerializeField] private LocalizedSpriteSet settingsSprite;
    [SerializeField] private LocalizedSpriteSet shopSprite;
    [SerializeField] private LocalizedSpriteSet customizeSprite;

    [Header("Para Birimi Panelleri (sayı üzerine yazılır, Currency'den okunur)")]
    [SerializeField] private Sprite goldPanelSprite;
    [SerializeField] private Sprite diamondPanelSprite;

    [Header("Oyun Başlatma")]
    [SerializeField] private NameEntryUI nameEntryUI;
    [SerializeField] private DifficultySelectUI difficultySelectUI;
    [SerializeField] private ChillModeManager chillModeManager;
    [SerializeField] private SettingsUI settingsUI;
    [SerializeField] private ShopUI shopUI;
    [SerializeField] private CustomizeUI customizeUI;
    [SerializeField] private PayUI payUI;

    private static readonly Color PanelScrimTop =
        new Color(0.03f, 0.07f, 0.14f, 0.1f);

    private static readonly Color PanelScrimBottom =
        new Color(0.02f, 0.05f, 0.1f, 0.6f);

    private static readonly Color LockedTint =
        new Color(0.55f, 0.55f, 0.58f, 0.85f);

    private GameObject root;
    private GameObject toastRoot;
    private TMP_Text toastText;
    private CanvasGroup toastGroup;
    private Coroutine toastRoutine;
    private TMP_Text goldValueText;
    private TMP_Text diamondValueText;

    private void Awake()
    {
        Build();

        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.PlayMenuMusic();
        }
    }

    private void Build()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();

        if (canvas == null)
        {
            Debug.LogError("MainMenuUI: Canvas bulunamadı!");
            return;
        }

        RectTransform rootRect =
            CreateRect("MainMenu", canvas.transform);

        root = rootRect.gameObject;
        Stretch(rootRect);

        if (backgroundSprite != null)
        {
            Image background =
                CreateImage("Background", rootRect, backgroundSprite);

            Stretch(background.rectTransform);
            background.type = Image.Type.Simple;
            background.preserveAspect = false;
        }

        // Görselin alt kısmı üstündeki butonların okunabilmesi
        // için hafif bir gradyan gölge bindiriyoruz.
        Image scrim =
            CreateImage(
                "Scrim",
                rootRect,
                CreateVerticalGradientSprite(
                    PanelScrimTop,
                    PanelScrimBottom
                )
            );

        Stretch(scrim.rectTransform);
        scrim.raycastTarget = false;

        // Tüm ekranı kaplayan görünmez bir dokunma engelleyici —
        // menü açıkken alttaki taşa dokunulmasın.
        Image blocker = CreateImage("TouchBlocker", rootRect, null);
        Stretch(blocker.rectTransform);
        blocker.color = new Color(0f, 0f, 0f, 0.001f);
        blocker.raycastTarget = true;

        BuildCurrencyRow(rootRect);
        BuildButtonStack(rootRect);
        BuildBottomRow(rootRect);
        BuildToast(canvas.transform);
    }

    private void BuildCurrencyRow(RectTransform parent)
    {
        goldValueText = CreateCurrencyPanel(
            parent,
            name: "GoldPanel",
            sprite: goldPanelSprite,
            anchorMinX: 0.04f,
            anchorMaxX: 0.46f,
            startingValue: Currency.Gold
        );

        diamondValueText = CreateCurrencyPanel(
            parent,
            name: "DiamondPanel",
            sprite: diamondPanelSprite,
            anchorMinX: 0.54f,
            anchorMaxX: 0.96f,
            startingValue: Currency.Diamonds
        );
    }

    /// <summary>
    /// Gold/Diamond bakiyesi menü açıkken (Shop/Pay ekranlarından)
    /// değiştiğinde üstteki panelleri günceller — bu ekranlar menüyü
    /// kapatmadan üstüne bindiği için panel yeniden inşa edilmiyor.
    /// </summary>
    public void RefreshCurrencyDisplay()
    {
        if (goldValueText != null)
        {
            goldValueText.text = Currency.Gold.ToString("N0");
        }

        if (diamondValueText != null)
        {
            diamondValueText.text = Currency.Diamonds.ToString("N0");
        }
    }

    private TMP_Text CreateCurrencyPanel(
        RectTransform parent,
        string name,
        Sprite sprite,
        float anchorMinX,
        float anchorMaxX,
        int startingValue)
    {
        if (sprite == null)
            return null;

        RectTransform panelRect = CreateRect(name, parent);

        panelRect.anchorMin = new Vector2(anchorMinX, 0.905f);
        panelRect.anchorMax = new Vector2(anchorMaxX, 0.96f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image image = CreateImage("Icon", panelRect, sprite);
        Stretch(image.rectTransform);
        image.type = Image.Type.Simple;
        image.preserveAspect = true;

        // Sayı, panelin sağ yarısındaki boş alana yazılır.
        TMP_Text valueText =
            CreateText(
                startingValue.ToString("N0"),
                panelRect,
                24f,
                FontStyles.Bold,
                TextAlignmentOptions.Center
            );

        valueText.color = Color.white;
        valueText.rectTransform.anchorMin = new Vector2(0.32f, 0.15f);
        valueText.rectTransform.anchorMax = new Vector2(0.78f, 0.85f);
        valueText.rectTransform.offsetMin = Vector2.zero;
        valueText.rectTransform.offsetMax = Vector2.zero;

        // Panel tıklanabilir bir buton — Gold ya da Diamond'a
        // basınca PayUI (Buy Gold/Diamonds) ekranı açılır.
        image.raycastTarget = true;

        Button button = panelRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.disabledColor = Color.white;
        button.colors = colors;

        button.onClick.AddListener(UISoundManager.PlayClick);
        button.onClick.AddListener(OpenPay);

        return valueText;
    }

    private void OpenPay()
    {
        if (payUI != null)
        {
            payUI.Show();
        }
    }

    private void BuildButtonStack(RectTransform parent)
    {
        RectTransform stack = CreateRect("ButtonStack", parent);

        stack.anchorMin = new Vector2(0.1f, 0.16f);
        stack.anchorMax = new Vector2(0.9f, 0.66f);
        stack.offsetMin = Vector2.zero;
        stack.offsetMax = Vector2.zero;

        CreateImageButton(
            stack,
            anchorTop: 1f,
            anchorBottom: 0.78f,
            sprite: playSprite.Get(Localization.Current),
            locked: false,
            onClick: StartAIGame
        );

        CreateImageButton(
            stack,
            anchorTop: 0.74f,
            anchorBottom: 0.52f,
            sprite: localSprite.Get(Localization.Current),
            locked: false,
            onClick: StartLocalGame
        );

        CreateImageButton(
            stack,
            anchorTop: 0.48f,
            anchorBottom: 0.26f,
            sprite: chillSprite.Get(Localization.Current),
            locked: false,
            onClick: StartChillGame
        );

        CreateImageButton(
            stack,
            anchorTop: 0.22f,
            anchorBottom: 0f,
            sprite: settingsSprite.Get(Localization.Current),
            locked: false,
            onClick: OpenSettings
        );
    }

    private void BuildBottomRow(RectTransform parent)
    {
        RectTransform bottomRow = CreateRect("BottomRow", parent);

        bottomRow.anchorMin = new Vector2(0.08f, 0.045f);
        bottomRow.anchorMax = new Vector2(0.92f, 0.135f);
        bottomRow.offsetMin = Vector2.zero;
        bottomRow.offsetMax = Vector2.zero;

        RectTransform shopHalf = CreateRect("ShopHalf", bottomRow);
        shopHalf.anchorMin = new Vector2(0f, 0f);
        shopHalf.anchorMax = new Vector2(0.48f, 1f);
        shopHalf.offsetMin = Vector2.zero;
        shopHalf.offsetMax = Vector2.zero;

        CreateImageButton(
            shopHalf,
            anchorTop: 1f,
            anchorBottom: 0f,
            sprite: shopSprite.Get(Localization.Current),
            locked: false,
            onClick: OpenShop
        );

        RectTransform customizeHalf =
            CreateRect("CustomizeHalf", bottomRow);

        customizeHalf.anchorMin = new Vector2(0.52f, 0f);
        customizeHalf.anchorMax = new Vector2(1f, 1f);
        customizeHalf.offsetMin = Vector2.zero;
        customizeHalf.offsetMax = Vector2.zero;

        CreateImageButton(
            customizeHalf,
            anchorTop: 1f,
            anchorBottom: 0f,
            sprite: customizeSprite.Get(Localization.Current),
            locked: false,
            onClick: OpenCustomize
        );
    }

    private void CreateImageButton(
        RectTransform parent,
        float anchorTop,
        float anchorBottom,
        Sprite sprite,
        bool locked,
        System.Action onClick)
    {
        if (sprite == null)
            return;

        RectTransform buttonRect =
            CreateRect(sprite.name + " Button", parent);

        buttonRect.anchorMin = new Vector2(0f, anchorBottom);
        buttonRect.anchorMax = new Vector2(1f, anchorTop);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;

        Image image = CreateImage("Artwork", buttonRect, sprite);
        Stretch(image.rectTransform);
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = true;

        if (locked)
        {
            image.color = LockedTint;
        }

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.disabledColor = Color.white;
        button.colors = colors;

        button.onClick.AddListener(UISoundManager.PlayClick);

        if (locked)
        {
            button.onClick.AddListener(() => ShowToast(Localization.Get("coming_soon")));
        }
        else if (onClick != null)
        {
            button.onClick.AddListener(() => onClick());
        }
    }

    private void BuildToast(Transform canvasTransform)
    {
        RectTransform toastRect =
            CreateRect("Toast", canvasTransform);

        toastRoot = toastRect.gameObject;

        toastRect.anchorMin = new Vector2(0.5f, 0.5f);
        toastRect.anchorMax = new Vector2(0.5f, 0.5f);
        toastRect.pivot = new Vector2(0.5f, 0.5f);
        toastRect.sizeDelta = new Vector2(360f, 90f);
        toastRect.anchoredPosition = Vector2.zero;

        toastGroup = toastRoot.AddComponent<CanvasGroup>();
        toastGroup.alpha = 0f;
        toastGroup.blocksRaycasts = false;
        toastGroup.interactable = false;

        Image panel =
            CreateImage(
                "Panel",
                toastRect,
                CreateRoundedSprite(
                    180, 45, 22,
                    new Color(0.02f, 0.06f, 0.14f, 0.92f)
                )
            );

        Stretch(panel.rectTransform);
        panel.raycastTarget = false;

        toastText =
            CreateText(
                "",
                toastRect,
                28f,
                FontStyles.Bold,
                TextAlignmentOptions.Center
            );

        toastText.color = new Color(1f, 0.85f, 0.35f);
        toastText.raycastTarget = false;
        Stretch(toastText.rectTransform);
    }

    private void ShowToast(string message)
    {
        if (toastGroup == null)
            return;

        if (toastRoutine != null)
        {
            StopCoroutine(toastRoutine);
        }

        toastRoutine = StartCoroutine(PlayToast(message));
    }

    private IEnumerator PlayToast(string message)
    {
        toastText.text = message;

        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime * 8f;
            toastGroup.alpha = Mathf.Clamp01(t);
            yield return null;
        }

        yield return new WaitForSeconds(0.9f);

        t = 1f;

        while (t > 0f)
        {
            t -= Time.deltaTime * 4f;
            toastGroup.alpha = Mathf.Clamp01(t);
            yield return null;
        }

        toastRoutine = null;
    }

    private void StartLocalGame()
    {
        if (nameEntryUI != null)
        {
            nameEntryUI.Show(OnNamesConfirmed);
        }
        else
        {
            OnNamesConfirmed("PLAYER 1", "PLAYER 2");
        }
    }

    private void OnNamesConfirmed(string player1Name, string player2Name)
    {
        TurnManager turnManager = FindAnyObjectByType<TurnManager>();

        if (turnManager != null)
        {
            turnManager.ConfigureOpponent(false, AIOpponent.AIDifficulty.Medium);
            turnManager.SetPlayerNames(player1Name, player2Name);
            turnManager.BeginMatch();
        }

        RevealGame();
    }

    private void StartAIGame()
    {
        if (difficultySelectUI != null)
        {
            difficultySelectUI.Show(OnDifficultyConfirmed);
        }
    }

    private void OnDifficultyConfirmed(AIOpponent.AIDifficulty difficulty)
    {
        TurnManager turnManager = FindAnyObjectByType<TurnManager>();

        if (turnManager != null)
        {
            turnManager.ConfigureOpponent(true, difficulty);

            string difficultyLabel =
                Localization.Get(difficulty switch
                {
                    AIOpponent.AIDifficulty.Easy => "easy",
                    AIOpponent.AIDifficulty.Hard => "hard",
                    _ => "medium",
                });

            turnManager.SetPlayerNames(
                Localization.Get("you_label"),
                "AI (" + difficultyLabel + ")"
            );

            turnManager.BeginMatch();
        }

        RevealGame();
    }

    private void OpenSettings()
    {
        if (settingsUI != null)
        {
            settingsUI.Show();
        }
    }

    private void OpenShop()
    {
        if (shopUI != null)
        {
            shopUI.Show();
        }
    }

    private void OpenCustomize()
    {
        if (customizeUI != null)
        {
            customizeUI.Show();
        }
    }

    private void StartChillGame()
    {
        if (chillModeManager != null)
        {
            chillModeManager.BeginChillMode();
        }

        RevealGame();
    }

    private void RevealGame()
    {
        if (root != null)
        {
            root.SetActive(false);
        }

        if (MusicManager.Instance != null)
        {
            MusicManager.Instance.EnterGameplay();
        }
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

    private static RectTransform CreateRect(
        string name,
        Transform parent)
    {
        GameObject obj =
            new GameObject(name, typeof(RectTransform));

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        return rect;
    }

    private static Image CreateImage(
        string name,
        Transform parent,
        Sprite sprite)
    {
        GameObject obj =
            new GameObject(name, typeof(RectTransform), typeof(Image));

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        Image image = obj.GetComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.raycastTarget = false;

        if (sprite != null)
        {
            image.type = Image.Type.Sliced;
        }

        return image;
    }

    private static TMP_Text CreateText(
        string text,
        Transform parent,
        float fontSize,
        FontStyles style,
        TextAlignmentOptions alignment)
    {
        GameObject obj =
            new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(TextMeshProUGUI)
            );

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        TMP_Text tmp = obj.GetComponent<TMP_Text>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.enableAutoSizing = false;

        return tmp;
    }

    // Yuvarlak köşeli sprite'lar UISpriteCache'te önbellekleniyor —
    // aynı parametre her çağrıda yeni doku üretip sızdırıyordu.
    private static Sprite CreateRoundedSprite(int width, int height, int radius, Color color)
    {
        return UISpriteCache.Rounded(width, height, radius, color);
    }

    private static Sprite CreateVerticalGradientSprite(Color top, Color bottom)
    {
        return UISpriteCache.VerticalGradient(top, bottom);
    }
}
