using TMPro;
using UnityEngine;

/// <summary>
/// UI yazılarının gölgesini TMP'nin kendi "underlay" materyal
/// özelliğiyle verir.
///
/// NEDEN: Önceden her etiketin arkasına koyu renkli ikinci bir TMP
/// nesnesi konuyordu — yani ekrandaki her yazı iki nesne, iki mesh,
/// iki draw call demekti. Underlay aynı işi tek nesnede yapıyor.
///
/// Materyal bir kez üretilip bütün etiketlerde PAYLAŞILIYOR; etiket
/// başına materyal kopyası çıkarılsaydı her yazı ayrı bir draw call'a
/// düşer, kazanç yerine kayıp olurdu.
///
/// Font shader'ı underlay desteklemiyorsa <see cref="ApplyShadow"/>
/// false döner ve çağıran eski (ikinci nesne) yöntemine geri döner —
/// böylece gölge hiçbir durumda kaybolmaz.
/// </summary>
public static class UITextStyle
{
    public static readonly Color ShadowColor = new Color(0f, 0.03f, 0.11f, 0.55f);

    private static Material shadowMaterial;
    private static bool resolved;

    /// <summary>
    /// Yazıya paylaşılan gölgeli materyali uygular. Shader underlay
    /// desteklemiyorsa false döner (çağıran kendi gölgesini kursun).
    /// </summary>
    public static bool ApplyShadow(TMP_Text text)
    {
        if (text == null)
            return false;

        Material material = Resolve(text);

        if (material == null)
            return false;

        text.fontSharedMaterial = material;

        return true;
    }

    private static Material Resolve(TMP_Text source)
    {
        // Sahne yeniden yüklenince materyal yok edilmiş olabilir.
        if (resolved && shadowMaterial != null)
            return shadowMaterial;

        resolved = true;

        Material baseMaterial = source.fontSharedMaterial;

        if (baseMaterial == null ||
            !baseMaterial.HasProperty(ShaderUtilities.ID_UnderlayColor))
        {
            shadowMaterial = null;
            return null;
        }

        Material material = new Material(baseMaterial);
        material.hideFlags = HideFlags.HideAndDontSave;

        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        material.SetColor(ShaderUtilities.ID_UnderlayColor, ShadowColor);

        // Offset em cinsinden — punto büyüyüp küçüldükçe gölge de
        // birlikte ölçeklenir (ikinci nesneli yöntemde bunu elle
        // oranlamak gerekiyordu).
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.4f);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.1f);
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.05f);

        shadowMaterial = material;

        return material;
    }
}
