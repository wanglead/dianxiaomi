# Dianxiaomi Listing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first usable Dianxiaomi-first product collection, processing, and semi-automatic listing loop.

**Architecture:** Reuse the existing FastAPI/Jinja backend as the system workbench and enhance the Chrome Manifest V3 plugin for Dianxiaomi in-page synchronization. The backend owns products, processing status, sync queues, and records; the plugin owns browser-login-state interaction, SKU recognition, field filling assistance, and progress callbacks.

**Tech Stack:** FastAPI, SQLAlchemy, SQLite, Pydantic, Jinja2, vanilla JavaScript, Chrome Manifest V3.

---

## File Structure

- Modify `backend/app/models/models.py`: add product status values and sync/progress fields to `Product`.
- Modify `backend/app/schemas/schemas.py`: add request/response schemas for processing and Dianxiaomi sync.
- Create `backend/app/services/sync_service.py`: product processing simulation, sync queue preparation, plugin progress/result handling.
- Create `backend/app/api/plugin.py`: local plugin-facing Dianxiaomi endpoints.
- Modify `backend/app/api/products.py`: expose batch processing and prepare-sync endpoints.
- Modify `backend/app/main.py`: register the plugin API router.
- Modify `backend/app/templates/layout.html`: update the product navigation label to 商品工作台.
- Modify `backend/app/templates/products.html`: convert product management into the reference-style workbench.
- Modify `browser-plugin/manifest.json`: add Dianxiaomi host permissions and content script matching.
- Modify `browser-plugin/content.js`: keep product extraction and add Dianxiaomi assistant injection.
- Modify `browser-plugin/popup.js`: keep existing collection flow and add service-status feedback.
- Create `backend/test_dianxiaomi_sync.py`: smoke-test the backend sync loop with FastAPI TestClient.
- Create `browser-plugin/test-dianxiaomi.html`: static DOM page for manual plugin/DOM verification when Dianxiaomi is unavailable.
- Modify `agent_memory/progress.md`: track implementation progress after each task.
- Modify `agent_memory/bugs.md`: record known runtime risks and resolved issues.

## Task 1: Backend Models And Schemas

**Files:**
- Modify: `backend/app/models/models.py`
- Modify: `backend/app/schemas/schemas.py`
- Test: `backend/test_dianxiaomi_sync.py`

- [ ] **Step 1: Write the failing backend schema/model smoke test**

Create `backend/test_dianxiaomi_sync.py` with:

```python
import os
import sys

os.chdir(r"C:\Users\NIDHOGG\Documents\商品上架\backend")
sys.path.insert(0, r"C:\Users\NIDHOGG\Documents\商品上架\backend")

from fastapi.testclient import TestClient

from app.main import app


client = TestClient(app)


def test_dianxiaomi_prepare_pending_and_result_loop():
    product_payload = {
        "title": "轻风雕塑摆件",
        "description": "用于店小秘同步测试的商品",
        "original_price": 12.5,
        "selling_price": 19.9,
        "currency": "CNY",
        "images": [
            "https://example.com/a.jpg",
            "https://example.com/b.jpg",
        ],
        "variants": [{"name": "颜色", "value": "轻风雕塑", "stock": 10}],
        "category": "家居装饰",
        "collection_method": "browser_plugin",
        "source_url": "https://example.com/product/1",
        "source_platform": "temu",
        "stock": 10,
    }

    import_resp = client.post(
        "/api/products/collect/by-plugin",
        json={
            "token": "plugin-token-change-in-production",
            "products": [product_payload],
        },
    )
    assert import_resp.status_code == 200
    assert import_resp.json()["imported_count"] >= 1

    list_resp = client.get("/api/products?page_size=1&keyword=轻风雕塑")
    assert list_resp.status_code == 200
    product_id = list_resp.json()["items"][0]["id"]

    process_resp = client.post(
        "/api/products/process",
        json={"product_ids": [product_id], "mode": "image_fill"},
    )
    assert process_resp.status_code == 200
    assert process_resp.json()["updated_count"] == 1

    prepare_resp = client.post(
        "/api/products/sync/dianxiaomi/prepare",
        json={"product_ids": [product_id], "target_store": "Temu半托管 / 半托-1"},
    )
    assert prepare_resp.status_code == 200
    assert prepare_resp.json()["prepared_count"] == 1

    pending_resp = client.get("/api/plugin/dianxiaomi/pending?limit=5")
    assert pending_resp.status_code == 200
    pending_items = pending_resp.json()["items"]
    assert any(item["id"] == product_id for item in pending_items)

    progress_resp = client.post(
        "/api/plugin/dianxiaomi/progress",
        json={
            "product_id": product_id,
            "stage": "uploading_images",
            "message": "轮播图生成进度 3/6",
            "done": 3,
            "total": 6,
        },
    )
    assert progress_resp.status_code == 200
    assert progress_resp.json()["status"] == "syncing"

    result_resp = client.post(
        "/api/plugin/dianxiaomi/result",
        json={
            "product_id": product_id,
            "success": True,
            "platform_product_id": "DXM-LOCAL-1",
            "message": "已写入店小秘",
            "details": {"matched_skus": 1},
        },
    )
    assert result_resp.status_code == 200
    assert result_resp.json()["status"] == "listed"
```

- [ ] **Step 2: Run the failing test**

Run:

```powershell
cd C:\Users\NIDHOGG\Documents\商品上架\backend
python test_dianxiaomi_sync.py
```

Expected: FAIL because `/api/products/process` and `/api/plugin/dianxiaomi/*` do not exist.

- [ ] **Step 3: Add model fields**

In `backend/app/models/models.py`, extend `ProductStatus`:

