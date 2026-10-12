using Microsoft.Win32;

namespace SpaceTranslate;

internal sealed class WindowsStartupStore : IStartupStore
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SpaceTranslate";
    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue(ValueName) as string;
    }
    public void Write(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key, true);
        key.SetValue(ValueName, command, RegistryValueKind.String);
        if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.Ordinal))
            throw new IOException("开机启动设置未保存。");
    }
    public void Remove()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key, true);
        key?.DeleteValue(ValueName, false);
        if (key?.GetValue(ValueName) != null) throw new IOException("未能关闭开机启动。");
    }
}
