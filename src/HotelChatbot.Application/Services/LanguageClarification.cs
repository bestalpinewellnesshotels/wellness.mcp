using HotelChatbot.Domain.Language;

namespace HotelChatbot.Application.Services;

/// <summary>
/// Nachfrage-Sätze und Alias-Namen für die Sprachwahl.
/// </summary>
internal static class LanguageClarification
{
    private static readonly Dictionary<string, string> Prompts = new(StringComparer.Ordinal)
    {
        ["de"] = "Möchten Sie auf Deutsch fortfahren?",
        ["en"] = "Would you like to continue in English?",
        ["nl"] = "Wilt u in het Nederlands verdergaan?",
        ["fr"] = "Souhaitez-vous continuer en français ?",
        ["it"] = "Desidera continuare in italiano?",
        ["es"] = "¿Desea continuar en español?",
        ["pt"] = "Deseja continuar em português?",
        ["pl"] = "Czy chcesz kontynuować po polsku?",
        ["cs"] = "Chcete pokračovat česky?",
        ["sk"] = "Chcete pokračovať po slovensky?",
        ["sl"] = "Želite nadaljevati v slovenščini?",
        ["hr"] = "Želite li nastaviti na hrvatskom?",
        ["hu"] = "Szeretné magyarul folytatni?",
        ["ro"] = "Doriți să continuați în română?",
        ["sv"] = "Vill du fortsätta på svenska?",
        ["da"] = "Vil du fortsætte på dansk?",
        ["no"] = "Vil du fortsette på norsk?",
        ["fi"] = "Haluatko jatkaa suomeksi?",
        ["el"] = "Θέλετε να συνεχίσουμε στα ελληνικά;",
        ["tr"] = "Türkçe devam etmek ister misiniz?",
        ["uk"] = "Бажаєте продовжити українською?",
        ["bg"] = "Искате ли да продължим на български?",
        ["sr"] = "Желите ли да наставимо на српском?",
        ["bs"] = "Želite li nastaviti na bosanskom?",
        ["mk"] = "Сакате ли да продолжиме на македонски?",
        ["sq"] = "Dëshironi të vazhdoni në shqip?",
        ["et"] = "Kas soovite jätkata eesti keeles?",
        ["lv"] = "Vai vēlaties turpināt latviešu valodā?",
        ["lt"] = "Ar norite tęsti lietuviškai?",
        ["ga"] = "Ar mhaith leat leanúint as Gaeilge?",
        ["cy"] = "Hoffech chi barhau yn Gymraeg?",
        ["is"] = "Viltu halda áfram á íslensku?",
        ["mt"] = "Tixtieq tkompli bil-Malti?",
        ["ca"] = "Voleu continuar en català?",
        ["gl"] = "Quere continuar en galego?",
        ["eu"] = "Euskaraz jarraitu nahi duzu?",
        ["lb"] = "Wëllt Dir op Lëtzebuergesch weiderfueren?",
        ["fy"] = "Wolle jo yn it Frysk fierdergean?",
        ["rm"] = "Vulais cuntinuar en rumantsch?",
        ["be"] = "Жадаеце працягнуць па-беларуску?",
        ["hy"] = "Ցանկանու՞մ եք շարունակել հայերեն:",
        ["ka"] = "გსურთ ქართულად გავაგრძელოთ?",
        ["br"] = "Ha c'hoant hoc'h eus da genderc'hel e brezhoneg?",
        ["gd"] = "A bheil thu airson leantainn anns a' Ghàidhlig?",
        ["fo"] = "Vilt tú halda fram á føroyskum?",
        ["se"] = "Háliidatgo joatkit davvisámegillii?"
    };

