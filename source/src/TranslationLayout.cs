using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpaceTranslate;

public sealed class TranslationLayout
{
    private readonly string[] lines;
    public IReadOnlyDictionary<string, string> Fields { get; }
    public TranslationLayout(string text)
    {
        lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        Fields = lines.Select((value, index) => (value, key: $"line{index:D3}"))
            .Where(p => !string.IsNullOrWhiteSpace(p.value)).ToDictionary(p => p.key, p => p.value);
        if (Fields.Count > 80) throw new InvalidDataException("内容超过 80 个非空行，请分段翻译。");
    }
    public string RestoreJson(string response)
    {
        string json = response.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal) && json.EndsWith("```", StringComparison.Ordinal) && json.Contains('\n'))
            json = json[(json.IndexOf('\n') + 1)..^3].Trim();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != Fields.Count ||
                root.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != Fields.Count)
                throw new InvalidDataException("多行译文结构不完整，原文已保留。");
            var output = (string[])lines.Clone();
            foreach (var (key, _) in Fields)
            {
                if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    throw new InvalidDataException("译文遗漏了标题或段落，原文已保留。");
                output[int.Parse(key[4..])] = value.GetString()!.Trim();
            }
            return string.Join("\n", output);
        }
        catch (JsonException e) { throw new InvalidDataException("多行译文 JSON 无效，原文已保留。", e); }
    }
    public static string CleanBreaks(string translated, string source)
    {
        const string breaks = @"<(?:br|dr)\s*/?>|&lt;(?:br|dr)\s*/?&gt;";
        if (Regex.IsMatch(source, breaks, RegexOptions.IgnoreCase)) return translated;
        return Regex.Replace(translated, breaks, "\n", RegexOptions.IgnoreCase);
    }
}
