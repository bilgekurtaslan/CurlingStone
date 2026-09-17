using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Atış anında tam ekranı kaplayan kısa bir video oynatır — hangi
/// takımın sırası olduğuna göre (mavi/kırmızı) farklı klip seçer.
/// Video bitene kadar ekranı kaplı tutar, bitince gizlenir ve
/// verilen callback çağrılır.
/// </summary>
public class VideoOverlayController : MonoBehaviour
{
    [Tooltip("Sıra mavi takımdaysa (playerIndex 0) oynayacak atış animasyonu.")]
    [SerializeField] private VideoClip blueTeamClip;

    [Tooltip("Sıra kırmızı takımdaysa (playerIndex 1) oynayacak atış animasyonu.")]
    [SerializeField] private VideoClip redTeamClip;

    [Tooltip("Video, siyahtan belirirken (giriş) ve bitince oyuna dönerken (çıkış) süren yumuşak geçiş süresi.")]
    [SerializeField] private float fadeDuration = 0.25f;

    [Tooltip("Girişte siyah zeminin kendisinin belirmesi için süre — kamerayı gizlemesi gerektiği için kısa tutulmalı, ama 0 olursa (anında kesme) giriş çıkışla aynı akışkanlıkta hissettirmez.")]
    [SerializeField] private float blackInDuration = 0.08f;

    private RawImage rawImage;
    private VideoPlayer player;
    private RenderTexture renderTexture;
    private CanvasGroup canvasGroup;
    private Action pendingCompletion;
    private VideoClip pendingClip;

    private void Awake()
    {
        Build();
    }

    private void Build()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();

        if (canvas == null)
        {
            Debug.LogError("VideoOverlayController: Canvas bulunamadı!");
            return;
        }

        GameObject root =
            new GameObject("ThrowVideoOverlay", typeof(RectTransform));

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.SetParent(canvas.transform, false);
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        // En üstte görünsün diye kardeşler arasında en sona alınır.
        rootRect.SetAsLastSibling();

        canvasGroup = root.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        Image backing = root.AddComponent<Image>();
        backing.color = Color.black;

        GameObject imageObj =
            new GameObject("Video", typeof(RectTransform));

        RectTransform imageRect = imageObj.GetComponent<RectTransform>();
        imageRect.SetParent(rootRect, false);
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;

        rawImage = imageObj.AddComponent<RawImage>();
        rawImage.color = Color.white;

        // Render texture hazır olana kadar (siyah zeminin
        // üstünde) beyaz bir dikdörtgen görünmesin.
        rawImage.enabled = false;

        player = root.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.isLooping = false;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.audioOutputMode = VideoAudioOutputMode.Direct;
        player.loopPointReached += OnVideoFinished;

        // Varsayılan olarak mavi takımın klibini en baştan
        // hazırlamaya başla ki ilk atışta beklemeden oynayabilsin
        // (hazırlanma süresi kamerayı nişan görünümünden çıkarken
        // ham haliyle açıkta bırakmasın).
        if (blueTeamClip != null)
        {
            player.clip = blueTeamClip;
            player.Prepare();
        }
    }

    /// <summary>
    /// Atış animasyonunu tam ekran oynatır — hangi takımın
    /// (isBlueTeam) sırası olduğuna göre doğru klibi seçer. Klip
    /// atanmamışsa callback anında çağrılır (animasyonsuz devam eder).
    /// </summary>
    public void PlayThrowAnimation(bool isBlueTeam, Action completed)
    {
        VideoClip clip = isBlueTeam ? blueTeamClip : redTeamClip;

        if (clip == null || player == null)
        {
            completed?.Invoke();
            return;
        }

        // Dokunuşları hemen engelle, ama siyah zemin de (çok kısa
        // olsa da) akışkan bir fade ile gelsin — çıkışla aynı
        // "hissi" versin, anında çat diye kesilmesin. Süre video
        // hazırlanma süresinden kısa olduğu için kamera hiçbir
        // zaman açıkta kalmaz.
        canvasGroup.blocksRaycasts = true;
        StartCoroutine(
            FadeCanvasGroupAlpha(canvasGroup, canvasGroup.alpha, 1f, blackInDuration)
        );

        pendingClip = clip;
        pendingCompletion = completed;
        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        if (player.clip != pendingClip)
        {
            player.clip = pendingClip;
            player.Prepare();
        }

        if (!player.isPrepared)
        {
            player.Prepare();
        }

        while (!player.isPrepared)
        {
            yield return null;
        }

        EnsureRenderTexture((int)player.width, (int)player.height);

        // Video siyahtan (arkadaki opak siyah zemin zaten kamerayı
        // gizliyor) yumuşakça belirsin — anında "pat" diye açılmasın.
        Color transparent = rawImage.color;
        transparent.a = 0f;
        rawImage.color = transparent;
        rawImage.enabled = true;

        player.Play();

        yield return StartCoroutine(
            FadeCanvasAlpha(rawImage, 0f, 1f, fadeDuration)
        );
    }

    private static IEnumerator FadeCanvasAlpha(
        Graphic graphic,
        float from,
        float to,
        float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;

            Color color = graphic.color;
            color.a = Mathf.Lerp(from, to, t);
            graphic.color = color;

            yield return null;
        }

        Color finalColor = graphic.color;
        finalColor.a = to;
        graphic.color = finalColor;
    }

    private void EnsureRenderTexture(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (renderTexture != null &&
            renderTexture.width == width &&
            renderTexture.height == height)
        {
            return;
        }

        if (renderTexture != null)
        {
            player.targetTexture = null;
            renderTexture.Release();
        }

        renderTexture = new RenderTexture(width, height, 0);
        player.targetTexture = renderTexture;
        rawImage.texture = renderTexture;
    }

    private void OnVideoFinished(VideoPlayer source)
    {
        canvasGroup.blocksRaycasts = false;

        // Bir sonraki atışta baştan oynasın ve beklemeden
        // oynayabilsin diye hemen yeniden hazırlanmaya başla.
        player.Stop();
        player.Prepare();

        // Fizik (taşın hareketi) hemen devam etsin — sadece
        // görsel olarak video, oyunu yumuşakça açığa çıkararak
        // (siyahtan çıkışın tersi) solup gitsin.
        StartCoroutine(FadeOutOverlay());

        Action callback = pendingCompletion;
        pendingCompletion = null;
        callback?.Invoke();
    }

    private IEnumerator FadeOutOverlay()
    {
        yield return StartCoroutine(
            FadeCanvasGroupAlpha(canvasGroup, canvasGroup.alpha, 0f, fadeDuration)
        );

        rawImage.enabled = false;
    }

    private static IEnumerator FadeCanvasGroupAlpha(
        CanvasGroup group,
        float from,
        float to,
        float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;

            group.alpha = Mathf.Lerp(from, to, t);

            yield return null;
        }

        group.alpha = to;
    }
}