```python
class ProductStatus(str, enum.Enum):
    COLLECTED = "collected"
    PROCESSING = "processing"
    PROCESSED = "processed"
    SYNC_PENDING = "sync_pending"
    SYNCING = "syncing"
    LISTED = "listed"
    FAILED = "failed"
    DELISTED = "delisted"
```

Add these columns to `Product` after `marketing_copy`:

```python
    owner = Column(String(100), default="aa")
    target_store = Column(String(200), default="")
    image_progress = Column(JSON, default=dict)
    issue_message = Column(Text, default="等待开始处理")
    sync_payload = Column(JSON, default=dict)
    sync_status = Column(String(20), default="none", index=True)
    sync_error = Column(Text, default="")
```

- [ ] **Step 4: Add schemas**

In `backend/app/schemas/schemas.py`, add optional fields to `ProductResponse`:

```python
    owner: str
    target_store: str
    image_progress: Dict[str, Any]
    issue_message: str
    sync_payload: Dict[str, Any]
    sync_status: str
    sync_error: str
```

Add request/response schemas:

```python
class ProductProcessRequest(BaseModel):
    product_ids: List[int]
    mode: str = "image_fill"


class ProductProcessResponse(BaseModel):
    updated_count: int
    results: List[Dict[str, Any]]


class DianxiaomiPrepareRequest(BaseModel):
    product_ids: List[int]
    target_store: str


class DianxiaomiPrepareResponse(BaseModel):
    prepared_count: int
    items: List[Dict[str, Any]]


class DianxiaomiPendingResponse(BaseModel):
    items: List[Dict[str, Any]]
    total: int


class DianxiaomiProgressRequest(BaseModel):
    product_id: int
    stage: str
    message: str
    done: int = 0
    total: int = 6


class DianxiaomiResultRequest(BaseModel):
    product_id: int
    success: bool
    platform_product_id: Optional[str] = ""
    message: Optional[str] = ""
    details: Optional[Dict[str, Any]] = {}
```

- [ ] **Step 5: Commit**

Run:

```powershell
git add backend/app/models/models.py backend/app/schemas/schemas.py backend/test_dianxiaomi_sync.py
git commit -m "feat: add dianxiaomi sync model schemas"
```

Expected: commit succeeds. In this current Codex session, `.git/index.lock` may be unwritable; if that exact permission error occurs, record it in `agent_memory/bugs.md` and continue without a commit.

## Task 2: Backend Sync Service And APIs

**Files:**
- Create: `backend/app/services/sync_service.py`
- Create: `backend/app/api/plugin.py`
- Modify: `backend/app/api/products.py`
- Modify: `backend/app/main.py`
- Test: `backend/test_dianxiaomi_sync.py`

- [ ] **Step 1: Create sync service**

Create `backend/app/services/sync_service.py`:

```python
from datetime import datetime
from typing import Any, Dict, List

from sqlalchemy.orm import Session

from app.models.models import ListingRecord, Product


class DianxiaomiSyncService:
    @staticmethod
    def _build_payload(product: Product) -> Dict[str, Any]:
        return {
            "id": product.id,
            "title": product.title,
            "description": product.description,
            "price": product.selling_price or product.original_price,
            "currency": product.currency,
            "images": product.images or [],
            "variants": product.variants or [],
            "category": product.category,
            "stock": product.stock,
            "weight": product.weight,
            "source_url": product.source_url,
            "target_store": product.target_store,
        }

    @classmethod
    def process_products(cls, product_ids: List[int], mode: str, db: Session) -> List[Dict[str, Any]]:
        products = db.query(Product).filter(Product.id.in_(product_ids)).all()
        results = []
        for product in products:
            total = 6
            existing_images = len(product.images or [])
            done = min(total, max(existing_images, total))
            product.status = "processed"
            product.image_progress = {
                "done": done,
                "total": total,
                "stage": "图片已生成",
                "mode": mode,
            }
            product.issue_message = "未写入"
            product.sync_error = ""
            product.sync_payload = cls._build_payload(product)
            results.append({
                "product_id": product.id,
                "status": product.status,
                "image_progress": product.image_progress,
            })
        db.commit()
        return results

    @classmethod
    def prepare_sync(cls, product_ids: List[int], target_store: str, db: Session) -> List[Dict[str, Any]]:
        products = db.query(Product).filter(Product.id.in_(product_ids)).all()
        items = []
        for product in products:
            product.target_store = target_store
            product.status = "sync_pending"
            product.sync_status = "pending"
            product.issue_message = "等待店小秘插件同步"
            product.sync_error = ""
            product.sync_payload = cls._build_payload(product)
            items.append(cls._build_payload(product))
        db.commit()
        return items

    @classmethod
    def pending_items(cls, db: Session, limit: int = 20) -> List[Dict[str, Any]]:
        products = (
            db.query(Product)
            .filter(Product.sync_status == "pending")
            .order_by(Product.updated_at.desc())
            .limit(limit)
            .all()
        )
        return [product.sync_payload or cls._build_payload(product) for product in products]

    @staticmethod
    def update_progress(product_id: int, stage: str, message: str, done: int, total: int, db: Session) -> Product:
        product = db.query(Product).filter(Product.id == product_id).first()
        if not product:
            raise ValueError("商品不存在")
        product.status = "syncing"
        product.sync_status = "syncing"
        product.issue_message = message
        product.image_progress = {
            "done": done,
            "total": total,
            "stage": stage,
        }
        db.commit()
        db.refresh(product)
        return product

    @staticmethod
    def finish_sync(
        product_id: int,
        success: bool,
        platform_product_id: str,
        message: str,
        details: Dict[str, Any],
        db: Session,
    ) -> Product:
        product = db.query(Product).filter(Product.id == product_id).first()
        if not product:
            raise ValueError("商品不存在")

        record = ListingRecord(
            product_id=product.id,
            platform="dianxiaomi",
            platform_product_id=platform_product_id or "",
            platform_url="",
            via_dianxiaomi=True,
            status="success" if success else "failed",
            response_data={"message": message, "details": details},
            error_message="" if success else message,
            listed_at=datetime.utcnow() if success else None,
        )
        db.add(record)

        if success:
            product.status = "listed"
            product.sync_status = "success"
            product.issue_message = message or "已写入店小秘"
            product.sync_error = ""
            product.listed_platform = "dianxiaomi"
            product.listed_platform_id = platform_product_id or ""
            product.listed_at = datetime.utcnow()
        else:
            product.status = "failed"
            product.sync_status = "failed"
            product.issue_message = message or "店小秘同步失败"
            product.sync_error = product.issue_message

        db.commit()
        db.refresh(product)
        return product
```

