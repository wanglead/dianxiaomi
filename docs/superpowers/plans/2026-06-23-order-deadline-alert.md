# Windows 11 Multi-Store Order Deadline Alert Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows 11 tray application and Chrome extension that scan Dianxiaomi plus multiple AliExpress stores, evaluate order deadlines, and deliver configurable, deduplicated alerts.

**Architecture:** Extend the existing Manifest V3 extension with platform-specific DOM adapters and a Native Messaging bridge. Add a .NET 8 WPF application whose Core library owns scheduling, deadline evaluation, SQLite persistence, Chrome profile launching, scan orchestration, and alert policy; the WPF layer only presents state and invokes those services.

**Tech Stack:** Chrome Manifest V3, JavaScript, Node.js built-in test runner, .NET 8, WPF, xUnit, Microsoft.Data.Sqlite, Windows toast notifications, Chrome Native Messaging.

---

## Scope and file map

### Existing files to modify

- `browser-plugin/manifest.json`: add Native Messaging permission and ordered order-alert content scripts.
- `browser-plugin/background.js`: keep existing collection behavior and delegate order-alert messages to the new bridge.
- `agent_memory/context.md`: record the new WPF/Chrome architecture after implementation begins.
- `agent_memory/progress.md`: track completed tasks and verification results.
- `agent_memory/bugs.md`: keep only current blockers and platform-adapter risks.

### Chrome extension files to create

- `browser-plugin/order-alert/scan-schema.js`: normalized scan and order shapes plus validation.
- `browser-plugin/order-alert/dom-utils.js`: text, time, row, pagination, and page-stability helpers.
- `browser-plugin/order-alert/dianxiaomi-adapter.js`: Dianxiaomi page detection and row extraction.
- `browser-plugin/order-alert/aliexpress-adapter.js`: AliExpress page detection and row extraction.
- `browser-plugin/order-alert/scan-coordinator.js`: complete-scan lifecycle and pagination safety.
- `browser-plugin/order-alert/native-bridge.js`: Chrome Native Messaging connection and command routing.
- `browser-plugin/order-alert/content-order-alert.js`: page entry point with no business rules.
- `browser-plugin/background-entry.js`: module service-worker entry that preserves collection listeners and starts the order-alert bridge.
- `browser-plugin/tests/fixtures/dianxiaomi-orders.html`: deterministic Dianxiaomi DOM fixture.
- `browser-plugin/tests/fixtures/aliexpress-orders.html`: deterministic AliExpress DOM fixture.
- `browser-plugin/tests/adapters.test.cjs`: adapter and schema tests.
- `browser-plugin/tests/scan-coordinator.test.cjs`: complete-versus-partial batch tests.
- `browser-plugin/package.json`: Node test command and jsdom development dependency.

### Windows application files to create

- `order-alert-desktop/OrderAlert.sln`: solution entry point.
- `order-alert-desktop/src/OrderAlert.Core/OrderAlert.Core.csproj`: platform-independent domain and services.
- `order-alert-desktop/src/OrderAlert.Core/Models/OrderModels.cs`: account, order, scan, risk, and setting records.
- `order-alert-desktop/src/OrderAlert.Core/Services/DeadlineEvaluator.cs`: all time boundary rules.
- `order-alert-desktop/src/OrderAlert.Core/Services/ScheduleWindow.cs`: monitoring-window and next-run calculations.
- `order-alert-desktop/src/OrderAlert.Core/Services/AlertPolicy.cs`: immediate, repeat, escalation, and snooze decisions.
- `order-alert-desktop/src/OrderAlert.Core/Persistence/SqliteStore.cs`: schema creation and transactional snapshot storage.
- `order-alert-desktop/src/OrderAlert.Core/Accounts/AccountService.cs`: account CRUD and independent profile paths.
- `order-alert-desktop/src/OrderAlert.Core/Chrome/ChromeProfileLauncher.cs`: profile-scoped Chrome startup and URL opening.
- `order-alert-desktop/src/OrderAlert.Core/Messaging/NativeMessageProtocol.cs`: typed JSON envelopes and framing.
- `order-alert-desktop/src/OrderAlert.Core/Messaging/NativeMessageHost.cs`: stdin/stdout Native Messaging host loop.
- `order-alert-desktop/src/OrderAlert.Core/Messaging/NativeHostPipeServer.cs`: WPF-side named-pipe endpoint for extension commands and results.
- `order-alert-desktop/src/OrderAlert.Core/Messaging/NativeHostPipeClient.cs`: console-host relay between Chrome stdio and the running WPF process.
- `order-alert-desktop/src/OrderAlert.Core/Services/ScanOrchestrator.cs`: account scheduling, non-reentrancy, batch commit, and failure retention.
- `order-alert-desktop/src/OrderAlert.App/OrderAlert.App.csproj`: WPF executable and packaging metadata.
- `order-alert-desktop/src/OrderAlert.App/App.xaml`: single-instance startup and dependency composition.
- `order-alert-desktop/src/OrderAlert.App/MainWindow.xaml`: dashboard shell.
- `order-alert-desktop/src/OrderAlert.App/ViewModels/MainViewModel.cs`: summary, filters, and commands.
- `order-alert-desktop/src/OrderAlert.App/Views/AccountsView.xaml`: account management.
- `order-alert-desktop/src/OrderAlert.App/Views/SettingsView.xaml`: schedule and notification settings.
- `order-alert-desktop/src/OrderAlert.App/Notifications/WindowsAlertService.cs`: toast and sound implementation.
- `order-alert-desktop/src/OrderAlert.App/Tray/TrayIconService.cs`: tray menu, risk badge, and window activation.
- `order-alert-desktop/src/OrderAlert.App/Startup/AutoStartService.cs`: per-user startup registration.
- `order-alert-desktop/src/OrderAlert.NativeHost/OrderAlert.NativeHost.csproj`: console Native Messaging host executable.
- `order-alert-desktop/src/OrderAlert.NativeHost/Program.cs`: host process entry point.
- `order-alert-desktop/tests/OrderAlert.Core.Tests/OrderAlert.Core.Tests.csproj`: xUnit test project.
- `order-alert-desktop/tests/OrderAlert.Core.Tests/DeadlineEvaluatorTests.cs`: deadline boundary tests.
- `order-alert-desktop/tests/OrderAlert.Core.Tests/ScheduleWindowTests.cs`: schedule boundary tests.
- `order-alert-desktop/tests/OrderAlert.Core.Tests/AlertPolicyTests.cs`: repeat and escalation tests.
- `order-alert-desktop/tests/OrderAlert.Core.Tests/SqliteStoreTests.cs`: snapshot transaction and isolation tests.
- `order-alert-desktop/tests/OrderAlert.Core.Tests/NativeMessageProtocolTests.cs`: framing and serialization tests.
- `order-alert-desktop/tests/OrderAlert.Core.Tests/ScanOrchestratorTests.cs`: partial-failure retention and non-reentrancy tests.
- `order-alert-desktop/installer/com.orderalert.native-host.json`: Native Messaging host manifest template.
- `order-alert-desktop/installer/install.ps1`: per-user installation, native-host registration, and autostart option.
- `order-alert-desktop/installer/uninstall.ps1`: clean per-user removal without deleting user data unless explicitly requested.
- `order-alert-desktop/README.md`: installation, adding stores, first login, settings, and troubleshooting.

