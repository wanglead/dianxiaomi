# Order Alert Login Flow Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add explicit multi-account Dianxiaomi login support and prevent account-login or shutdown failures from terminating the WPF application.

**Architecture:** Extend the existing account service and view model rather than adding a second account subsystem. Introduce small, testable helpers for extension-directory resolution, platform login targets, and single-instance mutex ownership; keep WPF commands responsible only for collecting input and presenting errors.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, xUnit, SQLite, Chrome process launching.

---

## File map

### Files to create

- `order-alert-desktop/src/OrderAlert.Core/Chrome/ExtensionDirectoryResolver.cs`: installed/development extension lookup.
- `order-alert-desktop/src/OrderAlert.Core/Chrome/AccountLoginTargets.cs`: platform-specific login URL sets.
- `order-alert-desktop/src/OrderAlert.App/Startup/SingleInstanceLease.cs`: mutex ownership and safe release.
- `order-alert-desktop/tests/OrderAlert.Core.Tests/ExtensionDirectoryResolverTests.cs`: extension lookup tests.
- `order-alert-desktop/tests/OrderAlert.Core.Tests/AccountLoginTargetsTests.cs`: exact platform URL tests.
- `order-alert-desktop/tests/OrderAlert.Core.Tests/SingleInstanceLeaseTests.cs`: owned/non-owned release tests.

### Files to modify

- `order-alert-desktop/src/OrderAlert.Core/Accounts/AccountService.cs`: create both platform account types through one validated path.
- `order-alert-desktop/src/OrderAlert.App/ViewModels/DesktopActions.cs`: separate add actions and shared login targets.
- `order-alert-desktop/src/OrderAlert.App/ViewModels/MainViewModel.cs`: two add commands and safe command exception boundary.
- `order-alert-desktop/src/OrderAlert.App/Views/AccountsView.xaml`: two explicit add buttons and visible error.
- `order-alert-desktop/src/OrderAlert.App/App.xaml.cs`: resolve extension directory and use safe mutex lease.
- `order-alert-desktop/tests/OrderAlert.Core.Tests/SqliteStoreTests.cs`: account creation coverage.
- `order-alert-desktop/tests/OrderAlert.Core.Tests/MainViewModelTests.cs`: command failure does not escape.
- `agent_memory/progress.md`: verification results and remaining real-page acceptance.
- `agent_memory/bugs.md`: move fixed login crashes out of current issues.

## Task 1: Create both platform account types

**Files:**
- Modify: `order-alert-desktop/src/OrderAlert.Core/Accounts/AccountService.cs`
- Test: `order-alert-desktop/tests/OrderAlert.Core.Tests/SqliteStoreTests.cs`

- [ ] **Step 1: Write failing account creation tests**

Add:

```csharp
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
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter SqliteStoreTests
```

Expected: compilation fails because `AddDianxiaomiAsync` does not exist.

- [ ] **Step 3: Implement one validated creation path**

Refactor `AccountService` to:

```csharp
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
```

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter SqliteStoreTests
```

Expected: all persistence/account tests pass.

- [ ] **Step 5: Commit**

```powershell
git add order-alert-desktop/src/OrderAlert.Core/Accounts/AccountService.cs order-alert-desktop/tests/OrderAlert.Core.Tests/SqliteStoreTests.cs
git commit -m "feat: add dianxiaomi account profiles"
```

## Task 2: Resolve the extension directory in installed and development layouts

**Files:**
- Create: `order-alert-desktop/src/OrderAlert.Core/Chrome/ExtensionDirectoryResolver.cs`
- Create: `order-alert-desktop/tests/OrderAlert.Core.Tests/ExtensionDirectoryResolverTests.cs`
- Modify: `order-alert-desktop/src/OrderAlert.App/App.xaml.cs`

- [ ] **Step 1: Write failing resolver tests**

Create tests:

```csharp
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

    var result = new ExtensionDirectoryResolver().Resolve(
        fixture.Path("order-alert-desktop/src/OrderAlert.App/bin/Release/net8.0-windows"),
        fixture.Root);

    Assert.Equal(fixture.Path("browser-plugin"), result);
}

