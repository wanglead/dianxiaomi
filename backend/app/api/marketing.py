from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.orm import Session

from app.core.database import get_db
from app.models.models import Product
from app.schemas.schemas import (
    MarketingGenerateRequest, MarketingGenerateResponse,
)
from app.services.marketing_service import MarketingService

router = APIRouter(prefix="/api/marketing", tags=["marketing"])


@router.post("/generate", response_model=MarketingGenerateResponse)
def generate_marketing(
    request: MarketingGenerateRequest, db: Session = Depends(get_db)
):
    """生成营销文案和关键词"""
    product = db.query(Product).filter(
        Product.id == request.product_id
    ).first()
    if not product:
        raise HTTPException(status_code=404, detail="商品不存在")

    result = MarketingService.generate_marketing(
        product=product,
        style=request.style,
        target_platform=request.target_platform,
    )

    # 保存到商品
    product.marketing_copy = result["marketing_copy"]
    product.keywords = result["keywords"]
    db.commit()

    return MarketingGenerateResponse(
        marketing_copy=result["marketing_copy"],
        keywords=result["keywords"],
        optimized_title=result["optimized_title"],
    )


@router.post("/price-suggestion")
def price_suggestion(
    product_id: int,
    platform: str = "",
    margin: float = 1.3,
    db: Session = Depends(get_db),
):
    """获取定价建议"""
    product = db.query(Product).filter(
        Product.id == product_id
    ).first()
    if not product:
        raise HTTPException(status_code=404, detail="商品不存在")

    result = MarketingService.calculate_optimal_price(
        original_price=product.original_price,
        platform=platform,
        margin_percent=margin,
    )
    return result
