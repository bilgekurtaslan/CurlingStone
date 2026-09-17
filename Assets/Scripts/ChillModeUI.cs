using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Chill Mode boyunca ekranda kalan, iki küçük butonlu (RESET /
/// MENU) sabit panel. Reset, ChillModeManager'a buzu temizletir;
/// Menu, sahneyi baştan yükleyip ana menüye döner.
/// </summary>
public class ChillModeUI : MonoBehaviour
{
    [Header("Buton Görselleri (dile göre değişir)")]
    [SerializeField] private LocalizedSpriteSet menuButtonSprite;
    [SerializeField] private LocalizedSpriteSet resetButtonSprite;

    /// <summary>Reset butonuna basılınca tetiklenir.</summary>
    public event Action ResetClicked;

    private GameObject root;

    public void Show()
    {
        EnsureBuilt();

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
            Debug.LogError("ChillModeUI: Canvas bulunamadı!");
            return;
        }

        RectTransform rootRect = CreateRect("ChillModeUI", canvas.transform);
        root = rootRect.gameObject;
        Stretch(rootRect);

        CreateActionButton(
            rootRect,
            "MenuButton",
            menuButtonSprite.Get(Localization.Current),
            anchorMinX: 0.04f,
            anchorMaxX: 0.32f,
            anchorMinY: 0.9f,
            anchorMaxY: 0.965f,
            onClick: ReturnToMainMenu
        );

        CreateActionButton(
            rootRect,
            "ResetButton",
            resetButtonSprite.Get(Localization.Current),
            anchorMinX: 0.68f,
            anchorMaxX: 0.96f,
            anchorMinY: 0.9f,
            anchorMaxY: 0.965f,
            onClick: () => ResetClicked?.Invoke()
        );

        root.SetActive(false);
    }

    private void CreateActionButton(
        RectTransform parent,
        string name,
        Sprite sprite,
        float anchorMinX,
        float anchorMaxX,
        float anchorMinY,
        float anchorMaxY,
        Action onClick)
    {
        if (sprite == null)
            return;

        RectTransform buttonRect = CreateRect(name, parent);
        buttonRect.anchorMin = new Vector2(anchorMinX, anchorMinY);
        buttonRect.anchorMax = new Vector2(anchorMaxX, anchorMaxY);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;

        Image image = CreateImage("Artwork", buttonRect, sprite);
        Stretch(image.rectTransform);
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = true;

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
        button.onClick.AddListener(() => onClick());
    }

    /// <summary>Sahneyi baştan yükler, en başa (ana menüye) döner.</summary>
    private static void ReturnToMainMenu()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.buildIndex);
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

}
