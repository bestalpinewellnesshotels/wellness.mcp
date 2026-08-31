namespace HotelChatbot.Infrastructure.Classifiers.Language;

public sealed record Language(string Code, string Name, string Family);

public static class LanguageCatalog
{
    public static readonly Language[] All =
    [
        new("sq", "Albanisch", "Albanisch"),
        new("hy", "Armenisch", "Armenisch"),
        new("eu", "Baskisch", "Baskisch"),
        new("be", "Weißrussisch", "Ostslawisch"),
        new("bs", "Bosnisch", "Südslawisch"),
        new("br", "Bretonisch", "Keltisch"),
        new("bg", "Bulgarisch", "Südslawisch"),
        new("ca", "Katalanisch", "Romanisch"),
        new("hr", "Kroatisch", "Südslawisch"),
        new("cs", "Tschechisch", "Westslawisch"),
        new("da", "Dänisch", "Nordgermanisch"),
        new("nl", "Niederländisch", "Westgermanisch"),
        new("en", "Englisch", "Westgermanisch"),
        new("et", "Estnisch", "Ostseefinnisch"),
        new("fo", "Färöisch", "Nordgermanisch"),
        new("fi", "Finnisch", "Ostseefinnisch"),
        new("fr", "Französisch", "Romanisch"),
        new("fy", "Westfriesisch", "Westgermanisch"),
        new("gl", "Galicisch", "Romanisch"),
        new("ka", "Georgisch", "Kartwelisch"),
        new("de", "Deutsch", "Westgermanisch"),
        new("el", "Griechisch", "Griechisch"),
        new("hu", "Ungarisch", "Ugrisch"),
        new("is", "Isländisch", "Nordgermanisch"),
        new("ga", "Irisch", "Keltisch"),
        new("it", "Italienisch", "Romanisch"),
        new("lv", "Lettisch", "Baltisch"),
        new("lt", "Litauisch", "Baltisch"),
        new("lb", "Luxemburgisch", "Westgermanisch"),
        new("mk", "Mazedonisch", "Südslawisch"),
        new("mt", "Maltesisch", "Semitisch"),
        new("se", "Nordsamisch", "Samisch"),
        new("no", "Norwegisch", "Nordgermanisch"),
        new("pl", "Polnisch", "Westslawisch"),
        new("pt", "Portugiesisch", "Romanisch"),
        new("ro", "Rumänisch", "Romanisch"),
        new("rm", "Rätoromanisch", "Romanisch"),
        new("gd", "Schottisch-Gälisch", "Keltisch"),
        new("sr", "Serbisch", "Südslawisch"),
        new("sk", "Slowakisch", "Westslawisch"),
        new("sl", "Slowenisch", "Südslawisch"),
        new("es", "Spanisch", "Romanisch"),
        new("sv", "Schwedisch", "Nordgermanisch"),
        new("tr", "Türkisch", "Turksprachen"),
        new("uk", "Ukrainisch", "Ostslawisch"),
        new("cy", "Walisisch", "Keltisch"),
    ];

    public static IReadOnlyDictionary<string, Language> ByCode { get; } =
        All.ToDictionary(language => language.Code, StringComparer.Ordinal);

    public static string NameOf(string code) =>
        ByCode.TryGetValue(code, out var language) ? language.Name : code;
}
