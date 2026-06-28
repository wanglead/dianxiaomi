namespace OrderAlert.App.Startup;

public sealed class SingleInstanceLease : IDisposable
{
    private readonly Action _release;
    private bool _owns;

    public SingleInstanceLease(bool owns, Action release)
    {
        _owns = owns;
        _release = release;
    }

    public void Dispose()
    {
        if (!_owns) return;
        _owns = false;
        _release();
    }
}