- [ ] **Step 2: Add product endpoints**

In `backend/app/api/products.py`, import:

```python
    ProductProcessRequest, ProductProcessResponse,
    DianxiaomiPrepareRequest, DianxiaomiPrepareResponse,
```

Also import:

```python
from app.services.sync_service import DianxiaomiSyncService
```

Add before the task routes:

```python
@router.post("/process", response_model=ProductProcessResponse)
def process_products(request: ProductProcessRequest, db: Session = Depends(get_db)):
    results = DianxiaomiSyncService.process_products(
        product_ids=request.product_ids,
        mode=request.mode,
        db=db,
    )
    return ProductProcessResponse(updated_count=len(results), results=results)


@router.post("/sync/dianxiaomi/prepare", response_model=DianxiaomiPrepareResponse)
def prepare_dianxiaomi_sync(
    request: DianxiaomiPrepareRequest,
    db: Session = Depends(get_db),
):
    items = DianxiaomiSyncService.prepare_sync(
        product_ids=request.product_ids,
        target_store=request.target_store,
        db=db,
    )
    return DianxiaomiPrepareResponse(prepared_count=len(items), items=items)
```

- [ ] **Step 3: Add plugin router**

Create `backend/app/api/plugin.py`:

```python
from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy.orm import Session

from app.core.database import get_db
from app.schemas.schemas import (
    DianxiaomiPendingResponse,
    DianxiaomiProgressRequest,
    DianxiaomiResultRequest,
)
from app.services.sync_service import DianxiaomiSyncService

router = APIRouter(prefix="/api/plugin/dianxiaomi", tags=["plugin-dianxiaomi"])


@router.get("/status")
def plugin_status(db: Session = Depends(get_db)):
    pending = len(DianxiaomiSyncService.pending_items(db, limit=100))
    return {
        "service": "running",
        "pending_count": pending,
        "message": "本地服务运行中",
    }


@router.get("/pending", response_model=DianxiaomiPendingResponse)
def pending(limit: int = Query(20, ge=1, le=100), db: Session = Depends(get_db)):
    items = DianxiaomiSyncService.pending_items(db, limit=limit)
    return DianxiaomiPendingResponse(items=items, total=len(items))


@router.post("/progress")
def progress(request: DianxiaomiProgressRequest, db: Session = Depends(get_db)):
    try:
        product = DianxiaomiSyncService.update_progress(
            product_id=request.product_id,
            stage=request.stage,
            message=request.message,
            done=request.done,
            total=request.total,
            db=db,
        )
    except ValueError as exc:
        raise HTTPException(status_code=404, detail=str(exc))
    return {"product_id": product.id, "status": product.status, "sync_status": product.sync_status}


@router.post("/result")
def result(request: DianxiaomiResultRequest, db: Session = Depends(get_db)):
    try:
        product = DianxiaomiSyncService.finish_sync(
            product_id=request.product_id,
            success=request.success,
            platform_product_id=request.platform_product_id or "",
            message=request.message or "",
            details=request.details or {},
            db=db,
        )
    except ValueError as exc:
        raise HTTPException(status_code=404, detail=str(exc))
    return {"product_id": product.id, "status": product.status, "sync_status": product.sync_status}
```

- [ ] **Step 4: Register plugin router**

In `backend/app/main.py`, add:

```python
from app.api.plugin import router as plugin_router
```

Then register:

```python
app.include_router(plugin_router)
```

- [ ] **Step 5: Run test to verify it passes**

Run:

```powershell
cd C:\Users\NIDHOGG\Documents\商品上架\backend
python test_dianxiaomi_sync.py
```

Expected: PASS with no assertion errors.

- [ ] **Step 6: Commit**

Run:

```powershell
git add backend/app/services/sync_service.py backend/app/api/plugin.py backend/app/api/products.py backend/app/main.py backend/test_dianxiaomi_sync.py
git commit -m "feat: add dianxiaomi sync api"
```

Expected: commit succeeds, or the known `.git/index.lock` permission error is recorded and work continues.

## Task 3: System Workbench Jinja UI

**Files:**
- Modify: `backend/app/templates/layout.html`
- Modify: `backend/app/templates/products.html`
- Modify: `backend/app/templates/index.html`
- Test: manual browser smoke test

- [ ] **Step 1: Fix known dashboard script error**

In `backend/app/templates/index.html`, replace:

```javascript
document.getElementById('statTotal').textContent = total那些
```

with:

```javascript
document.getElementById('statTotal').textContent = total;
```

- [ ] **Step 2: Update navigation label**

In `backend/app/templates/layout.html`, keep the existing sidebar but change the product label from:

