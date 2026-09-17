using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Atletin forma/şort rengini sıradaki oyuncuya göre değiştirir,
/// her tur başında çömelme (Crouching) pozunu yeniden tetikler ve
/// süpürme sırasında taşı takip eder.
///
/// Süpürgeci artık taşın çeperinde amaçsız dolaşmaz — Editor'de
/// yerleştirilen yanal (X) mesafeyi koruyarak taşı hem ileri/geri
/// (Z) hem de sağa/sola (X) takip eder. Süpürme animasyonu ise
/// konumdan bağımsız olarak ayrıca açılıp kapatılır.
/// </summary>
public class AthleteController : MonoBehaviour
{
    [Header("Referanslar")]
    [SerializeField] private Renderer topRenderer;
    [SerializeField] private Renderer bottomRenderer;
    [SerializeField] private Animator athleteAnimator;

    [Header("Top_Sport (Material.012)")]
    [SerializeField] private Material blueTop;
    [SerializeField] private Material redTop;

    [Header("Underwear (Material.008)")]
    [SerializeField] private Material blueBottom;
    [SerializeField] private Material redBottom;

    [Header("Süpürge Rengi")]
    [Tooltip("Süpürgenin gövde rengini boyayan Renderer. Boş bırakılırsa süpürge rengi değiştirilmez.")]
    [SerializeField] private Renderer broomRenderer;
    [SerializeField] private Color broomBlueColor = new Color(0.3160377f, 0.45993277f, 1f);
    [SerializeField] private Color broomRedColor = new Color(0.8509804f, 0.2794024f, 0.21960784f);

    [Header("Süpürme Sesi")]
    [Tooltip("Süpürürken çalan ses kaynağı. Boş bırakılırsa ses çalınmaz.")]
    [SerializeField] private AudioSource sweepAudioSource;
    [Tooltip("Basılı tutulduğu (süpürüldüğü) sürece loop halinde çalan süpürme sesi.")]
    [SerializeField] private AudioClip sweepClip;

    [Header("Animasyon")]
    [SerializeField] private string crouchingStateName = "Crouching";
    [SerializeField] private string sweepingStateName = "Sweeping";
    [Tooltip("Süpürgeci taşı takip ederken ama süpürmezken oynayan yürüme animasyonu.")]
    [SerializeField] private string walkingStateName = "Walking";

    [Header("Süpürge Tutuşu (elin kemiğine göre yerel)")]
    [Tooltip("Süpürgenin, elin altındaki child transformu. Boş bırakılırsa konum/açı değiştirilmez.")]
    [SerializeField] private Transform broomTransform;

    [Tooltip("Süpürgeci yürürken (süpürmezken) süpürgenin ele göre yerel konumu.")]
    [SerializeField] private Vector3 walkingBroomLocalPosition;

    [Tooltip("Süpürgeci yürürken (süpürmezken) süpürgenin ele göre yerel açısı.")]
    [SerializeField] private Vector3 walkingBroomLocalRotation;

    [Tooltip("Süpürgeci süpürürken süpürgenin ele göre yerel konumu.")]
    [SerializeField] private Vector3 sweepingBroomLocalPosition;

    [Tooltip("Süpürgeci süpürürken süpürgenin ele göre yerel açısı.")]
    [SerializeField] private Vector3 sweepingBroomLocalRotation;

    [Header("Taş Takibi")]
    [Tooltip("Taş buzda kayarken (hem ileri/geri hem sağa/sola yönlendirilirken) bu atletin ona ne kadar hızlı yetiştiği. Taşla arasındaki yanal mesafe, Editor'de bırakılan konumdan hesaplanıp korunur.")]
    [SerializeField] private float followSpeed = 12f;

    [Tooltip("Taşın bu hızda (m/s) kayması, animasyonun tam (1x) hızda oynamasına karşılık gelir — daha yavaş kayarken animasyon da hafifçe yavaşlar. Slow-motion hissi vermesin diye küçük tutulmalı.")]
    [SerializeField] private float fullAnimSpeedReference = 3.5f;

    [Tooltip("Animasyonun inebileceği en düşük hız çarpanı — 1'e ne kadar yakınsa yavaşlama o kadar hafif hissettirir.")]
    [Range(0.3f, 1f)]
    [SerializeField] private float minAnimSpeed = 0.7f;

    [Tooltip("Süpürgecinin gidebileceği en sol (küçük) X konumu — LeftKickBoard duvarı X=-4'te, içine girmesin diye geniş payla sınırlanır.")]
    [SerializeField] private float minX = -3.2f;

