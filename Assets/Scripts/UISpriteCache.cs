using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Kod içinde üretilen yuvarlak köşeli UI sprite'ları (panel zemini,
/// kart arkaplanı, seçim çerçevesi, slider barı...) tek bir yerden,
/// önbellekli üretir.
///
/// NEDEN: Önceden her UI sınıfı kendi kopyasını taşıyordu ve her
/// çağrıda yeni bir Texture2D üretiliyordu. İki sorun vardı:
///
/// 1) SIZINTI — Texture2D/Sprite bir GameObject'e ait olmadığı için,
///    kart yok edilince onlar bellekte kalıyordu. ShopUI her
///    RefreshRows()'ta ~11 doku sızdırıyordu (her satın almada).
///
/// 2) MALİYET — dokular SetPixel ile piksel piksel dolduruluyordu;
///    96x96'lık tek bir sprite 9.216 SetPixel çağrısı demek. Mağaza
///    listesi bir tazelemede ~100.000 çağrı yapıyordu.
///
/// Artık aynı (ölçü + yarıçap + renk + kalınlık) kombinasyonu bir kez
/// üretilip paylaşılıyor ve doldurma SetPixels ile tek seferde
/// yapılıyor. Ekranların çoğu aynı scrim/kart/çerçeve parametrelerini
/// kullandığı için önbellek ekranlar arasında da paylaşılıyor.
/// </summary>
public static class UISpriteCache
{
    private enum Shape { Solid, Outline, Bordered, VerticalGradient }

