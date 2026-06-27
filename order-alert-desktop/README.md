# Windows 11 订单临期预警

本工具由 Chrome 扩展和 .NET 8 Windows 托盘程序组成。扩展只读取已登录订单页，桌面程序负责多店铺配置、调度、SQLite 快照、风险判定、去重和提醒。程序不保存账号密码，也不会绕过验证码或二次验证。

## 安装

1. 安装 Google Chrome 和 .NET 8 Desktop Runtime。
2. 在 Chrome 打开 `chrome://extensions`，启用开发者模式。
3. 先临时加载仓库的 `browser-plugin`，复制页面显示的 32 位扩展 ID。
4. 在 PowerShell 执行：

   ```powershell
   .\order-alert-desktop\installer\install.ps1 -ExtensionId <扩展ID> -EnableAutoStart
   ```

5. 安装脚本会发布到 `%LOCALAPPDATA%\Programs\OrderAlert`。安装后在扩展管理页移除临时项，再加载 `%LOCALAPPDATA%\Programs\OrderAlert\app\extension`；确认 ID 没有变化。
6. 启动 `%LOCALAPPDATA%\Programs\OrderAlert\app\OrderAlert.App.exe`。

## 添加店铺与首次登录

在“店铺账号”选择“添加速卖通店铺”，输入显示名称和不含密码的账号标识。每个店铺都会使用 `%LOCALAPPDATA%\OrderAlert\ChromeProfiles\<account-id>` 下的独立 Chrome 配置。首次打开后由用户自行完成登录、验证码或二次验证。

店小秘账号检查四个固定订单状态页；速卖通账号检查订单管理页。点击“立即检查”会启动相应 Chrome 配置，由扩展扫描页面并通过 Native Messaging 回传完整批次。任何分页或页面解析失败都不会覆盖上一次成功快照。

## 设置与状态

- 默认监控时段为 `08:00–22:00`，检查间隔 15 分钟。
- 检查间隔允许 5–120 分钟，默认每 120 分钟复报。
- 弹窗和声音可以独立关闭。
- 风险规则：不足 24 小时为临期，不足或等于 2 小时为紧急，零或负数为超时。
- 数据库位于 `%LOCALAPPDATA%\OrderAlert\data\orders.db`。
- Chrome 登录资料位于 `%LOCALAPPDATA%\OrderAlert\ChromeProfiles`。

## 页面变化与故障排查

出现“页面无法识别”时，不要上传完整页面、Cookie 或买家信息。只记录订单列表容器、行、订单号、状态与时间字段的非敏感 DOM 结构，并同步更新对应 adapter 和 fixture 测试。

如果提示需要登录，请在对应独立 Chrome 配置中人工完成登录。如果遇到 CAPTCHA、二次验证、访问拒绝或平台明确禁止自动化，应停止该店铺联调，不尝试绕过。

Native Messaging 注册位置：

`HKCU\Software\Google\Chrome\NativeMessagingHosts\com.orderalert.native_host`

## 卸载

```powershell
.\order-alert-desktop\installer\uninstall.ps1
```

默认保留 SQLite 和 Chrome 登录配置。仅在明确需要清除用户数据时使用：

```powershell
.\order-alert-desktop\installer\uninstall.ps1 -RemoveUserData
```
