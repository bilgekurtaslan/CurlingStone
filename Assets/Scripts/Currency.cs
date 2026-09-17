using UnityEngine;

/// <summary>
/// Oyuncunun coin/elmas bakiyesi — PlayerPrefs'te kalıcı. Coin
/// AI'a karşı kazanılan maçlardan (zorluğa göre) kazanılır; elmas
/// şu an için sadece "Buy Diamonds" ekranından (gerçek ödeme henüz
/// bağlanmadı) artabilir.
/// </summary>
public static class Currency
{
    private const string GoldKey = "curling_gold";
    private const string DiamondsKey = "curling_diamonds";
    private const string AdsRemovedKey = "curling_ads_removed";

    public static int Gold => PlayerPrefs.GetInt(GoldKey, 0);
    public static int Diamonds => PlayerPrefs.GetInt(DiamondsKey, 0);
    public static bool AdsRemoved => PlayerPrefs.GetInt(AdsRemovedKey, 0) == 1;

    public static void AddGold(int amount)
    {
        if (amount <= 0)
            return;

        PlayerPrefs.SetInt(GoldKey, Gold + amount);
        PlayerPrefs.Save();
    }

    public static void AddDiamonds(int amount)
    {
        if (amount <= 0)
            return;

        PlayerPrefs.SetInt(DiamondsKey, Diamonds + amount);
        PlayerPrefs.Save();
    }

    public static bool TrySpendGold(int amount)
    {
        if (Gold < amount)
            return false;

        PlayerPrefs.SetInt(GoldKey, Gold - amount);
        PlayerPrefs.Save();
        return true;
    }

    public static bool TrySpendDiamonds(int amount)
    {
        if (Diamonds < amount)
            return false;

        PlayerPrefs.SetInt(DiamondsKey, Diamonds - amount);
        PlayerPrefs.Save();
        return true;
    }

    public static void MarkAdsRemoved()
    {
        PlayerPrefs.SetInt(AdsRemovedKey, 1);
        PlayerPrefs.Save();
    }

    /// <summary>Zorluğa göre AI'a karşı kazanılan maç başına coin ödülü.</summary>
    public static int GetMatchReward(AIOpponent.AIDifficulty difficulty)
    {
        switch (difficulty)
        {
            case AIOpponent.AIDifficulty.Easy:
                return 15;

            case AIOpponent.AIDifficulty.Hard:
                return 50;

            default:
                return 30;
        }
    }
}