[Fact]
public void Missing_candidates_report_every_checked_location()
{
    using var fixture = new DirectoryFixture();

    var error = Assert.Throws<DirectoryNotFoundException>(
        () => new ExtensionDirectoryResolver().Resolve(
            fixture.Path("app"),
            fixture.Root));

    Assert.Contains("extension", error.Message);
    Assert.Contains("browser-plugin", error.Message);
}
```

`DirectoryFixture` creates/deletes a unique temporary root and writes `{}` to requested `manifest.json` files.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter ExtensionDirectoryResolverTests
```

Expected: compilation fails because `ExtensionDirectoryResolver` is missing.

- [ ] **Step 3: Implement bounded lookup**

Create:

```csharp
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
                break;
        }

        throw new DirectoryNotFoundException(
            "未找到 Chrome 扩展目录。已检查：" + string.Join("; ", checkedPaths));
    }

    private static bool HasManifest(string path) =>
        File.Exists(Path.Combine(path, "manifest.json"));
}
```

- [ ] **Step 4: Compose the resolver in `App`**

Replace:

```csharp
var extensionDirectory = Path.Combine(AppContext.BaseDirectory, "extension");
```

with:

```csharp
var extensionDirectory = new ExtensionDirectoryResolver().Resolve(
    AppContext.BaseDirectory);
```

The application may still fail at startup only when neither installed nor repository extension exists; that failure is explicit and actionable.

- [ ] **Step 5: Verify GREEN**

Run:

```powershell
dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter ExtensionDirectoryResolverTests
dotnet build order-alert-desktop/src/OrderAlert.App/OrderAlert.App.csproj -c Release
```

Expected: resolver tests and Release build pass.

- [ ] **Step 6: Commit**

```powershell
git add order-alert-desktop/src/OrderAlert.Core/Chrome/ExtensionDirectoryResolver.cs order-alert-desktop/src/OrderAlert.App/App.xaml.cs order-alert-desktop/tests/OrderAlert.Core.Tests/ExtensionDirectoryResolverTests.cs
git commit -m "fix: resolve extension in development builds"
```

## Task 3: Add explicit platform login targets and safe account commands

**Files:**
- Create: `order-alert-desktop/src/OrderAlert.Core/Chrome/AccountLoginTargets.cs`
- Create: `order-alert-desktop/tests/OrderAlert.Core.Tests/AccountLoginTargetsTests.cs`
- Modify: `order-alert-desktop/src/OrderAlert.App/ViewModels/DesktopActions.cs`
- Modify: `order-alert-desktop/src/OrderAlert.App/ViewModels/MainViewModel.cs`
- Modify: `order-alert-desktop/tests/OrderAlert.Core.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write failing login-target tests**

Add:

```csharp
[Fact]
public void Dianxiaomi_login_opens_all_four_fixed_pages()
{
    var urls = AccountLoginTargets.For(PlatformKind.Dianxiaomi);

    Assert.Equal(4, urls.Count);
    Assert.Contains(urls, url => url.Contains("/paid?go=m100"));
    Assert.Contains(urls, url => url.Contains("/approved?go=m101"));
    Assert.Contains(urls, url => url.Contains("/processed/self_warehouse?go=m10201"));
    Assert.Contains(urls, url => url.Contains("/allocated/has?go=m10301"));
}

[Fact]
public void Aliexpress_login_opens_the_order_management_page()
{
    var url = Assert.Single(AccountLoginTargets.For(PlatformKind.AliExpress));
    Assert.Contains("order-manage/orderList", url);
}
```

- [ ] **Step 2: Write failing view-model safety tests**

Change `FakeActions` to allow `AddDianxiaomiAsync`, `AddAliExpressAsync`, and `ReloginAsync` to throw a configured exception. Add:

```csharp
[Fact]
public async Task Dianxiaomi_add_failure_is_exposed_without_escaping_the_command()
{
    var actions = new FakeActions
    {
        AccountError = new DirectoryNotFoundException("扩展目录不存在")
    };
    var viewModel = new MainViewModel(actions);

    await viewModel.AddDianxiaomiAccountCommand.ExecuteAsync(null);

    Assert.Contains("扩展目录不存在", viewModel.LastError);
}

