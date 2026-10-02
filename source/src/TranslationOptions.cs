using System.Text.RegularExpressions;

namespace SpaceTranslate;

public static class Languages
{
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    { ["en"]="英语", ["zh"]="简体中文", ["fr"]="法语", ["tr"]="土耳其语", ["th"]="泰语", ["ja"]="日语", ["ru"]="俄语", ["es"]="西班牙语", ["ko"]="韩语" };
    public static string PromptName(string code) => code switch
    { "en"=>"English", "zh"=>"Simplified Chinese", "fr"=>"French", "tr"=>"Turkish", "th"=>"Thai", "ja"=>"Japanese", "ru"=>"Russian", "es"=>"Spanish", "ko"=>"Korean", _=>throw new InvalidDataException("不支持的目标语言。") };
}

public sealed record Term(string Language, string Source, string Target);
public static class SupportEvents
{
    public static string? Filter(string message) => Regex.IsMatch(message,
        @"^(SESSION|GLOSSARY|PRE|DS|CAPTURE|MODEL|MODEL_IN_MASKED|MODEL_METRICS|REPLACE|DELIVER|CANCEL|ERROR|CLEANUP|JOB|TARGET|TOGGLE|LOCAL_PHRASE|LOCAL_DATE) [A-Za-z0-9_ =.,-]{1,550}$") ? message : null;
}
public sealed class Terminology
{
    public static readonly Terminology Empty = new([]);
    public IReadOnlyList<Term> Entries { get; }
    private Terminology(List<Term> entries) => Entries = entries.AsReadOnly();
    public static Terminology Parse(string content)
    {
        if (content.Length > 500_000) throw new InvalidDataException("词库过大，最多 500000 字符。");
        var entries = new List<Term>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        int lineNumber = 0;
        foreach (string line in content.Split('\n'))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            string[] parts = line.TrimEnd('\r').Split('\t').Select(p => p.Trim()).ToArray();
            if (parts.Length != 3 || !Languages.Names.ContainsKey(parts[0]) || parts.Skip(1).Any(p => p.Length is < 1 or > 200 || p.Any(char.IsControl) || p.Contains("ZX", StringComparison.Ordinal)))
                throw new InvalidDataException($"词库第 {lineNumber} 行格式无效：语言代码、原词、译法之间用 Tab 分隔，每个词最多 200 字。");
            // Numeric facts are owned by ProtectedText, never overridden by a glossary.
            if (parts.Skip(1).Any(p => new ProtectedText(p, false).ProtectedCount != 0))
                throw new InvalidDataException($"词库第 {lineNumber} 行包含受保护的数字、金额或日期，请只填写术语本身。");
            if (!seen.Add(parts[0] + "\t" + parts[1])) throw new InvalidDataException($"词库第 {lineNumber} 行与前面的原词重复。");
            entries.Add(new(parts[0], parts[1], parts[2]));
            if (entries.Count > 1000) throw new InvalidDataException("词库最多 1000 条。");
        }
        return new(entries);
    }
    public string Protect(string masked, string language, Func<string, string> register)
    {
        var selected = Entries.Where(e => e.Language == language).OrderByDescending(e => e.Source.Length).ToArray();
        if (selected.Length == 0) return masked;
        string Pattern(string term) => (char.IsAsciiLetter(term[0]) ? @"(?<![A-Za-z0-9_])" : "") + Regex.Escape(term) +
            (char.IsAsciiLetter(term[^1]) ? @"(?![A-Za-z0-9_])" : "");
        var map = selected.ToDictionary(e => e.Source, e => e.Target, StringComparer.Ordinal);
        // Existing fact tokens are skipped, even if a user term matches a token fragment.
        string pattern = @"\[ZX[A-Z0-9]+\]|(?:" + string.Join("|", selected.Select(e => Pattern(e.Source))) + ")";
        return Regex.Replace(masked, pattern, m => map.TryGetValue(m.Value, out var translated) ? register(translated) : m.Value,
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
