using OrderAlert.Core.Models;
using OrderAlert.Core.Persistence;

namespace OrderAlert.Core.Accounts;

public sealed class AccountService
{
    private readonly SqliteStore _store;
    private readonly string _profilesRoot;

    public AccountService(SqliteStore store, string? profilesRoot = null)
    {
        _store = store;
        _profilesRoot = profilesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrderAlert",
            "ChromeProfiles");
    }

    public Task<StoreAccount> AddDianxiaomiAsync(
        string displayName,
        string accountIdentifier,
        CancellationToken cancellationToken = default) =>
        AddAsync(
            PlatformKind.Dianxiaomi,
            displayName,
            accountIdentifier,
            cancellationToken);

    public Task<StoreAccount> AddAliExpressAsync(
        string displayName,
        string accountIdentifier,
        CancellationToken cancellationToken = default) =>
        AddAsync(
            PlatformKind.AliExpress,
            displayName,
            accountIdentifier,
            cancellationToken);

    private async Task<StoreAccount> AddAsync(
        PlatformKind platform,
        string displayName,
        string accountIdentifier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(accountIdentifier))
            throw new ArgumentException("Account identifier is required.", nameof(accountIdentifier));

        var id = Guid.NewGuid();
        var account = new StoreAccount(
            id,
            platform,
            displayName.Trim(),
            accountIdentifier.Trim(),
            Path.Combine(_profilesRoot, id.ToString("N")));
        await _store.SaveAccountAsync(account, cancellationToken);
        return account;
    }

    public Task<IReadOnlyList<StoreAccount>> ListAsync(
        CancellationToken cancellationToken = default) =>
        _store.ListAccountsAsync(cancellationToken);

    public async Task<StoreAccount> RenameAsync(
        StoreAccount account,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));
        var updated = account with { DisplayName = displayName.Trim() };
        await _store.SaveAccountAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<StoreAccount> SetEnabledAsync(
        StoreAccount account,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var updated = account with { IsEnabled = enabled };
        await _store.SaveAccountAsync(updated, cancellationToken);
        return updated;
    }

    public Task DeleteMetadataAsync(
        Guid accountId,
        CancellationToken cancellationToken = default) =>
        _store.DeleteAccountAsync(accountId, cancellationToken);
}
