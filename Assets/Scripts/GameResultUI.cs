using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Oyun bitince ekranı kaplayan kazanma ekranı: arkada oyun sahnesi
/// görünür kalır (üstüne yarı saydam bir karartma biner), üstte
/// kupa/bayrak grafiği ve kazanan takımın ismi, altında mock bir
/// ödül paneli (coin/elmas — gerçek matematik ileride bağlanacak)
/// ve iki buton (Main Menu / Play Again) bulunur.
/// </summary>
public class GameResultUI : MonoBehaviour
{
    [Header("Kupa / Bayrak (dile göre değişir)")]
    [SerializeField] private LocalizedSpriteSet victoryBannerSprite;

    [Header("Kaybetme Afişi (AI'a karşı kaybedince — dile göre değişir)")]
    [SerializeField] private LocalizedSpriteSet loseBannerSprite;

    [Header("Ödül Paneli (şimdilik mock — gerçek coin/elmas matematiği sonra bağlanacak, kaybedince hiç gösterilmez)")]
    [SerializeField] private Sprite rewardPanelSprite;

    [Header("Butonlar (dile göre değişir)")]
    [SerializeField] private LocalizedSpriteSet mainMenuButtonSprite;
    [SerializeField] private LocalizedSpriteSet playAgainButtonSprite;

    [Header("Kazanma Sesi (sadece kazanınca çalar, kaybedince çalmaz)")]
    [SerializeField] private AudioSource winAudioSource;
    [SerializeField] private AudioClip winClip;

    [Header("Kaybetme Sesi (sadece AI'a karşı kaybedince çalar)")]
    [SerializeField] private AudioSource loseAudioSource;
    [SerializeField] private AudioClip loseClip;

    /// <summary>"Play Again" tıklanınca tetiklenir — maçı sıfırlayıp yeniden başlatmak çağıranın (TurnManager) sorumluluğunda.</summary>
    public event System.Action PlayAgainClicked;

    private GameObject root;
    private TMP_Text winnerText;
    private TMP_Text coinsText;
    private GameObject victoryBannerObj;
    private GameObject loseBannerObj;
    private GameObject rewardPanelObj;

    /// <summary>
    /// Sonuç ekranını gösterir. isLoss true ise (AI'a karşı kaybedince)
    /// kupa yerine kaybetme afişi çıkar. Ödül paneli (gerçek kazanılan
    /// coin miktarıyla) sadece AI'a karşı kazanılınca gösterilir —
    /// Local'de (iki insan) ya da kaybedince hiç çıkmaz.
    /// </summary>
    public void ShowResult(string name, Color color, string subInfo, bool isLoss, bool isAIMatch, int coinsAwarded)
    {
        EnsureBuilt();

        winnerText.text = name;
        winnerText.color = color;

        if (victoryBannerObj != null)
        {
            victoryBannerObj.SetActive(!isLoss);
        }

        if (loseBannerObj != null)
        {
            loseBannerObj.SetActive(isLoss);
        }

        if (rewardPanelObj != null)
        {
            rewardPanelObj.SetActive(!isLoss && isAIMatch);
        }

        if (coinsText != null)
        {
            coinsText.text = "+" + coinsAwarded;
        }

        if (!isLoss && winAudioSource != null && winClip != null)
        {
            winAudioSource.PlayOneShot(winClip);
        }

        if (isLoss && loseAudioSource != null && loseClip != null)
        {
            loseAudioSource.PlayOneShot(loseClip);
        }

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
            Debug.LogError("GameResultUI: Canvas bulunamadı!");
            return;
        }

        RectTransform rootRect = CreateRect("Game Result", canvas.transform);
        root = rootRect.gameObject;
        Stretch(rootRect);

        // Arka planda oyun sahnesi görünür kalsın diye tam ekranı
        // kaplayan yarı saydam bir karartma — tam siyah kapatma değil.
        Image scrim = CreateImage("Scrim", rootRect, null);
        Stretch(scrim.rectTransform);
        scrim.color = new Color(0f, 0f, 0f, 0.55f);
        scrim.raycastTarget = true;

        Sprite resolvedVictoryBanner = victoryBannerSprite.Get(Localization.Current);
        Sprite resolvedLoseBanner = loseBannerSprite.Get(Localization.Current);
        Sprite resolvedMainMenuButton = mainMenuButtonSprite.Get(Localization.Current);
        Sprite resolvedPlayAgainButton = playAgainButtonSprite.Get(Localization.Current);

        if (resolvedVictoryBanner != null)
        {
            Image banner =
                CreateImage("TrophyBanner", rootRect, resolvedVictoryBanner);

            banner.rectTransform.anchorMin = new Vector2(0.08f, 0.60f);
            banner.rectTransform.anchorMax = new Vector2(0.92f, 0.90f);
            banner.rectTransform.offsetMin = Vector2.zero;
            banner.rectTransform.offsetMax = Vector2.zero;
            banner.preserveAspect = true;
            banner.raycastTarget = false;

            victoryBannerObj = banner.gameObject;
        }