## Task 1: Establish prerequisites and scaffold the testable solution

**Files:**
- Create: `browser-plugin/package.json`
- Create: `order-alert-desktop/OrderAlert.sln`
- Create: `order-alert-desktop/src/OrderAlert.Core/OrderAlert.Core.csproj`
- Create: `order-alert-desktop/src/OrderAlert.App/OrderAlert.App.csproj`
- Create: `order-alert-desktop/src/OrderAlert.NativeHost/OrderAlert.NativeHost.csproj`
- Create: `order-alert-desktop/tests/OrderAlert.Core.Tests/OrderAlert.Core.Tests.csproj`

- [ ] **Step 1: Verify execution prerequisites and stop on missing Git identity**

Run:

```powershell
node --version
dotnet --list-sdks
git config user.name
git config user.email
```

Expected: Node prints `v25.2.1` or newer; `.NET 8.x` appears; Git name and email are non-empty. The current machine has no .NET SDK and no Git identity, so execution must pause for the user to approve installation and provide their preferred commit identity before commits are attempted.

- [ ] **Step 2: Install .NET 8 SDK after user approval**

Run:

```powershell
winget install --id Microsoft.DotNet.SDK.8 --exact --source winget
dotnet --list-sdks
```

Expected: a line beginning with `8.` appears.

- [ ] **Step 3: Create the solution and projects**

Run:

```powershell
New-Item -ItemType Directory -Force order-alert-desktop/src,order-alert-desktop/tests | Out-Null
dotnet new sln -n OrderAlert -o order-alert-desktop
dotnet new classlib -n OrderAlert.Core -o order-alert-desktop/src/OrderAlert.Core -f net8.0
dotnet new wpf -n OrderAlert.App -o order-alert-desktop/src/OrderAlert.App -f net8.0-windows
dotnet new console -n OrderAlert.NativeHost -o order-alert-desktop/src/OrderAlert.NativeHost -f net8.0
dotnet new xunit -n OrderAlert.Core.Tests -o order-alert-desktop/tests/OrderAlert.Core.Tests -f net8.0
dotnet sln order-alert-desktop/OrderAlert.sln add order-alert-desktop/src/OrderAlert.Core/OrderAlert.Core.csproj order-alert-desktop/src/OrderAlert.App/OrderAlert.App.csproj order-alert-desktop/src/OrderAlert.NativeHost/OrderAlert.NativeHost.csproj order-alert-desktop/tests/OrderAlert.Core.Tests/OrderAlert.Core.Tests.csproj
dotnet add order-alert-desktop/src/OrderAlert.App/OrderAlert.App.csproj reference order-alert-desktop/src/OrderAlert.Core/OrderAlert.Core.csproj
dotnet add order-alert-desktop/src/OrderAlert.NativeHost/OrderAlert.NativeHost.csproj reference order-alert-desktop/src/OrderAlert.Core/OrderAlert.Core.csproj
dotnet add order-alert-desktop/tests/OrderAlert.Core.Tests/OrderAlert.Core.Tests.csproj reference order-alert-desktop/src/OrderAlert.Core/OrderAlert.Core.csproj
```

