namespace SpaceTranslate;

internal static class BrandIcons
{
    internal static Icon Load(string name, int size)
    {
        using var stream = typeof(BrandIcons).Assembly.GetManifestResourceStream($"SpaceTranslate.Assets.{name}.ico")
            ?? throw new InvalidOperationException($"Missing application icon: {name}");
        using var icon = new Icon(stream, size, size);
        return (Icon)icon.Clone();
    }
}