[Fact]
public async Task Relogin_failure_is_exposed_without_escaping_the_command()
{
    var actions = new FakeActions
    {
        AccountError = new InvalidOperationException("Chrome 启动失败")
    };
    var viewModel = new MainViewModel(actions)
    {
        SelectedAccount = Account(true)
    };

    await viewModel.ReloginCommand.ExecuteAsync(null);

    Assert.Contains("Chrome 启动失败", viewModel.LastError);
}
```

- [ ] **Step 3: Verify RED**

Run:

```powershell
dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter "AccountLoginTargetsTests|MainViewModelTests"
```

Expected: compilation fails for missing login targets and separate add command.

- [ ] **Step 4: Implement exact login targets**

Create:

```csharp
public static class AccountLoginTargets
{
    private static readonly string[] Dianxiaomi =
    [
        "https://www.dianxiaomi.com/web/order/paid?go=m100",
        "https://www.dianxiaomi.com/web/order/approved?go=m101",
        "https://www.dianxiaomi.com/web/order/processed/self_warehouse?go=m10201",
        "https://www.dianxiaomi.com/web/order/allocated/has?go=m10301"
    ];

    private static readonly string[] AliExpress =
    [
        "https://csp.aliexpress.com/m_apps/order-manage/orderList?channelId=244176"
    ];

    public static IReadOnlyList<string> For(PlatformKind platform) =>
        platform == PlatformKind.Dianxiaomi ? Dianxiaomi : AliExpress;
}
```

- [ ] **Step 5: Split the desktop add operations**

Change `IOrderAlertActions` to:

```csharp
Task AddDianxiaomiAccountAsync(CancellationToken cancellationToken) =>
    Task.CompletedTask;
Task AddAliExpressAccountAsync(CancellationToken cancellationToken) =>
    Task.CompletedTask;
```

In `DesktopActions`, extract:

```csharp
public Task AddDianxiaomiAccountAsync(CancellationToken cancellationToken) =>
    AddAccountAsync(
        PlatformKind.Dianxiaomi,
        "添加店小秘账号",
        cancellationToken);

public Task AddAliExpressAccountAsync(CancellationToken cancellationToken) =>
    AddAccountAsync(
        PlatformKind.AliExpress,
        "添加速卖通店铺",
        cancellationToken);
```

The private method collects display name and identifier, calls the matching `AccountService` method, raises `AccountAdded`, then calls `LaunchAccountAsync`. `LaunchAccountAsync` uses:

```csharp
return _launcher.LaunchAsync(
    account,
    AccountLoginTargets.For(account.Platform),
    cancellationToken);
```

- [ ] **Step 6: Add a shared safe command boundary**

In `MainViewModel`, expose:

```csharp
public IAsyncRelayCommand AddDianxiaomiAccountCommand { get; }
public IAsyncRelayCommand AddAliExpressAccountCommand { get; }
```

Initialize all account commands through:

```csharp
private async Task ExecuteAccountActionAsync(Func<Task> action)
{
    LastError = null;
    try
    {
        await action();
    }
    catch (Exception error)
    {
        LastError = error.Message;
    }
}
```

The two add commands and `ReloginCommand` call this helper. No account-operation exception may escape `ExecuteAsync`.

- [ ] **Step 7: Verify GREEN**

Run:

```powershell
dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter "AccountLoginTargetsTests|MainViewModelTests"
```

Expected: login target and exception-boundary tests pass.

- [ ] **Step 8: Commit**

```powershell
git add order-alert-desktop/src/OrderAlert.Core/Chrome order-alert-desktop/src/OrderAlert.App/ViewModels order-alert-desktop/tests/OrderAlert.Core.Tests
git commit -m "fix: add safe multi-platform login commands"
```

## Task 4: Update the account UI and make single-instance shutdown ownership-safe

**Files:**
- Create: `order-alert-desktop/src/OrderAlert.App/Startup/SingleInstanceLease.cs`
- Create: `order-alert-desktop/tests/OrderAlert.Core.Tests/SingleInstanceLeaseTests.cs`
- Modify: `order-alert-desktop/src/OrderAlert.App/Views/AccountsView.xaml`
- Modify: `order-alert-desktop/src/OrderAlert.App/App.xaml.cs`
- Modify: `order-alert-desktop/tests/OrderAlert.Core.Tests/OrderAlert.Core.Tests.csproj`

- [ ] **Step 1: Write failing lease tests**

Add:

```csharp
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
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter SingleInstanceLeaseTests
```

Expected: compilation fails because `SingleInstanceLease` is missing.

- [ ] **Step 3: Implement ownership lease**

Create:

```csharp
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
```

In `App`, create the lease immediately after the Mutex:

```csharp
_mutex = new Mutex(true, MutexName, out var createdNew);
_mutexLease = new SingleInstanceLease(createdNew, _mutex.ReleaseMutex);
```

Replace direct `ReleaseMutex` in `OnExit` with `_mutexLease?.Dispose()`, then dispose the Mutex object.

- [ ] **Step 4: Add the two UI entry points**

Replace the single add button with:

```xml
<Button Content="添加店小秘账号"
        Command="{Binding AddDianxiaomiAccountCommand}"
        Padding="14,7"/>