Expected: all templates and references are created successfully.

- [ ] **Step 4: Add exact dependencies and JS test command**

Write `browser-plugin/package.json`:

```json
{
  "private": true,
  "scripts": { "test": "node --test tests/*.test.cjs" },
  "devDependencies": { "jsdom": "^26.1.0" }
}
```

Run:

```powershell
npm install --prefix browser-plugin
dotnet add order-alert-desktop/src/OrderAlert.Core/OrderAlert.Core.csproj package Microsoft.Data.Sqlite --version 8.0.12
dotnet add order-alert-desktop/src/OrderAlert.App/OrderAlert.App.csproj package CommunityToolkit.Mvvm --version 8.4.0
dotnet add order-alert-desktop/src/OrderAlert.App/OrderAlert.App.csproj package Hardcodet.NotifyIcon.Wpf --version 1.1.0
```

Expected: package restore succeeds without version conflicts.

- [ ] **Step 5: Verify the clean scaffold**

Run:

```powershell
dotnet test order-alert-desktop/OrderAlert.sln
npm test --prefix browser-plugin
```

Expected: generated xUnit test passes; Node reports zero failing tests.

- [ ] **Step 6: Commit the scaffold**

```powershell
git add browser-plugin/package.json browser-plugin/package-lock.json order-alert-desktop
git commit -m "build: scaffold order alert desktop app"
```

## Task 2: Define normalized extension scan data

**Files:**
- Create: `browser-plugin/order-alert/scan-schema.js`
- Create: `browser-plugin/tests/adapters.test.cjs`

- [ ] **Step 1: Write the failing schema tests**

Add tests that require platform, store ID, order ID, source URL, and at least one time value:

```javascript
const test = require("node:test");
const assert = require("node:assert/strict");
const { normalizeOrder, validateBatch } = require("../order-alert/scan-schema.js");

test("normalizes an order key without crossing stores", () => {
  const order = normalizeOrder({
    platform: "aliexpress", storeId: "us-store", orderId: "815209",
    sourceUrl: "https://csp.aliexpress.com/m_apps/order-manage/orderList?channelId=244176",
    assessmentAt: null, shippingSeconds: 3600, rawStatus: "Awaiting shipment"
  });
  assert.equal(order.orderKey, "aliexpress:us-store:815209");
});

test("rejects an incomplete batch", () => {
  assert.throws(() => validateBatch({ complete: false, orders: [] }), /complete scan/);
});
```

- [ ] **Step 2: Run the tests and verify RED**

Run: `npm test --prefix browser-plugin`

Expected: FAIL with `Cannot find module '../order-alert/scan-schema.js'`.

- [ ] **Step 3: Implement the schema module**

Implement and export:

```javascript
function normalizeOrder(input) {
  const required = ["platform", "storeId", "orderId", "sourceUrl"];
  for (const key of required) if (!String(input[key] || "").trim()) throw new Error(`Missing ${key}`);
  if (!input.assessmentAt && !Number.isFinite(input.shippingSeconds)) throw new Error("Missing order time");
  return Object.freeze({
    platform: input.platform,
    storeId: input.storeId,
    orderId: String(input.orderId).trim(),
    orderKey: `${input.platform}:${input.storeId}:${String(input.orderId).trim()}`,
    sourceUrl: input.sourceUrl,
    assessmentAt: input.assessmentAt || null,
    shippingSeconds: Number.isFinite(input.shippingSeconds) ? input.shippingSeconds : null,
    rawStatus: String(input.rawStatus || "").trim(),
    rawAssessmentText: String(input.rawAssessmentText || "").trim(),
    rawShippingText: String(input.rawShippingText || "").trim()
  });
}

function validateBatch(batch) {
  if (!batch || batch.complete !== true) throw new Error("A complete scan is required");
  if (!Array.isArray(batch.orders)) throw new Error("orders must be an array");
  return batch.orders.map(normalizeOrder);
}

const api = { normalizeOrder, validateBatch };
if (typeof module !== "undefined") module.exports = api;
globalThis.OrderAlert = Object.assign(globalThis.OrderAlert || {}, api);
```

- [ ] **Step 4: Run the tests and verify GREEN**

Run: `npm test --prefix browser-plugin`

Expected: both schema tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add browser-plugin/order-alert/scan-schema.js browser-plugin/tests/adapters.test.cjs
git commit -m "test: define normalized order scan schema"
```

## Task 3: Implement Dianxiaomi and AliExpress DOM adapters

**Files:**
- Create: `browser-plugin/order-alert/dom-utils.js`
- Create: `browser-plugin/order-alert/dianxiaomi-adapter.js`
- Create: `browser-plugin/order-alert/aliexpress-adapter.js`
- Create: `browser-plugin/tests/fixtures/dianxiaomi-orders.html`
- Create: `browser-plugin/tests/fixtures/aliexpress-orders.html`
- Modify: `browser-plugin/tests/adapters.test.cjs`

- [ ] **Step 1: Create realistic fixture rows and failing extraction tests**

Use fixture attributes as stable test hooks while adapters also support visible labels:

```html
<table data-order-list data-store-id="dxm-main"><tbody>
  <tr data-order-row><td data-order-id>DXM372</td><td data-status>已付款</td>
  <td data-assessment-time>2026-06-24 18:30</td><td data-shipping-time>18小时25分</td></tr>
