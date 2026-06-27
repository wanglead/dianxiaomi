namespace OrderAlert.Core.Chrome;

public sealed class ExtensionDirectoryResolver
{
    public string Resolve(string applicationDirectory, string? searchStop = null)
    {
        var checkedPaths = new List<string>();
        var installed = Path.GetFullPath(
            Path.Combine(applicationDirectory, "extension"));
        checkedPaths.Add(installed);
        if (HasManifest(installed)) return installed;

        var stop = searchStop is null ? null : Path.GetFullPath(searchStop);
        for (var current = new DirectoryInfo(applicationDirectory);
             current is not null;
             current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "browser-plugin");
            checkedPaths.Add(candidate);
            if (HasManifest(candidate)) return Path.GetFullPath(candidate);
            if (stop is not null
                && string.Equals(
                    current.FullName,
                    stop,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        throw new DirectoryNotFoundException(
            "未找到 Chrome 扩展目录。已检查：" + string.Join("; ", checkedPaths));
    }

    private static bool HasManifest(string path) =>
        File.Exists(Path.Combine(path, "manifest.json"));
}
