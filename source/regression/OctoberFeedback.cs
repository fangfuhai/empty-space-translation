using System.Diagnostics;
using System.Text.Json;
using SpaceTranslate;

internal static class OctoberFeedback
{
    internal const string Packaging = "货物外包装📦要求须知\n\n1# 外箱请打编织袋\n\n或者\n\n2# 外箱缠 实色膜 或者 实色胶带 裹满\n(任何颜色不是透明的膜都行)\n\n*货物务必按照以上包装要求二选一操作\n 发货前请仔细检查 \n 如不符合包装要求的先不要发货以免产生不必要的费用\n\n*外箱请不要手写/或者张贴品名货值相关内容 \n 中英文都不要写\n\n只需要贴好指定唛头就可以";
    internal static readonly string[] Cases = [
        "我直接发图片给你吧，你来判断你那边的市场哪个产品属于便宜的，我们这边恨不得每一个玩具都能赚好几千块钱",
        "qta", "有一个这个，价格是260，你看看客户是否喜欢",
        "这个玩具之前的供应商已经售罄了，其他家的供应商价格会高20rmb",
        Packaging, "市场对于这个产品的常规流通价格在380左右", "有些已经变成27Q4-28Q1"
    ];
    internal static async Task Run(string selected = "")
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceTranslate", "settings.json");
        var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException();
        settings.Log = line => { if (line.StartsWith("MODEL retry")) Console.WriteLine(line); };
        using var client = new OllamaClient(settings, new FixtureTraceHandler());
        var ids = selected.Length == 0 ? [] : selected.Split(',').Select(int.Parse).ToHashSet();
        int returned = 0, failed = 0;
        for (int i = 0; i < Cases.Length; i++)
        {
            if (ids.Count > 0 && !ids.Contains(i+1)) continue;
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var watch = Stopwatch.StartNew();
            try { string output = await client.TranslateAsync(Cases[i], true, ct.Token); returned++; Console.WriteLine(JsonSerializer.Serialize(new { id=i+1, status="RETURNED", output, ms=watch.ElapsedMilliseconds })); }
            catch (Exception e) { failed++; Console.WriteLine($"FAIL {i+1}: {e.GetType().Name}: {e.Message}"); }
        }
        Console.WriteLine($"{returned} RETURNED / {failed} FAIL / 0 SKIP; semantic review required.");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
