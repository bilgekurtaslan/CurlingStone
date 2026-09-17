using System.Collections;
using UnityEngine;

/// <summary>
/// Tek oyunculu modda rakip oyuncunun atışlarını yönetir. Taşı
/// StoneController'ın AI arayüzü üzerinden (AIEnterAimPhase /
/// AIRelease / AiSweepLeft-Right / AiSteerDirection) sürer, böylece
/// insan atışıyla aynı kamera/video/atlet görselleri kullanılır —
/// sadece nişan ve süpürme kararları farklı bir kaynaktan gelir.
///
/// Güç, sabit bir "ideal" sayı yerine StoneController'ın gerçek
/// yavaşlama formülüyle (SimulateStopDistance) her atışta yeniden
/// hesaplanır — böylece house'a olan mesafe ya da buz fiziği
/// değişse bile AI otomatik kalibre olur.
///
/// KARAR: Oyunun kuralı "sonda merkeze en yakın taş kazanır"
/// olduğu için her atıştan önce tahta okunur (kimin taşı daha
/// yakın, kaç atış kaldı) ve buna göre bir atış tipi seçilir:
/// düz gelme (Draw), blokenin etrafından dolanma (ComeAround),
/// rakibin taşını çıkarma (Takeout) ya da kendi sayı taşının
/// önüne koruma koyma (Guard). Zorluklar bu ağacın ne kadarını
/// kullandıklarıyla ve ne kadar isabetli oldukları ile ayrışır.
/// </summary>
public class AIOpponent : MonoBehaviour
{
    public enum AIDifficulty { Easy, Medium, Hard }

    /// <summary>Bir atışın ne amaçla yapıldığı.</summary>
    private enum ShotType
    {
        /// House merkezine düz gelme.
        Draw,

        /// Yoldaki taşın yanından girip merkeze dönme.
        ComeAround,

        /// Rakibin sayı taşını buzdan çıkarma.
        Takeout,

        /// Kendi sayı taşının önünde durup onu korumaya alma.
        Guard,
    }

    /// AI her zaman 1. oyuncu, insan 0. oyuncu.
    private const int AIPlayerIndex = 1;
    private const int OpponentPlayerIndex = 0;

    [Header("İdeal Yanal Nişan (house merkezinin X'i)")]
    [SerializeField] private float idealLateralX = 0f;

    [Header("Zorluk: Nişan Sapması (± birim)")]
    [SerializeField] private float easyLateralNoise = 1.4f;
    [SerializeField] private float easyPowerNoise = 2.8f;
    [SerializeField] private float mediumLateralNoise = 0.7f;
    [SerializeField] private float mediumPowerNoise = 1.4f;
    [SerializeField] private float hardLateralNoise = 0.15f;
    [SerializeField] private float hardPowerNoise = 0.3f;

    [Header("Kolay: Sistematik Hata (maç boyunca sabit 'kötü alışkanlık')")]
    [Tooltip("Kolay AI her atışta aynı yöne bu kadar kayar — rastgele zar atmak yerine 'hep sola çeken acemi' gibi hissettirir.")]
    [SerializeField] private float easyBiasLateral = 1.1f;
    [Tooltip("Kolay AI'ın maç boyunca sabit güç hatası: eksi kısa, artı uzun atar.")]
    [SerializeField] private float easyBiasPower = 1.4f;

