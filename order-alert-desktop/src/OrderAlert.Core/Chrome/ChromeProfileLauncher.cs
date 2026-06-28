using System.Diagnostics;
using OrderAlert.Core.Models;

namespace OrderAlert.Core.Chrome;

public interface IChromeProfileLauncher
{
    Task LaunchAsync(
        StoreAccount account,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken);
}

public sealed class ChromeProfileLauncher : IChromeProfileLauncher
{
    private readonly string _chromePath;
    private readonly string _extensionDirectory;

    public ChromeProfileLauncher(string extensionDirectory, string? chromePath = null)
    {
        _extensionDirectory = Path.GetFullPath(extensionDirectory);
        _chromePath = chromePath ?? ResolveChromePath();
    }

    public Task LaunchAsync(
        StoreAccount account,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_chromePath))
            throw new FileNotFoundException("Google Chrome executable was not found.", _chromePath);
        if (!Directory.Exists(_extensionDirectory))
            throw new DirectoryNotFoundException(
                $"Extension directory does not exist: {_extensionDirectory}");
        Directory.CreateDirectory(account.ProfilePath);

        var startInfo = new ProcessStartInfo(_chromePath)
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add($"--user-data-dir={account.ProfilePath}");
        startInfo.ArgumentList.Add($"--load-extension={_extensionDirectory}");
        startInfo.ArgumentList.Add("--no-first-run");
        foreach (var url in urls) startInfo.ArgumentList.Add(url);
        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("Google Chrome failed to start.");
        return Task.CompletedTask;
    }

    private static string ResolveChromePath()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "Application", "chrome.exe")
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Google Chrome is not installed.");
    }
}
