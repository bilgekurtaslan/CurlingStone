using UnityEngine;

/// <summary>
/// Chill Mode: skor, rakip ya da sıra olmadan sınırsız atış.
/// Taş durunca otomatik olarak yenisi belirir; Reset butonu
/// buzdaki tüm taşları temizleyip baştan başlatır. Sahne/kamera/
/// atlet akışı normal maçtakiyle birebir aynı — sadece TurnManager
/// yerine bu basitleştirilmiş döngü kullanılır.
/// </summary>
public class ChillModeManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StoneController initialStone;
    [SerializeField] private CameraFollowController cameraFollow;
    [SerializeField] private ChillModeUI chillModeUI;
    [SerializeField] private AthleteController throwingAthlete;
    [SerializeField] private BroomSweeperController leftAthlete;
    [SerializeField] private BroomSweeperController rightAthlete;
    [SerializeField] private CosmeticCatalog cosmeticCatalog;

    [Header("Taş Rengi")]
    [SerializeField] private Color stoneColor = new Color(0.3160377f, 0.45993277f, 1f);

    [Header("Zamanlama")]
    [SerializeField] private float nextStoneDelay = 1f;

    private StoneController activeStone;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private bool isActive;

    private void Start()
    {
        if (chillModeUI != null)
        {
            chillModeUI.ResetClicked += HandleResetClicked;
        }
    }

    /// <summary>Chill Mode'u başlatır — MainMenuUI, Chill butonuna basılınca çağırır.</summary>
    public void BeginChillMode()
    {
        if (initialStone == null)
            return;

        isActive = true;

        spawnPosition = initialStone.transform.position;
        spawnRotation = initialStone.transform.rotation;

        // Chill Mode'da renk hep aynı (mavi) kaldığı için takım
        // sadece bir kere, başlangıçta ayarlanır.
        ApplyTeamToAthletes();
        ApplyCosmeticsToAthletes();

        activeStone = initialStone;
        SetUpStone(activeStone);
        activeStone.OnStoneSettled += HandleStoneSettled;

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(activeStone.GetComponent<Rigidbody>());
        }

        if (chillModeUI != null)
        {
            chillModeUI.Show();
        }
    }

    private void SetUpStone(StoneController stone)
    {
        stone.PlayerIndex = 0;
        stone.SetStoneColor(stoneColor);
    }

    /// <summary>Atan atleti ve her iki süpürgeciyi (forma + süpürge) taşla aynı takıma boyar.</summary>
    private void ApplyTeamToAthletes()
    {
        if (throwingAthlete != null)
        {
            throwingAthlete.SetTeam(0);
        }

        if (leftAthlete != null)
        {
            leftAthlete.SetTeam(0);
        }

        if (rightAthlete != null)
        {
            rightAthlete.SetTeam(0);
        }
    }

    /// <summary>Oyuncunun mağazadan kuşandığı eşyaları atan atlete uygular (süpürgelerin kuşanacağı bir şey yok).</summary>
    private void ApplyCosmeticsToAthletes()
    {
        if (cosmeticCatalog == null || throwingAthlete == null)
            return;

        foreach (CosmeticSlot slot in System.Enum.GetValues(typeof(CosmeticSlot)))
        {
            CosmeticItem item = cosmeticCatalog.FindById(CosmeticLoadout.GetEquipped(slot));
            throwingAthlete.EquipCosmetic(slot, item);
        }
    }

    private void HandleStoneSettled()
    {
        if (!isActive)
            return;

        if (activeStone != null)
        {
            activeStone.OnStoneSettled -= HandleStoneSettled;
        }

        Invoke(nameof(SpawnNextStone), nextStoneDelay);
    }

    private void SpawnNextStone()
    {
        if (!isActive || activeStone == null)
            return;

        // Duran taş olduğu yerde kalır; sınırsız atış için başlangıç
        // noktasında yeni bir klon oluşturulur (TurnManager'daki
        // SpawnNextStone ile aynı yaklaşım).
        GameObject newStoneObject = Instantiate(activeStone.gameObject);
        newStoneObject.name = "curling_stone";

        StoneController newStone = newStoneObject.GetComponent<StoneController>();

        if (newStone == null)
        {
            Destroy(newStoneObject);
            return;
        }

        newStone.ResetForNewTurn(spawnPosition, spawnRotation);
        SetUpStone(newStone);

        activeStone = newStone;
        activeStone.OnStoneSettled += HandleStoneSettled;

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(activeStone.GetComponent<Rigidbody>());
            cameraFollow.ResetToStatic();
        }
    }

    /// <summary>Buzdaki tüm taşları temizleyip baştan başlatır — Reset butonu bunu çağırır.</summary>
    private void HandleResetClicked()
    {
        CancelInvoke();

        StoneController[] allStones =
            FindObjectsByType<StoneController>(FindObjectsInactive.Include);

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
        SetUpStone(initialStone);
        initialStone.OnStoneSettled += HandleStoneSettled;

        ApplyTeamToAthletes();
        ApplyCosmeticsToAthletes();

        activeStone = initialStone;

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(activeStone.GetComponent<Rigidbody>());
            cameraFollow.ResetToStatic();
        }
    }
}
