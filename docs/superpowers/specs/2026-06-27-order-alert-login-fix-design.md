# 订单预警多平台登录入口与防闪退修复设计

## 背景

当前订单预警程序的店铺账号页只有“添加速卖通店铺”，没有店小秘登录入口。新增或重新登录账号时，开发版程序按 `AppContext.BaseDirectory/extension` 查找扩展，但该目录不存在，`ChromeProfileLauncher` 抛出的 `DirectoryNotFoundException` 又未被异步命令捕获，最终导致 WPF 进程退出。

Windows .NET Runtime 事件日志还记录了退出阶段释放未持有 Mutex 的 `ApplicationException`，需要在同一轮修复中消除。

## 已确认方案

采用两个明确入口：

- “添加店小秘账号”
- “添加速卖通店铺”

两个平台都允许添加多个账号。每个账号使用独立 Chrome `user-data-dir`，不保存账号密码。店小秘账号打开四个固定订单页，速卖通账号打开订单管理页。

## 账号创建

`AccountService` 增加通用的内部账号创建逻辑，并分别公开店小秘与速卖通创建方法。两者都：

- 生成 GUID 账号 ID；
- 使用账号 ID 创建隔离的 Chrome profile 路径；
- 校验显示名称和账号标识；
- 保存到现有 SQLite `accounts` 表。

店小秘与速卖通沿用现有 `PlatformKind`，不修改数据库表结构。

## 界面与命令

店铺账号页显示两个添加按钮。`MainViewModel` 分别公开店小秘和速卖通添加命令，并保留选中账号后的“重新登录”命令。

新增账号和重新登录都必须经过统一的安全执行边界：

- 操作成功时保持现有行为；
- 用户取消输入时静默返回；
- Chrome、扩展目录、profile 或进程启动失败时，将可行动错误写入 `LastError` 并显示在主窗口；
- 异常不得离开异步命令并终止 WPF 进程。

## 扩展目录解析

新增独立的扩展目录解析器，按以下顺序查找：

1. 安装版程序目录下的 `extension/manifest.json`；
2. 开发环境从程序目录向上查找仓库的 `browser-plugin/manifest.json`。

解析器只接受包含 `manifest.json` 的目录。全部候选路径无效时抛出包含已检查路径的可行动错误，由界面安全执行边界展示。

安装脚本仍负责将扩展复制到稳定的安装目录；开发回退仅用于本地构建和调试。

## Chrome 登录行为

- 店小秘账号登录时打开已付款、待审核、自营仓处理和已分配四个页面。
- 速卖通账号登录时打开订单管理页。
- 两个平台均使用账号自身的 profile 路径和同一个已解析扩展目录。
- 不自动填写账号密码，不处理或绕过验证码、二次验证和平台风控。

## 单实例退出

`App` 显式记录当前进程是否拥有单实例 Mutex。只有成功取得所有权的进程才调用 `ReleaseMutex`；二次启动后立即退出的进程只释放对象，不释放所有权。

## 测试与验证

先写失败测试，再实现：

- `AccountService` 可分别创建店小秘和速卖通账号，且 profile 互相隔离；
- 扩展目录解析器优先使用安装目录，并能回退到开发仓库；
- 所有候选目录缺失时返回可行动错误；
- 新增账号和重新登录失败时 `MainViewModel` 捕获异常、设置 `LastError`，进程不退出；
- 店小秘登录使用四个固定页面；
- Mutex 非所有者退出路径不调用释放操作。

完成后运行：

```powershell
dotnet test order-alert-desktop/OrderAlert.sln -c Release
dotnet build order-alert-desktop/src/OrderAlert.App/OrderAlert.App.csproj -c Release
npm test --prefix browser-plugin
```

最后启动程序，分别验证两个添加入口、店小秘登录、速卖通重新登录、错误提示以及关闭/再次启动行为。

## 范围边界

本修复不调整订单解析规则、预警阈值、SQLite 表结构或商品上架系统。真实页面若出现验证码、二次验证或 DOM 差异，只进行人工接管与非敏感结构校准，不绕过平台安全机制。
