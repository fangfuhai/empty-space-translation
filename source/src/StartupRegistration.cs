namespace SpaceTranslate;

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