    [Header("Son Taş Konsantrasyonu")]
    [Tooltip("Geride kalmışken oynanan son taşta nişan sapması bu oranla çarpılır (Zor). 1 = fark yok.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float hardClutchPrecision = 0.45f;
    [Tooltip("Aynısının Orta zorluktaki karşılığı.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float mediumClutchPrecision = 0.8f;

    [Header("Zamanlama (insan gibi hissettirsin diye kısa duraklamalar)")]
    [SerializeField] private float preAimDelay = 0.5f;
    [SerializeField] private float aimDelayMin = 0.6f;
    [SerializeField] private float aimDelayMax = 1.1f;

    [Header("Zorluk: Süpürme")]
    [Tooltip("Orta zorluk süpürme kararını bu aralıkla yeniler — anlık değil, gecikmeli tepki verir.")]
    [SerializeField] private float mediumSweepDecisionInterval = 0.4f;
    [Tooltip("Orta zorluk, kalan mesafeyi bu oranda yanlış tahmin eder (0.12 = ±%12). Hata kararın kendisine değil algıya ekleniyor.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float mediumSweepMisread = 0.12f;
    [Tooltip("Orta zorluk süpürgeye ancak yolun bu oranını geçtikten sonra dokunur — gerçek oyuncu gibi taş ölmeye başlayınca süpürür.")]
    [Range(0f, 0.9f)]
    [SerializeField] private float mediumSweepStartFraction = 0.55f;

    [Header("Hedef (mesafe hesabı için — TurnManager varsa oradan okunur)")]
    [SerializeField] private Vector3 houseCenter = new Vector3(0f, 0f, 16f);

    [Header("Bloke Farkındalığı (buzdaki taşları hesaba katar)")]
    [Tooltip("Merkezin bu genişlik kadar solunda/sağında duran, henüz durmuş bir taş 'bloke' sayılır.")]
    [SerializeField] private float blockerSearchWidth = 2.6f;
    [Tooltip("House merkezine bu mesafeden daha uzak duran taşlar bloke sayılmaz.")]
    [SerializeField] private float blockerSearchZRadius = 9f;
    [Tooltip("Bloke varsa, taş ilk önce merkezin bu kadar yanından girer, sonra süpürerek/yönlendirerek merkeze döner.")]
    [SerializeField] private float blockerAvoidOffset = 2.2f;
    [Tooltip("Hedef hattına bu toleransa kadar yaklaşınca yönlendirme bırakılır.")]
    [SerializeField] private float centerTolerance = 0.25f;
    [Tooltip("Merkeze dönerken yönlendirme gücü ne kadar 'keskin' olsun (büyüdükçe daha hızlı toparlar).")]
    [SerializeField] private float steerSharpness = 2.5f;

    [Header("Zorluk: Süpürerek Tamamlama (sadece Zor)")]
    [Tooltip("Zor zorlukta güç, hedeften bilerek bu kadar az hesaplanır — taş süpürülmezse kısa kalır, süpürülürse tam hedefe ulaşır. Kalibrasyon 'mükemmel' olduğunda süpürgenin hiç gerekmemesini önler.")]
    [SerializeField] private float hardUndershootMargin = 2.5f;
    [Tooltip("Zor zorlukta düz atışlarda, pistin bu oranından sonra nişan hatası yönlendirmeyle kısmen düzeltilir (0.5 = yarı yoldan sonra).")]
    [Range(0f, 1f)]
    [SerializeField] private float hardDrawSteerStart = 0.5f;

    [Header("Strateji: Taş Çıkarma")]
    [Tooltip("House merkezine bu mesafenin içindeki taşlar 'skor pozisyonunda' sayılır.")]
    [SerializeField] private float houseRadius = 3.2f;
    [Tooltip("Rakibin taşını gerçekten devirebilmek için, hedeften bu kadar fazlasına güç verilir.")]
    [SerializeField] private float takeoutOvershoot = 4f;
    [Tooltip("Bu genişlikten daha kenardaki bir rakip taş, güvenilir vurulamayacağı için çıkarma hedefi seçilmez.")]
    [SerializeField] private float takeoutMaxLateralX = 2.8f;
    [Tooltip("Çıkarma hattının yarı genişliği — bu koridorda başka bir taş varsa (kendi taşımız dahil) atış yapılmaz.")]
    [SerializeField] private float takeoutLaneWidth = 1f;
    [Tooltip("Orta zorluk çıkarmayı ancak kendi son bu kadar taşında düşünür.")]
    [SerializeField] private int mediumTakeoutMaxThrowsLeft = 2;

    [Header("Strateji: Koruma Taşı (sadece Zor)")]
    [Tooltip("Koruma taşı, korunan taşın bu kadar önünde durur.")]
    [SerializeField] private float guardStandoff = 3.5f;
    [Tooltip("Öndeyken koruma oynama olasılığı — her seferinde aynı şeyi yapıp tahmin edilebilir olmasın diye.")]
    [Range(0f, 1f)]
    [SerializeField] private float guardChance = 0.7f;
    [Tooltip("Korunacak taş, atış noktasından en az bu kadar uzakta olmalı (yoksa koruma taşı için yer kalmaz).")]
    [SerializeField] private float guardMinRun = 6f;

    /// <summary>Tahtanın atış öncesi okunmuş hali.</summary>
    private struct BoardState
    {
        public StoneController BestOwn;
        public float BestOwnDistance;

        public StoneController BestOpponent;
        public float BestOpponentDistance;

        /// Şu an oynanan atış dahil, AI'ın kalan taş sayısı.
        public int MyThrowsLeft;

        /// Bu atıştan sonra rakibin kalan taş sayısı.
        public int OpponentThrowsLeft;

        public bool IsLeading =>
            BestOwn != null && (BestOpponent == null || BestOwnDistance < BestOpponentDistance);

        public bool IsBehind =>
            BestOpponent != null && (BestOwn == null || BestOpponentDistance < BestOwnDistance);
    }

    private TurnManager turnManager;

    /// Kolay AI'ın maç boyunca değişmeyen sistematik hatası.
    private float easyLateralBias;
    private float easyPowerBias;

    private void Awake()
    {
        RollEasyBias();
    }

    /// <summary>Sıradaki taşı belirtilen zorlukla oynatır.</summary>
    public void TakeTurn(StoneController stone, AIDifficulty difficulty)
    {
        if (stone == null)
            return;

        StartCoroutine(RunTurn(stone, difficulty));
    }

    private IEnumerator RunTurn(StoneController stone, AIDifficulty difficulty)
    {
        yield return new WaitForSeconds(preAimDelay);

        if (stone == null)
            yield break;

        BoardState board = ReadBoard(stone);

        ShotPlan plan = PlanShot(stone, difficulty, board);

        GetNoise(difficulty, out float lateralNoise, out float powerNoise);

        // Geride kalınmış son taşta AI daha dikkatli nişan alır —
        // maçın kaderi o atışa bağlıyken hatanın aynı kalması
        // "umursamıyor" gibi hissettiriyordu.
        if (board.IsBehind && board.MyThrowsLeft <= 1)
        {
            float clutch = GetClutchPrecision(difficulty);
            lateralNoise *= clutch;
            powerNoise *= clutch;
        }

        float calibratedPower = CalibratePower(stone, plan.TargetDistance);

        float lateralX = plan.EntryLateralX + GetBiasedError(difficulty, lateralNoise, easyLateralBias);
        float power = calibratedPower + GetBiasedError(difficulty, powerNoise, easyPowerBias);

        stone.IsAIControlled = true;
        stone.AIEnterAimPhase(lateralX);

        yield return new WaitForSeconds(Random.Range(aimDelayMin, aimDelayMax));

        if (stone == null)
            yield break;

        stone.AIRelease(power);

        yield return new WaitUntil(() => stone == null || stone.HasBeenThrown);

        yield return RunSweeping(stone, difficulty, plan);
    }

    /// <summary>
    /// Taş kayarken her karede süpürme ve yönlendirme kararlarını
    /// tazeler — plan (nereye durmak istediği) atış öncesinden gelir.
    /// </summary>
    private IEnumerator RunSweeping(StoneController stone, AIDifficulty difficulty, ShotPlan plan)
    {
        float decisionTimer = 0f;
        bool sweepDecision = false;

        while (stone != null && !stone.IsFinished)
        {
            switch (difficulty)
            {
                case AIDifficulty.Easy:
                    // Kolay seviye süpürmeyi hiç kullanmaz.
                    sweepDecision = false;
                    break;

                case AIDifficulty.Medium:
                    decisionTimer -= Time.deltaTime;

                    if (decisionTimer <= 0f)
                    {
                        sweepDecision = DecideMediumSweep(stone, plan);
                        decisionTimer = mediumSweepDecisionInterval;
                    }

                    break;

                default:
                    // Zor zorluk her karede gerçek projeksiyona bakar:
                    // süpürmeden hedefe yetişemiyorsa süpürür, yetişiyorsa
                    // bırakır — hedefi aşmaması da bu sayede kendiliğinden olur.
                    sweepDecision = ShouldSweep(stone, plan.SweepTargetZ);
                    break;
            }

            stone.AiSweepLeft = sweepDecision;
            stone.AiSweepRight = sweepDecision;

            // Yönlendirme (insanın basılı tutup çekmesiyle aynı mekanik):
            // plan hangi derinlikten sonra hedefe çekileceğini söyler —
            // blokeyi geçtikten sonra, ya da Zor'da pistin yarısından sonra.
            if (stone.transform.position.z >= plan.SteerStartZ)
            {
                float xError = plan.TargetLateralX - stone.transform.position.x;

                stone.AiSteerDirection =
                    Mathf.Abs(xError) > centerTolerance
                        ? Mathf.Clamp(xError * plan.SteerSharpness, -1f, 1f)
                        : 0f;
            }
            else
            {
                stone.AiSteerDirection = 0f;
            }

            yield return null;
        }
    }

    /// <summary>Taş, süpürülmeden hedef derinliğe yetişemeyecekse true.</summary>
    private static bool ShouldSweep(StoneController stone, float targetZ)
    {
        float remaining = targetZ - stone.transform.position.z;

        if (remaining <= 0f)
            return false;

        return stone.SimulateStopDistance(stone.CurrentSpeed) < remaining;
    }

    /// <summary>
    /// Orta zorluğun süpürme kararı. İki yerde Zor'dan ayrılır:
    /// (1) süpürgeye ancak taş yolun sonuna yaklaşınca dokunur —
    /// gerçek oyuncu gibi; baştan süpürmek taş hızlıyken çok mesafe
    /// kattığı için hedefi aşırtıyordu. (2) kalan mesafeyi bir miktar
    /// yanlış okur. Hata bilerek karara değil algıya ekleniyor:
    /// "süpür/süpürme" kararını rastgele ters çevirmek, doğru karar
    /// çoğunlukla "süpürme" olduğu için taşı sistematik olarak
    /// uzatıyor ve Orta'yı Kolay'dan bile kötü yapıyordu.
    /// </summary>
    private bool DecideMediumSweep(StoneController stone, ShotPlan plan)
    {
        float stoneZ = stone.transform.position.z;
        float remaining = plan.SweepTargetZ - stoneZ;

        if (remaining <= 0f)
            return false;

        float total = plan.SweepTargetZ - plan.ReleaseZ;

        if (total > 0f && (stoneZ - plan.ReleaseZ) < total * mediumSweepStartFraction)
            return false;

        float perceived =
            remaining * (1f + Random.Range(-mediumSweepMisread, mediumSweepMisread));

        return stone.SimulateStopDistance(stone.CurrentSpeed) < perceived;
    }

    // =========================================
    // ATIŞ PLANI
    // =========================================

    /// <summary>Atış öncesi hesaplanan, taş durana kadar geçerli plan.</summary>
    private struct ShotPlan
    {
        /// Taşın bırakıldığı yanal konum.
        public float EntryLateralX;

        /// Yönlendirmenin çekmeye çalıştığı yanal konum.
        public float TargetLateralX;

        /// Bu derinlikten sonra yönlendirme devreye girer.
        public float SteerStartZ;

        public float SteerSharpness;

        /// Güç kalibrasyonunun hedeflediği mesafe.
        public float TargetDistance;

        /// Süpürme kararının "yetişmem gereken yer" olarak kullandığı derinlik.
        public float SweepTargetZ;

        /// Taşın bırakıldığı derinlik — yolun ne kadarının katedildiğini ölçmek için.
        public float ReleaseZ;
    }

    private ShotPlan PlanShot(StoneController stone, AIDifficulty difficulty, BoardState board)
    {
        Vector3 center = GetHouseCenter();
        float stoneZ = stone.transform.position.z;

        ShotPlan plan = new ShotPlan
        {
            EntryLateralX = idealLateralX,
            TargetLateralX = idealLateralX,
            SteerStartZ = float.MaxValue,
            SteerSharpness = steerSharpness,
            TargetDistance = center.z - stoneZ,
            SweepTargetZ = center.z,
            ReleaseZ = stoneZ,
        };

        ShotType shot = ChooseShot(stone, difficulty, board, out StoneController target);

        switch (shot)
        {
            case ShotType.Takeout:
                // Rakibin taşını doğrudan hedef al, devirmek için olması
                // gerekenden biraz daha güçlü at — düz ve sert vurulur,
                // yönlendirme devreye girmez.
                plan.TargetLateralX = target.transform.position.x;
                plan.EntryLateralX = plan.TargetLateralX;
                plan.TargetDistance =
                    (target.transform.position.z - stoneZ) + takeoutOvershoot;
                plan.SweepTargetZ = target.transform.position.z;
                break;

            case ShotType.Guard:
                // Kendi sayı taşının önünde, aynı hat üzerinde dur ki
                // insan onu çıkarmak için temiz bir yol bulamasın.
                plan.TargetLateralX = target.transform.position.x;
                plan.EntryLateralX = plan.TargetLateralX;
                plan.TargetDistance =
                    (target.transform.position.z - stoneZ) - guardStandoff;
                plan.SweepTargetZ = target.transform.position.z - guardStandoff;
                break;

            case ShotType.ComeAround:
                {
                    float blockerX = target.transform.position.x;
                    float side = blockerX >= plan.TargetLateralX ? -1f : 1f;

                    plan.EntryLateralX = plan.TargetLateralX + side * blockerAvoidOffset;
                    plan.SteerStartZ = target.transform.position.z;

                    if (difficulty == AIDifficulty.Hard)
                    {
                        plan.TargetDistance -= hardUndershootMargin;
                    }
                }

                break;

            default:
                if (difficulty == AIDifficulty.Hard)
                {
                    // Bilerek biraz kısa hesaplanır; mesafeyi süpürerek
                    // tamamlamak düz "mükemmel" bir atıştan hem daha
                    // gerçekçi hem de ekranda görünür bir davranış.
                    plan.TargetDistance -= hardUndershootMargin;

                    // Zor seviye, düz atışlarda da pistin ikinci yarısında
                    // nişan hatasını yönlendirmeyle kısmen toparlar. Düzeltme
                    // taşın hızıyla orantılı olduğu için hata tamamen
                    // kapanmaz — usta ama kusursuz olmayan bir his verir.
                    plan.SteerStartZ = stoneZ + (center.z - stoneZ) * hardDrawSteerStart;
                    plan.SteerSharpness = steerSharpness * 0.5f;
                }

                break;
        }

        return plan;
    }

    /// <summary>
    /// Tahtaya ve zorluğa bakarak atış tipini seçer.
    /// </summary>
    private ShotType ChooseShot(
        StoneController stone,
        AIDifficulty difficulty,
        BoardState board,
        out StoneController target)
    {
        target = null;

        // Kolay zorluk, buzdaki diğer taşları hesaba katacak
        // kadar "akıllı" değil — her zaman doğrudan nişan alır.
        if (difficulty == AIDifficulty.Easy)
            return ShotType.Draw;

        if (difficulty == AIDifficulty.Hard)
        {
            if (board.IsBehind)
            {
                target = FindTakeoutTarget(stone, board);

                if (target != null)
                    return ShotType.Takeout;
            }
            else if (board.IsLeading
                     && board.OpponentThrowsLeft > 0
                     && CanGuard(stone, board.BestOwn)
                     && Random.value < guardChance)
            {
                target = board.BestOwn;
                return ShotType.Guard;
            }
        }
        else
        {
            // Orta seviye çıkarmayı ancak geride kaldığında ve son
            // taşlarında düşünür; sürekli vurmaya kalkarsa Zor'dan
            // ayırt edilemez hale geliyordu.
            if (board.IsBehind && board.MyThrowsLeft <= mediumTakeoutMaxThrowsLeft)
            {
                target = FindTakeoutTarget(stone, board);

                if (target != null)
                    return ShotType.Takeout;
            }
        }

        StoneController blocker = FindNearestBlocker(stone, idealLateralX);

        if (blocker != null)
        {
            target = blocker;
            return ShotType.ComeAround;
        }

        return ShotType.Draw;
    }

    /// <summary>
    /// Koruma taşı, korunacak taşın yeterince önünde durabiliyorsa ve
    /// o taş henüz korunmuyorsa anlamlıdır.
    /// </summary>
    private bool CanGuard(StoneController stone, StoneController protectedStone)
    {
        if (protectedStone == null)
            return false;

        float run = protectedStone.transform.position.z - stone.transform.position.z;

        if (run < guardStandoff + guardMinRun)
            return false;

        return !IsGuarded(protectedStone);
    }

    /// <summary>Verilen taşın önünde, aynı hatta duran başka bir taş var mı.</summary>
    private bool IsGuarded(StoneController protectedStone)
    {
        foreach (StoneController other in GetSettledStones())
        {
            if (other == protectedStone)
                continue;

            if (Mathf.Abs(other.transform.position.x - protectedStone.transform.position.x) > takeoutLaneWidth)
                continue;

            float gap = protectedStone.transform.position.z - other.transform.position.z;

            if (gap > 0.5f && gap < guardStandoff * 2f)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Çıkarma hedefi: rakibin house içindeki en iyi taşı — ancak
    /// kenarda değilse ve vuruş hattı boşsa. Hat kontrolü olmadan AI
    /// bazen önündeki kendi taşını deviriyordu.
    /// </summary>
    private StoneController FindTakeoutTarget(StoneController self, BoardState board)
    {
        StoneController candidate = board.BestOpponent;

        if (candidate == null)
            return null;

        if (board.BestOpponentDistance > houseRadius)
            return null;

        if (Mathf.Abs(candidate.transform.position.x - GetHouseCenter().x) > takeoutMaxLateralX)
            return null;

        if (!IsLaneClear(self, candidate))
            return null;

        return candidate;
    }

    /// <summary>Atış noktasıyla hedef arasında, vuruş koridorunda başka taş var mı.</summary>
    private bool IsLaneClear(StoneController self, StoneController target)
    {
        float laneX = target.transform.position.x;
        float fromZ = self.transform.position.z;
        float toZ = target.transform.position.z;

        foreach (StoneController other in GetSettledStones())
        {
            if (other == self || other == target)
                continue;

            float z = other.transform.position.z;

            if (z <= fromZ || z >= toZ)
                continue;

            if (Mathf.Abs(other.transform.position.x - laneX) <= takeoutLaneWidth)
                return false;
        }

        return true;
    }

    private StoneController FindNearestBlocker(StoneController self, float targetX)
    {
        Vector3 center = GetHouseCenter();

        StoneController nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (StoneController other in GetSettledStones())
        {
            if (other == self)
                continue;

            if (Mathf.Abs(other.transform.position.x - targetX) > blockerSearchWidth)
                continue;

            float distanceToHouse =
                Mathf.Abs(other.transform.position.z - center.z);

            if (distanceToHouse > blockerSearchZRadius)
                continue;

            if (distanceToHouse < nearestDistance)
            {
                nearestDistance = distanceToHouse;
                nearest = other;
            }
        }

        return nearest;
    }

    // =========================================
    // TAHTA OKUMA
    // =========================================

    private BoardState ReadBoard(StoneController self)
    {
        BoardState board = new BoardState
        {
            BestOwnDistance = float.MaxValue,
            BestOpponentDistance = float.MaxValue,
            MyThrowsLeft = 1,
            OpponentThrowsLeft = 0,
        };

        foreach (StoneController other in GetSettledStones())
        {
            if (other == self)
                continue;

            float distance = HouseDistance(other);

            if (other.PlayerIndex == AIPlayerIndex)
            {
                if (distance < board.BestOwnDistance)
                {
                    board.BestOwnDistance = distance;
                    board.BestOwn = other;
                }
            }
            else if (distance < board.BestOpponentDistance)
            {
                board.BestOpponentDistance = distance;
                board.BestOpponent = other;
            }
        }

        TurnManager manager = ResolveTurnManager();

        if (manager != null)
        {
            board.MyThrowsLeft = manager.ThrowsRemainingFor(AIPlayerIndex);
            board.OpponentThrowsLeft = manager.ThrowsRemainingFor(OpponentPlayerIndex);
        }

        return board;
    }

    /// <summary>Buzda durmuş (artık hareket etmeyen) bütün taşlar.</summary>
    private static StoneController[] GetSettledStones()
    {
        StoneController[] all =
            FindObjectsByType<StoneController>(FindObjectsInactive.Exclude);

        int count = 0;

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].IsFinished)
            {
                all[count] = all[i];
                count++;
            }
        }

        System.Array.Resize(ref all, count);

        return all;
    }

    private float HouseDistance(StoneController stone)
    {
        Vector3 center = GetHouseCenter();

        float dx = stone.transform.position.x - center.x;
        float dz = stone.transform.position.z - center.z;

        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Skorlamayla aynı house merkezi kullanılsın diye önce
    /// TurnManager'a sorulur; bulunamazsa buradaki değer kullanılır.
    /// </summary>
    private Vector3 GetHouseCenter()
    {
        TurnManager manager = ResolveTurnManager();

        return manager != null ? manager.HouseCenter : houseCenter;
    }

    private TurnManager ResolveTurnManager()
    {
        if (turnManager == null)
        {
            turnManager = FindAnyObjectByType<TurnManager>();
        }

        return turnManager;
    }

    // =========================================
    // NİŞAN HATASI
    // =========================================

    /// <summary>
    /// Verilen mesafeye ulaşmak için gereken gücü, StoneController'ın
    /// gerçek yavaşlama formülü üzerinde ikili arama yaparak bulur —
    /// elle tahmin edilen sabit bir "ideal güç" yerine.
    /// </summary>
    private float CalibratePower(StoneController stone, float targetDistance)
    {
        float low = stone.MinPower;
        float high = stone.MaxPower;

        for (int i = 0; i < 20; i++)
        {
            float mid = (low + high) * 0.5f;
            float distance = stone.SimulateStopDistance(mid);

            if (distance < targetDistance)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return Mathf.Clamp((low + high) * 0.5f, stone.MinPower, stone.MaxPower);
    }

    /// <summary>
    /// Atış hatası: Kolay'da maç boyunca sabit bir sistematik kayma +
    /// rastgele sapma, diğerlerinde sadece rastgele sapma. Rastgelelik
    /// düz dağılım yerine üçgen dağılım (iki örneğin ortalaması) —
    /// uç hatalar seyrekleşince "zar atıyor" hissi kayboluyor.
    /// </summary>
    private float GetBiasedError(AIDifficulty difficulty, float noise, float bias)
    {
        float random = (Random.Range(-noise, noise) + Random.Range(-noise, noise)) * 0.5f;

        return difficulty == AIDifficulty.Easy ? bias + random : random;
    }

    private void RollEasyBias()
    {
        easyLateralBias = Random.Range(-easyBiasLateral, easyBiasLateral);
        easyPowerBias = Random.Range(-easyBiasPower, easyBiasPower);
    }

    private float GetClutchPrecision(AIDifficulty difficulty)
    {
        switch (difficulty)
        {
            case AIDifficulty.Hard:
                return hardClutchPrecision;

            case AIDifficulty.Medium:
                return mediumClutchPrecision;

            default:
                return 1f;
        }
    }

    private void GetNoise(
        AIDifficulty difficulty,
        out float lateralNoise,
        out float powerNoise)
    {
        switch (difficulty)
        {
            case AIDifficulty.Easy:
                lateralNoise = easyLateralNoise;
                powerNoise = easyPowerNoise;
                break;

            case AIDifficulty.Hard:
                lateralNoise = hardLateralNoise;
                powerNoise = hardPowerNoise;
                break;

            default:
                lateralNoise = mediumLateralNoise;
                powerNoise = mediumPowerNoise;
                break;
        }
    }
}
