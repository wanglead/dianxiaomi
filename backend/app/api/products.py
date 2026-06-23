from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy.orm import Session
from typing import List, Optional

from app.core.database import get_db
from app.models.models import Product, CollectionTask
from app.schemas.schemas import (
    ProductResponse, ProductListResponse, ProductCreate, ProductUpdate,
    CollectByUrlRequest, CollectByUrlResponse, PluginCollectRequest,
    PluginCollectResponse, SystemRecommendRequest,
    TaskResponse, ProductProcessRequest, ProductProcessResponse,
    DianxiaomiPrepareRequest, DianxiaomiPrepareResponse,
)
from app.services.collection_service import CollectionService
from app.services.sync_service import DianxiaomiSyncService

router = APIRouter(prefix="/api/products", tags=["products"])


@router.post("/process", response_model=ProductProcessResponse)
def process_products(request: ProductProcessRequest, db: Session = Depends(get_db)):
    results = DianxiaomiSyncService.process_products(
        request.product_ids, request.mode, db
    )
    return ProductProcessResponse(updated_count=len(results), results=results)


@router.post("/sync/dianxiaomi/prepare", response_model=DianxiaomiPrepareResponse)
def prepare_dianxiaomi_sync(
    request: DianxiaomiPrepareRequest, db: Session = Depends(get_db)
):
    items = DianxiaomiSyncService.prepare_sync(
        request.product_ids, request.target_store, db
    )
    return DianxiaomiPrepareResponse(prepared_count=len(items), items=items)


@router.get("", response_model=ProductListResponse)
def list_products(
    page: int = Query(1, ge=1),
    page_size: int = Query(20, ge=1, le=100),
    status: Optional[str] = None,
    platform: Optional[str] = None,
    keyword: Optional[str] = None,
    db: Session = Depends(get_db),
):
    """商品列表"""
    query = db.query(Product)

    if status:
        query = query.filter(Product.status == status)
    if platform:
        query = query.filter(Product.listed_platform == platform)
    if keyword:
        query = query.filter(Product.title.contains(keyword))

    total = query.count()
    items = query.order_by(Product.created_at.desc()).offset(
        (page - 1) * page_size
    ).limit(page_size).all()

    return ProductListResponse(
        items=[ProductResponse.model_validate(p) for p in items],
        total=total,
        page=page,
        page_size=page_size,
    )


@router.get("/{product_id}", response_model=ProductResponse)
def get_product(product_id: int, db: Session = Depends(get_db)):
    """获取商品详情"""
    product = db.query(Product).filter(Product.id == product_id).first()
    if not product:
        raise HTTPException(status_code=404, detail="商品不存在")
    return ProductResponse.model_validate(product)


@router.put("/{product_id}", response_model=ProductResponse)
def update_product(
    product_id: int, data: ProductUpdate, db: Session = Depends(get_db)
):
    """编辑商品"""
    product = db.query(Product).filter(Product.id == product_id).first()
    if not product:
        raise HTTPException(status_code=404, detail="商品不存在")

    update_data = data.model_dump(exclude_unset=True)
    for key, value in update_data.items():
        setattr(product, key, value)

    product.status = "edited"
    db.commit()
    db.refresh(product)
    return ProductResponse.model_validate(product)


@router.delete("/{product_id}")
def delete_product(product_id: int, db: Session = Depends(get_db)):
    """删除商品"""
    product = db.query(Product).filter(Product.id == product_id).first()
    if not product:
        raise HTTPException(status_code=404, detail="商品不存在")
    db.delete(product)
    db.commit()
    return {"message": "删除成功"}


# ===== 采集相关 =====

@router.post("/collect/by-url", response_model=CollectByUrlResponse)
async def collect_by_url(
    request: CollectByUrlRequest, db: Session = Depends(get_db)
):
    """通过链接采集商品"""
    task = await CollectionService.collect_by_urls(
        request.urls, request.source_platform, db
    )
    return CollectByUrlResponse(
        task_id=task.id,
        message=f"开始采集 {len(request.urls)} 个商品",
        urls_count=len(request.urls),
    )


@router.post("/collect/by-plugin", response_model=PluginCollectResponse)
async def collect_by_plugin(
    request: PluginCollectRequest, db: Session = Depends(get_db)
):
    """浏览器插件采集数据导入"""
    from app.core.config import settings

    if request.token != settings.PLUGIN_AUTH_TOKEN:
        raise HTTPException(status_code=403, detail="无效的插件认证令牌")

    imported, errors = CollectionService.import_from_plugin(
        request.products, db
    )
    return PluginCollectResponse(
        success=len(errors) == 0,
        imported_count=len(imported),
        errors=errors,
    )


@router.post("/collect/recommend")
async def system_recommend(
    request: SystemRecommendRequest, db: Session = Depends(get_db)
):
    """系统推荐采集"""
    items = await CollectionService.system_recommend(
        source_platform=request.source_platform,
        category=request.category,
        keywords=request.keywords,
        min_price=request.min_price,
        max_price=request.max_price,
        limit=request.limit,
    )
    return {"items": items, "total": len(items)}


@router.post("/collect/recommend/import")
async def import_recommendations(
    items: List[dict], db: Session = Depends(get_db)
):
    """将推荐的商品导入系统"""
    imported = []
    for item in items:
        product = Product(
            title=item.get("title", ""),
            description=item.get("description", ""),
            original_price=float(item.get("original_price", 0)),
            images=item.get("images", []),
            source_url=item.get("source_url", ""),
            source_platform=item.get("source_platform", ""),
            collection_method="system_recommend",
            status="collected",
        )
        db.add(product)
        db.flush()
        imported.append({"id": product.id, "title": product.title})

    db.commit()
    return {"imported": imported, "count": len(imported)}


# ===== 采集任务 =====

@router.get("/tasks", response_model=List[TaskResponse])
def list_tasks(db: Session = Depends(get_db)):
    """采集任务列表"""
    tasks = db.query(CollectionTask).order_by(
        CollectionTask.created_at.desc()
    ).limit(50).all()
    return [TaskResponse.model_validate(t) for t in tasks]
