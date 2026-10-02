using System.Diagnostics;
using System.Text.Json;
using SpaceTranslate;

// Explicit opt-in only: dotnet run --project source/tests -- live.
// Calls the configured local model with these fixed, non-private fixtures.
// Does not touch the clipboard, keyboard, configuration, model selection or desktop.
internal static class LiveSmoke
{
    public static async Task RunAsync()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceTranslate", "settings.json");
        var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Missing settings");
        settings.Validate();
        Console.WriteLine($"LIVE model={settings.Model} build={settings.BuildStamp}");
        using var client = new OllamaClient(settings, new TimingHandler());
        int failures = 0;
        // Cold loading is reported separately from the measured warm samples.
        await Run("warmup", "你好，请问需要多少件？", true, 120);
        (string Id, string Source, bool English)[] cases =
        [
            ("stock", "还有一些现货", true),
            ("date", "请在2026年9月29日发货。", true),
            ("prefix-money", "接下来测试：我给他100块钱，然后他还给我50美金。我不知道是为什么，反正最后，我就只剩下20日元了", true),
            ("mixed", "Has any part been拆开过？", true),
            ("mixed-statistics", "It's okay, this is just something I did casually. The trouble is gathering and统计信息", true),
            ("mixed-stock", "This is a new contact for a seller with现货", true),
            ("mixed-logistics", "Tomorrow, you can放心, I have chosen the fastest logistics", true),
            ("emotion", "我真的很不满意。我不能保证明天到，也不想再等了。", true),
            ("please", "请告诉我你的名字。", true),
            ("classifier", "我买了1块蛋糕，不是100元件。", true),
            ("reverse", "I cannot promise delivery tomorrow. Some are still in stock.", false)
        ];
        foreach (var item in cases) await Run(item.Id, item.Source, item.English, settings.TimeoutSeconds);
        Console.WriteLine($"LIVE completed={cases.Length + 1} failures={failures}; returned text still requires semantic review.");
        Environment.ExitCode = failures > 0 ? 1 : 0;

        async Task Run(string id, string source, bool english, int seconds)
        {
            var watch = Stopwatch.StartNew();
            using var token = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            try
            {
                string result = await client.TranslateAsync(source, english, token.Token);
                Console.WriteLine(JsonSerializer.Serialize(new { id, status = "RETURNED", elapsed_ms = watch.ElapsedMilliseconds, result }));
            }
            catch (Exception error)
            {
                failures++;
                Console.WriteLine(JsonSerializer.Serialize(new { id, status = "FAIL", elapsed_ms = watch.ElapsedMilliseconds, error = error.GetType().Name, message = error.Message }));
            }
        }
    }
    private sealed class TimingHandler : DelegatingHandler
    {
        public TimingHandler() : base(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            try
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                long Value(string name) => json.RootElement.TryGetProperty(name, out var value) && value.TryGetInt64(out long n) ? n : 0;
                Console.WriteLine(JsonSerializer.Serialize(new { load_ms = Value("load_duration") / 1_000_000, prompt_tokens = Value("prompt_eval_count"), prompt_ms = Value("prompt_eval_duration") / 1_000_000, output_tokens = Value("eval_count"), output_ms = Value("eval_duration") / 1_000_000 }));
                return response;
            }
            catch { response.Dispose(); throw; }
        }
    }
}
