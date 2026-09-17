using UnityEngine;

/// <summary>
/// Seyircinin gövde/kol kemiklerini isimle bulup doğrudan
/// döndürerek hafif bir canlılık verir — Mecanim/Animator
/// kullanmaz, bu yüzden bu iskeletlerin Humanoid retarget için
/// uygun olmaması (kırık kemik hiyerarşisi) hiç sorun olmaz.
/// </summary>
public class SpectatorIdleAnimator : MonoBehaviour
{
    public enum ArmBehavior { None, Cheer, Clap }

    [SerializeField] private ArmBehavior armBehavior = ArmBehavior.None;

    [Header("Gövde Sallanması (her zaman aktif)")]
    [SerializeField] private Vector3 swayAxis = Vector3.forward;
    [SerializeField] private float swaySpeed = 1f;
    [SerializeField] private float swayAmount = 4f;

    [Header("Kafa Sallanması")]
    [SerializeField] private Vector3 headBobAxis = Vector3.right;
    [SerializeField] private float headBobAmount = 3f;

    [Header("Kol Hareketi (Cheer / Clap)")]
    [Tooltip("Kemiğin KENDİ (yerel) ekseni yerine karakterin " +
             "DÜNYA uzayındaki sağ ekseni referans alınır — bu " +
             "sayede kemiğin iç ekseni ne olursa olsun (ayna/ters " +
             "olsa bile) her iki kol da güvenilir şekilde öne/" +
             "yukarı kalkar.")]
    [SerializeField] private float armSpeed = 2.2f;
    [SerializeField] private float armAmount = 60f;

    [Tooltip("Clap sırasında eller ortada birleşsin diye kolları " +
             "içe (karşı karşıya) çeken ek açı.")]
    [SerializeField] private float clapInwardAmount = 45f;

    private Transform chest;
    private Transform head;
    private Transform armL;
    private Transform armR;

    private Quaternion chestRest;
    private Quaternion headRest;

    // Kolların DÜNYA-uzayı dinlenme rotasyonu — yerel eksen
    // tahminine bağlı olmayan yöntem için.
    private Quaternion armLRestWorld;
    private Quaternion armRRestWorld;

    private float phase;
    private float speedScale;

    /// <summary>Bu seyircinin ne kadar canlı davranacağını dışarıdan ayarlar.</summary>
    public void SetBehavior(ArmBehavior behavior)
    {
        armBehavior = behavior;
    }

    private void Awake()
    {
        chest =
            FindByNameContains(transform, "Chest") ??
            FindByNameContains(transform, "Spine");

        head = FindByNameContains(transform, "Head");
        armL = FindByNameContains(transform, "UpperArm.L");
        armR = FindByNameContains(transform, "UpperArm.R");

        if (chest != null) chestRest = chest.localRotation;
        if (head != null) headRest = head.localRotation;
        if (armL != null) armLRestWorld = armL.rotation;
        if (armR != null) armRRestWorld = armR.rotation;

        // Not: bu component sadece Clap/Cheer atanan seyircilere
        // ekleniyor (bkz. CrowdManager), yani buraya kadar
        // geldiyse kol hareketi bekleniyor demektir.
        if (armL == null || armR == null)
        {
            Debug.LogWarning(
                "[SpectatorIdleAnimator] Kol kemiği bulunamadı (" +
                gameObject.name + "): armL=" + (armL != null) +
                " armR=" + (armR != null) +
                " — kol hareketi görünmeyecek."
            );
        }

        // Her seyirci farklı fazda/hızda sallansın,
        // hepsi aynı anda hareket etmesin.
        phase = Random.Range(0f, Mathf.PI * 2f);
        speedScale = Random.Range(0.85f, 1.15f);
    }

    private void Update()
    {
        float t = Time.time * speedScale + phase;

        if (chest != null)
        {
            float sway = Mathf.Sin(t * swaySpeed) * swayAmount;

            chest.localRotation =
                chestRest * Quaternion.AngleAxis(sway, swayAxis);
        }

        if (head != null)
        {
            float bob =
                Mathf.Sin(t * swaySpeed * 1.3f) * headBobAmount;

            head.localRotation =
                headRest * Quaternion.AngleAxis(bob, headBobAxis);
        }

        switch (armBehavior)
        {
            case ArmBehavior.Cheer:
                ApplyCheer(t);
                break;

            case ArmBehavior.Clap:
                ApplyClap(t);
                break;
        }
    }

    private void ApplyCheer(float t)
    {
        float envelope =
            Mathf.Sin(t * armSpeed) * 0.5f + 0.5f;

        float raise = envelope * armAmount;
        float inward = envelope * clapInwardAmount;

        Quaternion raiseDelta =
            Quaternion.AngleAxis(-raise, transform.right);

        // Dünya-uzayında, karakterin kendi sağ eksenine göre
        // döndürülüyor — kemiğin iç ekseni ne olursa olsun iki
        // kol da aynı şekilde (öne/yukarı) kalkar. Ayrıca içe
        // çekilerek eller buluşur (Clap ile aynı mantık).
        Quaternion inwardDeltaL =
            Quaternion.AngleAxis(inward, transform.up);

        Quaternion inwardDeltaR =
            Quaternion.AngleAxis(-inward, transform.up);

        if (armL != null)
            armL.rotation = inwardDeltaL * raiseDelta * armLRestWorld;

        if (armR != null)
            armR.rotation = inwardDeltaR * raiseDelta * armRRestWorld;
    }

    private void ApplyClap(float t)
    {
        float envelope = Mathf.Abs(Mathf.Sin(t * armSpeed * 2f));

        float raise = envelope * (armAmount * 0.9f);
        float inward = envelope * clapInwardAmount;

        Quaternion raiseDelta =
            Quaternion.AngleAxis(-raise, transform.right);

        // Eller ortada buluşsun diye kollar ayrıca içe
        // (karşı karşıya) çekilir — sol ve sağ zıt yönde.
        Quaternion inwardDeltaL =
            Quaternion.AngleAxis(inward, transform.up);

        Quaternion inwardDeltaR =
            Quaternion.AngleAxis(-inward, transform.up);

        if (armL != null)
            armL.rotation = inwardDeltaL * raiseDelta * armLRestWorld;

        if (armR != null)
            armR.rotation = inwardDeltaR * raiseDelta * armRRestWorld;
    }

    private static Transform FindByNameContains(
        Transform root,
        string keyword)
    {
        foreach (Transform child in
                 root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.IndexOf(
                    keyword,
                    System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return child;
            }
        }

        return null;
    }
}