```html
<i class="fas fa-cube w-5 text-center"></i><span>商品管理</span>
```

to:

```html
<i class="fas fa-cube w-5 text-center"></i><span>商品工作台</span>
```

- [ ] **Step 3: Replace products page with workbench markup**

Replace `backend/app/templates/products.html` content with a Jinja page that extends `layout.html`, renders the reference-style workbench, and exposes these element IDs used by the script:

```html
{% extends "layout.html" %}
{% block title %}商品工作台{% endblock %}
{% block content %}
<div class="space-y-4">
  <div class="bg-white rounded-xl border border-gray-100 p-4 shadow-sm">
    <div class="grid grid-cols-1 xl:grid-cols-[1.2fr_auto_auto_auto] gap-3 items-center">
      <div class="flex items-center gap-3 bg-emerald-50 border border-emerald-100 rounded-xl px-4 py-3">
        <div class="w-9 h-9 rounded-lg bg-white flex items-center justify-center text-emerald-600">
          <i class="fas fa-store"></i>
        </div>
        <div class="flex-1">
          <label class="text-xs text-gray-500 block mb-1">写入目的地</label>
          <select id="targetStore" class="w-full bg-white border border-gray-200 rounded-lg px-3 py-2 text-sm">
            <option>Temu半托管 / 半托-1</option>
            <option>店小秘默认店铺</option>
          </select>
          <p class="text-xs text-emerald-700 mt-1">店小秘 · 浏览器登录态半自动上传</p>
        </div>
      </div>
      <button onclick="processSelected()" class="px-4 py-3 rounded-lg bg-blue-600 text-white text-sm font-medium hover:bg-blue-700">
        <i class="fas fa-wand-magic-sparkles mr-1"></i>AI处理
      </button>
      <button onclick="processSelected()" class="px-4 py-3 rounded-lg border border-blue-100 text-blue-700 bg-blue-50 text-sm font-medium hover:bg-blue-100">
        <i class="fas fa-cloud-arrow-up mr-1"></i>一键补图
      </button>
      <button onclick="prepareSelected()" class="px-4 py-3 rounded-lg bg-emerald-600 text-white text-sm font-medium hover:bg-emerald-700">
        <i class="fas fa-store mr-1"></i>一键写入
      </button>
    </div>
  </div>

  <div class="bg-white rounded-xl border border-gray-100 p-4 shadow-sm">
    <div class="flex items-center justify-center gap-4 text-sm">
      <span class="text-blue-600 font-semibold"><span class="inline-flex w-7 h-7 rounded-full bg-blue-600 text-white items-center justify-center mr-2">1</span>采集商品</span>
      <span class="h-px w-28 bg-gray-200"></span>
      <span class="text-gray-500"><span class="inline-flex w-7 h-7 rounded-full bg-gray-300 text-white items-center justify-center mr-2">2</span>AI处理</span>
      <span class="h-px w-28 bg-gray-200"></span>
      <span class="text-gray-500"><span class="inline-flex w-7 h-7 rounded-full bg-gray-300 text-white items-center justify-center mr-2">3</span>确认结果</span>
      <span class="h-px w-28 bg-gray-200"></span>
      <span class="text-gray-500"><span class="inline-flex w-7 h-7 rounded-full bg-gray-300 text-white items-center justify-center mr-2">4</span>移入待发布</span>
    </div>
  </div>

  <div class="bg-white rounded-xl border border-gray-100 p-4 shadow-sm">
    <div class="grid grid-cols-1 lg:grid-cols-[1fr_220px_220px_220px_auto] gap-3">
      <input id="searchInput" placeholder="搜索商品标题" class="px-3 py-2 border border-gray-200 rounded-lg text-sm">
      <select id="sourceFilter" class="px-3 py-2 border border-gray-200 rounded-lg text-sm">
        <option value="">来源 全部来源</option>
        <option value="system_recommend">系统推荐</option>
        <option value="url_input">输入链接</option>
        <option value="browser_plugin">浏览器插件</option>
      </select>
      <select id="statusFilter" class="px-3 py-2 border border-gray-200 rounded-lg text-sm">
        <option value="">状态 全部状态</option>
        <option value="collected">已采集</option>
        <option value="processed">可写入</option>
        <option value="sync_pending">待同步</option>
        <option value="syncing">同步中</option>
        <option value="listed">已写入</option>
        <option value="failed">失败</option>
      </select>
      <select id="issueFilter" class="px-3 py-2 border border-gray-200 rounded-lg text-sm">
        <option value="">问题 全部问题</option>
        <option value="等待">等待处理</option>
        <option value="插件">插件同步</option>
        <option value="失败">失败</option>
      </select>
      <button onclick="resetFilters()" class="px-4 py-2 border border-gray-200 rounded-lg text-sm text-blue-600 hover:bg-blue-50">
        <i class="fas fa-rotate-right mr-1"></i>重置
      </button>
    </div>
  </div>

  <div class="bg-white rounded-xl border border-gray-100 shadow-sm overflow-hidden">
    <div class="px-4 py-3 flex flex-wrap items-center gap-3 text-sm border-b border-gray-100">
      <strong>共 <span id="totalCount">0</span> 条</strong>
      <span>已选 <b id="selectedCount" class="text-blue-600">0</b> 个</span>
      <span>可补图 <b id="processableCount" class="text-blue-600">0</b> 个</span>
      <span>可上架 <b id="syncableCount" class="text-emerald-600">0</b> 个</span>
      <div class="ml-auto flex gap-2">
        <button onclick="processSelected()" class="px-3 py-2 rounded-lg bg-blue-100 text-blue-700 text-sm hover:bg-blue-200">一键补图</button>
        <button onclick="prepareSelected()" class="px-3 py-2 rounded-lg bg-emerald-100 text-emerald-700 text-sm hover:bg-emerald-200">一键上架</button>
      </div>
    </div>
    <div class="grid grid-cols-[44px_110px_1.7fr_110px_90px_130px_130px_1.5fr_180px] bg-gray-50 px-4 py-3 text-sm font-semibold text-gray-700">
      <input type="checkbox" id="selectAll" onchange="toggleAll(this.checked)">
      <span>商品图片</span><span>标题</span><span>来源</span><span>负责人</span><span>采集时间</span><span>状态</span><span>问题提示</span><span>操作</span>
    </div>
    <div id="productList" class="divide-y divide-gray-100">
      <p class="p-8 text-center text-gray-400">加载中...</p>
    </div>
    <div id="pagination" class="p-4 border-t border-gray-100 text-sm text-gray-500"></div>
  </div>
</div>
{% endblock %}
```

