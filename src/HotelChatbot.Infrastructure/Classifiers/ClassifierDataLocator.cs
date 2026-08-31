namespace HotelChatbot.Infrastructure.Classifiers;

public static class ClassifierDataLocator
{
    public static string Resolve(string relativeUnderClassifiers, Type anchorType)
    {
        var fileName = Path.GetFileName(relativeUnderClassifiers);
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Classifiers", relativeUnderClassifiers),
            Path.Combine(AppContext.BaseDirectory, "Classifiers", "Data", fileName),
            Path.Combine(AppContext.BaseDirectory, "Data", "Classifiers", fileName),
            Path.Combine(AppContext.BaseDirectory, "Data", fileName),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        var asmDir = Path.GetDirectoryName(anchorType.Assembly.Location);
        if (!string.IsNullOrEmpty(asmDir))
        {
            var walk = new DirectoryInfo(asmDir);
            for (var i = 0; i < 8 && walk is not null; i++, walk = walk.Parent)
            {
                var nested = Path.Combine(walk.FullName, "Classifiers", relativeUnderClassifiers);
                if (File.Exists(nested))
                    return nested;
            }
        }

        throw new FileNotFoundException($"Klassifizierer-Daten fehlen: {relativeUnderClassifiers}");
    }
}
