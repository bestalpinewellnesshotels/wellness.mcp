using System.Text;

namespace HotelChatbot.Infrastructure.Classifiers.Language;

public static class Greetings
{
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.Ordinal)
    {
        ["hi"] = ["en"],
        ["hello"] = ["en"],
        ["hey"] = ["en"],
        ["good morning"] = ["en"],
        ["good afternoon"] = ["en"],
        ["good evening"] = ["en"],
        ["good day"] = ["en"],
        ["hallo"] = ["de", "nl", "da", "no"],
        ["guten morgen"] = ["de"],
        ["guten tag"] = ["de"],
        ["guten abend"] = ["de"],
        ["servus"] = ["de"],
        ["moin"] = ["de"],
        ["grüß gott"] = ["de"],
        ["grüss gott"] = ["de"],
        ["hoi"] = ["nl"],
        ["goedemorgen"] = ["nl"],
        ["goedendag"] = ["nl"],
        ["goedemiddag"] = ["nl"],
        ["goedenavond"] = ["nl"],
        ["goeie"] = ["fy"],
        ["goeie moarn"] = ["fy"],
        ["moien"] = ["lb"],
        ["gudde moien"] = ["lb"],
        ["hej"] = ["sv", "da"],
        ["hallå"] = ["sv"],
        ["halla"] = ["sv"],
        ["god morgon"] = ["sv"],
        ["goddag"] = ["da"],
        ["godmorgen"] = ["da"],
        ["hei"] = ["no", "fi"],
        ["god morgen"] = ["no"],
        ["halló"] = ["is"],
        ["hæ"] = ["is"],
        ["góðan daginn"] = ["is"],
        ["góðan morgun"] = ["fo"],
        ["bonjour"] = ["fr"],
        ["salut"] = ["fr"],
        ["bonsoir"] = ["fr"],
        ["coucou"] = ["fr"],
        ["hola"] = ["es"],
        ["buenos días"] = ["es"],
        ["buenos dias"] = ["es"],
        ["buenas tardes"] = ["es"],
        ["buenas noches"] = ["es"],
        ["ciao"] = ["it"],
        ["buongiorno"] = ["it"],
        ["salve"] = ["it"],
        ["buonasera"] = ["it"],
        ["olá"] = ["pt"],
        ["ola"] = ["gl", "pt"],
        ["oi"] = ["pt"],
        ["bom dia"] = ["pt"],
        ["boa tarde"] = ["pt"],
        ["boa noite"] = ["pt"],
        ["bon dia"] = ["ca"],
        ["bos días"] = ["gl"],
        ["bună"] = ["ro"],
        ["buna"] = ["ro"],
        ["bună ziua"] = ["ro"],
        ["alo"] = ["ro"],
        ["allegra"] = ["rm"],
        ["bun di"] = ["rm"],
        ["cześć"] = ["pl"],
        ["czesc"] = ["pl"],
        ["witaj"] = ["pl"],
        ["dzień dobry"] = ["pl"],
        ["dzien dobry"] = ["pl"],
        ["ahoj"] = ["cs", "sk"],
        ["čau"] = ["cs", "sk", "lv"],
        ["cau"] = ["cs"],
        ["nazdar"] = ["cs"],
        ["dobrý den"] = ["cs"],
        ["dobrý deň"] = ["sk"],
        ["živjo"] = ["sl"],
        ["zdravo"] = ["sl", "hr", "bs", "sr"],
        ["dober dan"] = ["sl"],
        ["bok"] = ["hr"],
        ["dobar dan"] = ["hr", "bs", "sr"],
        ["ćao"] = ["hr", "bs", "sr"],
        ["cao"] = ["hr"],
        ["здраво"] = ["sr", "mk"],
        ["добар дан"] = ["sr", "mk"],
        ["здравей"] = ["bg"],
        ["здрасти"] = ["bg"],
        ["добър ден"] = ["bg"],
        ["привіт"] = ["uk"],
        ["вітаю"] = ["uk"],
        ["доброго дня"] = ["uk"],
        ["прывітанне"] = ["be"],
        ["добры дзень"] = ["be"],
        ["labas"] = ["lt"],
        ["sveiki"] = ["lt", "lv"],
        ["labas rytas"] = ["lt"],
        ["labdien"] = ["lv"],
        ["tere"] = ["et"],
        ["tervist"] = ["et"],
        ["moi"] = ["fi"],
        ["terve"] = ["fi"],
        ["hyvää päivää"] = ["fi"],
        ["szia"] = ["hu"],
        ["helló"] = ["hu"],
        ["jó napot"] = ["hu"],
        ["γεια"] = ["el"],
        ["γεια σου"] = ["el"],
        ["καλημέρα"] = ["el"],
        ["καλησπέρα"] = ["el"],
        ["tungjatjeta"] = ["sq"],
        ["përshëndetje"] = ["sq"],
        ["pershendetje"] = ["sq"],
        ["mirëdita"] = ["sq"],
        ["dia dhuit"] = ["ga"],
        ["haigh"] = ["ga"],
        ["helo"] = ["cy"],
        ["shwmae"] = ["cy"],
        ["bore da"] = ["cy"],
        ["halò"] = ["gd"],
        ["demat"] = ["br"],
        ["kaixo"] = ["eu"],
        ["egun on"] = ["eu"],
        ["bonġu"] = ["mt"],
        ["bongu"] = ["mt"],
        ["bures"] = ["se"],
        ["merhaba"] = ["tr"],
        ["selam"] = ["tr"],
        ["günaydın"] = ["tr"],
        ["გამარჯობა"] = ["ka"],
        ["բարև"] = ["hy"],
        ["բարև ձեզ"] = ["hy"],
    };

    public static bool TryMatch(string text, out string[] codes)
    {
        codes = [];
        var key = Normalize(text);
        if (key.Length == 0)
        {
            return false;
        }

        if (Map.TryGetValue(key, out var matched))
        {
            codes = Known(matched);
            return codes.Length > 0;
        }

        return false;
    }

    public static bool IsKnown(string text) => TryMatch(text, out _);

    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.Normalize(NormalizationForm.FormC).ToLowerInvariant().EnumerateRunes())
        {
            if (Rune.IsLetter(rune) || rune.Value is ' ')
            {
                builder.Append(rune);
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string[] Known(string[] codes) =>
        codes.Where(LanguageCatalog.ByCode.ContainsKey).Distinct().ToArray();
}