<Button Content="添加速卖通店铺"
        Command="{Binding AddAliExpressAccountCommand}"
        Padding="14,7"
        Margin="8,0,0,0"/>
```

Add a visible error below the toolbar:

```xml
<TextBlock Text="{Binding LastError}"
           Foreground="#C62828"
           TextWrapping="Wrap"
           Margin="0,0,0,10"/>
```

- [ ] **Step 5: Verify GREEN and WPF compilation**

Run:

```powershell
dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter SingleInstanceLeaseTests
dotnet build order-alert-desktop/src/OrderAlert.App/OrderAlert.App.csproj -c Release
```

Expected: lease tests pass and WPF Release build has zero errors.

- [ ] **Step 6: Commit**

```powershell
git add order-alert-desktop/src/OrderAlert.App/Startup order-alert-desktop/src/OrderAlert.App/Views/AccountsView.xaml order-alert-desktop/src/OrderAlert.App/App.xaml.cs order-alert-desktop/tests/OrderAlert.Core.Tests
git commit -m "fix: keep account login failures inside the app"
```

## Task 5: Run regression and interactive acceptance

**Files:**
- Modify: `agent_memory/progress.md`
- Modify: `agent_memory/bugs.md`

- [ ] **Step 1: Run the complete automated suite**

Run:

```powershell
npm test --prefix browser-plugin
dotnet test order-alert-desktop/OrderAlert.sln -c Release
dotnet build order-alert-desktop/src/OrderAlert.App/OrderAlert.App.csproj -c Release
python backend/test_inventory.py
git diff --check
```

Expected: Node and .NET tests pass, WPF builds without errors, backend prints `ALL OK`, and diff check reports no whitespace errors.

- [ ] **Step 2: Start the Release application**

Run:

```powershell
dotnet run --project order-alert-desktop/src/OrderAlert.App -c Release
```

Expected: the store-account page shows both add buttons and the process remains alive.

- [ ] **Step 3: Verify Dianxiaomi login**

Add a Dianxiaomi account with a test display name and non-secret identifier. Expected:

- a new isolated profile directory is created;
- Chrome opens the four fixed Dianxiaomi pages;
- canceling either input dialog does not add an account;
- login, CAPTCHA, or two-factor prompts remain under user control.

- [ ] **Step 4: Verify error containment**

Temporarily run a published app without `extension/`, click “重新登录”, and confirm:

- the WPF process remains alive;
- `LastError` shows the missing-extension paths;
- no new `.NET Runtime` crash event is emitted.

Restore the normal development layout after the check.

- [ ] **Step 5: Verify single-instance shutdown**

Start the application twice, close the visible window, activate it again, then exit from the tray. Expected: one process remains during activation and no new Mutex ownership exception appears in Windows Application events.

- [ ] **Step 6: Update project memory**

In `progress.md`, record exact test counts and interactive results. In `bugs.md`, move the three fixed login/Mutex defects to “已修复”; keep only real-page DOM acceptance risks.

- [ ] **Step 7: Commit**

```powershell
git add agent_memory/progress.md agent_memory/bugs.md
git commit -m "docs: record login flow verification"
```

## Completion criteria

- Both platforms have explicit add buttons and support multiple isolated accounts.
- Dianxiaomi login opens all four required pages.
- Development and installed extension layouts resolve deterministically.
- Add/relogin failures display actionable text without terminating WPF.
- Non-owner processes never release the single-instance Mutex.
- Automated suites and interactive checks pass; real authentication remains user-controlled.