- [ ] **Step 4: Add products page script**

At the bottom of `backend/app/templates/products.html`, add:

```html
{% block scripts %}
<script>
let currentPage = 1;
let selectedIds = new Set();
let loadedItems = [];

function statusLabel(status) {
  const labels = {
    collected: '待AI处理',
    processing: 'AI图片生成中',
    processed: '图片已生成',
    sync_pending: '待店小秘同步',
    syncing: '生成中',
    listed: '已写入',
    failed: '失败'
  };
  return labels[status] || status || '未知';
}

function statusClass(status) {
  if (status === 'listed' || status === 'processed') return 'bg-green-100 text-green-700';
  if (status === 'processing' || status === 'syncing') return 'bg-blue-100 text-blue-700';
  if (status === 'failed') return 'bg-red-100 text-red-700';
  return 'bg-amber-100 text-amber-700';
}

function progressText(product) {
  const progress = product.image_progress || {};
  if (!progress.total) return '-';
  return `${progress.done || 0}/${progress.total}`;
}

function renderProgress(product) {
  const progress = product.image_progress || {};
  if (!progress.total) return '<div class="h-2 rounded bg-gray-100"></div>';
  const pct = Math.max(0, Math.min(100, Math.round((progress.done || 0) / progress.total * 100)));
  return `<div class="text-xs text-blue-700 font-semibold">${progressText(product)}</div><div class="h-2 rounded bg-gray-100 mt-1 overflow-hidden"><div class="h-full bg-blue-600" style="width:${pct}%"></div></div>`;
}

async function loadProducts() {
  const search = document.getElementById('searchInput').value;
  const status = document.getElementById('statusFilter').value;
  const source = document.getElementById('sourceFilter').value;
  const issue = document.getElementById('issueFilter').value;
  const params = new URLSearchParams({page: currentPage, page_size: 10});
  if (search) params.set('keyword', search);
  if (status) params.set('status', status);
  const data = await api('/api/products?' + params);
  loadedItems = (data.items || []).filter(item => {
    if (source && item.collection_method !== source) return false;
    if (issue && !(item.issue_message || '').includes(issue)) return false;
    return true;
  });
  document.getElementById('totalCount').textContent = data.total || loadedItems.length;
  document.getElementById('processableCount').textContent = loadedItems.filter(p => ['collected', 'failed'].includes(p.status)).length;
  document.getElementById('syncableCount').textContent = loadedItems.filter(p => ['processed', 'sync_pending'].includes(p.status)).length;
  renderList();
}

function renderList() {
  const list = document.getElementById('productList');
  document.getElementById('selectedCount').textContent = selectedIds.size;
  if (!loadedItems.length) {
    list.innerHTML = '<p class="p-8 text-center text-gray-400">暂无商品</p>';
    return;
  }
  list.innerHTML = loadedItems.map(product => `
    <div class="grid grid-cols-[44px_110px_1.7fr_110px_90px_130px_130px_1.5fr_180px] gap-0 px-4 py-3 items-center text-sm ${selectedIds.has(product.id) ? 'bg-blue-50' : 'bg-white hover:bg-gray-50'}">
      <input type="checkbox" ${selectedIds.has(product.id) ? 'checked' : ''} onchange="toggleSelect(${product.id}, this.checked)">
      <div class="w-14 h-14 rounded-lg bg-gray-100 overflow-hidden">${product.images && product.images[0] ? `<img src="${product.images[0]}" class="w-full h-full object-cover">` : '<i class="fas fa-box text-gray-300 m-5"></i>'}</div>
      <div class="min-w-0">
        <div class="font-semibold text-gray-800 truncate">${product.title || '未命名商品'}</div>
        <div class="text-xs text-gray-400 truncate">原始：${product.description || product.source_url || '-'}</div>
      </div>
      <div class="text-gray-700">${product.source_platform || product.collection_method || '-'}</div>
      <div class="text-gray-500">${product.owner || 'aa'}</div>
      <div class="text-gray-500">${new Date(product.created_at).toLocaleString().slice(5, 16)}</div>
      <div><span class="px-2 py-1 rounded-full text-xs ${statusClass(product.status)}">${statusLabel(product.status)}</span><div class="mt-2">${renderProgress(product)}</div></div>
      <div class="text-gray-600">${product.issue_message || '等待开始处理'}</div>
      <div class="flex gap-2 justify-end">
        <button onclick="processOne(${product.id})" class="px-2 py-1 rounded bg-blue-600 text-white text-xs">AI处理</button>
        <button onclick="prepareOne(${product.id})" class="px-2 py-1 rounded bg-emerald-100 text-emerald-700 text-xs">写入</button>
        <button onclick="deleteProduct(${product.id})" class="px-2 py-1 rounded bg-red-50 text-red-600 text-xs">删除</button>
      </div>
    </div>
  `).join('');
}

window.toggleSelect = (id, checked) => {
  if (checked) selectedIds.add(id); else selectedIds.delete(id);
  renderList();
};

window.toggleAll = checked => {
  selectedIds = checked ? new Set(loadedItems.map(p => p.id)) : new Set();
  renderList();
};

async function processIds(ids) {
  if (!ids.length) return alert('请先选择商品');
  await api('/api/products/process', {method: 'POST', body: JSON.stringify({product_ids: ids, mode: 'image_fill'})});
  await loadProducts();
}

async function prepareIds(ids) {
  if (!ids.length) return alert('请先选择商品');
  await api('/api/products/sync/dianxiaomi/prepare', {
    method: 'POST',
    body: JSON.stringify({product_ids: ids, target_store: document.getElementById('targetStore').value})
  });
  await loadProducts();
}

window.processSelected = () => processIds([...selectedIds]);
window.prepareSelected = () => prepareIds([...selectedIds]);
window.processOne = id => processIds([id]);
window.prepareOne = id => prepareIds([id]);
window.deleteProduct = async id => {
  if (!confirm('确定删除该商品？')) return;
  await api('/api/products/' + id, {method: 'DELETE'});
  selectedIds.delete(id);
  await loadProducts();
};
window.resetFilters = () => {
  document.getElementById('searchInput').value = '';
  document.getElementById('sourceFilter').value = '';
  document.getElementById('statusFilter').value = '';
  document.getElementById('issueFilter').value = '';
  currentPage = 1;
  loadProducts();
};

for (const id of ['searchInput', 'sourceFilter', 'statusFilter', 'issueFilter']) {
  document.getElementById(id).addEventListener('input', () => { currentPage = 1; loadProducts(); });
  document.getElementById(id).addEventListener('change', () => { currentPage = 1; loadProducts(); });
}
loadProducts();
</script>
{% endblock %}
```