    [Tooltip("Süpürgecinin gidebileceği en sağ (büyük) X konumu — RightKickBoard duvarı X=4'te, içine girmesin diye geniş payla sınırlanır.")]
    [SerializeField] private float maxX = 3.2f;

    [Tooltip("Taşın istediği yanal konum duvar sınırını aştığında, süpürgeci düz duvara sürtünmek yerine geriye doğru bu yarıçapta bir yay (çeyrek daire) çizerek kıvrılır.")]
    [SerializeField] private float wallCornerRadius = 4f;

    [Tooltip("Köşeyi tam dönerken (duvara tam yaslandığında) yay hesabına ek olarak geriye atılan bir adımlık ekstra mesafe.")]
    [SerializeField] private float wallCornerExtraStepBack = 2.6f;

    [Tooltip("Süpürgeci köşeyi dönerken (duvara tam yaslandığında) bakış yönünün ne kadar (derece) dönmüş olacağı — çeperde dönüyormuş gibi görünsün diye.")]
    [SerializeField] private float wallCornerTurnAngle = 90f;

    private Vector3 homePosition;
    private Quaternion homeRotation;
    private bool? currentSweepState;

    private readonly Dictionary<CosmeticSlot, GameObject> equippedCosmetics =
        new Dictionary<CosmeticSlot, GameObject>();

    private void Awake()
    {
        homePosition = transform.position;
        homeRotation = transform.rotation;

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        if (sweepAudioSource != null)
        {
            sweepAudioSource.clip = sweepClip;
            sweepAudioSource.loop = true;
            sweepAudioSource.playOnAwake = false;
        }
    }

    private void OnEnable()
    {
        // Obje yeniden aktif olunca Animator varsayılan duruma
        // dönmüş olabilir — bir sonraki SetSweeping çağrısı doğru
        // animasyonu (yürüme/süpürme) baştan zorlasın.
        currentSweepState = null;

        if (athleteAnimator != null)
        {
            athleteAnimator.speed = 1f;
        }
    }

    /// <summary>0 = mavi takım, 1 = kırmızı takım.</summary>
    public void SetTeam(int playerIndex)
    {
        ApplyOutfit(playerIndex == 0);
        ReplayCrouch();
    }

    private void ApplyOutfit(bool isBlueTeam)
    {
        Material top = isBlueTeam ? blueTop : redTop;
        Material bottom = isBlueTeam ? blueBottom : redBottom;

        ReplaceMaterial(topRenderer, "Material.012", top);
        ReplaceMaterial(bottomRenderer, "Material.008", bottom);

        if (broomRenderer != null)
        {
            try
            {
                broomRenderer.material.color =
                    isBlueTeam ? broomBlueColor : broomRedColor;
            }
            catch (System.Exception exception)
            {
                // Broom Renderer yanlışlıkla bir prefab asset'ine
                // (sahne objesi yerine) atanmışsa Unity burada
                // exception fırlatır — bu tek atlet için süpürge
                // renklenmesin diye burada tutulur, ama tur akışı
                // (SpawnNextStone) kesintiye uğramaz.
                Debug.LogWarning(
                    "[AthleteController] Broom Renderer'a süpürge rengi uygulanamadı — " +
                    "'" + name + "' objesindeki Broom Renderer alanı sahne " +
                    "objesi yerine bir prefab asset'ine atanmış olabilir. " +
                    exception.Message
                );
            }
        }
    }