    private readonly struct Key : System.IEquatable<Key>
    {
        private readonly Shape shape;
        private readonly int width;
        private readonly int height;
        private readonly int radius;
        private readonly Color primary;
        private readonly Color secondary;
        private readonly float thickness;

        public Key(Shape shape, int width, int height, int radius, Color primary, Color secondary, float thickness)
        {
            this.shape = shape;
            this.width = width;
            this.height = height;
            this.radius = radius;
            this.primary = primary;
            this.secondary = secondary;
            this.thickness = thickness;
        }

        public Shape Shape => shape;
        public int Width => width;
        public int Height => height;
        public int Radius => radius;
        public Color Primary => primary;
        public Color Secondary => secondary;
        public float Thickness => thickness;

        public bool Equals(Key other)
        {
            return shape == other.shape
                && width == other.width
                && height == other.height
                && radius == other.radius
                && primary == other.primary
                && secondary == other.secondary
                && thickness == other.thickness;
        }

        public override bool Equals(object obj)
        {
            return obj is Key other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)shape;
                hash = hash * 397 ^ width;
                hash = hash * 397 ^ height;
                hash = hash * 397 ^ radius;
                hash = hash * 397 ^ primary.GetHashCode();
                hash = hash * 397 ^ secondary.GetHashCode();
                hash = hash * 397 ^ thickness.GetHashCode();
                return hash;
            }
        }
    }

    /// Dolu (thickness = 0) ve çerçeve sprite'ları aynı sözlükte;
    /// kalınlık anahtarın parçası olduğu için çakışmazlar.
    private static readonly Dictionary<Key, Sprite> Cache = new Dictionary<Key, Sprite>();

    /// <summary>İçi dolu, yuvarlak köşeli sprite.</summary>
    public static Sprite Rounded(int width, int height, int radius, Color color)
    {
        return Get(new Key(Shape.Solid, width, height, radius, color, default, 0f));
    }

    /// <summary>Sadece kenarı çizilen (içi boş) yuvarlak köşeli sprite.</summary>
    public static Sprite RoundedOutline(int width, int height, int radius, Color color, float thickness)
    {
        return Get(new Key(Shape.Outline, width, height, radius, color, default, Mathf.Max(0.01f, thickness)));
    }

    /// <summary>İçi dolu, ayrı renkte kenarlığı olan yuvarlak köşeli sprite.</summary>
    public static Sprite RoundedBordered(
        int width,
        int height,
        int radius,
        Color fillColor,
        Color borderColor,
        float borderThickness)
    {
        return Get(new Key(Shape.Bordered, width, height, radius, fillColor, borderColor, borderThickness));
    }

    /// <summary>Alttan üste doğru renk geçişi olan 1x128'lik dikey gradyan.</summary>
    public static Sprite VerticalGradient(Color top, Color bottom)
    {
        return Get(new Key(Shape.VerticalGradient, 1, 128, 0, top, bottom, 0f));
    }

    private static Sprite Get(Key key)
    {
        // Sahne yeniden yüklenince (dil değişimi) eski dokular yok
        // edilmiş olabilir — Unity'nin "yok edilmiş nesne" kontrolü
        // için null karşılaştırması yeterli.
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null)
        {
            return cached;
        }

        Sprite sprite = Build(key);
        Cache[key] = sprite;

        return sprite;
    }

    private static Sprite Build(Key key)
    {
        return key.Shape == Shape.VerticalGradient
            ? BuildGradient(key)
            : BuildRounded(key);
    }

    private static Sprite BuildRounded(Key key)
    {
        int width = key.Width;
        int height = key.Height;

        Texture2D texture = CreateTexture(width, height);

        Color[] pixels = new Color[width * height];

        float halfW = width * 0.5f;
        float halfH = height * 0.5f;

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width;

            for (int x = 0; x < width; x++)
            {
                float distance =
                    RoundedRectSDF(x + 0.5f - halfW, y + 0.5f - halfH, halfW, halfH, key.Radius);

                float shapeAlpha = Mathf.Clamp01(0.5f - distance);

                Color pixel;

                switch (key.Shape)
                {
                    case Shape.Outline:
                        pixel = key.Primary;
                        pixel.a *=
                            Mathf.Clamp01(
                                0.5f - Mathf.Abs(distance + key.Thickness * 0.5f) + key.Thickness * 0.5f
                            );
                        break;

                    case Shape.Bordered:
                        pixel = distance > -key.Thickness ? key.Secondary : key.Primary;
                        pixel.a *= shapeAlpha;
                        break;

                    default:
                        pixel = key.Primary;
                        pixel.a *= shapeAlpha;
                        break;
                }

                pixels[rowStart + x] = pixel;
            }
        }

        // SetPixel yerine tek seferde yükleme — piksel başına çağrı
        // maliyeti (ve sınır kontrolü) ortadan kalkıyor.
        texture.SetPixels(pixels);
        texture.Apply(false, false);

        int border = Mathf.Min(key.Radius, Mathf.Min(width, height) / 2);

        return CreateSprite(
            texture,
            width,
            height,
            new Vector4(border, border, border, border)
        );
    }

    private static Sprite BuildGradient(Key key)
    {
        int height = key.Height;

        Texture2D texture = CreateTexture(1, height);
        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[height];

        for (int y = 0; y < height; y++)
        {
            pixels[y] = Color.Lerp(key.Secondary, key.Primary, y / (float)(height - 1));
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);

        return CreateSprite(texture, 1, height, Vector4.zero);
    }

    private static Texture2D CreateTexture(int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        // Sahne değişimlerinde çöpe gitmesin diye (önbellek statik,
        // sahne ömründen bağımsız yaşıyor).
        texture.hideFlags = HideFlags.HideAndDontSave;

        return texture;
    }

    private static Sprite CreateSprite(Texture2D texture, int width, int height, Vector4 border)
    {
        Sprite sprite =
            Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                border
            );

        sprite.hideFlags = HideFlags.HideAndDontSave;

        return sprite;
    }

    private static float RoundedRectSDF(
        float x,
        float y,
        float halfWidth,
        float halfHeight,
        float radius)
    {
        float px = Mathf.Abs(x) - (halfWidth - radius);
        float py = Mathf.Abs(y) - (halfHeight - radius);

        float outsideX = Mathf.Max(px, 0f);
        float outsideY = Mathf.Max(py, 0f);

        float outsideDist = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
        float insideDist = Mathf.Min(Mathf.Max(px, py), 0f);

        return outsideDist + insideDist - radius;
    }
}
