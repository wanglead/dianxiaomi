from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.orm import Session
from typing import List

from app.core.database import get_db
from app.models.models import Product, PlatformAccount
from app.schemas.schemas import (
    ListingRequest, ListingResponse,
    PlatformAccountCreate, PlatformAccountResponse,
)
from app.services.listing_service import ListingService
from app.services.marketing_service import MarketingService

router = APIRouter(prefix="/api/listing", tags=["listing"])


@router.post("/list", response_model=ListingResponse)
async def list_products(
    request: ListingRequest, db: Session = Depends(get_db)
):
    """上架商品到平台"""
    results = await ListingService.list_products(
        product_ids=request.product_ids,
        platform=request.platform,
        via_dianxiaomi=request.via_dianxiaomi,
        account_id=request.account_id,
        db=db,
    )
    return ListingResponse(results=results)


# ===== 平台账号管理 =====

@router.get("/accounts", response_model=List[PlatformAccountResponse])
def list_accounts(
    platform: str = None, db: Session = Depends(get_db)
):
    """平台账号列表"""
    query = db.query(PlatformAccount)
    if platform:
        query = query.filter(PlatformAccount.platform == platform)
    accounts = query.all()
    return [PlatformAccountResponse.model_validate(a) for a in accounts]


@router.post("/accounts", response_model=PlatformAccountResponse)
def create_account(
    data: PlatformAccountCreate, db: Session = Depends(get_db)
):
    """添加平台账号"""
    account = PlatformAccount(**data.model_dump())
    db.add(account)
    db.commit()
    db.refresh(account)
    return PlatformAccountResponse.model_validate(account)


@router.delete("/accounts/{account_id}")
def delete_account(account_id: int, db: Session = Depends(get_db)):
    """删除平台账号"""
    account = db.query(PlatformAccount).filter(
        PlatformAccount.id == account_id
    ).first()
    if not account:
        raise HTTPException(status_code=404, detail="账号不存在")
    db.delete(account)
    db.commit()
    return {"message": "删除成功"}
