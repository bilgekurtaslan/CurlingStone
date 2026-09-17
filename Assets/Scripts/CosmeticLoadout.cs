using UnityEngine;

/// <summary>
/// Oyuncunun sahip olduğu ve o an kuşandığı kozmetik eşyaları
/// kalıcı tutar (PlayerPrefs). Fiyat düşme işi ShopUI'da (Currency
/// üzerinden) yapılır — Unlock burada sadece sahiplik kaydını yazar.
/// </summary>
public static class CosmeticLoadout
{
    private const string OwnedPrefsKeyPrefix = "curling_cosmetic_owned_";
    private const string EquippedPrefsKeyPrefix = "curling_cosmetic_equipped_";

    public static bool IsOwned(string itemId)
    {
        return PlayerPrefs.GetInt(OwnedPrefsKeyPrefix + itemId, 0) == 1;
    }

    /// <summary>Eşyayı açar — fiyatın düşülüp düşülmediği çağıranın sorumluluğunda.</summary>
    public static void Unlock(string itemId)
    {
        PlayerPrefs.SetInt(OwnedPrefsKeyPrefix + itemId, 1);
        PlayerPrefs.Save();
    }

    /// <summary>Boşsa hiçbir şey kuşanılmamış demektir.</summary>
    public static string GetEquipped(CosmeticSlot slot)
    {
        return PlayerPrefs.GetString(EquippedPrefsKeyPrefix + slot, "");
    }

    /// <summary>itemId boş/null verilirse o slot çıkarılır (hiçbir şey kuşanılmaz).</summary>
    public static void SetEquipped(CosmeticSlot slot, string itemId)
    {
        PlayerPrefs.SetString(EquippedPrefsKeyPrefix + slot, itemId ?? "");
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Test/geliştirme amaçlı: kataloğdaki tüm eşyaların sahiplik ve
    /// kuşanım kaydını sıfırlar (gold/elmas bakiyesine dokunmaz).
    /// </summary>
    public static void ResetAll(CosmeticCatalog catalog)
    {
        if (catalog != null)
        {
            foreach (CosmeticItem item in catalog.Items)
            {
                PlayerPrefs.DeleteKey(OwnedPrefsKeyPrefix + item.id);
            }
        }

        foreach (CosmeticSlot slot in System.Enum.GetValues(typeof(CosmeticSlot)))
        {
            PlayerPrefs.DeleteKey(EquippedPrefsKeyPrefix + slot);
        }

        PlayerPrefs.Save();
    }
}