</tbody></table>
```

```html
<div data-order-list data-store-id="ali-us">
  <article data-order-row><span data-order-id>815209</span><span data-status>Awaiting shipment</span>
  <time data-shipping-time>01:42:16</time></article>
</div>
```

Test exact normalized output for both fixtures, including `storeId`, `orderId`, ISO assessment time, shipping seconds, raw status, and source URL.

- [ ] **Step 2: Run adapters tests and verify RED**

Run: `npm test --prefix browser-plugin`

Expected: FAIL because adapter modules do not exist.

- [ ] **Step 3: Implement shared DOM utilities**

Export focused helpers:

```javascript
const text = (root, selectors) => selectors.map(s => root.querySelector(s)?.textContent?.trim()).find(Boolean) || "";
function parseCountdown(value) {
  const source = String(value || "").trim();
  const clock = source.match(/^(\d+):(\d{2}):(\d{2})$/);
  if (clock) return Number(clock[1]) * 3600 + Number(clock[2]) * 60 + Number(clock[3]);
  const days = Number(source.match(/(\d+)\s*天/)?.[1] || 0);
  const hours = Number(source.match(/(\d+)\s*(?:小时|时)/)?.[1] || 0);
  const minutes = Number(source.match(/(\d+)\s*分/)?.[1] || 0);
  return days || hours || minutes ? days * 86400 + hours * 3600 + minutes * 60 : null;
}
```

Add `parseLocalDateTime`, `findOrderRows`, and `detectStoreId`; export for Node and attach to `globalThis.OrderAlert`.

- [ ] **Step 4: Implement both adapters behind the same interface**

Each module exports:

```javascript
{
  matches(location, document),
  getStoreId(document),
  extractCurrentPage(document, location)
}
```

`extractCurrentPage` maps rows through `normalizeOrder`. Prefer `data-*` hooks, then bounded selectors and label-based cells. Never select all page text or buyer information.

- [ ] **Step 5: Run adapter tests**

Run: `npm test --prefix browser-plugin`

Expected: both platform fixture tests PASS; malformed time text produces a controlled `Missing order time` failure.

- [ ] **Step 6: Commit**

```powershell
git add browser-plugin/order-alert browser-plugin/tests
git commit -m "feat: parse dianxiaomi and aliexpress orders"
```

## Task 4: Make scans complete, paginated, and failure-safe

**Files:**
- Create: `browser-plugin/order-alert/scan-coordinator.js`
- Create: `browser-plugin/tests/scan-coordinator.test.cjs`
- Create: `browser-plugin/order-alert/content-order-alert.js`

- [ ] **Step 1: Write failing coordinator tests**

Cover three cases with fake adapters and pagination drivers:

```javascript
test("deduplicates an order seen on two pages", async () => {
  const pages = [[{ orderKey: "dianxiaomi:main:1" }], [{ orderKey: "dianxiaomi:main:1" }]];
  const result = await scanAllPages(fakeAdapter(pages), fakePager(pages.length));
  assert.equal(result.complete, true);
  assert.equal(result.orders.length, 1);
});

