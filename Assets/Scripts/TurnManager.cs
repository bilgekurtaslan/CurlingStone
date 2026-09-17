using UnityEngine;

/// <summary>
/// Taş durunca yerinde bırakır, sırayı diğer oyuncuya devreder:
/// yeni renkte bir taş başlangıç noktasında belirir, kamera
/// ilk (sabit) konumuna döner. Her oyuncu belirli sayıda atış
/// yapınca, center'a en yakın taşın sahibi kazanır.
/// </summary>
public class TurnManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StoneController initialStone;
    [SerializeField] private CameraFollowController cameraFollow;
    [SerializeField] private GameResultUI resultUI;
    [SerializeField] private AthleteController athlete;
    [SerializeField] private BroomSweeperController leftAthlete;
    [SerializeField] private BroomSweeperController rightAthlete;
    [SerializeField] private ScoreboardController scoreboard;
    [SerializeField] private CosmeticCatalog cosmeticCatalog;

    [Header("Yapay Zeka Rakip (tek oyunculu mod)")]
    [SerializeField] private AIOpponent aiOpponent;
    [SerializeField] private bool player2IsAI;
    [SerializeField] private AIOpponent.AIDifficulty aiDifficulty = AIOpponent.AIDifficulty.Medium;

    [Header("Player Colors")]
    [SerializeField] private Color player1Color = new Color(0.62f, 0.15f, 0.13f);
    [SerializeField] private Color player2Color = new Color(0.85f, 0.78f, 0.22f);

    [Header("Player Names")]
    [SerializeField] private string player1Name = "PLAYER 1";
    [SerializeField] private string player2Name = "PLAYER 2";

    [Header("Kurallar")]
    [SerializeField] private int throwsPerPlayer = 4;
    [SerializeField] private Vector3 houseCenter = new Vector3(0f, 0f, 16f);

    [Header("Turn Timing")]
    [SerializeField] private float nextStoneDelay = 1f;

    [Header("Tribün Koltukları")]
    [SerializeField] private GameObject[] leftGrandstands;
    [SerializeField] private GameObject[] rightGrandstands;
    [SerializeField] private Color leftSeatColor = new Color(0.91f, 0.56f, 0.55f);
    [SerializeField] private Color rightSeatColor = new Color(0.57f, 0.68f, 0.87f);
    [SerializeField] private string seatObjectName = "Paint - Sand Beige";

    private StoneController activeStone;
    private int currentPlayerIndex;
    private int totalThrowsTaken;

    private Vector3 spawnPosition;
    private Quaternion spawnRotation;

    private bool gameOver;

    private void Start()
    {
        // Maç kurulumu (taş/kamera/skor tablosu vs.) burada değil,
        // Play Local'e gerçekten basılınca BeginMatch()'te yapılır
        // — menüdeyken boşuna çalışıp kaynak tüketmesin.
        if (resultUI != null)
        {
            resultUI.PlayAgainClicked += HandlePlayAgainClicked;
        }
    }

    /// <summary>
    /// Maçı ilk kez kurar — MainMenuUI, oyuncu Play Local'e (ve
    /// varsa isim ekranını) geçtikten sonra bunu çağırır.
    /// </summary>
    public void BeginMatch()
    {
        if (initialStone == null)
            return;

        spawnPosition = initialStone.transform.position;
        spawnRotation = initialStone.transform.rotation;

        currentPlayerIndex = 0;

        activeStone = initialStone;
        activeStone.PlayerIndex = currentPlayerIndex;
        activeStone.SetStoneColor(player1Color);
        activeStone.OnStoneSettled += HandleStoneSettled;

        ApplyTeamToAthletes(currentPlayerIndex);
        ApplyCosmeticsToAthletes();

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(
                activeStone.GetComponent<Rigidbody>()
            );
        }

        ColorizeAll(leftGrandstands, leftSeatColor);
        ColorizeAll(rightGrandstands, rightSeatColor);

        UpdateScoreboard();
    }

    private void HandlePlayAgainClicked()
    {
        resultUI.Hide();
        RestartMatch();
    }

    /// <summary>
    /// Maçı, sahneyi baştan yüklemeden yerinde sıfırlar: skoru,
    /// sırayı ve taşları başa alır — "Play Again" bunu çağırır.
    /// </summary>
    private void RestartMatch()
    {
        CancelInvoke();

        gameOver = false;
        totalThrowsTaken = 0;
        currentPlayerIndex = 0;

        StoneController[] allStones =
            FindObjectsByType<StoneController>(
                FindObjectsInactive.Include
            );

        foreach (StoneController stone in allStones)
        {
            if (stone != initialStone)
            {
                Destroy(stone.gameObject);
            }
        }

        if (activeStone != null)
        {
            activeStone.OnStoneSettled -= HandleStoneSettled;
        }

        initialStone.ResetForNewTurn(spawnPosition, spawnRotation);
        initialStone.PlayerIndex = currentPlayerIndex;
        initialStone.SetStoneColor(player1Color);
        initialStone.OnStoneSettled += HandleStoneSettled;

        activeStone = initialStone;

        ApplyTeamToAthletes(currentPlayerIndex);
        ApplyCosmeticsToAthletes();

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(
                activeStone.GetComponent<Rigidbody>()
            );

            cameraFollow.ResetToStatic();
        }

        UpdateScoreboard();
    }

    /// <summary>
    /// Skorlamanın kullandığı house merkezi — AIOpponent de buraya
    /// nişan alır ki iki taraf aynı noktayı hedeflesin (ikisinde ayrı
    /// birer sabit tutulursa Inspector'da biri değişince AI yanlış
    /// yere atmaya başlıyordu).
    /// </summary>
    public Vector3 HouseCenter => houseCenter;

    public int ThrowsPerPlayer => throwsPerPlayer;

    /// <summary>
    /// Verilen oyuncunun, şu an oynanan atış dahil kaç atışı kaldığı.
    /// Sıra kendisindeyken 1 dönmesi "bu benim son taşım" demektir.
    /// </summary>
    public int ThrowsRemainingFor(int playerIndex)
    {
        // Atışlar 0. oyuncudan başlayarak sırayla yapılıyor: N atış
        // tamamlanmışsa 0. oyuncu ceil(N/2), 1. oyuncu floor(N/2) atmıştır.
        int taken =
            playerIndex == 0
                ? (totalThrowsTaken + 1) / 2
                : totalThrowsTaken / 2;

        return Mathf.Max(0, throwsPerPlayer - taken);
    }

    /// <summary>
    /// Rakibin insan mı yapay zeka mı olduğunu (ve zorluğunu) menüden
    /// ayarlar — BeginMatch()'ten önce çağrılmalı.
    /// </summary>
    public void ConfigureOpponent(bool isAI, AIOpponent.AIDifficulty difficulty)
    {
        player2IsAI = isAI;
        aiDifficulty = difficulty;
    }

    /// <summary>Sıra AI'a geçtiyse atışını başlatır.</summary>
    private void MaybeStartAITurn()
    {
        if (player2IsAI && currentPlayerIndex == 1 && aiOpponent != null)
        {
            aiOpponent.TakeTurn(activeStone, aiDifficulty);
        }
    }

    /// <summary>
    /// Oyuncu isimlerini dışarıdan (isim girme ekranından) ayarlar
    /// ve tabelayı hemen günceller.
    /// </summary>
    public void SetPlayerNames(string firstPlayerName, string secondPlayerName)
    {
        if (!string.IsNullOrWhiteSpace(firstPlayerName))
        {
            player1Name = firstPlayerName;
        }

        if (!string.IsNullOrWhiteSpace(secondPlayerName))
        {
            player2Name = secondPlayerName;
        }

        UpdateScoreboard();
    }

    /// <summary>Atan atleti ve her iki süpürgeciyi (forma + süpürge) sıradaki oyuncunun rengine boyar.</summary>
    private void ApplyTeamToAthletes(int playerIndex)
    {
        if (athlete != null)
        {
            athlete.SetTeam(playerIndex);
        }

        if (leftAthlete != null)
        {
            leftAthlete.SetTeam(playerIndex);
        }

        if (rightAthlete != null)
        {
            rightAthlete.SetTeam(playerIndex);
        }
    }

    /// <summary>Oyuncunun mağazadan kuşandığı eşyaları atan atlete uygular (süpürgelerin kuşanacağı bir şey yok).</summary>
    private void ApplyCosmeticsToAthletes()
    {
        if (cosmeticCatalog == null || athlete == null)
            return;

        foreach (CosmeticSlot slot in System.Enum.GetValues(typeof(CosmeticSlot)))
        {
            CosmeticItem item = cosmeticCatalog.FindById(CosmeticLoadout.GetEquipped(slot));
            athlete.EquipCosmetic(slot, item);
        }
    }

    private void UpdateScoreboard()
    {
        if (scoreboard == null)
            return;

        scoreboard.UpdateTurn(
            player1Name,
            player1Color,
            player2Name,
            player2Color,
            currentPlayerIndex,
            totalThrowsTaken,
            throwsPerPlayer * 2
        );
    }

    private void ColorizeAll(GameObject[] grandstands, Color color)
    {
        if (grandstands == null)
            return;

        foreach (GameObject grandstand in grandstands)
        {
            ColorizeSeats(grandstand, color);
        }
    }

    private void ColorizeSeats(GameObject grandstand, Color color)
    {
        if (grandstand == null)
            return;

        Transform seatTransform =
            FindDeepChild(grandstand.transform, seatObjectName);

        if (seatTransform == null)
        {
            Debug.LogWarning(
                "[TurnManager] '" + seatObjectName +
                "' bulunamadı: " + grandstand.name
            );

            return;
        }

        Renderer seatRenderer =
            seatTransform.GetComponent<Renderer>();

        if (seatRenderer != null)
        {
            seatRenderer.material.color = color;
        }
    }

    private static Transform FindDeepChild(
        Transform parent,
        string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;

            Transform found = FindDeepChild(child, name);

            if (found != null)
                return found;
        }

        return null;
    }

    private void HandleStoneSettled()
    {
        if (activeStone != null)
        {
            activeStone.OnStoneSettled -= HandleStoneSettled;
        }

        totalThrowsTaken++;

        int totalThrowsAllowed = throwsPerPlayer * 2;

        Debug.Log(
            "[TurnManager] Taş durdu. Atış: " +
            totalThrowsTaken + " / " + totalThrowsAllowed
        );

        if (totalThrowsTaken >= totalThrowsAllowed)
        {
            Invoke(nameof(EndGame), nextStoneDelay);
        }
        else
        {
            Invoke(nameof(SpawnNextStone), nextStoneDelay);
        }
    }

    private void SpawnNextStone()
    {
        if (activeStone == null || gameOver)
            return;

        currentPlayerIndex = 1 - currentPlayerIndex;

        ApplyTeamToAthletes(currentPlayerIndex);

        // Duran taş olduğu yerde kalır; yeni tur için
        // onun bir klonu başlangıç noktasında oluşturulur.
        GameObject newStoneObject =
            Instantiate(activeStone.gameObject);

        newStoneObject.name = "curling_stone";

        StoneController newStone =
            newStoneObject.GetComponent<StoneController>();

        if (newStone == null)
        {
            Destroy(newStoneObject);
            return;
        }

        newStone.ResetForNewTurn(spawnPosition, spawnRotation);
        newStone.PlayerIndex = currentPlayerIndex;

        Color nextColor =
            currentPlayerIndex == 0
                ? player1Color
                : player2Color;

        newStone.SetStoneColor(nextColor);

        activeStone = newStone;
        activeStone.OnStoneSettled += HandleStoneSettled;

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(
                activeStone.GetComponent<Rigidbody>()
            );

            cameraFollow.ResetToStatic();
        }

        UpdateScoreboard();
        MaybeStartAITurn();
    }

    private void EndGame()
    {
        gameOver = true;

        Debug.Log("[TurnManager] Oyun bitti, kazanan hesaplanıyor...");

        if (cameraFollow != null)
        {
            cameraFollow.ResetToStatic();
        }

        StoneController[] allStones =
            FindObjectsByType<StoneController>(
                FindObjectsInactive.Exclude
            );

        StoneController closestStone = null;
        float closestDistance = float.MaxValue;

        foreach (StoneController stone in allStones)
        {
            Vector3 stonePos = stone.transform.position;

            float dx = stonePos.x - houseCenter.x;
            float dz = stonePos.z - houseCenter.z;

            float distance =
                Mathf.Sqrt(dx * dx + dz * dz);

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestStone = stone;
            }
        }

        if (closestStone == null)
        {
            Debug.LogWarning("[TurnManager] Hiç taş bulunamadı!");
            return;
        }

        string winnerName =
            closestStone.PlayerIndex == 0
                ? player1Name
                : player2Name;

        Color winnerColor =
            closestStone.PlayerIndex == 0
                ? player1Color
                : player2Color;

        Debug.Log(
            "[TurnManager] Kazanan: " + winnerName +
            " (mesafe " + closestDistance.ToString("0.00") + ")"
        );

        if (scoreboard != null)
        {
            scoreboard.ShowFinal(winnerName, winnerColor);
        }

        if (resultUI == null)
        {
            Debug.LogWarning("[TurnManager] Result UI atanmamış!");
            return;
        }

        string subInfo =
            "Center'a mesafe: " +
            closestDistance.ToString("0.00");

        // AI'a karşı oynanan bir maçta insan (0. oyuncu) kaybettiyse
        // kupa yerine kaybetme afişi, ödül paneli olmadan gösterilir.
        bool isLoss = player2IsAI && closestStone.PlayerIndex != 0;

        // AI'a karşı kazanılan bir maç, zorluğa göre coin kazandırır
        // (kolay az, orta biraz fazla, zor daha fazla).
        int coinsAwarded = 0;

        if (player2IsAI && !isLoss)
        {
            coinsAwarded = Currency.GetMatchReward(aiDifficulty);
            Currency.AddGold(coinsAwarded);
        }

        resultUI.ShowResult(winnerName, winnerColor, subInfo, isLoss, player2IsAI, coinsAwarded);
    }
}