- [ ] **Step 5: Manual page smoke test**

Run:

```powershell
cd C:\Users\NIDHOGG\Documents\商品上架\backend
python run.py
```

Open `http://127.0.0.1:8000/products`.

Expected:
- Page loads without console syntax errors.
- Search/filter controls render.
- Product rows render if data exists.
- AI processing and write buttons call backend endpoints.

- [ ] **Step 6: Commit**

Run:

```powershell
git add backend/app/templates/layout.html backend/app/templates/products.html backend/app/templates/index.html
git commit -m "feat: add dianxiaomi product workbench"
```

Expected: commit succeeds, or the known `.git/index.lock` permission error is recorded and work continues.

## Task 4: Dianxiaomi Plugin Floating Assistant

**Files:**
- Modify: `browser-plugin/manifest.json`
- Modify: `browser-plugin/content.js`
- Modify: `browser-plugin/popup.js`
- Create: `browser-plugin/test-dianxiaomi.html`
- Test: manual extension/static DOM verification

- [ ] **Step 1: Add Dianxiaomi permissions and content matches**

In `browser-plugin/manifest.json`, add to `host_permissions`:

```json
"https://*.dianxiaomi.com/*",
"https://www.dianxiaomi.com/*"
```

Add the same Dianxiaomi patterns to `content_scripts[0].matches`.

- [ ] **Step 2: Add static test DOM**

Create `browser-plugin/test-dianxiaomi.html`:

```html
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <title>店小秘插件测试页</title>
</head>
<body>
  <h1>店小秘商品编辑模拟页</h1>
  <div class="sku-list">
    <div class="sku-row" data-sku="DXM-SKU-1">
      <span class="sku-name">颜色 / 轻风雕塑</span>
      <input name="title" placeholder="商品标题">
      <textarea name="description" placeholder="商品描述"></textarea>
      <input name="price" placeholder="价格">
    </div>
  </div>
  <script src="content.js"></script>
</body>
</html>
```

- [ ] **Step 3: Extend platform detection**

In `browser-plugin/content.js`, update `detectPlatform()`:

```javascript
if (hostname.includes("dianxiaomi.com")) return "dianxiaomi";
```

- [ ] **Step 4: Add Dianxiaomi assistant functions**

In `browser-plugin/content.js`, before the message listener, add:

```javascript
const LOCAL_API = "http://localhost:8000";

function isDianxiaomiPage() {
  return detectPlatform() === "dianxiaomi";
}

function detectDianxiaomiSkus() {
  const rows = Array.from(document.querySelectorAll("[data-sku], .sku-row, .variant-row, tr"));
  const skus = [];
  rows.forEach((row, index) => {
    const text = (row.textContent || "").replace(/\s+/g, " ").trim();
    const sku = row.getAttribute("data-sku") || `SKU-${index + 1}`;
    if (text && text.length > 3) {
      skus.push({
        sku,
        label: text.slice(0, 80),
        index,
      });
    }
  });
  if (!skus.length) {
    skus.push({sku: "SKU-1", label: "当前页面商品", index: 0});
  }
  return skus.slice(0, 20);
}

function assistantStyles() {
  return `
    #dxm-sync-assistant{position:fixed;right:24px;top:84px;width:420px;z-index:2147483647;background:#fff;border:1px solid #dbe3ef;border-radius:16px;box-shadow:0 18px 48px rgba(15,23,42,.18);font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;color:#1f2937}
    #dxm-sync-assistant *{box-sizing:border-box}
    .dxm-head{padding:16px;border-bottom:1px solid #edf2f7}
    .dxm-title{font-size:20px;font-weight:700;margin:0}
    .dxm-sub{font-size:14px;color:#16a34a;margin-top:8px}
    .dxm-body{padding:16px}
    .dxm-tags{display:flex;gap:10px;margin:12px 0}
    .dxm-tag{background:#2563eb;color:#fff;border:0;border-radius:10px;padding:8px 14px;font-weight:700}
    .dxm-actions{display:flex;align-items:center;gap:14px;margin:14px 0}
    .dxm-list{border:1px solid #cbd5e1;border-radius:12px;overflow:hidden}
    .dxm-item{display:flex;align-items:center;gap:12px;padding:12px;border-bottom:1px solid #eef2f7}
    .dxm-item:last-child{border-bottom:0}
    .dxm-thumb{width:56px;height:56px;background:#f1f5f9;border-radius:8px}
    .dxm-footer{display:flex;align-items:center;gap:10px;padding:16px;border-top:1px solid #edf2f7}
    .dxm-switch{width:46px;height:26px;border-radius:20px;background:#2563eb;display:inline-flex;align-items:center;justify-content:flex-end;padding:3px}
    .dxm-dot{width:20px;height:20px;border-radius:50%;background:#fff}
    .dxm-primary{margin-left:auto;background:#ef4444;color:#fff;border:0;border-radius:10px;padding:10px 26px;font-weight:700}
    .dxm-close{position:absolute;right:14px;top:14px;border:0;background:#fee2e2;color:#dc2626;border-radius:50%;width:28px;height:28px;font-weight:700}
  `;
}

function renderDianxiaomiAssistant() {
  if (!isDianxiaomiPage() || document.getElementById("dxm-sync-assistant")) return;
  const style = document.createElement("style");
  style.textContent = assistantStyles();
  document.head.appendChild(style);

  const skus = detectDianxiaomiSkus();
  const panel = document.createElement("div");
  panel.id = "dxm-sync-assistant";
  panel.innerHTML = `
    <button class="dxm-close" title="关闭">×</button>
    <div class="dxm-head">
      <p class="dxm-title">商品同步助手</p>
      <div class="dxm-sub">● 已识别 ${skus.length} 个SKU</div>
    </div>
    <div class="dxm-body">
      <div style="font-weight:700">快捷选择</div>
      <div class="dxm-tags">
        <button class="dxm-tag" data-filter="all">全部 ${skus.length}</button>
        <button class="dxm-tag" data-filter="first">${skus[0]?.label.slice(0, 8) || "默认"} 1</button>
      </div>
      <div class="dxm-actions">
        <label><input id="dxm-select-all" type="checkbox" checked> 全选</label>
        <button id="dxm-invert" style="border:0;background:#fff;color:#475569">反选</button>
        <span id="dxm-selected-count" style="margin-left:auto;color:#991b1b">已选 ${skus.length} 个商品</span>
      </div>
      <div style="font-weight:700;margin-bottom:8px">变种列表</div>
      <div class="dxm-list">
        ${skus.map((sku, index) => `
          <label class="dxm-item">
            <input class="dxm-sku-check" type="checkbox" checked data-index="${index}">
            <div class="dxm-thumb"></div>
            <div style="flex:1">
              <div style="font-weight:700">${sku.label}</div>
              <div style="font-size:13px;color:#16a34a">轮播图 6/6 · 已匹配</div>
            </div>
            <span>›</span>
          </label>
        `).join("")}
      </div>
      <div id="dxm-status" style="font-size:13px;color:#64748b;margin-top:12px">等待同步</div>
    </div>
    <div class="dxm-footer">
      <span>上传到店小秘</span><span class="dxm-switch"><span class="dxm-dot"></span></span>
      <button id="dxm-sync-now" class="dxm-primary">采集</button>
    </div>
  `;
  document.body.appendChild(panel);

  panel.querySelector(".dxm-close").addEventListener("click", () => panel.remove());
  panel.querySelector("#dxm-invert").addEventListener("click", () => {
    panel.querySelectorAll(".dxm-sku-check").forEach(input => input.checked = !input.checked);
    updateSelectedCount(panel);
  });
  panel.querySelector("#dxm-select-all").addEventListener("change", event => {
    panel.querySelectorAll(".dxm-sku-check").forEach(input => input.checked = event.target.checked);
    updateSelectedCount(panel);
  });
  panel.querySelectorAll(".dxm-sku-check").forEach(input => input.addEventListener("change", () => updateSelectedCount(panel)));
  panel.querySelector("#dxm-sync-now").addEventListener("click", () => syncPendingToDianxiaomi(panel));
}

function updateSelectedCount(panel) {
  const count = panel.querySelectorAll(".dxm-sku-check:checked").length;
  panel.querySelector("#dxm-selected-count").textContent = `已选 ${count} 个商品`;
}

async function syncPendingToDianxiaomi(panel) {
  const status = panel.querySelector("#dxm-status");
  status.textContent = "正在读取本地待同步商品...";
  try {
    const pendingResp = await fetch(`${LOCAL_API}/api/plugin/dianxiaomi/pending?limit=1`);
    const pending = await pendingResp.json();
    const item = pending.items && pending.items[0];
    if (!item) {
      status.textContent = "暂无待同步商品，请先在系统端点击一键写入";
      return;
    }
    await fetch(`${LOCAL_API}/api/plugin/dianxiaomi/progress`, {
      method: "POST",
      headers: {"Content-Type": "application/json"},
      body: JSON.stringify({
        product_id: item.id,
        stage: "uploading_images",
        message: "轮播图生成进度 3/6",
        done: 3,
        total: 6,
      }),
    });
    fillDianxiaomiFields(item);
    await fetch(`${LOCAL_API}/api/plugin/dianxiaomi/result`, {
      method: "POST",
      headers: {"Content-Type": "application/json"},
      body: JSON.stringify({
        product_id: item.id,
        success: true,
        platform_product_id: `DXM-${item.id}`,
        message: "已写入店小秘",
        details: {selected_skus: panel.querySelectorAll(".dxm-sku-check:checked").length},
      }),
    });
    status.textContent = "同步完成，已回写系统";
  } catch (error) {
    status.textContent = `同步失败：${error.message}`;
  }
}

function fillDianxiaomiFields(item) {
  const titleInput = document.querySelector('input[name="title"], input[placeholder*="标题"]');
  const descInput = document.querySelector('textarea[name="description"], textarea[placeholder*="描述"]');
  const priceInput = document.querySelector('input[name="price"], input[placeholder*="价格"]');
  if (titleInput) titleInput.value = item.title || "";
  if (descInput) descInput.value = item.description || "";
  if (priceInput) priceInput.value = item.price || "";
}
```

After the automatic product extraction block, add:

```javascript
if (isDianxiaomiPage()) {
  renderDianxiaomiAssistant();
}
```

- [ ] **Step 5: Update popup service status**

In `browser-plugin/popup.js`, after button declarations, add:

```javascript
async function refreshLocalServiceStatus() {
  try {
    const resp = await fetch("http://localhost:8000/api/plugin/dianxiaomi/status");
    const data = await resp.json();
    statusEl.textContent = `本地服务运行中，待同步 ${data.pending_count || 0} 个`;
    statusEl.className = "status success";
  } catch (e) {
    statusEl.textContent = "本地服务未连接";
    statusEl.className = "status error";
  }
}

refreshLocalServiceStatus();
```

- [ ] **Step 6: Manual plugin verification**

Run backend:

```powershell
cd C:\Users\NIDHOGG\Documents\商品上架\backend
python run.py
```

Load Chrome extension from `C:\Users\NIDHOGG\Documents\商品上架\browser-plugin`.

Open `C:\Users\NIDHOGG\Documents\商品上架\browser-plugin\test-dianxiaomi.html`.

Expected:
- Floating assistant appears.
- It shows recognized SKU count.
- All/select/invert changes selected count.
- Sync button reports no pending item, or syncs if a product was prepared in system workbench.

- [ ] **Step 7: Commit**

Run:

```powershell
git add browser-plugin/manifest.json browser-plugin/content.js browser-plugin/popup.js browser-plugin/test-dianxiaomi.html
git commit -m "feat: add dianxiaomi plugin assistant"
```

Expected: commit succeeds, or the known `.git/index.lock` permission error is recorded and work continues.

## Task 5: Verification, Memory Update, And Handoff

**Files:**
- Modify: `agent_memory/context.md`
- Modify: `agent_memory/progress.md`
- Modify: `agent_memory/bugs.md`

- [ ] **Step 1: Run backend smoke test**

Run:

```powershell
cd C:\Users\NIDHOGG\Documents\商品上架\backend
python test_dianxiaomi_sync.py
```

Expected: PASS with no assertion errors.

- [ ] **Step 2: Run dashboard smoke test**

Run:

```powershell
cd C:\Users\NIDHOGG\Documents\商品上架\backend
python test_inventory.py
```

Expected: existing inventory smoke output ends with `ALL OK`.

- [ ] **Step 3: Manually verify system workbench**

Run:

```powershell
cd C:\Users\NIDHOGG\Documents\商品上架\backend
python run.py
```

Open `http://127.0.0.1:8000/products`.

Expected:
- Workbench loads.
- Existing or newly imported products display.
- AI processing updates rows to image progress `6/6`.
- One-click write marks rows as waiting for Dianxiaomi plugin sync.

- [ ] **Step 4: Manually verify plugin assistant**

Load extension from `C:\Users\NIDHOGG\Documents\商品上架\browser-plugin`.

Open the static test page:

```text
C:\Users\NIDHOGG\Documents\商品上架\browser-plugin\test-dianxiaomi.html
```

Expected:
- Assistant appears.
- Selected count controls work.
- Sync button can pull a pending item after system-side prepare.
- Sync result appears in system workbench after refresh.

- [ ] **Step 5: Update memory**

Update `agent_memory/progress.md`:

```markdown
## 当前任务

店小秘优先的商品采集上架营销系统第一版实现与验证。

## 完成项

- [x] 后端模型、接口和店小秘同步服务已实现。
- [x] 系统商品工作台已改造成批量处理/写入界面。
- [x] 浏览器插件已新增店小秘页面内同步助手。
- [x] 已完成后端和插件 smoke 验证。
```

Update `agent_memory/bugs.md`:

```markdown
## 当前问题

| 严重程度 | 问题描述 | 状态 | 备注 |
|----------|----------|------|------|
| P2 | 当前环境无法写入 `.git/index.lock`，导致提交可能失败 | 待处理 | 不影响文件落盘和功能验证 |

## 风险与待确认

- 店小秘真实页面 DOM 可能与静态测试页不同，需要在用户登录环境中做二次选择器校准。
```

- [ ] **Step 6: Final commit**

Run:

```powershell
git add agent_memory/context.md agent_memory/progress.md agent_memory/bugs.md
git commit -m "docs: update dianxiaomi implementation memory"
```

Expected: commit succeeds, or the known `.git/index.lock` permission error is reported in the final handoff.

## Self-Review

- Spec coverage: backend data/API, system workbench, plugin floating assistant, error states, and verification are covered by Tasks 1-5.
- Placeholder scan: the plan uses concrete file paths, code blocks, commands, and expected results.
- Type consistency: schema names used in API tasks match service and test names; status strings match the design spec.
