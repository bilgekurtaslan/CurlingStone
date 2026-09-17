using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Segmented (LED-bar style) power gauge shown next to the stone
/// while aiming. Ported from a Flame/Dart PowerGaugeComponent design.
/// </summary>
public class SpeedGaugeUI : MonoBehaviour
{
    [Header("Speed")]
    [SerializeField] private float maxSpeed = 23f;
    [SerializeField] private float idealMinSpeed = 12.3f;
    [SerializeField] private float idealMaxSpeed = 14.6f;

    [Header("Segmented Gauge")]
    [SerializeField] private int segmentCount = 10;
    [SerializeField] private float defaultFadeSpeed = 3.5f;

    [Header("Layout")]
    [SerializeField] private Vector2 gaugeSize = new Vector2(84f, 340f);
    [SerializeField] private float paddingX = 8f;
    [SerializeField] private float paddingY = 10f;
    [SerializeField] private float segmentGap = 6f;

    private static readonly Color ColorControlled = new Color32(56, 189, 248, 255);
    private static readonly Color ColorGood = new Color32(234, 179, 8, 255);
    private static readonly Color ColorRisky = new Color32(239, 68, 68, 255);
    private static readonly Color ColorEmpty = new Color32(51, 65, 85, 90);
    private static readonly Color ColorBackground = new Color32(15, 23, 42, 225);
    private static readonly Color ColorBorder = new Color32(56, 189, 248, 190);

    private GameObject gaugeRoot;
    private CanvasGroup canvasGroup;
    private Image[] segmentImages;

    private float power;
    private bool isFadingOut;
    private float fadeSpeed;

    private bool built = false;

    private void Awake()
    {
        maxSpeed = Mathf.Max(0.1f, maxSpeed);

        idealMinSpeed = Mathf.Clamp(
            idealMinSpeed,
            0f,
            maxSpeed
        );

        idealMaxSpeed = Mathf.Clamp(
            idealMaxSpeed,
            idealMinSpeed,
            maxSpeed
        );

        fadeSpeed = defaultFadeSpeed;

        Build();

        SetSpeed(0f, maxSpeed);
    }

    private void Update()
    {
        if (!isFadingOut || canvasGroup == null)
            return;

        canvasGroup.alpha -=
            fadeSpeed * Time.deltaTime;

        if (canvasGroup.alpha <= 0f)
        {
            canvasGroup.alpha = 0f;
            isFadingOut = false;

            if (gaugeRoot != null)
            {
                gaugeRoot.SetActive(false);
            }
        }
    }

    /// <summary>Updates current power (as speed / maxSpeed) and shows the gauge.</summary>
    public void SetSpeed(
        float speed,
        float maximumSpeed)
    {
        EnsureBuilt();

        maxSpeed = Mathf.Max(
            0.1f,
            maximumSpeed
        );

        speed = Mathf.Clamp(
            speed,
            0f,
            maxSpeed
        );

        power = speed / maxSpeed;

        isFadingOut = false;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }

