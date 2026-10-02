using System.Text;

namespace SpaceTranslate;

internal static class FeatureUi
{
    internal static readonly string TermsPath = Path.Combine(Program.DataDirectory, "glossary.txt");
    internal static ComboBox LanguagePicker(string selected, Action<string> changed)
    {
        var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, AccessibleName = "双空格目标语言" };
        foreach (var pair in Languages.Names) picker.Items.Add(pair.Value + (pair.Key is "en" or "zh" ? "" : "（试用）"));
        picker.SelectedIndex = Languages.Names.Keys.ToList().IndexOf(selected);
        picker.SelectedIndexChanged += (_, _) => changed(Languages.Names.Keys.ElementAt(picker.SelectedIndex));
        return picker;
    }
    internal static void ImportTerms(IWin32Window owner, Settings settings)
    {
        using var dialog = new OpenFileDialog { Filter = "UTF-8 词库 (*.txt)|*.txt", Title = "导入词库：语言代码、原词、译法，用 Tab 分隔" };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 2_000_000) throw new InvalidDataException("词库文件过大。");
            string content = File.ReadAllText(dialog.FileName, new UTF8Encoding(false, true)).TrimStart('\uFEFF');
            var parsed = Terminology.Parse(content);
            if (MessageBox.Show(owner, $"检查通过，共 {parsed.Entries.Count} 条。替换当前自定义词库？", "导入词库", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            Directory.CreateDirectory(Program.DataDirectory);
            File.WriteAllText(TermsPath + ".tmp", content, new UTF8Encoding(false));
            File.Move(TermsPath + ".tmp", TermsPath, true);
            settings.Terms = parsed;
            Program.Log(settings, $"GLOSSARY imported count={parsed.Entries.Count}");
            MessageBox.Show(owner, "词库已导入，从下一次翻译开始生效。", "导入词库");
        }
        catch (Exception e) { MessageBox.Show(owner, e is InvalidDataException ? e.Message : "导入失败，请检查 UTF-8 编码和文件访问权限。旧词库未替换。", "导入词库"); }
    }
    internal static void ExportLog(IWin32Window owner, Settings settings)
    {
        using var dialog = new SaveFileDialog { Filter = "诊断文本 (*.txt)|*.txt", FileName = "SpaceTranslate-support.txt" };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            string metadata = $"SpaceTranslate {settings.BuildStamp}\nOS={Environment.OSVersion.Version} Runtime={Environment.Version}\nTarget={settings.TargetLanguage} Terms={settings.Terms.Entries.Count} Diagnostics={settings.LogDiagnostics}\n请补充：操作步骤、预期行为、实际提示。默认不包含输入、译文、词库内容或个人配置。\n\n";
            lock (Program.LogLock)
            {
                var parts = new List<string> { metadata };
                foreach (string path in new[] { SupportLog.Path + ".previous", SupportLog.Path })
                    if (File.Exists(path)) parts.Add(File.ReadAllText(path));
                File.WriteAllText(dialog.FileName, string.Join("\n", parts), new UTF8Encoding(false));
            }
            MessageBox.Show(owner, "日志已保存。可检查后附上复现步骤，手动发送给维护者。", "导出日志");
        }
        catch { MessageBox.Show(owner, "无法保存日志，请选择可写目录。", "导出日志"); }
    }
}

internal static class SupportLog
{
    internal static string Path => System.IO.Path.Combine(Program.DataDirectory, "support.log");
    internal static void Write(string message)
    {
        // Export only structured application events, never arbitrary exception messages,
        // executable paths, prompts, glossary values or model output fingerprints.
        if (SupportEvents.Filter(message) == null) return;
        Directory.CreateDirectory(Program.DataDirectory);
        if (File.Exists(Path) && new FileInfo(Path).Length > 256 * 1024) File.Move(Path, Path + ".previous", true);
        File.AppendAllText(Path, $"{DateTimeOffset.Now:O} {message}\n");
    }
}
