using OrderAlert.App.Startup;

namespace OrderAlert.Core.Tests;

public sealed class SingleInstanceLeaseTests
{
    [Fact]
    public void Non_owner_does_not_release_mutex()
    {
        var releases = 0;
        using var lease = new SingleInstanceLease(false, () => releases++);

        lease.Dispose();

        Assert.Equal(0, releases);
    }

    [Fact]
    public void Owner_releases_mutex_once()
    {
        var releases = 0;
        using var lease = new SingleInstanceLease(true, () => releases++);

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, releases);
    }
}
