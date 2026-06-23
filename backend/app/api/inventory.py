from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy.orm import Session
from typing import List, Optional
from datetime import datetime

from app.core.database import get_db
from app.models.models import Product, InventoryRecord, InventoryAlert

router = APIRouter(prefix="/api/inventory", tags=["inventory"])


# ===== 库存变动记录 =====

@router.get("/records")
def list_inventory_records(
    product_id: Optional[int] = None,
    limit: int = Query(50, le=200),
    db: Session = Depends(get_db),
):
    query = db.query(InventoryRecord).order_by(InventoryRecord.created_at.desc())
    if product_id:
        query = query.filter(InventoryRecord.product_id == product_id)
    records = query.limit(limit).all()
    return {
        "records": [
            {
                "id": r.id,
                "product_id": r.product_id,
                "change_type": r.change_type,
                "quantity": r.quantity,
                "before_stock": r.before_stock,
                "after_stock": r.after_stock,
                "remark": r.remark,
                "operator": r.operator,
                "created_at": r.created_at.isoformat() if r.created_at else None,
                "product_title": r.product.title if r.product else "",
            }
            for r in records
        ]
    }


@router.post("/inbound")
def inbound_stock(
    product_id: int,
    quantity: int = Query(..., ge=1),
    remark: str = "",
    operator: str = "system",
    db: Session = Depends(get_db),
):
    product = db.query(Product).filter(Product.id == product_id).first()
    if not product:
        raise HTTPException(status_code=404, detail="商品不存在")
    
    before = product.stock or 0
    after = before + quantity
    product.stock = after
    
    record = InventoryRecord(
        product_id=product_id,
        change_type="inbound",
        quantity=quantity,
        before_stock=before,
        after_stock=after,
        remark=remark,
        operator=operator,
    )
    db.add(record)
    db.commit()
    return {"success": True, "product_id": product_id, "before_stock": before, "after_stock": after, "change": f"+{quantity}"}


@router.post("/outbound")
def outbound_stock(
    product_id: int,
    quantity: int = Query(..., ge=1),
    remark: str = "",
    operator: str = "system",
    db: Session = Depends(get_db),
):
    product = db.query(Product).filter(Product.id == product_id).first()
    if not product:
        raise HTTPException(status_code=404, detail="商品不存在")
    
    before = product.stock or 0
    if before < quantity:
        raise HTTPException(status_code=400, detail=f"库存不足：当前 {before}，需要 {quantity}")
    
    after = before - quantity
    product.stock = after
    
    record = InventoryRecord(
        product_id=product_id,
        change_type="outbound",
        quantity=-quantity,
        before_stock=before,
        after_stock=after,
        remark=remark,
        operator=operator,
    )
    db.add(record)
    db.commit()
    return {"success": True, "product_id": product_id, "before_stock": before, "after_stock": after, "change": f"-{quantity}"}


@router.post("/adjust")
def adjust_stock(
    product_id: int,
    new_stock: int = Query(..., ge=0),
    remark: str = "",
    operator: str = "system",
    db: Session = Depends(get_db),
):
    product = db.query(Product).filter(Product.id == product_id).first()
    if not product:
        raise HTTPException(status_code=404, detail="商品不存在")
    
    before = product.stock or 0
    diff = new_stock - before
    product.stock = new_stock
    
    record = InventoryRecord(
        product_id=product_id,
        change_type="adjust",
        quantity=diff,
        before_stock=before,
        after_stock=new_stock,
        remark=remark,
        operator=operator,
    )
    db.add(record)
    db.commit()
    return {"success": True, "product_id": product_id, "before_stock": before, "after_stock": new_stock, "change": f"{'+' if diff >= 0 else ''}{diff}"}


# ===== 库存预警 =====

@router.get("/alerts")
def list_alerts(db: Session = Depends(get_db)):
    alerts = db.query(InventoryAlert).filter(InventoryAlert.enabled == True).all()
    results = []
    for alert in alerts:
        product = alert.product
        stock = product.stock if product else 0
        triggered = alert.min_stock > 0 and stock <= alert.min_stock
        if alert.max_stock > 0 and stock >= alert.max_stock:
            triggered = True
        results.append({
            "id": alert.id,
            "product_id": alert.product_id,
            "product_title": product.title if product else "",
            "current_stock": stock,
            "min_stock": alert.min_stock,
            "max_stock": alert.max_stock,
            "triggered": triggered,
            "enabled": alert.enabled,
        })
    return {"alerts": results}


@router.post("/alerts")
def set_alert(
    product_id: int,
    min_stock: int = 10,
    max_stock: int = 0,
    enabled: bool = True,
    db: Session = Depends(get_db),
):
    existing = db.query(InventoryAlert).filter(InventoryAlert.product_id == product_id).first()
    if existing:
        existing.min_stock = min_stock
        existing.max_stock = max_stock
        existing.enabled = enabled
    else:
        alert = InventoryAlert(product_id=product_id, min_stock=min_stock, max_stock=max_stock, enabled=enabled)
        db.add(alert)
    db.commit()
    return {"success": True, "product_id": product_id, "min_stock": min_stock, "max_stock": max_stock}


@router.delete("/alerts/{alert_id}")
def delete_alert(alert_id: int, db: Session = Depends(get_db)):
    alert = db.query(InventoryAlert).filter(InventoryAlert.id == alert_id).first()
    if not alert:
        raise HTTPException(status_code=404, detail="预警配置不存在")
    db.delete(alert)
    db.commit()
    return {"message": "删除成功"}