    private static readonly (string Alias, string Code)[] Aliases =
    [
        ("deutsch", "de"), ("german", "de"), ("allemand", "de"), ("tedesco", "de"),
        ("duits", "de"), ("alemán", "de"), ("aleman", "de"), ("német", "de"),
        ("niemiecki", "de"), ("německy", "de"), ("nemecko", "de"),
        ("english", "en"), ("englisch", "en"), ("anglais", "en"), ("inglese", "en"),
        ("inglés", "en"), ("ingles", "en"), ("engels", "en"), ("angol", "en"),
        ("angielski", "en"), ("anglicky", "en"),
        ("nederlands", "nl"), ("dutch", "nl"), ("niederländisch", "nl"),
        ("niederlandisch", "nl"), ("holländisch", "nl"), ("hollandisch", "nl"),
        ("flemish", "nl"), ("flämisch", "nl"), ("flaemisch", "nl"),
        ("vlaams", "nl"), ("flamand", "nl"), ("fiammingo", "nl"),
        ("français", "fr"), ("francais", "fr"), ("french", "fr"), ("französisch", "fr"),
        ("franzosisch", "fr"), ("francese", "fr"), ("francés", "fr"),
        ("italiano", "it"), ("italian", "it"), ("italienisch", "it"), ("italien", "it"),
        ("español", "es"), ("espanol", "es"), ("spanish", "es"), ("spanisch", "es"),
        ("spagnolo", "es"), ("castellano", "es"),
        ("português", "pt"), ("portugues", "pt"), ("portuguese", "pt"), ("portugiesisch", "pt"),
        ("polski", "pl"), ("polish", "pl"), ("polnisch", "pl"),
        ("čeština", "cs"), ("cestina", "cs"), ("czech", "cs"), ("tschechisch", "cs"),
        ("slovenčina", "sk"), ("slovencina", "sk"), ("slovak", "sk"), ("slowakisch", "sk"),
        ("slovenščina", "sl"), ("slovenscina", "sl"), ("slovene", "sl"), ("slowenisch", "sl"),
        ("hrvatski", "hr"), ("croatian", "hr"), ("kroatisch", "hr"),
        ("magyar", "hu"), ("hungarian", "hu"), ("ungarisch", "hu"),
        ("română", "ro"), ("romana", "ro"), ("romanian", "ro"), ("rumänisch", "ro"),
        ("svenska", "sv"), ("swedish", "sv"), ("schwedisch", "sv"),
        ("dansk", "da"), ("danish", "da"), ("dänisch", "da"), ("daenisch", "da"),
        ("norsk", "no"), ("norwegian", "no"), ("norwegisch", "no"),
        ("suomi", "fi"), ("finnish", "fi"), ("finnisch", "fi"),
        ("ελληνικά", "el"), ("greek", "el"), ("griechisch", "el"),
        ("türkçe", "tr"), ("turkce", "tr"), ("turkish", "tr"), ("türkisch", "tr"),
        ("українська", "uk"), ("ukrainian", "uk"), ("ukrainisch", "uk"),
        ("български", "bg"), ("bulgarian", "bg"), ("bulgarisch", "bg"),
        ("српски", "sr"), ("serbian", "sr"), ("serbisch", "sr"),
        ("bosanski", "bs"), ("bosnian", "bs"), ("bosnisch", "bs"),
        ("македонски", "mk"), ("macedonian", "mk"), ("mazedonisch", "mk"),
        ("shqip", "sq"), ("albanian", "sq"), ("albanisch", "sq"),
        ("eesti", "et"), ("estonian", "et"), ("estnisch", "et"),
        ("latviešu", "lv"), ("latvian", "lv"), ("lettisch", "lv"),
        ("lietuvių", "lt"), ("lithuanian", "lt"), ("litauisch", "lt"),
        ("gaeilge", "ga"), ("irish", "ga"), ("irisch", "ga"),
        ("cymraeg", "cy"), ("welsh", "cy"), ("walisisch", "cy"),
        ("íslenska", "is"), ("icelandic", "is"), ("isländisch", "is"),
        ("malti", "mt"), ("maltese", "mt"), ("maltesisch", "mt"),
        ("català", "ca"), ("catalan", "ca"), ("katalanisch", "ca"),
        ("galego", "gl"), ("galician", "gl"), ("galicisch", "gl"),
        ("euskara", "eu"), ("basque", "eu"), ("baskisch", "eu"),
        ("lëtzebuergesch", "lb"), ("luxembourgish", "lb"), ("luxemburgisch", "lb"),
        ("frysk", "fy"), ("frisian", "fy"), ("friesisch", "fy"), ("westfriesisch", "fy"),
        ("rumantsch", "rm"), ("romansh", "rm"), ("rätoromanisch", "rm"),
        ("беларуская", "be"), ("belarusian", "be"), ("weißrussisch", "be"),
        ("հայերեն", "hy"), ("armenian", "hy"), ("armenisch", "hy"),
        ("ქართული", "ka"), ("georgian", "ka"), ("georgisch", "ka")
    ];

    internal static readonly HashSet<string> AllCodes = new(Prompts.Keys, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Affirmatives = new(StringComparer.OrdinalIgnoreCase)
    {
        "ja", "yes", "oui", "sí", "si", "ok", "okay", "gerne", "sure",
        "jo", "yep", "yeah", "ja bitte", "yes please", "bitte"
    };

    public static string Build(IReadOnlyList<LanguageScore> candidates) =>
        string.Join("\n", candidates.Select(PromptFor));

    public static string PromptFor(LanguageScore candidate) =>
        Prompts.TryGetValue(candidate.Code, out var prompt)
            ? prompt
            : $"Would you like to continue in {candidate.Name}?";

    public static string? TryMatchAnyChoice(string text) =>
        TryMatchChoice(text, AllCodes);

    public static bool IsAffirmative(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return Affirmatives.Contains(Normalize(text));
    }

    /// <summary>
    /// Erkennt eine explizite Sprachwahl (Name oder ISO-Code) in den erlaubten Kandidaten.
    /// </summary>
    public static string? TryMatchChoice(string text, IReadOnlyCollection<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(text) || allowed.Count == 0)
            return null;

        var allowedSet = allowed as HashSet<string> ??
                         new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        var normalized = Normalize(text);

        if (normalized.Length == 2 && allowedSet.Contains(normalized))
            return normalized.ToLowerInvariant();

        foreach (var (alias, code) in Aliases.OrderByDescending(a => a.Alias.Length))
        {
            if (!allowedSet.Contains(code))
                continue;
            if (normalized == alias || HasWord(normalized, alias))
                return code;
        }

        return null;
    }

    private static string Normalize(string text)
    {
        var lower = text.Trim().ToLowerInvariant();
        return string.Join(' ',
            lower.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool HasWord(string haystack, string needle)
    {
        if (haystack == needle)
            return true;
        var start = 0;
        while (start <= haystack.Length - needle.Length)
        {
            var index = haystack.IndexOf(needle, start, StringComparison.Ordinal);
            if (index < 0)
                return false;
            var beforeOk = index == 0 || !char.IsLetter(haystack[index - 1]);
            var after = index + needle.Length;
            var afterOk = after >= haystack.Length || !char.IsLetter(haystack[after]);
            if (beforeOk && afterOk)
                return true;
            start = index + 1;
        }

        return false;
    }
}
