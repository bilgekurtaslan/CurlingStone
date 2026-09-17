using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tek seferlik araç: Ice VFX materyallerinden hâlâ eski Built-in
/// parçacık shader'larını (Particles/Alpha Blended, VertexLit,
/// Additive vs.) kullananları "Universal Render Pipeline/Particles/
/// Unlit" shader'ına çevirir. Bu shader sahne ışığından (spot ışıkları
/// dahil) TAMAMEN bağımsızdır — kar/buz parçacıkları artık ışığın
/// altında/dışında olmalarına göre kararmıyor, her yerde aynı
/// parlaklıkta görünüyor.
/// Kullanımı: Tools/Curling/Convert Ice Trail Materials To Unlit.
/// </summary>
public static class ConvertIceTrailMaterialsToUnlit
{
    private const string MaterialsFolder =
        "Assets/Magic VFX/Magic VFX - Ice (FREE)/Models/Materials";

    // additive = true  -> ışıma/parlama efektleri (Glow_Ice_*)
    // additive = false -> kar taneleri gibi normal alfa karışımlı sprite'lar
    private static readonly (string name, bool additive)[] Targets =
    {
        ("SnowFlakes_01", false),
        ("SnowFlakes_02", false),
        ("SnowFlakes_03", false),
        ("SnowFlakes_04", false),
        // Additive artık kullanılmıyor: parlak (spot ışıklı) zeminde
        // "ışık üstüne ışık eklemek" beyaza yakınsayıp görünmez oluyordu.
        // Alpha Blend ile parçacık, arka plan ne kadar parlak olursa
        // olsun sabit/görünür kalıyor.
        ("Glow_Ice_02mobile", false),
        ("Glow_Ice_02add", false),
    };

    [MenuItem("Tools/Curling/Convert Ice Trail Materials To Unlit")]
    public static void Convert()
    {
        Shader unlitShader =
            Shader.Find("Universal Render Pipeline/Particles/Unlit");

        if (unlitShader == null)
        {
            Debug.LogError(
                "[ConvertIceTrailMaterialsToUnlit] 'Universal Render Pipeline/Particles/Unlit' shader'ı bulunamadı."
            );
            return;
        }

        int converted = 0;

        foreach ((string name, bool additive) in Targets)
        {
            string path = MaterialsFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Debug.LogWarning("[ConvertIceTrailMaterialsToUnlit] Bulunamadı: " + path);
                continue;
            }

            // İkinci bir çalıştırmada materyal zaten Unlit'e çevrilmiş
            // olabilir — bu durumda _TintColor artık yok, tekrar okuyup
            // rengi beyaza sıfırlamamak için sadece ilk seferde renk/doku
            // aktarımı yapılır, sonraki çalıştırmalar sadece blend modunu
            // günceller.
            bool alreadyConverted = material.shader == unlitShader;

            if (!alreadyConverted)
            {
                Texture mainTex = material.HasProperty("_MainTex")
                    ? material.GetTexture("_MainTex")
                    : null;

                Color tint = material.HasProperty("_TintColor")
                    ? material.GetColor("_TintColor")
                    : Color.white;

                material.shader = unlitShader;

                if (mainTex != null)
                {
                    material.SetTexture("_BaseMap", mainTex);
                    material.mainTexture = mainTex;
                }

                material.SetColor("_BaseColor", new Color(tint.r * 2f, tint.g * 2f, tint.b * 2f, tint.a));
            }

            // Parlak (spot ışıklı) zeminde parçacık soluyor şikayeti,
            // düşük alpha'nın arka planla fazla karışmasından kaynaklanıyordu
            // (ışığa tepki vermekten değil — Unlit zaten ışıktan bağımsız).
            // Alpha'yı belirgin şekilde yükseltip her çalıştırmada
            // zorluyoruz ki arka plan ne kadar parlak olursa olsun
            // parçacık kendi rengini korusun.
            Color current = material.GetColor("_BaseColor");
            material.SetColor("_BaseColor", new Color(current.r, current.g, current.b, 0.95f));

            material.SetFloat("_Surface", 1f); // Transparent
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");

            if (additive)
            {
                material.SetFloat("_Blend", 2f); // Additive
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.One);
                material.EnableKeyword("_BLENDMODE_ADD");
                material.DisableKeyword("_BLENDMODE_ALPHA");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            else
            {
                material.SetFloat("_Blend", 0f); // Alpha
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.EnableKeyword("_BLENDMODE_ALPHA");
                material.DisableKeyword("_BLENDMODE_ADD");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }

            material.renderQueue = (int)RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            converted++;
        }

        AssetDatabase.SaveAssets();

        Debug.Log("[ConvertIceTrailMaterialsToUnlit] " + converted + " materyal Unlit'e çevrildi (artık ışıktan etkilenmiyor).");
    }
}
