using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MuslimMatrimony.Tests;

/// <summary>Every user-facing English string must have a Swedish translation (same rules as tools/extract_strings.py).</summary>
public partial class LocalizationTests
{
    private static string WebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "MuslimMatrimony.Web")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("repo root"), "MuslimMatrimony.Web");
    }

    [GeneratedRegex("""\b[Ll]\["((?:[^"\\]|\\.)*)"[\],]""")]
    private static partial Regex LocalizerCall();

    [GeneratedRegex("ErrorMessage = \"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex ErrorMessage();

    [GeneratedRegex("""=> "([^"]+)",""")]
    private static partial Regex Label();

    [GeneratedRegex("""(?:Status\(|TempData\["Status"\] = |=> )"([A-Z][^"\\{}]*? [^"\\{}]*?[.!])"[,);]""")]
    private static partial Regex StatusMessage();

    [Fact]
    public void All_strings_have_a_swedish_translation()
    {
        var root = WebRoot();
        var resx = XDocument.Load(Path.Combine(root, "Resources", "SharedResource.sv.resx"));
        var translated = resx.Root!.Elements("data").ToDictionary(e => e.Attribute("name")!.Value, e => e.Element("value")!.Value);

        var keys = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".cshtml", StringComparison.Ordinal))
                                 && !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("Migrations", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            keys.UnionWith(LocalizerCall().Matches(text).Select(m => m.Groups[1].Value));
            keys.UnionWith(ErrorMessage().Matches(text).Select(m => m.Groups[1].Value));
            if (file.EndsWith("SharedResource.cs", StringComparison.Ordinal))
            {
                keys.UnionWith(Label().Matches(text).Select(m => m.Groups[1].Value));
            }

            if (file.Contains("/Controllers/", StringComparison.Ordinal) || file.Contains("/Areas/", StringComparison.Ordinal))
            {
                keys.UnionWith(StatusMessage().Matches(text).Select(m => m.Groups[1].Value));
            }
        }

        Assert.True(keys.Count > 200, $"Only found {keys.Count} strings; the scanner is probably broken");
        var missing = keys.Where(k => !translated.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "Missing Swedish translations:\n" + string.Join('\n', missing));
        Assert.DoesNotContain(translated, kv => string.IsNullOrWhiteSpace(kv.Value));
    }
}
