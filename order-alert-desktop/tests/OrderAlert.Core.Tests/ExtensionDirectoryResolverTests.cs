using OrderAlert.Core.Chrome;

namespace OrderAlert.Core.Tests;

public sealed class ExtensionDirectoryResolverTests
{
    [Fact]
    public void Prefers_extension_beside_the_application()
    {
        using var fixture = new DirectoryFixture();
        fixture.CreateManifest("app/extension");
        fixture.CreateManifest("browser-plugin");

        var result = new ExtensionDirectoryResolver().Resolve(
            fixture.Path("app"),
            fixture.Root);

        Assert.Equal(fixture.Path("app/extension"), result);
    }

    [Fact]
    public void Falls_back_to_repository_browser_plugin()
    {
        using var fixture = new DirectoryFixture();
        fixture.CreateManifest("browser-plugin");
        fixture.CreateDirectory(
            "order-alert-desktop/src/OrderAlert.App/bin/Release/net8.0-windows");

        var result = new ExtensionDirectoryResolver().Resolve(
            fixture.Path(
                "order-alert-desktop/src/OrderAlert.App/bin/Release/net8.0-windows"),
            fixture.Root);

        Assert.Equal(fixture.Path("browser-plugin"), result);
    }

    [Fact]
    public void Missing_candidates_report_every_checked_location()
    {
        using var fixture = new DirectoryFixture();
        fixture.CreateDirectory("app");

        var error = Assert.Throws<DirectoryNotFoundException>(
            () => new ExtensionDirectoryResolver().Resolve(
                fixture.Path("app"),
                fixture.Root));

        Assert.Contains("extension", error.Message);
        Assert.Contains("browser-plugin", error.Message);
    }

    private sealed class DirectoryFixture : IDisposable
    {
        public DirectoryFixture()
        {
            Root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"OrderAlertExtensionTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Path(string relativePath) =>
            System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    Root,
                    relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));

        public void CreateDirectory(string relativePath) =>
            Directory.CreateDirectory(Path(relativePath));

        public void CreateManifest(string relativePath)
        {
            var directory = Path(relativePath);
            Directory.CreateDirectory(directory);
            File.WriteAllText(System.IO.Path.Combine(directory, "manifest.json"), "{}");
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