test("marks a batch incomplete when page two fails", async () => {
  await assert.rejects(() => scanAllPages(failingAdapter(2), fakePager(3)), /page 2/);
});
```

Also assert a 100-page hard limit and repeated-page fingerprint detection.

- [ ] **Step 2: Run and verify RED**

Run: `npm test --prefix browser-plugin`

Expected: FAIL because `scanAllPages` is undefined.

- [ ] **Step 3: Implement `scanAllPages`**

The implementation must:

- wait for the order container and loading mask to settle;
- extract and validate each page;
- deduplicate by `orderKey`;
- click the enabled next control and wait for the row fingerprint to change;
- reject on timeout, repeated fingerprint, adapter error, or page limit;
- return `{ complete: true, scannedAt, pageCount, orders }` only after the last page.

- [ ] **Step 4: Implement the content entry point**

Select the matching adapter, answer `{ type: "scan-page" }` commands, and return structured failures:

```javascript
chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type !== "order-alert:scan") return false;
  runCurrentPlatformScan(message.account).then(
    batch => sendResponse({ ok: true, batch }),
    error => sendResponse({ ok: false, error: { code: "SCAN_FAILED", message: error.message } })
  );
  return true;
});
```

- [ ] **Step 5: Run tests**

Run: `npm test --prefix browser-plugin`

Expected: schema, adapter, pagination, deduplication, and partial-failure tests PASS.

- [ ] **Step 6: Commit**

```powershell
git add browser-plugin/order-alert browser-plugin/tests
git commit -m "feat: coordinate complete order scans"
```

## Task 5: Wire Manifest V3 and Native Messaging

**Files:**
- Create: `browser-plugin/order-alert/native-bridge.js`
- Modify: `browser-plugin/manifest.json`
- Modify: `browser-plugin/background.js`
- Test: `browser-plugin/tests/native-bridge.test.cjs`

- [ ] **Step 1: Write failing bridge tests with a fake Chrome port**

Assert that `scanAccount` commands are forwarded to the correct tab, results include the request ID, disconnects emit `hostOffline`, and unknown commands return `UNSUPPORTED_COMMAND`.

- [ ] **Step 2: Run and verify RED**

Run: `npm test --prefix browser-plugin`

Expected: FAIL because `createNativeBridge` is undefined.

- [ ] **Step 3: Implement the bridge**

Use a single host name constant:

```javascript
const HOST_NAME = "com.orderalert.native_host";
function createNativeBridge(chromeApi) {
  const port = chromeApi.runtime.connectNative(HOST_NAME);
  port.onMessage.addListener(command => routeCommand(chromeApi, port, command));
  port.onDisconnect.addListener(() => chromeApi.storage.local.set({ orderAlertHostOnline: false }));
  chromeApi.storage.local.set({ orderAlertHostOnline: true });
  return port;
}
```

`routeCommand` locates or creates the target tab, waits for completion, sends `order-alert:scan`, and posts a response envelope.

- [ ] **Step 4: Update the manifest in script dependency order**

Add `nativeMessaging` to `permissions`. Add the seven `order-alert/*.js` files after the existing `content.js` for Dianxiaomi and AliExpress matches. Change the module service worker to `background-entry.js`. Keep existing collection permissions and hosts unchanged.

- [ ] **Step 5: Delegate startup from `background.js`**

Preserve `productExtracted` handling. Create `background-entry.js` with side-effect imports in dependency order:

```javascript
import "./background.js";
import "./order-alert/native-bridge.js";
globalThis.OrderAlert.createNativeBridge(chrome);
```

Keep the bridge module compatible with Node tests through `module.exports` while attaching the same API to `globalThis.OrderAlert` for the service worker.

- [ ] **Step 6: Verify**

Run:

```powershell
npm test --prefix browser-plugin
node --check browser-plugin/background.js
node --check browser-plugin/order-alert/content-order-alert.js
```

Expected: all tests PASS and syntax checks print no errors.

- [ ] **Step 7: Commit**

```powershell
git add browser-plugin
git commit -m "feat: connect order scanner to native host"
```

## Task 6: Implement deadline and schedule domain rules in .NET

**Files:**
- Create: `order-alert-desktop/src/OrderAlert.Core/Models/OrderModels.cs`
- Create: `order-alert-desktop/src/OrderAlert.Core/Services/DeadlineEvaluator.cs`
- Create: `order-alert-desktop/src/OrderAlert.Core/Services/ScheduleWindow.cs`
- Create: `order-alert-desktop/tests/OrderAlert.Core.Tests/DeadlineEvaluatorTests.cs`
- Create: `order-alert-desktop/tests/OrderAlert.Core.Tests/ScheduleWindowTests.cs`

- [ ] **Step 1: Write failing time-boundary tests**

Define fixed-time tests using `2026-06-23 19:00:00 +08:00`:

```csharp
[Theory]
[InlineData(86400, RiskLevel.Normal)]
[InlineData(86399, RiskLevel.DueSoon)]
[InlineData(7200, RiskLevel.Critical)]
[InlineData(1, RiskLevel.Critical)]
[InlineData(0, RiskLevel.Overdue)]
public void Shipping_countdown_uses_exact_boundaries(int seconds, RiskLevel expected)
{
    var order = OrderSnapshot.WithShippingSeconds(seconds);
    Assert.Equal(expected, new DeadlineEvaluator().Evaluate(order, BeijingNow));
}
```

Add assessment tests for exactly 24 hours, one second under 24 hours, zero, and negative values relative to that day's 19:00. Add a test proving the higher risk wins when both fields exist.

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter "DeadlineEvaluatorTests|ScheduleWindowTests"`

Expected: compile failure because domain types do not exist.

- [ ] **Step 3: Implement immutable domain records and evaluator**

Define `PlatformKind`, `RiskLevel`, `StoreAccount`, `OrderSnapshot`, `ScanBatch`, `AppSettings`, and `EvaluationResult`. `DeadlineEvaluator.Evaluate` returns the maximum of assessment and shipping risk plus a machine-readable reason.

Implement assessment anchor explicitly:

```csharp
var anchor = new DateTimeOffset(now.Year, now.Month, now.Day, 19, 0, 0, now.Offset);
var remaining = assessmentAt - anchor;
```

Use strict `< 24 hours`, `<= 2 hours`, and `<= 0` comparisons matching the design.

- [ ] **Step 4: Implement monitoring windows**

`ScheduleWindow.Contains` supports normal windows such as `08:00–22:00` and cross-midnight windows such as `22:00–06:00`. `NextRun` rounds from the last run using the configured interval and moves to the next opening time when outside the window.

- [ ] **Step 5: Run tests**

Run: `dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests`

Expected: all deadline and schedule tests PASS.

- [ ] **Step 6: Commit**

```powershell
git add order-alert-desktop/src/OrderAlert.Core order-alert-desktop/tests/OrderAlert.Core.Tests
git commit -m "feat: evaluate order deadlines and schedules"
```

## Task 7: Persist accounts, complete snapshots, and settings transactionally

**Files:**
- Create: `order-alert-desktop/src/OrderAlert.Core/Persistence/SqliteStore.cs`
- Create: `order-alert-desktop/src/OrderAlert.Core/Accounts/AccountService.cs`
- Create: `order-alert-desktop/tests/OrderAlert.Core.Tests/SqliteStoreTests.cs`

- [ ] **Step 1: Write failing persistence tests**

Cover:

- account profile directories are unique and derived from generated account IDs, not raw account names;
- identical order numbers in two stores create two records;
- a complete batch replaces the store's current snapshot atomically;
- an incomplete batch throws and preserves the prior snapshot;
- settings survive store reconstruction.

Use a temporary SQLite file per test and delete it in `DisposeAsync`.

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter SqliteStoreTests`

Expected: compile failure because `SqliteStore` and `AccountService` do not exist.

- [ ] **Step 3: Implement schema initialization**

Create tables `accounts`, `orders`, `scan_batches`, `alert_states`, and `settings`. Add a unique constraint on `(platform, account_id, order_id)` and foreign keys from orders and scans to accounts.

- [ ] **Step 4: Implement transactional complete-batch commit**

`CommitBatchAsync` must reject `Complete == false`, insert the scan record, upsert all returned orders, and mark previously active missing orders inactive inside one transaction. Roll back on any exception.

- [ ] **Step 5: Implement account operations**

`AddAliExpressAsync(displayName, accountIdentifier)` creates a GUID account ID and profile path under `%LOCALAPPDATA%\OrderAlert\ChromeProfiles\<account-id>`. Implement edit display name, enable/disable, delete metadata, and list accounts. Deletion leaves the profile directory until the user confirms data deletion in the UI.

- [ ] **Step 6: Run tests**

Run: `dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests`

Expected: persistence and all earlier tests PASS.

- [ ] **Step 7: Commit**

```powershell
git add order-alert-desktop/src/OrderAlert.Core order-alert-desktop/tests/OrderAlert.Core.Tests
git commit -m "feat: persist store accounts and scan snapshots"
```

## Task 8: Implement Native Messaging, Chrome launch, and scan orchestration

**Files:**
- Create: `order-alert-desktop/src/OrderAlert.Core/Messaging/NativeMessageProtocol.cs`
- Create: `order-alert-desktop/src/OrderAlert.Core/Messaging/NativeMessageHost.cs`
- Create: `order-alert-desktop/src/OrderAlert.Core/Messaging/NativeHostPipeServer.cs`
- Create: `order-alert-desktop/src/OrderAlert.Core/Messaging/NativeHostPipeClient.cs`
- Create: `order-alert-desktop/src/OrderAlert.Core/Chrome/ChromeProfileLauncher.cs`
- Create: `order-alert-desktop/src/OrderAlert.Core/Services/ScanOrchestrator.cs`
- Create: `order-alert-desktop/src/OrderAlert.NativeHost/Program.cs`
- Create: `order-alert-desktop/tests/OrderAlert.Core.Tests/NativeMessageProtocolTests.cs`
- Create: `order-alert-desktop/tests/OrderAlert.Core.Tests/ScanOrchestratorTests.cs`

- [ ] **Step 1: Write failing protocol tests**

Assert little-endian four-byte length framing, UTF-8 JSON round-trip, maximum message size rejection, request/response ID preservation, clean end-of-stream handling, and named-pipe relay preserving the same request ID.

- [ ] **Step 2: Write failing orchestrator tests**

Use fakes for launcher, host client, store, and clock. Assert:

- the same account cannot scan twice concurrently;
- a successful complete batch commits once;
- a partial or timed-out batch records failure without changing the last snapshot;
- one account failure does not stop other accounts;
- disabled accounts are skipped.

- [ ] **Step 3: Run and verify RED**

Run: `dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter "NativeMessageProtocolTests|ScanOrchestratorTests"`

Expected: compile failures for missing protocol and orchestrator types.

- [ ] **Step 4: Implement protocol and host loop**

Use `BinaryReader.ReadInt32` / `BinaryWriter.Write(int)` for Chrome framing, `System.Text.Json` for envelopes, a 4 MiB limit, cancellation tokens, and structured `{ requestId, type, payload, error }` messages. The console host writes logs to a file stream, never stdout.

The console native host does not own application state. `NativeHostPipeClient` relays each Chrome envelope to the named pipe `OrderAlert.NativeBridge`; `NativeHostPipeServer` runs inside the WPF process, forwards commands to `ScanOrchestrator`, and returns the response with the same request ID. If the WPF process is absent, the native host starts it once, waits up to 10 seconds for the pipe, then returns `APP_UNAVAILABLE` on failure.

- [ ] **Step 5: Implement Chrome profile launcher**

Resolve Chrome from standard per-machine and per-user paths. Start with an argument list, not a concatenated command string:

```csharp
startInfo.ArgumentList.Add($"--user-data-dir={profilePath}");
startInfo.ArgumentList.Add($"--load-extension={extensionDirectory}");
startInfo.ArgumentList.Add("--no-first-run");
startInfo.ArgumentList.Add(targetUrl);
```

The installer copies the unpacked extension into one stable absolute directory, and every managed profile loads that same directory so the registered extension ID remains consistent. Do not pass credentials. Return explicit errors for missing Chrome, missing extension directory, inaccessible profile path, and process launch failure.

- [ ] **Step 6: Implement orchestration**

Use a `ConcurrentDictionary<Guid, SemaphoreSlim>` for per-account non-reentrancy. Launch the account profile, await bridge availability, request the correct platform URL set, enforce account and page timeouts, validate complete batches, commit only complete results, and record failures independently.

- [ ] **Step 7: Run tests**

Run: `dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests`

Expected: all protocol, orchestration, persistence, schedule, and deadline tests PASS.

- [ ] **Step 8: Commit**

```powershell
git add order-alert-desktop
git commit -m "feat: orchestrate profile-scoped chrome scans"
```

## Task 9: Implement alert decisions, Windows notifications, and snooze

**Files:**
- Create: `order-alert-desktop/src/OrderAlert.Core/Services/AlertPolicy.cs`
- Create: `order-alert-desktop/src/OrderAlert.App/Notifications/WindowsAlertService.cs`
- Create: `order-alert-desktop/tests/OrderAlert.Core.Tests/AlertPolicyTests.cs`

- [ ] **Step 1: Write failing alert-policy tests**

Test immediate alerts for first due-soon state, critical escalation, overdue transition, risk-count increase, and account failure. Test suppression before two hours, repeat at exactly two hours, snooze suppression, and the rule that a new order or escalation bypasses an old snooze.

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter AlertPolicyTests`

Expected: compile failure because `AlertPolicy` does not exist.

- [ ] **Step 3: Implement pure alert policy**

`AlertPolicy.Decide(previous, current, settings, now)` returns `AlertDecision` with `ShouldNotify`, `ShouldPlaySound`, `Reason`, `NextEligibleAt`, and affected order keys. It must have no WPF or Windows dependencies.

- [ ] **Step 4: Implement Windows alert service**

Show a toast containing due-soon, critical, overdue, affected-store, and failure counts. Add activation arguments `action=open-orders` and `action=snooze&minutes=120`. Play the selected WAV only when sound is enabled; catch playback errors and still show the toast.

- [ ] **Step 5: Run tests and a manual notification smoke test**

Run:

```powershell
dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests
dotnet run --project order-alert-desktop/src/OrderAlert.App -- --notification-smoke-test
```

Expected: all tests PASS; one Windows notification appears; sound follows the current setting; activation opens the app.

- [ ] **Step 6: Commit**

```powershell
git add order-alert-desktop
git commit -m "feat: alert on deadline changes and repeats"
```

## Task 10: Build the WPF tray dashboard, account management, and settings

**Files:**
- Modify: `order-alert-desktop/src/OrderAlert.App/App.xaml`
- Create: `order-alert-desktop/src/OrderAlert.App/MainWindow.xaml`
- Create: `order-alert-desktop/src/OrderAlert.App/ViewModels/MainViewModel.cs`
- Create: `order-alert-desktop/src/OrderAlert.App/Views/AccountsView.xaml`
- Create: `order-alert-desktop/src/OrderAlert.App/Views/SettingsView.xaml`
- Create: `order-alert-desktop/src/OrderAlert.App/Tray/TrayIconService.cs`
- Create: `order-alert-desktop/src/OrderAlert.App/Startup/AutoStartService.cs`
- Test: `order-alert-desktop/tests/OrderAlert.Core.Tests/MainViewModelTests.cs`

- [ ] **Step 1: Write failing view-model tests**

Test summary counts, platform/store/risk filters, disabled-account exclusion, immediate-scan command gating, and settings validation for intervals outside `5–120` minutes.

Before adding the tests, change `OrderAlert.Core.Tests.csproj` to target `net8.0-windows`, add `<EnableWindowsTargeting>true</EnableWindowsTargeting>`, and add a project reference to `../../src/OrderAlert.App/OrderAlert.App.csproj`. This lets the test project compile `MainViewModel` without moving UI state into Core.

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test order-alert-desktop/tests/OrderAlert.Core.Tests --filter MainViewModelTests`

Expected: compile failure because `MainViewModel` does not exist.

- [ ] **Step 3: Implement the main view model**

Expose observable collections for orders and accounts, summary count properties, selected filters, `CheckNowCommand`, `OpenOrderCommand`, `AddAccountCommand`, `ReloginCommand`, and `SaveSettingsCommand`. Commands call interfaces from Core; they do not access SQLite or launch processes directly.

- [ ] **Step 4: Implement the confirmed UI layout**

Create:

- header with run state, next check, and “立即检查”;
- cards for 24-hour, 2-hour, overdue, and online-account counts;
- order grid with platform, store, ID, status, both time values, risk, last check, and open action;
- account list with add, edit, enable, delete, relogin, and single-store scan;
- settings for window, 5–120 minute interval, repeat interval, popup, sound, sound preview, login failure notification, and autostart;
- scan-history view with complete/failure status and actionable error text.

- [ ] **Step 5: Implement tray and single-instance behavior**

The tray menu contains Open, Check Now, Pause/Resume, and Exit. Closing the main window hides it; Exit stops the scheduler and disposes the host. Use a named mutex so a second process activates the existing window and exits.

- [ ] **Step 6: Implement per-user autostart**

Use `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with the published executable path. The service must read current state, enable, disable, and report access errors without elevating.

- [ ] **Step 7: Verify view models and WPF build**

Run:

```powershell
dotnet test order-alert-desktop/OrderAlert.sln
dotnet build order-alert-desktop/src/OrderAlert.App/OrderAlert.App.csproj -c Release
```

Expected: all tests PASS and WPF Release build succeeds with zero errors.

- [ ] **Step 8: Manually inspect the main window**

Run: `dotnet run --project order-alert-desktop/src/OrderAlert.App`

Expected: the dashboard matches the approved hierarchy, remains usable at 100%, 125%, and 150% Windows scaling, and all Chinese labels fit without clipping.

- [ ] **Step 9: Commit**

```powershell
git add order-alert-desktop
git commit -m "feat: add order alert tray dashboard"
```

## Task 11: Install, register, and verify the complete product

**Files:**
- Create: `order-alert-desktop/installer/com.orderalert.native-host.json`
- Create: `order-alert-desktop/installer/install.ps1`
- Create: `order-alert-desktop/installer/uninstall.ps1`
- Create: `order-alert-desktop/README.md`
- Modify: `agent_memory/context.md`
- Modify: `agent_memory/progress.md`
- Modify: `agent_memory/bugs.md`

- [ ] **Step 1: Write the Native Messaging manifest template**

Use:

```json
{
  "name": "com.orderalert.native_host",
  "description": "Order Alert Chrome native messaging host",
  "path": "ORDER_ALERT_NATIVE_HOST_PATH",
  "type": "stdio",
  "allowed_origins": ["chrome-extension://ORDER_ALERT_EXTENSION_ID/"]
}
```

The installer replaces both tokens with the absolute published host path and user-supplied extension ID, then writes the manifest path under `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.orderalert.native_host`.

- [ ] **Step 2: Implement idempotent install and uninstall scripts**

`install.ps1` accepts `-ExtensionId`, publishes the app and host for `win-x64`, installs under `%LOCALAPPDATA%\Programs\OrderAlert`, registers the native host, and optionally enables autostart. `uninstall.ps1` stops running processes, removes program files and registry entries, and retains `%LOCALAPPDATA%\OrderAlert\data` and Chrome profiles unless `-RemoveUserData` is supplied.

- [ ] **Step 3: Document the exact user flow**

README sections: prerequisites, unpacked-extension installation, extension ID registration, app installation, adding a store, first manual login, monitoring settings, immediate check, interpreting statuses, updating selectors after platform changes, data location, logs, and uninstall.

- [ ] **Step 4: Run the complete automated verification**

Run:

```powershell
npm test --prefix browser-plugin
Get-ChildItem browser-plugin/order-alert -Filter *.js | ForEach-Object { node --check $_.FullName }
dotnet test order-alert-desktop/OrderAlert.sln -c Release
dotnet publish order-alert-desktop/src/OrderAlert.App/OrderAlert.App.csproj -c Release -r win-x64 --self-contained false
dotnet publish order-alert-desktop/src/OrderAlert.NativeHost/OrderAlert.NativeHost.csproj -c Release -r win-x64 --self-contained false
```

Expected: all JS and .NET tests PASS; all syntax checks and both publishes succeed.

- [ ] **Step 5: Run the local fixture end-to-end test**

Load the unpacked extension, open both fixture pages through a local static server, start the native host and app, trigger “立即检查”, and confirm:

- Dianxiaomi duplicate rows merge by platform, account, and order ID;
- AliExpress stores remain isolated;
- 18-hour orders are due soon, 1-hour orders are critical, zero is overdue;
- partial fixture failure preserves the last successful snapshot;
- popup and sound switches act independently;
- repeat alerts are suppressed until the configured interval.

- [ ] **Step 6: Run real-page acceptance with user-controlled accounts**

With the user present, verify one logged-in Dianxiaomi account across all four URLs and at least one AliExpress store. If selectors differ, capture only non-sensitive element structure, update the relevant adapter and fixture, rerun its tests, and retry. Stop on CAPTCHA, second-factor prompts, access denial, or platform policy restrictions and hand control to the user.

- [ ] **Step 7: Update project memory with current facts only**

Record final architecture and commands in `context.md`, completed verification and remaining work in `progress.md`, and only unresolved real-page risks in `bugs.md`. Remove stale entries superseded by this implementation.

- [ ] **Step 8: Final diff and regression check**

Run:

```powershell
git diff --check
git status --short
python backend/test_inventory.py
```

Expected: no whitespace errors; only intended files are changed; the existing backend inventory test still prints `ALL OK`.

- [ ] **Step 9: Commit documentation and installer**

```powershell
git add order-alert-desktop/installer order-alert-desktop/README.md agent_memory
git commit -m "docs: add order alert installation and operations guide"
```

## Completion criteria

- All Node and .NET tests pass from a clean checkout with documented prerequisites.
- The extension scans both platform fixtures and communicates through the registered Native Messaging host.
- The Windows app schedules enabled accounts only inside the configured window and prevents per-account overlap.
- Complete batches replace snapshots atomically; partial failures preserve previous data.
- Deadline boundaries, cross-store isolation, escalation, snooze, and repeat alerts match the approved design.
- The tray UI, popup and sound controls, autostart, Chrome auto-launch, independent profiles, and account login-failure flow work on Windows 11.
- One real Dianxiaomi account and one real AliExpress store pass user-controlled acceptance, or the exact platform-side blocker is documented without bypass attempts.
