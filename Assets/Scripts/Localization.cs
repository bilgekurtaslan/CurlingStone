using UnityEngine;

public enum Language { EN, TR, FR, ES }

/// <summary>
/// Uygulama genelinde tek dil kaynağı. Seçim PlayerPrefs'te kalıcı
/// tutulur; dil değişince ekranlar SettingsUI'nin sahneyi yeniden
/// yüklemesiyle tazelenir (her sınıfın kendi canlı-yenileme mantığı
/// yazmasına gerek kalmaz).
/// </summary>
public static class Localization
{
    private const string PrefsKey = "curling_language";

    private static Language current = LoadSaved();

    public static Language Current => current;

    private static Language LoadSaved()
    {
        string saved = PlayerPrefs.GetString(PrefsKey, Language.EN.ToString());

        return System.Enum.TryParse(saved, out Language language)
            ? language
            : Language.EN;
    }

    public static void SetLanguage(Language language)
    {
        current = language;

        PlayerPrefs.SetString(PrefsKey, language.ToString());
        PlayerPrefs.Save();
    }

    public static string Get(string key)
    {
        switch (key)
        {
            case "team_names":
                return current switch
                {
                    Language.TR => "TAKIM İSİMLERİ",
                    Language.FR => "NOMS DES ÉQUIPES",
                    Language.ES => "NOMBRES DE EQUIPOS",
                    _ => "TEAM NAMES",
                };

            case "player1_label":
                return current switch
                {
                    Language.TR => "OYUNCU 1",
                    Language.FR => "JOUEUR 1",
                    Language.ES => "JUGADOR 1",
                    _ => "PLAYER 1",
                };

            case "player2_label":
                return current switch
                {
                    Language.TR => "OYUNCU 2",
                    Language.FR => "JOUEUR 2",
                    Language.ES => "JUGADOR 2",
                    _ => "PLAYER 2",
                };

            case "start":
                return current switch
                {
                    Language.TR => "BAŞLA",
                    Language.FR => "COMMENCER",
                    Language.ES => "EMPEZAR",
                    _ => "START",
                };

            case "select_difficulty":
                return current switch
                {
                    Language.TR => "ZORLUK SEÇ",
                    Language.FR => "CHOISIR LA DIFFICULTÉ",
                    Language.ES => "SELECCIONAR DIFICULTAD",
                    _ => "SELECT DIFFICULTY",
                };

            case "easy":
                return current switch
                {
                    Language.TR => "KOLAY",
                    Language.FR => "FACILE",
                    Language.ES => "FÁCIL",
                    _ => "EASY",
                };

            case "medium":
                return current switch
                {
                    Language.TR => "ORTA",
                    Language.FR => "MOYEN",
                    Language.ES => "MEDIO",
                    _ => "MEDIUM",
                };

            case "hard":
                return current switch
                {
                    Language.TR => "ZOR",
                    Language.FR => "DIFFICILE",
                    Language.ES => "DIFÍCIL",
                    _ => "HARD",
                };

            case "throw_label":
                return current switch
                {
                    Language.TR => "ATIŞ",
                    Language.FR => "LANCER",
                    Language.ES => "TIRO",
                    _ => "THROW",
                };

            case "game_over":
                return current switch
                {
                    Language.TR => "OYUN BİTTİ",
                    Language.FR => "PARTIE TERMINÉE",
                    Language.ES => "JUEGO TERMINADO",
                    _ => "GAME OVER",
                };

            case "coming_soon":
                return current switch
                {
                    Language.TR => "Yakında!",
                    Language.FR => "Bientôt disponible !",
                    Language.ES => "¡Próximamente!",
                    _ => "Coming soon!",
                };

            case "you_label":
                return current switch
                {
                    Language.TR => "SEN",
                    Language.FR => "TOI",
                    Language.ES => "TÚ",
                    _ => "YOU",
                };

            case "settings_title":
                return current switch
                {
                    Language.TR => "AYARLAR",
                    Language.FR => "PARAMÈTRES",
                    Language.ES => "AJUSTES",
                    _ => "SETTINGS",
                };

            case "language_label":
                return current switch
                {
                    Language.TR => "DİL",
                    Language.FR => "LANGUE",
                    Language.ES => "IDIOMA",
                    _ => "LANGUAGE",
                };

            case "music_label":
                return current switch
                {
                    Language.TR => "MÜZİK",
                    Language.FR => "MUSIQUE",
                    Language.ES => "MÚSICA",
                    _ => "MUSIC",
                };

            case "back":
                return current switch
                {
                    Language.TR => "GERİ",
                    Language.FR => "RETOUR",
                    Language.ES => "ATRÁS",
                    _ => "BACK",
                };

            case "shop_title":
                return current switch
                {
                    Language.TR => "MAĞAZA",
                    Language.FR => "BOUTIQUE",
                    Language.ES => "TIENDA",
                    _ => "SHOP",
                };

            case "customize_title":
                return current switch
                {
                    Language.TR => "ÖZELLEŞTİR",
                    Language.FR => "PERSONNALISER",
                    Language.ES => "PERSONALIZAR",
                    _ => "CUSTOMIZE",
                };

            case "unlock":
                return current switch
                {
                    Language.TR => "AÇ",
                    Language.FR => "DÉBLOQUER",
                    Language.ES => "DESBLOQUEAR",
                    _ => "UNLOCK",
                };

            case "owned":
                return current switch
                {
                    Language.TR => "SAHİPSİN",
                    Language.FR => "POSSÉDÉ",
                    Language.ES => "OBTENIDO",
                    _ => "OWNED",
                };

            case "none":
                return current switch
                {
                    Language.TR => "YOK",
                    Language.FR => "AUCUN",
                    Language.ES => "NINGUNO",
                    _ => "NONE",
                };

            case "equipped":
                return current switch
                {
                    Language.TR => "KUŞANILDI",
                    Language.FR => "ÉQUIPÉ",
                    Language.ES => "EQUIPADO",
                    _ => "EQUIPPED",
                };

            case "confirm":
                return current switch
                {
                    Language.TR => "ONAYLA",
                    Language.FR => "CONFIRMER",
                    Language.ES => "CONFIRMAR",
                    _ => "CONFIRM",
                };

            case "customize_hint":
                return current switch
                {
                    Language.TR => "Şapka almak için mağazaya uğra",
                    Language.FR => "Visite la boutique pour des chapeaux",
                    Language.ES => "Visita la tienda para sombreros",
                    _ => "Visit the shop to get hats",
                };

            case "slot_head":
                return current switch
                {
                    Language.TR => "BAŞ",
                    Language.FR => "TÊTE",
                    Language.ES => "CABEZA",
                    _ => "HEAD",
                };

            case "slot_top":
                return current switch
                {
                    Language.TR => "ÜST",
                    Language.FR => "HAUT",
                    Language.ES => "SUPERIOR",
                    _ => "TOP",
                };

            case "slot_bottom":
                return current switch
                {
                    Language.TR => "ALT",
                    Language.FR => "BAS",
                    Language.ES => "INFERIOR",
                    _ => "BOTTOM",
                };

            case "gold_unit":
                return current switch
                {
                    Language.TR => "ALTIN",
                    Language.FR => "OR",
                    Language.ES => "ORO",
                    _ => "GOLD",
                };

            case "diamond_unit":
                return current switch
                {
                    Language.TR => "ELMAS",
                    Language.FR => "DIAMANT",
                    Language.ES => "DIAMANTE",
                    _ => "DIAMOND",
                };

            case "currency_store_title":
                return current switch
                {
                    Language.TR => "ALTIN / ELMAS SATIN AL",
                    Language.FR => "ACHETER OR / DIAMANTS",
                    Language.ES => "COMPRAR ORO / DIAMANTES",
                    _ => "BUY GOLD / DIAMONDS",
                };

            case "not_enough_diamonds":
                return current switch
                {
                    Language.TR => "Yetersiz elmas!",
                    Language.FR => "Diamants insuffisants !",
                    Language.ES => "¡Diamantes insuficientes!",
                    _ => "Not enough diamonds!",
                };

            case "remove_ads":
                return current switch
                {
                    Language.TR => "REKLAMLARI KALDIR",
                    Language.FR => "SUPPRIMER LES PUBS",
                    Language.ES => "QUITAR ANUNCIOS",
                    _ => "REMOVE ADS",
                };

            case "slot_feet":
                return current switch
                {
                    Language.TR => "AYAK",
                    Language.FR => "PIEDS",
                    Language.ES => "PIES",
                    _ => "FEET",
                };

            default:
                return key;
        }
    }
}

/// <summary>Dile göre değişen bir görsel için EN/TR/FR/ES sprite kümesi — atanmamış bir dil EN'e düşer.</summary>
[System.Serializable]
public class LocalizedSpriteSet
{
    public Sprite en;
    public Sprite tr;
    public Sprite fr;
    public Sprite es;

    public Sprite Get(Language language)
    {
        Sprite sprite = language switch
        {
            Language.TR => tr,
            Language.FR => fr,
            Language.ES => es,
            _ => en,
        };

        return sprite != null ? sprite : en;
    }
}
