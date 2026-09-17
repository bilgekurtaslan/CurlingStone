using UnityEngine;
using UnityEngine.InputSystem;

public class StoneController : MonoBehaviour
{
    [Header("Position Settings")]
    [SerializeField] private float moveSpeed = 0.01f;
    [SerializeField] private float minX = -3.5f;
    [SerializeField] private float maxX = 3.5f;

    [Header("Throw Settings")]
    [SerializeField] private float minPower = 5f;
    [SerializeField] private float maxPower = 23f;
    [SerializeField] private float swipeMultiplier = 0.065f;

    [Tooltip("Çekiş gücünün fırlatma hızına dönüşme eğrisi — 1 = doğrusal, büyüdükçe zayıf/orta çekişler orantısız şekilde daha yavaş çıkar, sadece sonuna kadar çekilince (full) hız maksimuma yaklaşır.")]
    [SerializeField] private float powerCurveExponent = 1.6f;

    [Header("Speed UI")]
    [SerializeField] private SpeedGaugeUI speedGauge;

    [Header("Kamera")]
    [SerializeField] private CameraFollowController cameraFollow;

    [Header("Sweep Steering")]
    [SerializeField] private float steerHoldSpeed = 1f;
    [SerializeField] private float maxSteerSpeed = 1f;

    [Header("Sweep Friction Reduction")]
    [Range(0f, 0.95f)]
    [SerializeField] private float sweepFrictionReduction = 0.6f;

    [Header("Atan Atlet (top elden çıkana kadar tek başına çömelmiş görünür)")]
    [Tooltip("Nişan alma sırasında görünen, çömelme pozundaki tek atlet. Top elden çıkar çıkmaz (atış animasyonuyla birlikte) gizlenir; süpürgeciler onun yerini alır.")]
    [SerializeField] private AthleteController throwingAthlete;

    [Header("Süpürgeler (taşla birlikte hareket eder — artık atlet yok, sadece süpürge)")]
    [Tooltip("Taşı hem ileri/geri hem sağa/sola takip eden sol süpürge.")]
    [SerializeField] private BroomSweeperController leftAthlete;

    [Tooltip("Taşı hem ileri/geri hem sağa/sola takip eden sağ süpürge.")]
    [SerializeField] private BroomSweeperController rightAthlete;

