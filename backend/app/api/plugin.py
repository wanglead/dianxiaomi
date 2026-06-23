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
    return {"service": "running", "pending_count": pending, "message": "本地服务运行中"}


@router.get("/pending", response_model=DianxiaomiPendingResponse)
def pending(limit: int = Query(20, ge=1, le=100), db: Session = Depends(get_db)):
    items = DianxiaomiSyncService.pending_items(db, limit=limit)
    return DianxiaomiPendingResponse(items=items, total=len(items))


@router.post("/progress")
def progress(request: DianxiaomiProgressRequest, db: Session = Depends(get_db)):
    try:
        product = DianxiaomiSyncService.update_progress(
            request.product_id, request.stage, request.message,
            request.done, request.total, db,
        )
    except ValueError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    return {"product_id": product.id, "status": product.status, "sync_status": product.sync_status}


@router.post("/result")
def result(request: DianxiaomiResultRequest, db: Session = Depends(get_db)):
    try:
        product = DianxiaomiSyncService.finish_sync(
            request.product_id, request.success,
            request.platform_product_id or "", request.message or "",
            request.details or {}, db,
        )
    except ValueError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc
    return {"product_id": product.id, "status": product.status, "sync_status": product.sync_status}
