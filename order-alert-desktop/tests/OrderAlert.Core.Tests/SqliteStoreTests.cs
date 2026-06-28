using OrderAlert.Core.Accounts;
using OrderAlert.Core.Models;
using OrderAlert.Core.Persistence;

namespace OrderAlert.Core.Tests;

public sealed class SqliteStoreTests
{
    [Fact]
    public async Task Account_profiles_use_generated_ids_not_display_names()
    {
        await WithStore(async (store, root) =>
        {
            var service = new AccountService(store, Path.Combine(root, "profiles"));
            var first = await service.AddAliExpressAsync("店铺 A", "owner-a");
            var second = await service.AddAliExpressAsync("店铺 B", "owner-b");

            Assert.NotEqual(first.ProfilePath, second.ProfilePath);
            Assert.Contains(first.Id.ToString("N"), first.ProfilePath);
            Assert.DoesNotContain("店铺 A", first.ProfilePath);
        });
    }

    [Fact]
    public async Task Creates_dianxiaomi_and_aliexpress_accounts_with_isolated_profiles()
    {
        await WithStore(async (store, root) =>
        {
            var service = new AccountService(store, Path.Combine(root, "profiles"));
            var dianxiaomi = await service.AddDianxiaomiAsync("店小秘一号", "dxm-owner");
            var aliexpress = await service.AddAliExpressAsync("速卖通美国店", "ali-owner");

            Assert.Equal(PlatformKind.Dianxiaomi, dianxiaomi.Platform);
            Assert.Equal(PlatformKind.AliExpress, aliexpress.Platform);
            Assert.NotEqual(dianxiaomi.ProfilePath, aliexpress.ProfilePath);
            Assert.Contains(dianxiaomi.Id.ToString("N"), dianxiaomi.ProfilePath);
            Assert.Contains(aliexpress.Id.ToString("N"), aliexpress.ProfilePath);
        });
    }

    [Theory]
    [InlineData("", "owner")]
    [InlineData("store", "")]
    public async Task Both_platforms_reject_blank_account_fields(
        string displayName,
        string identifier)
    {
        await WithStore(async (store, root) =>
        {
            var service = new AccountService(store, root);
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.AddDianxiaomiAsync(displayName, identifier));
            await Assert.ThrowsAsync<ArgumentException>(
                () => service.AddAliExpressAsync(displayName, identifier));
        });
    }

    [Fact]
    public async Task Same_order_number_in_two_stores_remains_isolated()
    {
        await WithStore(async (store, root) =>
        {
            var accounts = new AccountService(store, Path.Combine(root, "profiles"));
            var first = await accounts.AddAliExpressAsync("A", "a");
            var second = await accounts.AddAliExpressAsync("B", "b");
            await store.CommitBatchAsync(Batch(first.Id, Order(first.Id, "815209")));
            await store.CommitBatchAsync(Batch(second.Id, Order(second.Id, "815209")));

            Assert.Single(await store.GetActiveOrdersAsync(first.Id));
            Assert.Single(await store.GetActiveOrdersAsync(second.Id));
        });
    }

    [Fact]
    public async Task Complete_batch_replaces_the_current_snapshot()
    {
        await WithStore(async (store, root) =>
        {
            var account = await new AccountService(store, root)
                .AddAliExpressAsync("A", "a");
            await store.CommitBatchAsync(Batch(account.Id, Order(account.Id, "old")));
            await store.CommitBatchAsync(Batch(account.Id, Order(account.Id, "new")));

            var active = await store.GetActiveOrdersAsync(account.Id);
            Assert.Equal("new", Assert.Single(active).OrderId);
        });
    }

    [Fact]
    public async Task Incomplete_batch_preserves_the_previous_snapshot()
    {
        await WithStore(async (store, root) =>
        {
            var account = await new AccountService(store, root)
                .AddAliExpressAsync("A", "a");
            await store.CommitBatchAsync(Batch(account.Id, Order(account.Id, "old")));
            var incomplete = Batch(account.Id, Order(account.Id, "partial")) with
            {
                Complete = false
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.CommitBatchAsync(incomplete));
            Assert.Equal(
                "old",
                Assert.Single(await store.GetActiveOrdersAsync(account.Id)).OrderId);
        });
    }

    [Fact]
    public async Task Settings_survive_store_reconstruction()
    {
        var root = CreateRoot();
        var path = Path.Combine(root, "orders.db");
        try
        {
            var settings = AppSettings.Default with { CheckIntervalMinutes = 37 };
            var first = new SqliteStore(path);
            await first.InitializeAsync();
            await first.SaveSettingsAsync(settings);

            var second = new SqliteStore(path);
            await second.InitializeAsync();

            Assert.Equal(settings, await second.GetSettingsAsync());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static ScanBatch Batch(Guid accountId, params OrderSnapshot[] orders)
    {
        var now = DateTimeOffset.UtcNow;
        return new ScanBatch(accountId, true, now, now, orders, 1);
    }

    private static OrderSnapshot Order(Guid accountId, string orderId) =>
        new(
            PlatformKind.AliExpress,
            accountId,
            orderId,
            "https://example.invalid/order",
            "Awaiting shipment",
            null,
            3600,
            DateTimeOffset.UtcNow);

    private static async Task WithStore(Func<SqliteStore, string, Task> test)
    {
        var root = CreateRoot();
        try
        {
            var store = new SqliteStore(Path.Combine(root, "orders.db"));
            await store.InitializeAsync();
            await test(store, root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"OrderAlertTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