    [Tooltip("Ekranın bu oranından soluna basılırsa sadece soldaki süpürgeci süpürür.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float leftSweepZone = 0.4f;

    [Tooltip("Ekranın bu oranından sağına basılırsa sadece sağdaki süpürgeci süpürür. İkisi arasında (öne) basılırsa ikisi de süpürür.")]
    [Range(0.5f, 1f)]
    [SerializeField] private float rightSweepZone = 0.6f;

    [Header("El Görseli (Nişan Alma)")]
    [SerializeField] private Transform handVisual;
    [SerializeField] private Vector3 handRestOffset = new Vector3(0f, 0.15f, 0.05f);
    [SerializeField] private float handMaxPullBack = 0.6f;

    [Header("Atış Animasyonu (tam ekran video)")]
    [Tooltip("Atılınca oynar; video bitene kadar taş hareket etmez.")]
    [SerializeField] private VideoOverlayController throwAnimation;

    [Header("Stopping")]
    [Tooltip("Taşın buzda ne kadar hızlı yavaşlayacağı (üstel/exponansiyel sönüm oranı, saniyede). Büyüdükçe taş daha çabuk durur.")]
    [SerializeField] private float iceDeceleration = 0.55f;

    [Tooltip("Hız bu değerin altına inince ek bir fren yumuşakça devreye girmeye başlar — taş son kısımda uzun süre sürünmesin diye.")]
    [SerializeField] private float lowSpeedThreshold = 5.5f;

    [Tooltip("Hız sıfıra yaklaştıkça ek olarak eklenen en yüksek sönüm oranı (yumuşakça, kareli eğriyle artar — ani durma hissi vermez).")]
    [SerializeField] private float lowSpeedExtraDeceleration = 3.4f;

    [SerializeField] private float fullStopSpeed = 0.05f;

    [Header("Motion Trail (Ice VFX)")]
    [Tooltip("Taş kayarken arkasında bırakılan buz/kar parçacık efekti (Magic VFX - Ice paketinden ayıklanan StoneIceTrail prefab'ı). Taşın child'ı olarak Editor'de yerleştirilmeli.")]
    [SerializeField] private ParticleSystem iceTrailEffect;

    /// <summary>Taş tamamen durduğunda bir kere tetiklenir.</summary>
    public event System.Action OnStoneSettled;

    /// <summary>Bu taşın kime (0 veya 1) ait olduğu.</summary>
    public int PlayerIndex { get; set; }

    /// <summary>true ise nişan/atış/süpürme pointer'dan değil, AIOpponent'tan gelir.</summary>
    public bool IsAIControlled { get; set; }

    /// <summary>AIOpponent, süpürme kararını her karede buraya yazar.</summary>
    public bool AiSweepLeft { get; set; }

    /// <summary>AIOpponent, süpürme kararını her karede buraya yazar.</summary>
    public bool AiSweepRight { get; set; }

    /// <summary>AIOpponent'ın taşı bloke eden bir taşın etrafından merkeze çekmek için yazdığı yön (-1..1, 0 = düz).</summary>
    public float AiSteerDirection { get; set; }

    public bool HasBeenThrown => hasBeenThrown;
    public bool IsFinished => isFinished;
    public float CurrentSpeed => rb != null ? rb.linearVelocity.magnitude : 0f;
    public float MinPower => minPower;
    public float MaxPower => maxPower;

    /// <summary>
    /// Gerçek fizik motorunu çalıştırmadan, FixedUpdate'teki aynı
    /// üstel yavaşlama formülüyle verilen güçte atılan bir taşın
    /// buzda ne kadar ilerleyip duracağını önceden hesaplar.
    /// AIOpponent, bunu istediği mesafeye ulaşacak gücü bulmak
    /// (ikili arama ile) için kullanır.
    /// </summary>
    public float SimulateStopDistance(float power)
    {
        float speed = power;
        float distance = 0f;
        const float dt = 0.02f;

        for (int i = 0; i < 5000 && speed > fullStopSpeed; i++)
        {
            float lowSpeedT =
                lowSpeedThreshold > 0f
                    ? Mathf.Clamp01(1f - speed / lowSpeedThreshold)
                    : 0f;

            float deceleration =
                iceDeceleration + lowSpeedExtraDeceleration * lowSpeedT * lowSpeedT;

            distance += speed * dt;
            speed *= Mathf.Exp(-deceleration * dt);
        }

        return distance;
    }

    private Rigidbody rb;
    private Camera mainCamera;
    private Renderer[] stoneRenderers;

    private Vector2 pointerStart;
    private Vector3 aimRestPosition;

    private bool isDragging = false;
    private bool isPositioning = true;
    private bool hasBeenThrown = false;
    private bool isSweepActive = false;

    // Taş durduktan sonra bu taş artık hiçbir şeye
    // karışmaz (süpürge/atlet gibi ortak nesneleri
    // yeni taşla çekişerek sıfırlamasın diye).
    private bool isFinished = false;

    // Atış animasyonu (tam ekran video) oynarken taş
    // fiziksel olarak hareket etmez, girdi de alınmaz.
    private bool isAnimatingThrow = false;

    // Klonlanan taşlar el offsetini yeniden hesaplamasın:
    // Instantiate sırasında klon, eski taşın durduğu yerde
    // oluşuyor ve oradan hesaplanan offset tamamen yanlış olur.
    // Serialize edildiği için değer klona aynen kopyalanır.
    [HideInInspector]
    [SerializeField] private bool handOffsetCaptured = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        mainCamera = Camera.main;

        // Yavaşlama tamamen kod tarafında (üstel) yönetiliyor —
        // Rigidbody'nin kendi Drag'i araya girip iki kat
        // yavaşlatmasın diye sıfırlanır.
        rb.linearDamping = 0f;

        if (speedGauge == null)
        {
            speedGauge =
                FindAnyObjectByType<SpeedGaugeUI>();
        }

        if (cameraFollow == null)
        {
            cameraFollow =
                FindAnyObjectByType<CameraFollowController>();
        }

        if (throwAnimation == null)
        {
            throwAnimation =
                FindAnyObjectByType<VideoOverlayController>();
        }

        // Taş başlangıçta fizik tarafından hareket etmesin.
        rb.isKinematic = true;

        // Hız göstergesini gizle.
        if (speedGauge != null)
        {
            speedGauge.SetGaugeVisible(false);
            speedGauge.SetSpeed(
                0f,
                maxPower
            );
        }

        if (iceTrailEffect != null)
        {
            iceTrailEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        stoneRenderers = GetComponentsInChildren<Renderer>();

        // Süpürgeciler taş atılana kadar gizli — sadece atış
        // sonrası kayma aşamasında görünürler.
        SetSweepersActive(false);

        // Atan atlet en başta görünsün.
        if (throwingAthlete != null)
        {
            throwingAthlete.gameObject.SetActive(true);
        }

        // El görseli başta gizli, taş fırlatılana kadar
        // sadece nişan alma aşamasında görünecek. Editor'de
        // (Play başlamadan önce) elin taşa göre nereye
        // yerleştirildiği burada okunup dinlenme konumu
        // olarak kullanılır — yani elin konumu Inspector'dan/
        // Scene view'dan elle ayarlanabilir.
        if (handVisual != null)
        {
            if (!handOffsetCaptured)
            {
                handRestOffset =
                    handVisual.position - transform.position;

                handOffsetCaptured = true;
            }

            handVisual.gameObject.SetActive(false);
        }
    }

    /// <summary>Taşın (ve buz izinin) rengini değiştirir (oyuncu ayrımı için).</summary>
    public void SetStoneColor(Color color)
    {
        if (stoneRenderers != null)
        {
            foreach (Renderer stoneRenderer in stoneRenderers)
            {
                if (stoneRenderer == null)
                    continue;

                stoneRenderer.material.color = color;
            }
        }

        TintIceTrail(color);
    }

    /// <summary>
    /// Buz izindeki tüm alt parçacık sistemlerinin (kar taneleri, buz
    /// damlaları vs.) başlangıç rengini takım rengine boyar — kendi
    /// orijinal saydamlığı (alpha) korunur, sadece ton değişir.
    /// </summary>
    private void TintIceTrail(Color color)
    {
        if (iceTrailEffect == null)
            return;

        ParticleSystem[] systems =
            iceTrailEffect.GetComponentsInChildren<ParticleSystem>(true);

        foreach (ParticleSystem system in systems)
        {
            ParticleSystem.MainModule main = system.main;

            // Düşük alpha, parlak (spot ışıklı) zeminle karışıp parçacığı
            // soluk gösteriyordu — yüksek/sabit bir alpha ile arka plan
            // ne kadar parlak olursa olsun renk korunuyor.
            main.startColor = new Color(color.r, color.g, color.b, 0.95f);
        }
    }

    /// <summary>
    /// Yeni bir tur için taşı başlangıç noktasına, fırlatılmamış
    /// duruma sıfırlar (yeni bir taş klonu üzerinde çağrılmalı).
    /// </summary>
    public void ResetForNewTurn(
        Vector3 spawnPosition,
        Quaternion spawnRotation)
    {
        transform.position = spawnPosition;
        transform.rotation = spawnRotation;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;

        isPositioning = true;
        isDragging = false;
        hasBeenThrown = false;
        isSweepActive = false;
        isFinished = false;
        isAnimatingThrow = false;

        if (iceTrailEffect != null)
        {
            iceTrailEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (speedGauge != null)
        {
            speedGauge.SetGaugeVisible(false);
            speedGauge.SetSpeed(0f, maxPower);
        }

        ReturnSweepersHome();

        // Yeni tur: atan atlet tekrar görünsün.
        if (throwingAthlete != null)
        {
            throwingAthlete.gameObject.SetActive(true);
            throwingAthlete.ReplayCrouch();
        }

        if (handVisual != null)
        {
            StopAllCoroutines();
            handVisual.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        // Durmuş taş artık pasif: süpürgeyi/atleti
        // sıradaki taşın elinden almasın.
        if (isFinished)
            return;

        // Atış animasyonu oynarken ne girdi alınır ne de taş
        // hareket eder.
        if (isAnimatingThrow)
            return;

        if (hasBeenThrown)
        {
            HandleSweep();
        }
        else if (!IsAIControlled)
        {
            HandleInput();
        }
    }

    private void FixedUpdate()
    {
        if (!hasBeenThrown || rb.isKinematic)
            return;

        float speed = rb.linearVelocity.magnitude;

        if (speed <= 0f)
            return;

        // Çok yavaşladıysa sonsuza kadar sürünmesin,
        // tamamen durdur.
        if (speed < fullStopSpeed)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            isSweepActive = false;

            if (iceTrailEffect != null)
            {
                // Durunca hemen temizlenir — parçacık ömrü yoğunlaştırma
                // sonrası epey uzadı, doğal solmaya bırakılırsa taş
                // durduktan çok sonra bile sahnede "kar birikintisi"
                // gibi kalıyordu.
                iceTrailEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            // Ortak görselleri sadece ilk duruşta topla:
            // sonradan başka bir taş çarpıp bu taşı
            // kaydırırsa, aktif taşın süpürmesine karışmasın.
            if (!isFinished)
            {
                isFinished = true;

                ReturnSweepersHome();

                OnStoneSettled?.Invoke();
            }

            return;
        }

        // Taş buzda tek bir üstel (exponansiyel) eğriyle yavaşlar
        // — hızdan bağımsız olarak her fizik adımında sabit bir
        // yüzde kaybeder, bu yüzden başta hızlı, sona doğru
        // yumuşakça sönümlenerek durur. Süpürme, taşı itmez;
        // sadece bu sönüm oranını azaltarak kaymayı uzatır.
        // Hız düşükken yumuşakça (kareli eğriyle, ani değil)
        // artan bir ek fren — son kısım sonsuza kadar sürünmesin.
        float lowSpeedT =
            lowSpeedThreshold > 0f
                ? Mathf.Clamp01(1f - speed / lowSpeedThreshold)
                : 0f;

        float baseDeceleration =
            iceDeceleration + lowSpeedExtraDeceleration * lowSpeedT * lowSpeedT;

        float effectiveDeceleration =
            isSweepActive
                ? baseDeceleration * (1f - sweepFrictionReduction)
                : baseDeceleration;

        rb.linearVelocity *=
            Mathf.Exp(-effectiveDeceleration * Time.fixedDeltaTime);
    }

    private void HandleInput()
    {
        if (!TryGetPointer(
            out Vector2 pointerPosition,
            out bool pressedThisFrame,
            out bool releasedThisFrame))
        {
            return;
        }

        // =========================================
        // BASILDI
        // =========================================

        if (pressedThisFrame)
        {
            if (IsTouchingStone(pointerPosition))
            {
                pointerStart =
                    pointerPosition;

                isDragging = true;
            }
        }

        // =========================================
        // SÜRÜKLENİYOR
        // =========================================

        if (isDragging)
        {
            if (isPositioning)
            {
                // İlk aşama:
                // Taşı sağa-sola konumlandır.
                PositionStone(
                    pointerPosition
                );
            }
            else
            {
                // İkinci aşama:
                // Atış hızını hesapla.
                UpdateThrowPreview(
                    pointerStart,
                    pointerPosition
                );
            }
        }

        // =========================================
        // BIRAKILDI
        // =========================================

        if (releasedThisFrame)
        {
            if (!isDragging)
                return;

            Vector2 pointerEnd =
                pointerPosition;

            if (isPositioning)
            {
                // İlk aşama tamamlandı.
                EnterAimPhase();
            }
            else
            {
                // İkinci aşama:
                // Taşı at.
                Throw(
                    pointerStart,
                    pointerEnd
                );
            }

            isDragging = false;
        }
    }

    /// <summary>Konumlandırma bitip nişan alma aşaması başlarken çağrılır (insan ya da AI, aynı görseller).</summary>
    private void EnterAimPhase()
    {
        isPositioning = false;

        aimRestPosition = transform.position;

        UpdateSpeedUI(0f);

        if (speedGauge != null)
        {
            speedGauge.SetGaugeVisible(true);
        }

        if (cameraFollow != null)
        {
            cameraFollow.EnterAimView(transform);
        }

        if (throwingAthlete != null)
        {
            throwingAthlete.gameObject.SetActive(false);
        }

        if (handVisual != null)
        {
            handVisual.gameObject.SetActive(true);
            UpdateHandVisual();
        }
    }

    /// <summary>AIOpponent'ın taşı sağa/sola konumlandırıp nişan aşamasına geçirmesi için kullanılır.</summary>
    public void AIEnterAimPhase(float lateralX)
    {
        Vector3 position = transform.position;
        position.x = Mathf.Clamp(lateralX, minX, maxX);
        transform.position = position;

        EnterAimPhase();
    }

    /// <summary>AIOpponent'ın taşı fırlatması için kullanılır — insan atışıyla aynı ExecuteThrow yolunu izler.</summary>
    public void AIRelease(float power)
    {
        ExecuteThrow(Mathf.Clamp(power, minPower, maxPower));
    }

    private bool TryGetPointer(
        out Vector2 position,
        out bool pressedThisFrame,
        out bool releasedThisFrame)
    {
        position = Vector2.zero;

        pressedThisFrame = false;
        releasedThisFrame = false;

        // TELEFON / TABLET
        if (Touchscreen.current != null)
        {
            var touch =
                Touchscreen.current.primaryTouch;

            position =
                touch.position.ReadValue();

            pressedThisFrame =
                touch.press.wasPressedThisFrame;

            releasedThisFrame =
                touch.press.wasReleasedThisFrame;

            return true;
        }

        // PC TEST
        if (Mouse.current != null)
        {
            position =
                Mouse.current.position.ReadValue();

            pressedThisFrame =
                Mouse.current.leftButton
                    .wasPressedThisFrame;

            releasedThisFrame =
                Mouse.current.leftButton
                    .wasReleasedThisFrame;

            return true;
        }

        return false;
    }

    private bool IsTouchingStone(
        Vector2 screenPosition)
    {
        Ray ray =
            mainCamera.ScreenPointToRay(
                screenPosition
            );

        if (Physics.Raycast(
            ray,
            out RaycastHit hit))
        {
            return hit.transform == transform;
        }

        return false;
    }

    private void PositionStone(
        Vector2 currentPosition)
    {
        float deltaX =
            currentPosition.x -
            pointerStart.x;

        Vector3 position =
            transform.position;

        position.x +=
            deltaX * moveSpeed;

        position.x =
            Mathf.Clamp(
                position.x,
                minX,
                maxX
            );

        transform.position =
            position;

        pointerStart =
            currentPosition;
    }

    private void UpdateThrowPreview(
        Vector2 start,
        Vector2 current)
    {
        Vector2 swipe =
            current - start;

        // Sadece geri doğru çekme (yay gerer gibi)
        // hız olarak kullanılır.
        float pullSwipe =
            Mathf.Max(
                0f,
                -swipe.y
            );

        float power =
            pullSwipe *
            swipeMultiplier;

        power = ApplyPowerCurve(Mathf.Clamp(power, 0f, maxPower));

        UpdateSpeedUI(power);

        // Taş, elle beraber geriye (yay gerer gibi) kaysın.
        float pullBack =
            (power / maxPower) *
            handMaxPullBack;

        transform.position =
            aimRestPosition +
            Vector3.back * pullBack;

        UpdateHandVisual();
    }

    /// <summary>
    /// El görselini, taşa göre sabit (dinlenme) ofsetinde
    /// tutar — taş geri kayınca el de onunla beraber gider.
    /// </summary>
    private void UpdateHandVisual()
    {
        if (handVisual == null)
            return;

        handVisual.position =
            transform.position +
            handRestOffset;
    }

    private void UpdateSpeedUI(
        float power)
    {
        if (speedGauge != null)
        {
            speedGauge.SetSpeed(
                power,
                maxPower
            );
        }
    }

    // =========================================
    // SÜPÜRME (ATIŞTAN SONRA)
    // =========================================

    private void HandleSweep()
    {
        if (rb.isKinematic)
        {
            isSweepActive = false;
            return;
        }

        // Taş durduysa süpürmenin bir anlamı yok.
        if (rb.linearVelocity.magnitude < 0.05f)
        {
            isSweepActive = false;
            ReturnSweepersHome();
            return;
        }

        // Süpürgeciler taş buzda kaydığı sürece onunla birlikte
        // hareket eder — dokunulmasa da hep görünür/takipte
        // kalırlar, basılınca sadece süpürme animasyonu değişir.
        // Animasyon hızı da taşın anlık hızıyla hafifçe orantılı.
        float currentSpeed = rb.linearVelocity.magnitude;

        if (leftAthlete != null)
        {
            leftAthlete.FollowStone(transform.position, currentSpeed);
        }

        if (rightAthlete != null)
        {
            rightAthlete.FollowStone(transform.position, currentSpeed);
        }

        bool sweepLeft;
        bool sweepRight;

        if (IsAIControlled)
        {
            // AIOpponent kararını her karede AiSweepLeft/Right ve
            // AiSteerDirection'a yazıyor — bir blokla karşılaşınca
            // etrafından dönüp merkeze çekmek için ikisi birlikte
            // kullanılır (insanın basılı tutup yönlendirmesiyle aynı yol).
            sweepLeft = AiSweepLeft;
            sweepRight = AiSweepRight;

            if (AiSteerDirection != 0f)
            {
                SteerStone(AiSteerDirection * steerHoldSpeed * Time.deltaTime);
            }
        }
        else
        {
            bool isHeld = TryGetHeldPointer(out Vector2 pointerPosition);

            sweepLeft = false;
            sweepRight = false;

            if (isHeld)
            {
                // Ekranın sağına basılırsa taş sola,
                // soluna basılırsa taş sağa kayar. Parmak
                // basılı tutuldukça yönlendirme sürer.
                float steerDirection =
                    pointerPosition.x > Screen.width * 0.5f
                        ? -1f
                        : 1f;

                SteerStone(
                    steerDirection * steerHoldSpeed * Time.deltaTime
                );

                // Hangi süpürgecinin süpüreceği dokunulan tarafa
                // göre belirlenir: sağa basılırsa sağdaki, sola
                // basılırsa soldaki; ikisinin arasına (öne)
                // basılırsa ikisi birden süpürür.
                float xFraction = pointerPosition.x / Screen.width;

                if (xFraction <= leftSweepZone)
                {
                    sweepLeft = true;
                }
                else if (xFraction >= rightSweepZone)
                {
                    sweepRight = true;
                }
                else
                {
                    sweepLeft = true;
                    sweepRight = true;
                }
            }
        }

        if (leftAthlete != null)
        {
            leftAthlete.SetSweeping(sweepLeft);
        }

        if (rightAthlete != null)
        {
            rightAthlete.SetSweeping(sweepRight);
        }

        // Süpürme = en az bir taraf süpürüyor. Asıl etki
        // FixedUpdate'te, sürtünme azaltması olarak uygulanıyor.
        isSweepActive = sweepLeft || sweepRight;
    }

    /// <summary>Her iki süpürgeciyi de kendi sabit ev konumuna döndürür.</summary>
    private void ReturnSweepersHome()
    {
        if (leftAthlete != null)
        {
            leftAthlete.ReturnHome();
        }

        if (rightAthlete != null)
        {
            rightAthlete.ReturnHome();
        }

        SetSweepersActive(false);
    }

    private void SetSweepersActive(bool active)
    {
        if (leftAthlete != null)
        {
            leftAthlete.gameObject.SetActive(active);
        }

        if (rightAthlete != null)
        {
            rightAthlete.gameObject.SetActive(active);
        }
    }

    private bool TryGetHeldPointer(out Vector2 position)
    {
        position = Vector2.zero;

        if (Touchscreen.current != null &&
            Touchscreen.current.primaryTouch.press.isPressed)
        {
            position =
                Touchscreen.current.primaryTouch.position.ReadValue();

            return true;
        }

        if (Mouse.current != null &&
            Mouse.current.leftButton.isPressed)
        {
            position =
                Mouse.current.position.ReadValue();

            return true;
        }

        return false;
    }

    private void SteerStone(float steerAmount)
    {
        // Yönlendirme de süpürme gibi taşın mevcut hızıyla
        // orantılı: taş hızlıyken itki güçlü, taş yavaşlayıp
        // durmaya yaklaştıkça itki de sıfıra yaklaşır.
        float appliedImpulse =
            steerAmount * GetSpeedFactor();

        Vector3 velocity = rb.linearVelocity;

        velocity.x =
            Mathf.Clamp(
                velocity.x + appliedImpulse,
                -maxSteerSpeed,
                maxSteerSpeed
            );

        rb.linearVelocity = velocity;
    }

    /// <summary>
    /// Ham çekiş gücünü (0..maxPower) fırlatma hızına çevirir — zayıf/orta
    /// çekişler orantısız şekilde daha yavaş çıkar, sadece iyice (full)
    /// çekilince maksimuma yaklaşır. Hem önizlemede (UI/el görseli) hem
    /// gerçek atışta aynı eğri kullanılır ki ikisi tutarlı hissettirsin.
    /// </summary>
    private float ApplyPowerCurve(float rawPower)
    {
        float t = maxPower > 0f ? Mathf.Clamp01(rawPower / maxPower) : 0f;
        float eased = Mathf.Pow(t, powerCurveExponent);
        return eased * maxPower;
    }

    private float GetSpeedFactor()
    {
        return
            Mathf.Clamp01(
                rb.linearVelocity.magnitude / maxPower
            );
    }

    private void Throw(
        Vector2 start,
        Vector2 end)
    {
        Vector2 swipe =
            end - start;

        // Çok kısa hareket.
        if (swipe.magnitude < 20f)
        {
            transform.position = aimRestPosition;
            UpdateHandVisual();
            UpdateSpeedUI(0f);
            return;
        }

        float pullSwipe =
            Mathf.Max(
                0f,
                -swipe.y
            );

        float power =
            pullSwipe *
            swipeMultiplier;

        power = ApplyPowerCurve(Mathf.Clamp(power, 0f, maxPower));

        power =
            Mathf.Clamp(
                power,
                minPower,
                maxPower
            );

        ExecuteThrow(power);
    }

    /// <summary>
    /// Nişan konumundan gerçek atışa geçer — insan (swipe'tan
    /// hesaplanan power ile) ve AI (doğrudan verilen power ile)
    /// aynı yolu izler.
    /// </summary>
    private void ExecuteThrow(float power)
    {
        // Fırlatmadan önce taş dinlenme konumuna geri gelsin
        // (geri çekiş sadece görsel bir hazırlıktı).
        transform.position = aimRestPosition;

        // Taş pist boyunca ileri gider (parmak geri
        // çekildiği kadar güçlü, yay gerer gibi).
        Vector3 direction = Vector3.forward;

        UpdateSpeedUI(power);

        // Atış sonrası gösterge yumuşakça kaybolsun.
        if (speedGauge != null)
        {
            speedGauge.FadeOut();
        }

        // Nişan görünümünden çık, takip kamerası devralsın.
        if (cameraFollow != null)
        {
            cameraFollow.ExitAimView();
        }

        // El taşı bırakır bırakmaz kaybolsun.
        if (handVisual != null)
        {
            StopAllCoroutines();
            handVisual.gameObject.SetActive(false);
        }

        // Top elden çıktığı an atış animasyonu (tam ekran video)
        // oynar; video bitene kadar taş fiziksel olarak hareket
        // etmez. Video yoksa (throwAnimation atanmamışsa) fizik
        // hemen uygulanır.
        isAnimatingThrow = true;

        if (throwAnimation != null)
        {
            throwAnimation.PlayThrowAnimation(
                PlayerIndex == 0,
                () => ApplyThrowPhysics(direction, power)
            );
        }
        else
        {
            ApplyThrowPhysics(direction, power);
        }
    }

    /// <summary>
    /// Taşı gerçekten hareket ettirir — atış animasyonu varsa
    /// bitiminde, yoksa Throw() içinde hemen çağrılır.
    /// </summary>
    private void ApplyThrowPhysics(Vector3 direction, float power)
    {
        isAnimatingThrow = false;

        // Fizik tekrar aktif.
        rb.isKinematic = false;

        rb.linearVelocity =
            direction * power;

        hasBeenThrown = true;

        // Süpürgeciler taş kaymaya başlar başlamaz görünsün.
        SetSweepersActive(true);

        // Taş buzda kayarken arkasında buz/kar parçacıkları bıraksın.
        if (iceTrailEffect != null)
        {
            iceTrailEffect.Clear();
            iceTrailEffect.Play();
        }
    }
}
