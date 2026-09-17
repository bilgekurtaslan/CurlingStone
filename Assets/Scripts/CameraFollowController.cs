using UnityEngine;

public class CameraFollowController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Rigidbody stoneRigidbody;

    [Header("Follow (throw sonrası)")]
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 7f, -7f);
    [SerializeField] private float followSmoothTime = 0.4f;
    [SerializeField] private float followFov = 48f;
    [SerializeField] private float fovLerpSpeed = 1.5f;

    [Header("Nişan Alma Görünümü (pozisyon seçimi bitince)")]
    [SerializeField] private Vector3 aimOffset = new Vector3(0f, 1.5f, -2.9f);
    [SerializeField] private float aimLookAheadDistance = 1f;
    [SerializeField] private float aimFov = 40f;
    [SerializeField] private float aimTransitionSpeed = 4f;
    [SerializeField] private GameObject athleteToHide;

    private Camera cam;
    private Vector3 followVelocity;
    private bool isFollowing;

    private Transform aimTarget;
    private bool isAiming;

    private Vector3 staticPosition;
    private Quaternion staticRotation;
    private float staticFov;

    private void Awake()
    {
        cam = GetComponent<Camera>();

        staticPosition = transform.position;
        staticRotation = transform.rotation;
        staticFov = cam != null ? cam.fieldOfView : 60f;
    }

    /// <summary>Sırası gelen taşı takip etmesi için hedefi değiştirir.</summary>
    public void SetTarget(Rigidbody newStoneRigidbody)
    {
        stoneRigidbody = newStoneRigidbody;
        isFollowing = false;
    }

    /// <summary>
    /// Konum seçimi bitince taşa yakın, zoomlu (birinci şahıs gibi)
    /// bir görünüme geçer.
    /// </summary>
    public void EnterAimView(Transform stoneTransform)
    {
        aimTarget = stoneTransform;
        isAiming = true;
        isFollowing = false;

        if (athleteToHide != null)
        {
            athleteToHide.SetActive(false);
        }
    }

    /// <summary>Taş fırlatılınca nişan görünümünden çıkar.</summary>
    public void ExitAimView()
    {
        isAiming = false;

        // Nişan görünümü kamera rotasyonunu değiştiriyordu; takip
        // modu rotasyonu hiç yönetmiyor, o yüzden eski (sabit) açıya
        // burada anında geri dönmemiz lazım, yoksa taş kadraj dışında kalır.
        transform.rotation = staticRotation;

        if (athleteToHide != null)
        {
            athleteToHide.SetActive(true);
        }
    }

    /// <summary>Kamerayı ilk (sabit) konumuna anında döndürür.</summary>
    public void ResetToStatic()
    {
        isFollowing = false;
        isAiming = false;
        followVelocity = Vector3.zero;

        transform.position = staticPosition;
        transform.rotation = staticRotation;

        if (cam != null)
        {
            cam.fieldOfView = staticFov;
        }

        if (athleteToHide != null)
        {
            athleteToHide.SetActive(true);
        }
    }

    private void LateUpdate()
    {
        if (isAiming && aimTarget != null)
        {
            UpdateAimView();
            return;
        }

        if (stoneRigidbody == null)
            return;

        if (!isFollowing && !stoneRigidbody.isKinematic)
        {
            isFollowing = true;
        }

        if (!isFollowing)
            return;

        Vector3 targetPosition =
            stoneRigidbody.position + followOffset;

        transform.position =
            Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref followVelocity,
                followSmoothTime
            );

        if (cam != null)
        {
            cam.fieldOfView =
                Mathf.Lerp(
                    cam.fieldOfView,
                    followFov,
                    Time.deltaTime * fovLerpSpeed
                );
        }
    }

    private void UpdateAimView()
    {
        Vector3 targetPosition = aimTarget.position + aimOffset;

        transform.position =
            Vector3.Lerp(
                transform.position,
                targetPosition,
                Time.deltaTime * aimTransitionSpeed
            );

        Vector3 lookPoint =
            aimTarget.position +
            Vector3.forward * aimLookAheadDistance;

        Vector3 lookDirection = lookPoint - transform.position;

        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(lookDirection.normalized);

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    Time.deltaTime * aimTransitionSpeed
                );
        }

        if (cam != null)
        {
            cam.fieldOfView =
                Mathf.Lerp(
                    cam.fieldOfView,
                    aimFov,
                    Time.deltaTime * aimTransitionSpeed
                );
        }
    }
}