    private static void ReplaceMaterial(
        Renderer renderer,
        string originalNameStartsWith,
        Material replacement)
    {
        if (renderer == null || replacement == null)
            return;

        Material[] materials = renderer.materials;
        bool changedAny = false;

        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] == null)
                continue;

            string materialName =
                materials[i].name.Replace(" (Instance)", "");

            if (materialName.StartsWith(originalNameStartsWith))
            {
                materials[i] = replacement;
                changedAny = true;
            }
        }

        if (changedAny)
        {
            renderer.materials = materials;
        }
    }

    /// <summary>
    /// Verilen slota bir kozmetik eşya takar (item null ise slot
    /// boşaltılır). Baş için Head kemiğine, üst/alt için Spine/Hips
    /// kemiğine katı (deforme olmayan) şekilde ebeveynlenir — tam
    /// oturması item'ın localPositionOffset/localRotationOffsetEuler/
    /// localScale alanlarıyla (CosmeticCatalog Inspector'ında, Play
    /// modu DIŞINDA) ince ayar ister, el asseti gibi. Top/Bottom
    /// kuşanılınca varsayılan forma/şort gizlenir.
    /// </summary>
    public void EquipCosmetic(CosmeticSlot slot, CosmeticItem item)
    {
        if (equippedCosmetics.TryGetValue(slot, out GameObject existing) && existing != null)
        {
            Destroy(existing);
            equippedCosmetics.Remove(slot);
        }

        if (item != null && item.prefab != null && athleteAnimator != null)
        {
            Transform boneTransform =
                athleteAnimator.GetBoneTransform(GetBoneForSlot(slot));

            if (boneTransform != null)
            {
                try
                {
                    GameObject instance = Instantiate(item.prefab, boneTransform);
                    instance.transform.localPosition = item.localPositionOffset;
                    instance.transform.localRotation = Quaternion.Euler(item.localRotationOffsetEuler);
                    instance.transform.localScale = item.localScale;

                    if (item.colorTexture != null || item.tintColor != Color.white)
                    {
                        ApplyColorTexture(instance, item.colorTexture, item.tintColor);
                    }

                    equippedCosmetics[slot] = instance;
                }
                catch (System.Exception exception)
                {
                    // Kataloğdaki bir eşyanın prefab referansı bozuksa
                    // (ör. FBX içindeki yanlış alt-nesneye işaret ediyorsa)
                    // maç başlatma akışı burada çökmesin — sadece o
                    // eşya takılmadan devam edilir.
                    Debug.LogWarning(
                        "[AthleteController] '" + item.prefab.name +
                        "' kozmetik eşyası takılamadı (" + slot + " slotu): " +
                        exception.Message
                    );
                }
            }
        }

        if (slot == CosmeticSlot.Top && topRenderer != null)
        {
            topRenderer.enabled = item == null;
        }

        if (slot == CosmeticSlot.Bottom && bottomRenderer != null)
        {
            bottomRenderer.enabled = item == null;
        }
    }

    /// <summary>
    /// Mağaza eşyalarının çoğu genel bir asset paketinden geldiği
    /// için FBX'in kendi materyali bazen dokusuz/renksiz (varsayılan
    /// gri/beyaz) çıkabiliyor — bu, o eşyanın tüm renderer'larına
    /// basit bir URP/Lit materyal + doku uygulayarak düzeltir. tint
    /// beyaz değilse ayrıca o renge boyanır ve neon/parlak görünmesi
    /// için ışıma (emission) eklenir.
    /// </summary>
    private static void ApplyColorTexture(GameObject instance, Texture2D texture, Color tint)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
            return;

        Material material = new Material(shader);

        if (texture != null)
        {
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }
        }

        if (tint != Color.white)
        {
            material.color = tint;

            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            const float neonIntensity = 3.5f;
            material.SetColor("_EmissionColor", (Vector4)tint * neonIntensity);
        }

        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
        {
            Material[] materials = new Material[renderer.sharedMaterials.Length];

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = material;
            }

            renderer.materials = materials;
        }
    }

    private static HumanBodyBones GetBoneForSlot(CosmeticSlot slot)
    {
        switch (slot)
        {
            case CosmeticSlot.Head:
                return HumanBodyBones.Head;

            case CosmeticSlot.Top:
                return HumanBodyBones.Spine;

            default:
                return HumanBodyBones.Hips;
        }
    }

    public void ReplayCrouch()
    {
        if (athleteAnimator == null)
            return;

        athleteAnimator.Play(crouchingStateName, 0, 0f);
    }

    /// <summary>
    /// Taşı takip eder — hem ileri/geri (Z) hem de sağa/sola (X)
    /// kayışını izler, ama Editor'de bırakılan yanal mesafeyi
    /// (taşın yanında durma payını) korur; yüksekliği (Y) hiç
    /// değişmez. Yani artık çeperde amaçsız dolaşmaz ama taş
    /// yönlendirilince (steer) onunla birlikte kayar.
    /// </summary>
    public void FollowStone(Vector3 stonePosition, float stoneSpeed = -1f)
    {
        if (athleteAnimator != null && stoneSpeed >= 0f && fullAnimSpeedReference > 0f)
        {
            athleteAnimator.speed =
                Mathf.Clamp(
                    stoneSpeed / fullAnimSpeedReference,
                    minAnimSpeed,
                    1f
                );
        }

        // Yanal mesafe, Editor'de bırakılan konumun merkeze (X=0)
        // olan uzaklığı kadar sabit bir paydır — taş hangi taraftan
        // atılırsa atılsın (sağdan/soldan) süpürgeci hep taşın o
        // tarafında, aynı sabit payla durur.
        float desiredX = stonePosition.x + homePosition.x;
        float clampedX = Mathf.Clamp(desiredX, minX, maxX);

        // Duvar sınırını aşmak istediği kadar (excess), süpürgeci
        // düz çizgide duvara sürtünmek yerine geriye doğru bir
        // çeyrek daire çizerek kıvrılır — hiçbir zaman taşın
        // önüne geçmez, sadece geride kalır.
        float excess = Mathf.Min(Mathf.Abs(desiredX - clampedX), wallCornerRadius);

        float curveT =
            wallCornerRadius > 0f ? excess / wallCornerRadius : 0f;

        float zPullback =
            wallCornerRadius -
            Mathf.Sqrt(
                Mathf.Max(
                    0f,
                    wallCornerRadius * wallCornerRadius - excess * excess
                )
            ) +
            curveT * wallCornerExtraStepBack;

        Vector3 target =
            new Vector3(
                clampedX,
                homePosition.y,
                stonePosition.z - zPullback
            );

        transform.position =
            Vector3.Lerp(
                transform.position,
                target,
                Time.deltaTime * followSpeed
            );

        // Köşeyi dönerken bakış yönü de dönsün — hangi duvara
        // yaslandığına göre (sağ/sol) yön değişir.
        float wallSide =
            desiredX > clampedX ? 1f :
            desiredX < clampedX ? -1f : 0f;

        Quaternion targetRotation =
            homeRotation *
            Quaternion.Euler(0f, wallSide * curveT * wallCornerTurnAngle, 0f);

        transform.rotation =
            Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Time.deltaTime * followSpeed
            );
    }

    /// <summary>
    /// Süpürme animasyonunu açar/kapatır. Süpürmediği zaman
    /// (ama taşı takip ederken) oturup beklemek yerine yürür.
    /// Konum ayrıca FollowStone ile yönetilir — bu sadece
    /// animasyon.
    /// </summary>
    public void SetSweeping(bool sweeping)
    {
        if (athleteAnimator == null)
        {
            if (currentSweepState != sweeping)
            {
                currentSweepState = sweeping;
                ApplyBroomPose(sweeping);
                UpdateSweepAudio(sweeping);
            }

            return;
        }

        string activeStateName = sweeping ? sweepingStateName : walkingStateName;

        if (currentSweepState != sweeping)
        {
            currentSweepState = sweeping;

            ApplyBroomPose(sweeping);
            UpdateSweepAudio(sweeping);

            if (!string.IsNullOrEmpty(activeStateName))
            {
                athleteAnimator.Play(activeStateName, 0, 0f);
            }

            return;
        }

        // Klip "Loop Time" işaretli değilse sonunda donup
        // kalmasın; bitince baştan başlat.
        if (!string.IsNullOrEmpty(activeStateName))
        {
            AnimatorStateInfo state =
                athleteAnimator.GetCurrentAnimatorStateInfo(0);

            if (state.IsName(activeStateName) &&
                !state.loop &&
                state.normalizedTime >= 1f)
            {
                athleteAnimator.Play(activeStateName, 0, 0f);
            }
        }
    }

    /// <summary>Süpürme sesini başlatır/durdurur — basılı tutulduğu sürece loop, bırakılınca durur.</summary>
    private void UpdateSweepAudio(bool sweeping)
    {
        if (sweepAudioSource == null || sweepClip == null)
            return;

        if (sweeping)
        {
            if (!sweepAudioSource.isPlaying)
            {
                sweepAudioSource.Play();
            }
        }
        else
        {
            sweepAudioSource.Stop();
        }
    }

    /// <summary>Süpürgeyi, duruma göre (yürüme/süpürme) elin içinde ayarlanan açıya çevirir.</summary>
    private void ApplyBroomPose(bool sweeping)
    {
        if (broomTransform == null)
            return;

        broomTransform.localPosition =
            sweeping ? sweepingBroomLocalPosition : walkingBroomLocalPosition;

        broomTransform.localRotation =
            Quaternion.Euler(
                sweeping ? sweepingBroomLocalRotation : walkingBroomLocalRotation
            );
    }

    /// <summary>
    /// Atleti kendi sabit ev konumuna geri döndürür. Animasyonu
    /// zorlamaz — çömelme pozu sadece atan atlete özgü, bunu
    /// StoneController/TurnManager ayrıca (ReplayCrouch ile)
    /// tetikler.
    /// </summary>
    public void ReturnHome()
    {
        currentSweepState = null;

        transform.position = homePosition;
        transform.rotation = homeRotation;

        if (athleteAnimator != null)
        {
            athleteAnimator.speed = 1f;
        }

        if (sweepAudioSource != null)
        {
            sweepAudioSource.Stop();
        }
    }
}