        if (resolvedLoseBanner != null)
        {
            Image loseBanner =
                CreateImage("LoseBanner", rootRect, resolvedLoseBanner);

            loseBanner.rectTransform.anchorMin = new Vector2(0.08f, 0.60f);
            loseBanner.rectTransform.anchorMax = new Vector2(0.92f, 0.90f);
            loseBanner.rectTransform.offsetMin = Vector2.zero;
            loseBanner.rectTransform.offsetMax = Vector2.zero;
            loseBanner.preserveAspect = true;
            loseBanner.raycastTarget = false;

            loseBannerObj = loseBanner.gameObject;
            loseBannerObj.SetActive(false);
        }

        winnerText =
            CreateText(
                "",
                rootRect,
                72f,
                FontStyles.Bold,
                TextAlignmentOptions.Center
            );

        // Beyaz dış çizgi + hafif gölge — renk ne olursa olsun
        // koyu/açık her arka planda okunaklı ve "kalın" dursun.
        winnerText.outlineWidth = 0.25f;
        winnerText.outlineColor = Color.white;
        winnerText.fontMaterial.EnableKeyword("OUTLINE_ON");

        winnerText.rectTransform.anchorMin = new Vector2(0.05f, 0.52f);
        winnerText.rectTransform.anchorMax = new Vector2(0.95f, 0.615f);
        winnerText.rectTransform.offsetMin = Vector2.zero;
        winnerText.rectTransform.offsetMax = Vector2.zero;

        if (rewardPanelSprite != null)
        {
            Image reward =
                CreateImage("RewardPanel", rootRect, rewardPanelSprite);

            reward.rectTransform.anchorMin = new Vector2(0.1f, 0.30f);
            reward.rectTransform.anchorMax = new Vector2(0.9f, 0.50f);
            reward.rectTransform.offsetMin = Vector2.zero;
            reward.rectTransform.offsetMax = Vector2.zero;
            reward.preserveAspect = true;
            reward.raycastTarget = false;

            rewardPanelObj = reward.gameObject;

            coinsText =
                CreateText(
                    "",
                    reward.rectTransform,
                    36f,
                    FontStyles.Bold,
                    TextAlignmentOptions.Center
                );

            coinsText.color = new Color(1f, 0.85f, 0.3f);
            coinsText.outlineWidth = 0.2f;
            coinsText.outlineColor = new Color(0.3f, 0.2f, 0f);
            coinsText.fontMaterial.EnableKeyword("OUTLINE_ON");
            coinsText.rectTransform.anchorMin = new Vector2(0.1f, 0.08f);
            coinsText.rectTransform.anchorMax = new Vector2(0.9f, 0.4f);
            coinsText.rectTransform.offsetMin = Vector2.zero;
            coinsText.rectTransform.offsetMax = Vector2.zero;
        }

        RectTransform buttonRow = CreateRect("ButtonRow", rootRect);
        buttonRow.anchorMin = new Vector2(0.08f, 0.08f);
        buttonRow.anchorMax = new Vector2(0.92f, 0.20f);
        buttonRow.offsetMin = Vector2.zero;
        buttonRow.offsetMax = Vector2.zero;

        RectTransform mainMenuHalf = CreateRect("MainMenuHalf", buttonRow);
        mainMenuHalf.anchorMin = new Vector2(0f, 0f);
        mainMenuHalf.anchorMax = new Vector2(0.48f, 1f);
        mainMenuHalf.offsetMin = Vector2.zero;
        mainMenuHalf.offsetMax = Vector2.zero;

        CreateButton(mainMenuHalf, resolvedMainMenuButton, ReturnToMainMenu);

        RectTransform playAgainHalf = CreateRect("PlayAgainHalf", buttonRow);
        playAgainHalf.anchorMin = new Vector2(0.52f, 0f);
        playAgainHalf.anchorMax = new Vector2(1f, 1f);
        playAgainHalf.offsetMin = Vector2.zero;
        playAgainHalf.offsetMax = Vector2.zero;

        CreateButton(
            playAgainHalf,
            resolvedPlayAgainButton,
            () => PlayAgainClicked?.Invoke()
        );

        root.SetActive(false);
    }

    private void CreateButton(
        RectTransform parent,
        Sprite sprite,
        UnityEngine.Events.UnityAction onClick)
    {
        if (sprite == null)
            return;

        Image image = CreateImage("Button", parent, sprite);
        Stretch(image.rectTransform);
        image.preserveAspect = true;
        image.raycastTarget = true;

        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.disabledColor = Color.white;
        button.colors = colors;

        button.onClick.AddListener(UISoundManager.PlayClick);
        button.onClick.AddListener(onClick);
    }

    /// <summary>"Main Menu" — sahneyi baştan yükler, en başa (ana menüye) döner.</summary>
    private static void ReturnToMainMenu()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.buildIndex);
    }

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
        GameObject obj = new GameObject(name, typeof(RectTransform));

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
            new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));

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

        return tmp;
    }
}
