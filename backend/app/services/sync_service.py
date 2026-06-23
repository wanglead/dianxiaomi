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
    def process_products(
        cls, product_ids: List[int], mode: str, db: Session
    ) -> List[Dict[str, Any]]:
        products = db.query(Product).filter(Product.id.in_(product_ids)).all()
        results = []
        for product in products:
            total = 6
            product.status = "processed"
            product.image_progress = {
                "done": total,
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
    def prepare_sync(
        cls, product_ids: List[int], target_store: str, db: Session
    ) -> List[Dict[str, Any]]:
        products = db.query(Product).filter(Product.id.in_(product_ids)).all()
        items = []
        for product in products:
            product.target_store = target_store
            product.status = "sync_pending"
            product.sync_status = "pending"
            product.issue_message = "等待店小秘插件同步"
            product.sync_error = ""
            product.sync_payload = cls._build_payload(product)
            items.append(product.sync_payload)
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
    def update_progress(
        product_id: int,
        stage: str,
        message: str,
        done: int,
        total: int,
        db: Session,
    ) -> Product:
        product = db.query(Product).filter(Product.id == product_id).first()
        if not product:
            raise ValueError("商品不存在")
        product.status = "syncing"
        product.sync_status = "syncing"
        product.issue_message = message
        product.image_progress = {"done": done, "total": total, "stage": stage}
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

        now = datetime.utcnow()
        db.add(ListingRecord(
            product_id=product.id,
            platform="dianxiaomi",
            platform_product_id=platform_product_id or "",
            platform_url="",
            via_dianxiaomi=True,
            status="success" if success else "failed",
            response_data={"message": message, "details": details},
            error_message="" if success else message,
            listed_at=now if success else None,
        ))

        if success:
            product.status = "listed"
            product.sync_status = "success"
            product.issue_message = message or "已写入店小秘"
            product.sync_error = ""
            product.listed_platform = "dianxiaomi"
            product.listed_platform_id = platform_product_id or ""
            product.listed_at = now
        else:
            product.status = "failed"
            product.sync_status = "failed"
            product.issue_message = message or "店小秘同步失败"
            product.sync_error = product.issue_message

        db.commit()
        db.refresh(product)
        return product
