using UnityEngine;

/// <summary>
/// Süpürgeci insan atletlerin yerini alır — artık sahnede bir karakter yok,
/// sadece taşın iki yanında duran birer süpürge var. Taşı AthleteController
/// ile aynı mantıkla (yanal mesafe korunarak, duvar köşelerinde kıvrılarak)
/// takip eder; süpürme sırasında ise animasyon klibi yerine kendi ileri/geri
/// (dönmeden, düz bir çizgide) kayan bir osilasyonla süpürme hareketi yapar.
/// </summary>
public class BroomSweeperController : MonoBehaviour
{
    [Header("Süpürge Rengi")]
    [Tooltip("Süpürgenin gövde rengini boyayan Renderer. Boş bırakılırsa renk değiştirilmez.")]
    [SerializeField] private Renderer broomRenderer;
    [SerializeField] private Color blueColor = new Color(0.3160377f, 0.45993277f, 1f);
    [SerializeField] private Color redColor = new Color(0.8509804f, 0.2794024f, 0.21960784f);

    [Header("Süpürme Sesi")]
    [SerializeField] private AudioSource sweepAudioSource;
    [SerializeField] private AudioClip sweepClip;

    [Header("Süpürme Hareketi (ileri/geri, dönme değil)")]
    [Tooltip("Süpürürken ileri/geri kaç metre gidip geleceği.")]
    [SerializeField] private float sweepDistance = 0.15f;

    [Tooltip("Süpürürken saniyede kaç tam gidiş-geliş yapılacağı.")]
    [SerializeField] private float sweepFrequency = 2.2f;

    [Tooltip("İleri/geri hareketin yönü, dünya ekseninde (döndürmeden — broom'un kendi rotasyonundan bağımsız). Sağa/sola süpürme için X.")]
    [SerializeField] private Vector3 sweepAxisWorld = Vector3.right;

    [Header("Taş Takibi")]
    [Tooltip("Taş buzda kayarken bu süpürgenin ona ne kadar hızlı yetiştiği.")]
    [SerializeField] private float followSpeed = 12f;

    [Tooltip("Taşın bu hızda (m/s) kayması sallanmanın tam (1x) hızda olmasına karşılık gelir.")]
    [SerializeField] private float fullSweepSpeedReference = 3.5f;

    [Range(0.3f, 1f)]
    [SerializeField] private float minSweepSpeed = 0.7f;

    [Tooltip("Taş +Z yönünde (ev/house'a doğru) kayar. Süpürge taşın bu kadar ÖNÜNDE (daha büyük Z'de) durur — gerçek curling'de süpürgeciler taşın önündeki buzu süpürür.")]
    [SerializeField] private float leadDistance = 1.8f;

    [SerializeField] private float minX = -3.2f;
    [SerializeField] private float maxX = 3.2f;
    [SerializeField] private float wallCornerRadius = 4f;
    [SerializeField] private float wallCornerExtraStepBack = 2.6f;
    [SerializeField] private float wallCornerTurnAngle = 90f;

    private Vector3 homePosition;
    private Quaternion homeRotation;
    private Vector3 basePosition;
    private Quaternion baseRotation;
    private bool isSweeping;
    private float sweepPhase;
    private float speedMultiplier = 1f;

    private void Awake()
    {
        homePosition = transform.position;
        homeRotation = transform.rotation;
        basePosition = transform.position;
        baseRotation = transform.rotation;

        if (sweepAudioSource != null)
        {
            sweepAudioSource.clip = sweepClip;
            sweepAudioSource.loop = true;
            sweepAudioSource.playOnAwake = false;
        }
    }

    private void OnEnable()
    {
        isSweeping = false;
        sweepPhase = 0f;
        speedMultiplier = 1f;
        basePosition = homePosition;
        baseRotation = homeRotation;
    }

