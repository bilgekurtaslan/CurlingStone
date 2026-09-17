using TMPro;
using UnityEngine;

/// <summary>
/// Jumbotron panelinin üzerine oyuncu isimlerini, sırası gelen
/// oyuncuyu ve kalan atış sayısını 3D metin olarak basar.
/// TurnManager tur değiştikçe UpdateTurn'ü çağırır.
/// </summary>
[RequireComponent(typeof(ArenaBackWallBuilder))]
public class ScoreboardController : MonoBehaviour
{
    [Header("Metin Görünümü")]
    [SerializeField] private float nameFontSize = 7f;
    [SerializeField] private float statusFontSize = 10f;
    [SerializeField] private Color inactiveNameAlpha = new Color(1f, 1f, 1f, 0.45f);

    private ArenaBackWallBuilder wallBuilder;
    private TMP_Text player1Text;
    private TMP_Text player2Text;
    private TMP_Text statusText;

    private void Awake()
    {
        wallBuilder = GetComponent<ArenaBackWallBuilder>();
        EnsureBuilt();
    }

    private void EnsureBuilt()
    {
        if (wallBuilder.ScreenTransform == null)
            return;

        if (player1Text != null)
            return;

        Transform screen = wallBuilder.ScreenTransform;

        // Panelin ön yüzü -Z'ye (buz/kameraya) baksın diye
        // metinler screen'in hemen önünde, ona bakacak şekilde
        // döndürülür. Konum değerleri screen'in kendi (ölçeklenmemiş)
        // yerel uzayında: yükseklik ekseni -0.5..0.5 arası (yüzeyin
        // sınırları), Z için -0.5 tam yüzey, biraz daha negatif
        // (-0.7) yüzeyin önüne taşırır — screen'in kalınlığına
        // (Z ölçeğine) bölünerek sabit bir dünya-uzayı boşluğu verir.
        player1Text =
            CreateLabel(screen, "Player1Text", new Vector3(0f, 0.33f, -0.7f), nameFontSize);

        statusText =
            CreateLabel(screen, "StatusText", new Vector3(0f, 0f, -0.7f), statusFontSize);

        player2Text =
            CreateLabel(screen, "Player2Text", new Vector3(0f, -0.33f, -0.7f), nameFontSize);
    }

    private static TMP_Text CreateLabel(
        Transform parent,
        string name,
        Vector3 localPosition,
        float fontSize)
    {
        GameObject obj = new GameObject(name, typeof(TextMeshPro));

        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPosition;
        obj.transform.localRotation = Quaternion.identity;

        // Panel (screen) çok orantısız ölçeklenmiş (genişlik/
        // yükseklik/kalınlık birbirinden çok farklı); metin onun
        // çocuğu olduğu için bu ölçeği miras alıp gerilir/bozulur.
        // Panelin dünya ölçeğinin tersini uygulayarak metni normal
        // (1:1) boyutta tutuyoruz.
        Vector3 parentScale = parent.lossyScale;

        obj.transform.localScale =
            new Vector3(
                1f / Mathf.Max(0.0001f, parentScale.x),
                1f / Mathf.Max(0.0001f, parentScale.y),
                1f / Mathf.Max(0.0001f, parentScale.z)
            );

        TMP_Text tmp = obj.GetComponent<TMP_Text>();
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.fontStyle = FontStyles.Bold;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(6f, 1.5f);

        return tmp;
    }

    /// <summary>
    /// Tur bilgisini günceller: sırası gelen oyuncunun ismi
    /// vurgulanır, diğeri soluklaşır.
    /// </summary>
    public void UpdateTurn(
        string player1Name,
        Color player1Color,
        string player2Name,
        Color player2Color,
        int currentPlayerIndex,
        int throwsTaken,
        int throwsTotal)
    {
        EnsureBuilt();

        if (player1Text == null)
            return;

        player1Text.text = player1Name;
        player1Text.color =
            currentPlayerIndex == 0
                ? player1Color
                : ApplyAlpha(player1Color, inactiveNameAlpha.a);

        player2Text.text = player2Name;
        player2Text.color =
            currentPlayerIndex == 1
                ? player2Color
                : ApplyAlpha(player2Color, inactiveNameAlpha.a);

        statusText.text =
            Localization.Get("throw_label") + "  " + throwsTaken + " / " + throwsTotal;
    }

    /// <summary>Oyun bitince kazananı tabelada gösterir.</summary>
    public void ShowFinal(string winnerName, Color winnerColor)
    {
        EnsureBuilt();

        if (player1Text == null)
            return;

        statusText.text = Localization.Get("game_over");

        if (player1Text.text == winnerName)
        {
            player1Text.color = winnerColor;
            player2Text.color = ApplyAlpha(player2Text.color, inactiveNameAlpha.a);
        }
        else
        {
            player2Text.color = winnerColor;
            player1Text.color = ApplyAlpha(player1Text.color, inactiveNameAlpha.a);
        }
    }

    private static Color ApplyAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
