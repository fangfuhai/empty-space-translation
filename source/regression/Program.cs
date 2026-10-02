using System.Diagnostics;
using System.Net;
using System.Text.Json;
using SpaceTranslate;

if (args is ["live-feedback"]) { await LiveFeedback(); return; }
if (args is ["live-feedback", var selected]) { await LiveFeedback(selected); return; }
if (args is ["live-languages"]) { await LiveLanguages(); return; }
int passed = 0, failed = 0;
async Task Check(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception e) { Console.WriteLine($"FAIL {name}: {e}"); failed++; }
}
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
}
Task Sync(Action action) { action(); return Task.CompletedTask; }
void Reject(Action action)
{
    try { action(); } catch (InvalidDataException) { return; }
    throw new Exception("Expected InvalidDataException");
}
async Task RejectAsync(Func<Task> action)
{
    try { await action(); } catch (InvalidDataException) { return; }
    throw new Exception("Expected InvalidDataException");
}
await Check("glossary validates complete input and rejects duplicates and protected facts", () => Sync(() =>
{
    Equal(2, Terminology.Parse("# comment\nen\t轴承\tbearing\nfr\t轴承\troulement").Entries.Count);
    foreach (string invalid in new[] { "xx\t轴承\tx", "en\t轴承", "en\t轴承\t", "en\t轴承\tx\nen\t轴承\ty", "en\t价格\t100美元", "en\tAB-123\tpart" })
        Reject(() => Terminology.Parse(invalid));
}));
await Check("glossary longest match, language isolation, boundaries and strict restoration", () => Sync(() =>
{
    var terms = Terminology.Parse("en\t轴承\tbearing\nen\t滚珠轴承\tball bearing\nen\tcat\tfeline\nfr\t轴承\troulement");
    var text = new ProtectedText("滚珠轴承和轴承 cat category 100美元", true, terms, "en");
    Equal("ball bearing和bearing feline category USD 100", text.Restore(text.Masked));
    Equal(4, text.ProtectedCount);
    Reject(() => text.Restore("nothing"));
    Reject(() => text.Restore(text.Masked + text.ProtectedMap.Keys.Last()));
    Equal("roulement", new ProtectedText("轴承", true, terms, "fr").Restore("[ZXQTERM0Z]"));
    var collision = new ProtectedText("[ZXQTERM0Z] 轴承", true, terms, "en");
    Equal("[ZXQTERM0Z] bearing", collision.Restore(collision.Masked));
}));
await Check("user glossary wins over built-in phrase and remains out of saved settings", async () =>
{
    var settings = new Settings { Terms = Terminology.Parse("en\t我知道了\tUnderstood.") };
    Equal(false, JsonSerializer.Serialize(settings).Contains("Understood"));
    using var client = new OllamaClient(settings, new MockHandler(_ => Task.FromResult(Reply("{\"translation\":\"[ZXQTERM0Z]\"}"))));
    Equal("Understood.", await client.TranslateAsync("我知道了", "en", CancellationToken.None));
});
await Check("each language uses its own prompt and accepts its writing system", async () =>
{
    foreach (var pair in new Dictionary<string,string> { ["fr"]="Bonjour", ["tr"]="Merhaba", ["th"]="สวัสดี", ["ja"]="こんにちは", ["ru"]="Здравствуйте", ["es"]="Hola", ["ko"]="안녕하세요" })
    {
        using var client = new OllamaClient(new Settings(), new MockHandler(async request =>
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            string prompt = json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            Equal(true, prompt.Contains("Translate ALL user text into " + Languages.PromptName(pair.Key)));
            Equal(false, prompt.Contains("Output must be Simplified Chinese"));
            return Reply(JsonSerializer.Serialize(new { translation = pair.Value }));
        }));
        Equal(pair.Value, await client.TranslateAsync("你好", pair.Key, CancellationToken.None));
    }
});
await Check("support export excludes raw output, arbitrary errors, personal paths and hashes", () => Sync(() =>
{
    Equal("ERROR stage=request type=IOException", SupportEvents.Filter("ERROR stage=request type=IOException"));
    foreach (string message in new[] { "START Exe=C:\\Users\\private", "FATAL private chat", "MODEL_OUT_RAW sha=1234", "ERROR 私人文字", "MODEL hello\nprivate" })
        Equal<string?>(null, SupportEvents.Filter(message));
}));
await Check("term tokens survive a sentence; values are hidden from model prompt", async () =>
{
    var settings = new Settings { Terms = Terminology.Parse("fr\t滚珠轴承\troulement à billes") };
    using var client = new OllamaClient(settings, new MockHandler(async request =>
    {
        string body = await request.Content!.ReadAsStringAsync();
        Equal(false, body.Contains("roulement"));
        return Reply("{\"translation\":\"Envoyez [ZXQTERM0Z] demain.\"}");
    }));
    Equal("Envoyez roulement à billes demain.", await client.TranslateAsync("明天发送滚珠轴承。", "fr", CancellationToken.None));
});
await Check("non-Japanese foreign targets reject Chinese residue after bounded retry", async () =>
{
    foreach (string code in new[] { "fr", "tr", "th", "ru", "es", "ko" })
    {
        int calls = 0;
        using var client = new OllamaClient(new Settings(), new MockHandler(_ =>
        { calls++; return Task.FromResult(Reply("{\"translation\":\"hello 不要提前\"}")); }));
        await RejectAsync(() => client.TranslateAsync("不要提前", code, CancellationToken.None));
        Equal(2, calls);
    }
});
await Check("language settings save atomically and old configuration defaults to English", () => Sync(() =>
{
    var old = JsonSerializer.Deserialize<Settings>("{}")!;
    Equal("en", old.TargetLanguage);
    string folder = Path.Combine(Path.GetTempPath(), "SpaceTranslate-settings-test-" + Guid.NewGuid());
    Directory.CreateDirectory(folder);
    string path = Path.Combine(folder, "settings.json");
    try
    {
        old.TargetLanguage = "ja"; old.Save(path);
        Equal("ja", Settings.Load(path).TargetLanguage);
        Equal(false, File.Exists(path + ".tmp"));
        old.TargetLanguage = "es"; old.Save(path);
        Equal("es", Settings.Load(path).TargetLanguage);
    }
    finally { File.Delete(path); Directory.Delete(folder); }
}));
await Check("wait-and-see is an exact whole-sentence match without added praise", async () =>
{
    using var client = new OllamaClient(new Settings(), new MockHandler(_ => throw new Exception("Unexpected model request")));
    foreach (string source in new[] { "那我就拭目以待吧", "那我就拭目以待吧。" })
        Equal("I'll just wait and see then.", await client.TranslateAsync(source, true, CancellationToken.None));
});
await Check("ISO leap date is one intact fact in both directions", () => Sync(() =>
{
    foreach (bool english in new[] { true, false })
    {
        var p = new ProtectedText("Please ship on 2028-02-29, do not ship early.", english);
        Equal(1, p.ProtectedCount);
        Equal(true, p.ProtectedMap.Keys.Single().Contains("DATE"));
        Equal("Please ship on 2028-02-29, do not ship early.", p.Restore(p.Masked));
        string token = p.ProtectedMap.Keys.Single();
        Reject(() => p.Restore("请在2028-02-29发货，不要提前发货。"));
        Reject(() => p.Restore(token + token));
        Reject(() => p.Restore(token.Replace("ZXQ", "XZQ")));
    }
    foreach (string invalid in new[] { "2027-02-29", "2028-04-31", "2028-00-01" })
        Reject(() => new ProtectedText(invalid, false));
    foreach (string code in new[] { "SKU2028-02-29A", "AB-2028-02-29", "2028-02-29-ABC" })
    {
        var p = new ProtectedText(code, false);
        Equal(false, p.ProtectedMap.Keys.Any(k => k.Contains("DATE")));
        Equal(code, p.Restore(p.Masked));
    }
}));
await Check("Chinese request hides date values and restores date with the shipping refusal", async () =>
{
    using var client = new OllamaClient(new Settings(), new MockHandler(async request =>
    {
        string body = await request.Content!.ReadAsStringAsync();
        Equal(false, body.Contains("2028-02-29"));
        using var json = JsonDocument.Parse(body);
        string user = json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Equal("Please ship on [ZXQDATE0Z], do not ship early.", user);
        return Reply("{\"translation\":\"请在[ZXQDATE0Z]发货，不要提前发货。\"}");
    }));
    Equal("请在2028-02-29发货，不要提前发货。", await client.TranslateAsync("Please ship on 2028-02-29, do not ship early.", false, CancellationToken.None));
});
await Check("Chinese numeral dates and omitted day suffix are exact local translations", async () =>
{
    using var client = new OllamaClient(new Settings(), new MockHandler(_ => throw new Exception("Unexpected model request")));
    foreach (var pair in new[] {
        ("二〇二六年九月二十九日", "2026-09-29"), ("二零二六年九月二十九号", "2026-09-29"),
        ("二千零二十六年九月二十九", "2026-09-29"), ("十月二十七", "Oct 27"), ("9月10", "Sep 10"),
        ("十二月三十一号", "Dec 31"), ("二〇二八年二月二十九日", "2028-02-29"), ("2026年9月29日", "2026-09-29") })
        Equal(pair.Item2, await client.TranslateAsync(pair.Item1, true, CancellationToken.None));
});
await Check("invalid and ambiguous date numerals fail instead of guessing", () => Sync(() =>
{
    foreach (string source in new[] { "二〇二六年二月二十九日", "十三月十日", "九月三十一", "十十月二日", "2026年0月1日" })
        Reject(() => new ProtectedText(source, true));
}));
await Check("dates embedded in prose stay whole; counts are not dates", () => Sync(() =>
{
    var p = new ProtectedText("请在九月十号发货，十月二十七到货", true);
    Equal(2, p.ProtectedCount);
    Equal("请在Sep 10发货，Oct 27到货", p.Restore(p.Masked));
    Equal<string?>(null, p.StandaloneDate);
    foreach (string source in new[] { "9月10个订单", "九月十件货", "这个月二十天", "一块蛋糕、这个模块、三个单元" })
    {
        var text = new ProtectedText(source, true);
        Equal(false, text.ProtectedMap.Keys.Any(x => x.Contains("DATE")));
        Equal(source, text.Restore(text.Masked));
    }
}));
await Check("Chinese currency amounts preserve exact numeric value as whole facts", () => Sync(() =>
{
    foreach (var pair in new[] {
        ("八百八十八人民币", "CNY 888"), ("一千零二十美元", "USD 1020"), ("三万零五十日元", "JPY 30050"),
        ("一亿零三万零五人民币", "CNY 100030005"), ("两百点五美金", "USD 200.5"), ("一百零二欧元", "EUR 102") })
    {
        var p = new ProtectedText(pair.Item1, true);
        Equal(1, p.ProtectedCount);
        Equal(pair.Item2, p.Restore(p.Masked));
    }
    Reject(() => new ProtectedText("一百二人民币", true));
    Reject(() => new ProtectedText("一万二美元", true));
}));
await Check("Chinese classifiers and longer words never become money", () => Sync(() =>
{
    foreach (string source in new[] { "一块蛋糕", "三元组", "十元件", "1块蛋糕", "100元件", "两个单元", "模块" })
    {
        var p = new ProtectedText(source, true);
        Equal(source, p.Restore(p.Masked));
        Equal(false, p.ProtectedMap.Values.Any(x => x.StartsWith("CNY")));
    }
}));
await Check("new Chinese amount, missing fact and duplicate fact are rejected", () => Sync(() =>
{
    var p = new ProtectedText("八百八十八人民币", true);
    string key = p.ProtectedMap.Keys.Single();
    Reject(() => p.Restore("No amount"));
    Reject(() => p.Restore(key + key));
    Reject(() => p.Restore(key + " 加一百美元"));
    Equal("CNY 888", p.Restore(key));
}));
await Check("reverse direction retains Chinese amount and date spelling", () => Sync(() =>
{
    foreach (string source in new[] { "八百八十八人民币", "二〇二六年九月二十九日" })
    {
        var p = new ProtectedText(source, false);
        Equal(source, p.Restore(p.Masked));
    }
}));
await Check("reassurance exact matches add no shipment promise", async () =>
{
    using var client = new OllamaClient(new Settings(), new MockHandler(_ => throw new Exception("Unexpected model request")));
    Equal("You can rest assured.", await client.TranslateAsync("你可以放心", true, CancellationToken.None));
    Equal("You can rest assured about tomorrow.", await client.TranslateAsync("明天，你可以放心", true, CancellationToken.None));
    Equal("It will arrive tomorrow, so you can rest assured.", await client.TranslateAsync("明天就到了，你可以放心", true, CancellationToken.None));
});
await Check("reassurance negation and longer sentences do not hit phrase dictionary", async () =>
{
    int calls = 0;
    using var client = new OllamaClient(new Settings(), new MockHandler(_ => { calls++; return Task.FromResult(Reply("I am still worried.")); }));
    foreach (string source in new[] { "我不放心", "你不可以放心", "明天，你可以放心，但我不能保证到货", "你可以放心，我已核对地址" })
        await client.TranslateAsync(source, true, CancellationToken.None);
    Equal(4, calls);
});
await Check("colon prefix and body are required in one stateless contextual request", async () =>
{
    int calls = 0;
    using var client = new OllamaClient(new Settings(), new MockHandler(async request =>
    {
        calls++;
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        var root = json.RootElement;
        Equal(2, root.GetProperty("messages").GetArrayLength());
        Equal(2, root.GetProperty("format").GetProperty("required").GetArrayLength());
        string readableInput = root.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Equal(true, readableInput.Contains("接下来测试")); // The model must see Chinese, not literal \\u escapes.
        using var input = JsonDocument.Parse(readableInput);
        Equal("接下来测试一段稍微长一点的东西", input.RootElement.GetProperty("prefix").GetString());
        Equal("我不知道为什么。", input.RootElement.GetProperty("body").GetString());
        return Reply(JsonSerializer.Serialize(new { prefix = "Next, let's test something a little longer", translation = "I don't know why." }));
    }));
    Equal("Next, let's test something a little longer: I don't know why.", await client.TranslateAsync("接下来测试一段稍微长一点的东西：我不知道为什么。", true, CancellationToken.None));
    Equal(1, calls);
});
await Check("missing, empty or punctuation-only prefix fails rather than silently dropping it", async () =>
{
    foreach (string content in new[] { "{\"translation\":\"Hello\"}", "{\"prefix\":\"\",\"translation\":\"Hello\"}", "{\"prefix\":\"：\",\"translation\":\"Hello\"}", "Hello" })
    {
        int calls = 0;
        using var client = new OllamaClient(new Settings(), new MockHandler(_ => { calls++; return Task.FromResult(Reply(content)); }));
        await RejectAsync(() => client.TranslateAsync("测试：你好", true, CancellationToken.None));
        Equal(2, calls);
    }
});
await Check("prefix repair can recover; no extra translation request for the body", async () =>
{
    int calls = 0;
    using var client = new OllamaClient(new Settings(), new MockHandler(_ => Task.FromResult(Reply(++calls == 1 ? "{\"translation\":\"Hello\"}" : "{\"prefix\":\"Test\",\"translation\":\"Hello\"}"))));
    Equal("Test: Hello", await client.TranslateAsync("测试：你好", true, CancellationToken.None));
    Equal(2, calls);
});
await Check("URL and clock colon are not split as a label", async () =>
{
    using var client = new OllamaClient(new Settings(), new MockHandler(async request =>
    {
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        Equal(1, json.RootElement.GetProperty("format").GetProperty("required").GetArrayLength());
        return Reply("Hello");
    }));
    Equal("Hello", await client.TranslateAsync("https://example.com", true, CancellationToken.None));
    // Clock numbers are protected; asserting the request shape occurs before the expected missing-fact failure.
    await RejectAsync(() => client.TranslateAsync("12:30", true, CancellationToken.None));
});
await Check("short tokens are stable and cannot collide with literal source tokens", () => Sync(() =>
{
    var first = new ProtectedText("100块钱", true);
    var second = new ProtectedText("100块钱", true);
    Equal(first.Masked, second.Masked);
    var collision = new ProtectedText(first.Masked + "是标签，100块钱是价格", true);
    Equal(false, collision.ProtectedMap.Keys.Any(first.Masked.Contains));
    Equal(first.Masked + "是标签，CNY 100是价格", collision.Restore(collision.Masked));
}));
await Check("English schema excludes raw JSON delimiters and Chinese while allowing tokens", async () =>
{
    using var client = new OllamaClient(new Settings(), new MockHandler(async request =>
    {
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        string pattern = json.RootElement.GetProperty("format").GetProperty("properties").GetProperty("translation").GetProperty("pattern").GetString()!;
        Equal(true, System.Text.RegularExpressions.Regex.IsMatch("Hello [ZXQ0Z].", pattern));
        foreach(string invalid in new[]{"你好", "hello\"},\"body\":\"bad", "hello\\world", "hello\nworld"})
            Equal(false, System.Text.RegularExpressions.Regex.IsMatch(invalid, pattern));
        return Reply("Hello");
    }));
    Equal("Hello", await client.TranslateAsync("你好", true, CancellationToken.None));
});
Console.WriteLine($"{passed} PASS / {failed} FAIL / 0 SKIP");
Environment.ExitCode = failed == 0 ? 0 : 1;

