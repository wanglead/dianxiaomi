# context.md

## 项目概述

商品采集上架营销系统，用于从多来源采集商品，进行商品资料/图片/营销处理，并通过店小秘或销售平台完成上架。

## 技术栈

- 后端：FastAPI、SQLAlchemy、SQLite、Jinja2、httpx、BeautifulSoup。
- 前端：Next.js 16 / React 19，目前仍是默认模板页。
- 浏览器插件：Chrome Manifest V3，popup/content/background 脚本。

## 项目结构

- `backend/`：API、数据库模型、Jinja 页面和采集/上架/营销服务。
- `frontend/`：Next.js 前端工程，尚未承载业务界面。
- `browser-plugin/`：商品采集浏览器插件，可从商品页提取数据并导入本地后端。
- `data/` 与 `backend/data/`：SQLite 数据库文件。

## 关键约定

- 默认中文沟通；代码、接口与目录沿用仓库现有命名风格。
- 采集方式使用 `system_recommend`、`url_input`、`browser_plugin`。
- 目标销售平台暂定为 `aliexpress`、`ozon`、`joom`、`shopify`，店小秘作为中间上传通道 `dianxiaomi`。

## 当前状态

- 商品上架系统已复用 FastAPI/Jinja 实现批量 AI 处理、图片进度、问题提示、目标店铺选择和店小秘待同步队列。
- 已增加店小秘同步服务与插件 API，旧 SQLite 数据通过幂等缺列迁移保持兼容。
- 浏览器插件可在店小秘页面注入 SKU 同步助手，使用浏览器登录态填充表单并回写本地状态。
- 第一版不直接调用店小秘开放 API；四个销售平台真实直传仍为后续范围。
- 店小秘真实页面 DOM 选择器需要在用户登录环境中继续校准。