        UpdateSegments();
    }

    public void SetGaugeVisible(bool visible)
    {
        EnsureBuilt();

        if (gaugeRoot == null)
            return;

        if (visible)
        {
            gaugeRoot.SetActive(true);

            isFadingOut = false;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
            }
        }
        else
        {
            gaugeRoot.SetActive(false);
        }
    }

    /// <summary>Smooth fade-out, e.g. right after the stone is thrown.</summary>
    public void FadeOut(float speed = -1f)
    {
        EnsureBuilt();

        fadeSpeed =
            speed > 0f
                ? speed
                : defaultFadeSpeed;

        isFadingOut = true;
    }

    /// <summary>Hides the gauge immediately.</summary>
    public void Hide()
    {
        EnsureBuilt();

        power = 0f;
        isFadingOut = false;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }

        if (gaugeRoot != null)
        {
            gaugeRoot.SetActive(false);
        }
    }

    private void EnsureBuilt()
    {
        if (!built)
        {
            Build();
        }
    }

    private void UpdateSegments()
    {
        if (segmentImages == null)
            return;

        int filledSegments =
            Mathf.CeilToInt(power * segmentCount);

        float goodZoneMin =
            idealMinSpeed / maxSpeed;

        float riskyZoneMin =
            idealMaxSpeed / maxSpeed;

        for (int i = 0; i < segmentCount; i++)
        {
            // Alttan (0) üste doğru dolar.
            float normalizedValue =
                (i + 1) / (float)segmentCount;

            bool isFilled =
                i < filledSegments;

            Color color;

            if (isFilled)
            {
                if (normalizedValue >= riskyZoneMin)
                {
                    color = ColorRisky;
                }
                else if (normalizedValue >= goodZoneMin)
                {
                    color = ColorGood;
                }
                else
                {
                    color = ColorControlled;
                }
            }
            else
            {
                color = ColorEmpty;
            }

            segmentImages[i].color = color;
        }
    }

    private void Build()
    {
        if (built)
            return;

        Canvas canvas =
            GetComponentInParent<Canvas>();

        if (canvas == null)
        {
            canvas =
                FindAnyObjectByType<Canvas>();
        }

        if (canvas == null)
        {
            Debug.LogError(
                "SpeedGaugeUI: Canvas bulunamadı!"
            );

            return;
        }

        built = true;

        // =========================================
        // GÖSTERGE KÖKÜ
        // EKRANIN SOL ALTINDA, DİKEY
        // =========================================

        RectTransform root =
            CreateRect(
                "Speed Gauge",
                canvas.transform,
                gaugeSize
            );

        gaugeRoot =
            root.gameObject;

        root.anchorMin = new Vector2(0f, 0f);
        root.anchorMax = new Vector2(0f, 0f);
        root.pivot = new Vector2(0f, 0f);
        root.anchoredPosition = new Vector2(30f, 260f);

        canvasGroup =
            gaugeRoot.AddComponent<CanvasGroup>();

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        gaugeRoot.SetActive(false);

        // =========================================
        // ARKA PLAN (KENARLIKLI PANEL)
        // =========================================

        Image panel =
            CreateImage(
                "Panel",
                root,
                CreateBorderedRoundedSprite(
                    96,
                    Mathf.RoundToInt(96f * (gaugeSize.y / gaugeSize.x)),
                    16,
                    ColorBackground,
                    ColorBorder,
                    2.5f
                )
            );

        Stretch(panel.rectTransform);

        // =========================================
        // SEGMENTLER
        // =========================================

        BuildSegments(root);
    }

    private void BuildSegments(RectTransform root)
    {
        float availableHeight =
            gaugeSize.y - paddingY * 2f;

        float totalGap =
            segmentGap * (segmentCount - 1);

        float segmentHeight =
            (availableHeight - totalGap) / segmentCount;

        float segmentWidth =
            gaugeSize.x - paddingX * 2f;

        Sprite segmentSprite =
            CreateRoundedSprite(
                Mathf.Max(8, Mathf.RoundToInt(segmentWidth * 2f)),
                Mathf.Max(8, Mathf.RoundToInt(segmentHeight * 2f)),
                6,
                Color.white
            );

        segmentImages = new Image[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            float bottomOffset =
                paddingY + i * (segmentHeight + segmentGap);

            RectTransform segmentRect =
                CreateRect(
                    "Segment" + i,
                    root,
                    new Vector2(segmentWidth, segmentHeight)
                );

            segmentRect.anchorMin = new Vector2(0f, 0f);
            segmentRect.anchorMax = new Vector2(0f, 0f);
            segmentRect.pivot = new Vector2(0f, 0f);

            segmentRect.anchoredPosition =
                new Vector2(paddingX, bottomOffset);

            Image segmentImage =
                CreateImage(
                    "SegmentImage" + i,
                    segmentRect,
                    segmentSprite
                );

            Stretch(segmentImage.rectTransform);

            segmentImage.color = ColorEmpty;

            segmentImages[i] = segmentImage;
        }
    }

    private static void Stretch(
        RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static RectTransform CreateRect(
        string name,
        Transform parent,
        Vector2 size)
    {
        GameObject obj =
            new GameObject(
                name,
                typeof(RectTransform)
            );

        RectTransform rect =
            obj.GetComponent<RectTransform>();

        rect.SetParent(
            parent,
            false
        );

        rect.sizeDelta = size;

        return rect;
    }

    private static Image CreateImage(
        string name,
        Transform parent,
        Sprite sprite)
    {
        GameObject obj =
            new GameObject(
                name,
                typeof(RectTransform),
                typeof(Image)
            );

        RectTransform rect =
            obj.GetComponent<RectTransform>();

        rect.SetParent(
            parent,
            false
        );

        Image image =
            obj.GetComponent<Image>();

        image.sprite = sprite;
        image.color = Color.white;
        image.raycastTarget = false;

        return image;
    }

    // =========================================
    // SDF TABANLI YUVARLAK DİKDÖRTGEN
    // =========================================

    // Yuvarlak köşeli sprite'lar UISpriteCache'te önbellekleniyor —
    // aynı parametre her çağrıda yeni doku üretip sızdırıyordu.
    private static Sprite CreateRoundedSprite(int width, int height, int radius, Color color)
    {
        return UISpriteCache.Rounded(width, height, radius, color);
    }

    private static Sprite CreateBorderedRoundedSprite(
        int width,
        int height,
        int radius,
        Color fillColor,
        Color borderColor,
        float borderThickness)
    {
        return UISpriteCache.RoundedBordered(width, height, radius, fillColor, borderColor, borderThickness);
    }
}
