using UnityEngine;

/// <summary>
/// Oyun açılırken bir kez çalışan kare hızı ayarı.
///
/// NEDEN: QualitySettings'te vSyncCount 0 ve hiçbir yerde
/// Application.targetFrameRate ayarlanmamıştı. Mobilde bunun anlamı
/// "cihaz gücü yettiğince render et" — menüde 200 FPS basıp pil yakar,
/// telefon ısınınca işlemci kısılır ve oyun asıl gerektiği anda
/// dalgalanır. Sabit bir tavan, hem ısınmayı hem de kare süresindeki
/// oynamayı belirgin şekilde azaltır.
///
/// Sahneye bileşen eklemeye gerek yok: RuntimeInitializeOnLoadMethod
/// ile sahne yüklenmeden önce kendiliğinden çalışır.
/// </summary>
public static class PerformanceSettings
{
    /// Hedef kare hızı. 30'a çekmek pil ömrünü daha da uzatır ama
    /// taşın kayışı daha az akıcı görünür.
    private const int TargetFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        // vSync açıkken targetFrameRate yok sayılır — mobilde zaten
        // ekran yenileme hızına kilitlenmek istemiyoruz, tavanı biz
        // koyuyoruz.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFrameRate;
    }
}
