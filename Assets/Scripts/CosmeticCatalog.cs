using System.Collections.Generic;
using UnityEngine;

/// <summary>Mağazada satılan tüm eşyaların listesi (Inspector'dan doldurulur).</summary>
public class CosmeticCatalog : MonoBehaviour
{
    [SerializeField] private List<CosmeticItem> items = new List<CosmeticItem>();

    public IReadOnlyList<CosmeticItem> Items => items;

    public CosmeticItem FindById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        foreach (CosmeticItem item in items)
        {
            if (item.id == id)
                return item;
        }

        return null;
    }
}
