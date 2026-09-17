using UnityEngine;

/// <summary>Bir kozmetik eşyanın hangi kemiğe takılacağını belirler.</summary>
public enum CosmeticSlot { Head, Top, Bottom, Feet }

/// <summary>Bir eşyanın hangi para birimiyle satın alındığı.</summary>
public enum CurrencyType { Gold, Diamond }

/// <summary>Mağazada satılan / Customize'da giyilebilen tek bir eşya.</summary>
[System.Serializable]
public class CosmeticItem
{
    public string id;
    public string displayName;
    public CosmeticSlot slot;
    public GameObject prefab;
    public Sprite icon;
    public CurrencyType currency;
    public int price;

    [Tooltip("FBX'in kendi materyali bozuk/renksiz çıkarsa, takılırken bunun yerine bu tekstürlü basit bir materyal uygulanır.")]
    public Texture2D colorTexture;

    [Tooltip("Beyaz bırakılırsa dokunulmaz. Başka bir renk verilirse eşya o renge boyanır ve parlak (neon/glow) görünmesi için ışıma (emission) eklenir.")]
    public Color tintColor = Color.white;

    [Header("Kemiğe göre yerel konum/açı/ölçek (Editor'de ince ayar için)")]
    public Vector3 localPositionOffset = Vector3.zero;
    public Vector3 localRotationOffsetEuler = Vector3.zero;
    public Vector3 localScale = Vector3.one;
}
