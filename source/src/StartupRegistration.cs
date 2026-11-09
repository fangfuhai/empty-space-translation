namespace SpaceTranslate;

public static class ServiceReadiness
{
    // One launch, bounded polling; only connection failures/timeouts are retryable.
    // false means the service is healthy but the configured model is absent.
    public static async Task<bool> WaitAsync(Func<CancellationToken, Task<bool>> probe,
        Action start, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try { return await probe(timeout.Token); }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            ct.ThrowIfCancellationRequested();
            if (attempt == 0) start();
            if (attempt < 29) await delay(TimeSpan.FromSeconds(1), ct);
        }
        throw new IOException("Ollama 启动等待超时，请检查服务后重试。");
    }
}

public interface IStartupStore
{
    string? Read();
    void Write(string command);
    void Remove();
}

public sealed class StartupRegistration(IStartupStore store, string executable)
{
    public bool Enabled => !string.IsNullOrWhiteSpace(store.Read());
    public bool UsesCurrentExecutable => string.Equals(store.Read(), BuildCommand(executable), StringComparison.OrdinalIgnoreCase);
    public static string BuildCommand(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.IndexOfAny(['"', '\r', '\n']) >= 0)
            throw new InvalidDataException("开机启动路径必须是有效的 EXE 完整路径。");
        return "\"" + path + "\" --background";
    }
    public void SetEnabled(bool enabled)
    {
        if (enabled) store.Write(BuildCommand(executable));
        else store.Remove();
    }
}