static HttpResponseMessage Reply(string content) => new(HttpStatusCode.OK)
{ Content = new StringContent(JsonSerializer.Serialize(new { done = true, message = new { content } })) };

static async Task LiveLanguages()
{
    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceTranslate", "settings.json");
    var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Missing settings");
    settings.Terms = Terminology.Parse("fr\t滚珠轴承\troulement à billes");
    using var client = new OllamaClient(settings);
    int failures = 0;
    foreach (string language in new[] { "en", "zh", "fr", "tr", "th", "ja", "ru", "es", "ko" })
    {
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            string source = language == "zh" ? "Please ship tomorrow, but do not ship early." : language == "fr" ? "请明天发出滚珠轴承，但不要提前发货。" : "请明天发货，但不要提前发货。";
            string result = await client.TranslateAsync(source, language, ct.Token);
            Console.WriteLine(JsonSerializer.Serialize(new {language, status="RETURNED", result}));
        }
        catch (Exception e) { failures++; Console.WriteLine($"FAIL {language} {e.GetType().Name}: {e.Message}"); }
    }
    Console.WriteLine($"{9-failures} RETURNED / {failures} FAIL / 0 SKIP; human language review required.");
    Environment.ExitCode = failures == 0 ? 0 : 1;
}
static async Task LiveFeedback(string selected = "")
{
    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceTranslate", "settings.json");
    var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Missing settings");
    settings.Validate();
    Console.WriteLine($"Model={settings.Model}; Build={settings.BuildStamp}");
    settings.Log = line => { if (line.StartsWith("MODEL retry") || line.StartsWith("LOCAL_")) Console.WriteLine(line); };
    using var client = new OllamaClient(settings, selected.Length > 0 ? new FixtureTraceHandler() : null);
    string[] cases = [
        "二〇二六年九月二十九日", "十月二十七", "9月10", "你可以放心", "明天，你可以放心", "明天就到了，你可以放心",
        "八百八十八人民币怎么可能转换成778美金呢？你以为是日元吗，欧元也不是这么点钱才对啊，你是不是数学不好",
        "接下来测试一段稍微长一点的东西：我给他100块钱，然后他还给我50美金。我不知道是为什么，反正最后，我就只剩下20日元了",
        "我不放心，而且我不能保证明天到货。", "你可以放心，我已经核对过地址了，但我不能保证明天到货。",
        "Tomorrow, you can放心, I have chosen the fastest logistics",
        "以下只是一个例子，不是承诺：我可能在十月二十七发货，但还没有确定。",
        "那我就拭目以待吧", "Please ship on 2028-02-29, do not ship early."
    ];
    int failures = 0;
    var selectedIds = selected.Length == 0 ? [] : selected.Split(',').Select(int.Parse).ToHashSet();
    int completed = 0;
    for (int i = 0; i < cases.Length; i++)
    {
        if (selectedIds.Count > 0 && !selectedIds.Contains(i + 1)) continue;
        completed++;
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        var watch = Stopwatch.StartNew();
        try { Console.WriteLine(JsonSerializer.Serialize(new { id = i + 1, status = "RETURNED", result = await client.TranslateAsync(cases[i], i != 13, ct.Token), elapsed_ms = watch.ElapsedMilliseconds })); }
        catch (Exception e) { failures++; Console.WriteLine(JsonSerializer.Serialize(new { id = i + 1, status = "FAIL", error = e.GetType().Name, message = e.Message, elapsed_ms = watch.ElapsedMilliseconds })); }
    }
    Console.WriteLine($"Live: {completed - failures} RETURNED / {failures} FAIL / 0 SKIP; semantic review required.");
    Environment.ExitCode = failures == 0 ? 0 : 1;
}
sealed class MockHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
}
// Opt-in trace only for the fixed fixtures above, never real desktop/private input.
sealed class FixtureTraceHandler : DelegatingHandler
{
    public FixtureTraceHandler() : base(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (json.RootElement.TryGetProperty("message", out var message))
                Console.WriteLine("FIXTURE_RAW " + message.GetProperty("content").GetString());
            return response;
        }
        catch { response.Dispose(); throw; }
    }
}
