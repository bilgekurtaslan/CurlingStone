using UnityEngine;

/// <summary>
/// Arka plan müziği: sahneler arasında (dil değişince ya da Main
/// Menu'ye dönülünce yaşanan reload'larda) hayatta kalan tek
/// instance. Ses seviyesi PlayerPrefs'te kalıcı. musicClip henüz
/// atanmadıysa sessizce bekler — sistem hazır, parça sonradan
/// Inspector'dan eklenebilir.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicManager : MonoBehaviour
{
    private const string VolumePrefsKey = "curling_music_volume";
    private const float DefaultVolume = 0.6f;

    [SerializeField] private AudioClip musicClip;

    [Tooltip("Kulaklık kuşanılmışsa, maç/mod başlayınca menü müziği yerine bu parça çalar.")]
    [SerializeField] private AudioClip headphoneGameplayClip;

    private const string HeadphonesItemId = "headphones";

    private static MusicManager instance;
    private AudioSource audioSource;

    public static MusicManager Instance => instance;

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
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.clip = musicClip;

        SetVolume(PlayerPrefs.GetFloat(VolumePrefsKey, DefaultVolume));

        if (musicClip != null)
        {
            audioSource.Play();
        }
    }

    public float CurrentVolume => audioSource != null ? audioSource.volume : 0f;

    /// <summary>Ana menü açıldığında (ilk yüklemede ya da geri dönüldüğünde) çağrılır.</summary>
    public void PlayMenuMusic()
    {
        if (audioSource == null || musicClip == null)
            return;

        audioSource.clip = musicClip;
        audioSource.loop = true;

        if (!audioSource.isPlaying)
        {
            audioSource.Play();
        }
    }

    /// <summary>
    /// Bir maç/mod başladığında (Local/AI/Chill) çağrılır — menü
    /// müziği kesilir. Kulaklık kuşanılmışsa yerine oyun içi parça
    /// çalar, kuşanılmamışsa hiç müzik çalmaz.
    /// </summary>
    public void EnterGameplay()
    {
        if (audioSource == null)
            return;

        bool headphonesEquipped =
            CosmeticLoadout.GetEquipped(CosmeticSlot.Head) == HeadphonesItemId;

        if (headphonesEquipped && headphoneGameplayClip != null)
        {
            audioSource.clip = headphoneGameplayClip;
            audioSource.loop = true;
            audioSource.Play();
        }
        else
        {
            audioSource.Stop();
        }
    }

    public void SetVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);

        if (audioSource != null)
        {
            audioSource.volume = volume;
        }

        PlayerPrefs.SetFloat(VolumePrefsKey, volume);
        PlayerPrefs.Save();
    }
}