    /// <summary>0 = mavi takım, 1 = kırmızı takım.</summary>
    public void SetTeam(int playerIndex)
    {
        if (broomRenderer == null)
            return;

        Color targetColor = playerIndex == 0 ? blueColor : redColor;

        Debug.Log(
            "[BroomSweeperController] SetTeam(" + playerIndex + ") -> " + targetColor +
            "  renderer=" + broomRenderer.name +
            "  materialCount=" + broomRenderer.sharedMaterials.Length +
            "  shader=" + (broomRenderer.sharedMaterials.Length > 0 && broomRenderer.sharedMaterials[0] != null
                ? broomRenderer.sharedMaterials[0].shader.name
                : "none")
        );

        try
        {
            // material.color çarpımsal (tint) çalışır — dokunun kendi
            // rengi zaten mavimsi/koyuysa kırmızı tint soluk/karanlık
            // çıkabilir. Bunu önlemek için hem tüm materyal slotlarına
            // (tek materyale değil) hem de emission'a aynı rengi
            // uyguluyoruz — kozmetik eşyalardaki ApplyColorTexture ile
            // aynı yöntem, base doku ne olursa olsun net okunur.
            Material[] materials = broomRenderer.materials;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                material.color = targetColor;

                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", targetColor);
                }

                if (material.HasProperty("_EmissionColor"))
                {
                    material.EnableKeyword("_EMISSION");
                    material.globalIlluminationFlags =
                        MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    material.SetColor("_EmissionColor", (Vector4)targetColor * 0.12f);
                }
            }

            broomRenderer.materials = materials;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning(
                "[BroomSweeperController] Renk uygulanamadı — '" + name +
                "' objesindeki Broom Renderer alanı sahne objesi yerine bir " +
                "prefab asset'ine atanmış olabilir. " + exception.Message
            );
        }
    }

    /// <summary>Taşı takip eder — AthleteController.FollowStone ile aynı mantık.</summary>
    public void FollowStone(Vector3 stonePosition, float stoneSpeed = -1f)
    {
        if (stoneSpeed >= 0f && fullSweepSpeedReference > 0f)
        {
            speedMultiplier = Mathf.Clamp(
                stoneSpeed / fullSweepSpeedReference,
                minSweepSpeed,
                1f
            );
        }

        float desiredX = stonePosition.x + homePosition.x;
        float clampedX = Mathf.Clamp(desiredX, minX, maxX);

        float excess = Mathf.Min(Mathf.Abs(desiredX - clampedX), wallCornerRadius);
        float curveT = wallCornerRadius > 0f ? excess / wallCornerRadius : 0f;

        float zPullback =
            wallCornerRadius -
            Mathf.Sqrt(Mathf.Max(0f, wallCornerRadius * wallCornerRadius - excess * excess)) +
            curveT * wallCornerExtraStepBack;

        Vector3 target = new Vector3(clampedX, homePosition.y, stonePosition.z + leadDistance - zPullback);

        basePosition = Vector3.Lerp(basePosition, target, Time.deltaTime * followSpeed);

        float wallSide = desiredX > clampedX ? 1f : desiredX < clampedX ? -1f : 0f;

        Quaternion targetRotation =
            homeRotation * Quaternion.Euler(0f, wallSide * curveT * wallCornerTurnAngle, 0f);

        baseRotation = Quaternion.Slerp(baseRotation, targetRotation, Time.deltaTime * followSpeed);

        if (!isSweeping)
        {
            transform.position = basePosition;
            transform.rotation = baseRotation;
        }
    }

    /// <summary>Süpürme sallanışını açar/kapatır.</summary>
    public void SetSweeping(bool sweeping)
    {
        if (isSweeping == sweeping)
            return;

        isSweeping = sweeping;

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

    private void Update()
    {
        if (!isSweeping)
        {
            sweepPhase = 0f;
            return;
        }

        sweepPhase += Time.deltaTime * sweepFrequency * speedMultiplier * Mathf.PI * 2f;
        float swing = Mathf.Sin(sweepPhase) * sweepDistance;

        Vector3 axis = sweepAxisWorld.sqrMagnitude > 0.0001f ? sweepAxisWorld.normalized : Vector3.right;

        transform.rotation = baseRotation;
        transform.position = basePosition + axis * swing;
    }

    /// <summary>Süpürgeyi kendi sabit ev konumuna geri döndürür.</summary>
    public void ReturnHome()
    {
        isSweeping = false;
        sweepPhase = 0f;

        transform.position = homePosition;
        transform.rotation = homeRotation;
        basePosition = homePosition;
        baseRotation = homeRotation;

        if (sweepAudioSource != null)
        {
            sweepAudioSource.Stop();
        }
    }
}
