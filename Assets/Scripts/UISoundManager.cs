using UnityEngine;

/// <summary>
/// Menüdeki tüm butonlar için ortak tıklama sesi. MusicManager gibi
/// sahneler arası kalıcı (DontDestroyOnLoad) tek instance.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class UISoundManager : MonoBehaviour
{
    [SerializeField] private AudioClip clickClip;

    private static UISoundManager instance;
    private AudioSource audioSource;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
    }

    /// <summary>Herhangi bir menü butonuna basılınca çağrılır.</summary>
    public static void PlayClick()
    {
        if (instance == null || instance.clickClip == null)
            return;

        instance.audioSource.PlayOneShot(instance.clickClip);
    }
}
