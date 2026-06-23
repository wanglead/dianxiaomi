import httpx
import logging
import json
from typing import List, Dict, Any, Optional
from sqlalchemy.orm import Session
from datetime import datetime

from app.models.models import Product, ListingRecord, PlatformAccount

logger = logging.getLogger(__name__)


class ListingService:
    """商品上架服务"""

    @staticmethod
    async def list_products(
        product_ids: List[int],
        platform: str,
        via_dianxiaomi: bool,
        account_id: Optional[int],
        db: Session,
    ) -> List[Dict[str, Any]]:
        """批量上架商品"""
        products = db.query(Product).filter(Product.id.in_(product_ids)).all()
        results = []

        for product in products:
            try:
                if via_dianxiaomi:
                    result = await ListingService._list_via_dianxiaomi(product, db)
                else:
                    result = await ListingService._list_directly(
                        product, platform, account_id, db
                    )

                # 保存上架记录
                record = ListingRecord(
                    product_id=product.id,
                    platform=platform,
                    platform_product_id=result.get("platform_product_id", ""),
                    platform_url=result.get("platform_url", ""),
                    via_dianxiaomi=via_dianxiaomi,
                    status=result.get("status", "failed"),
                    response_data=result,
                )
                db.add(record)

                if result.get("status") == "success":
                    product.status = "listed"
                    product.listed_platform = platform
                    product.listed_platform_id = result.get("platform_product_id", "")
                    product.listed_at = datetime.utcnow()

                results.append({
                    "product_id": product.id,
                    "title": product.title,
                    "status": result.get("status", "failed"),
                    "platform_product_id": result.get("platform_product_id", ""),
                    "platform_url": result.get("platform_url", ""),
                    "error": result.get("error", ""),
                })

            except Exception as e:
                logger.exception(f"Error listing product {product.id}: {e}")
                results.append({
                    "product_id": product.id,
                    "title": product.title,
                    "status": "failed",
                    "error": str(e),
                })

            db.commit()

        return results

    @staticmethod
    async def _list_via_dianxiaomi(
        product: Product, db: Session
    ) -> Dict[str, Any]:
        """通过店小秘平台上传商品"""
        from app.core.config import settings

        if not settings.DIANXIAOMI_API_KEY:
            return {
                "status": "failed",
                "error": "店小秘 API 未配置，请在 .env 中设置 DIANXIAOMI_API_KEY",
            }

        dianxiaomi_url = f"{settings.DIANXIAOMI_BASE_URL}/api/v1/product/create"

        # 构造店小秘 API 所需的数据格式
        payload = {
            "api_key": settings.DIANXIAOMI_API_KEY,
            "product": {
                "title": product.title,
                "description": product.description,
                "images": product.images,
                "price": product.selling_price or product.original_price,
                "currency": product.currency,
                "category": product.category,
                "variants": product.variants,
                "stock": product.stock,
                "weight": product.weight,
                "source_url": product.source_url,
            },
        }

        async with httpx.AsyncClient(timeout=60) as client:
            try:
                resp = await client.post(dianxiaomi_url, json=payload)
                data = resp.json()

                if resp.status_code == 200 and data.get("success"):
                    return {
                        "status": "success",
                        "platform_product_id": data.get("data", {}).get("product_id", ""),
                        "platform_url": data.get("data", {}).get("url", ""),
                    }
                else:
                    return {
                        "status": "failed",
                        "error": data.get("message", "店小秘 API 返回错误"),
                    }

            except httpx.HTTPError as e:
                logger.error(f"Dianxiaomi API error: {e}")
                return {"status": "failed", "error": f"店小秘接口请求失败: {str(e)}"}

    @staticmethod
    async def _list_directly(
        product: Product,
        platform: str,
        account_id: Optional[int],
        db: Session,
    ) -> Dict[str, Any]:
        """直接上架到销售平台"""
        # 获取平台账号配置
        account = None
        if account_id:
            account = db.query(PlatformAccount).filter_by(
                id=account_id, platform=platform, is_active=True
            ).first()

        if not account:
            # 使用默认配置
            from app.core.config import settings
            platform_config = PLATFORM_CONFIGS.get(platform, {})
            if not platform_config.get("api_key"):
                return {
                    "status": "failed",
                    "error": f"平台 {platform} 未配置 API 信息",
                }

        # 根据平台选择不同的上架逻辑
        listing_methods = {
            "aliexpress": ListingService._list_to_aliexpress,
            "ozon": ListingService._list_to_ozon,
            "joom": ListingService._list_to_joom,
            "shopify": ListingService._list_to_shopify,
        }

        method = listing_methods.get(platform)
        if not method:
            return {"status": "failed", "error": f"不支持的平台: {platform}"}

        return await method(product, account)

    @staticmethod
    async def _list_to_aliexpress(
        product: Product, account: Optional[PlatformAccount]
    ) -> Dict[str, Any]:
        """上架到速卖通"""
        # 速卖通 API 对接
        # API 文档: https://developers.aliexpress.com/
        from app.core.config import settings

        config = PLATFORM_CONFIGS.get("aliexpress", {})
        if account:
            config = {
                "app_key": account.api_key,
                "app_secret": account.api_secret,
            }

        if not config.get("app_key"):
            return {"status": "failed", "error": "速卖通未配置 API Key"}

        # 构建速卖通商品数据
        product_data = {
            "subject": product.title[:128],  # 速卖通标题限制
            "detail": product.description,
            "image_urls": ",".join(product.images[:6]),  # 最多6张主图
            "product_price": str(product.selling_price or product.original_price),
            "product_stock": str(product.stock or 1),
            "category_id": product.category_id or "0",
        }

        # 调用速卖通开放平台 API
        aliexpress_api_url = "https://api.aliexpress.com/rest"
        if config.get("api_base_url"):
            aliexpress_api_url = config["api_base_url"]

        async with httpx.AsyncClient(timeout=60) as client:
            try:
                resp = await client.post(
                    f"{aliexpress_api_url}/product/create",
                    json=product_data,
                    headers={
                        "Content-Type": "application/json",
                    },
                )
                data = resp.json()
                if resp.status_code == 200 and data.get("success"):
                    return {
                        "status": "success",
                        "platform_product_id": data.get("product_id", ""),
                        "platform_url": f"https://www.aliexpress.com/item/{data.get('product_id', '')}.html",
                    }
                return {"status": "failed", "error": str(data)}
            except Exception as e:
                return {"status": "failed", "error": str(e)}

    @staticmethod
    async def _list_to_ozon(
        product: Product, account: Optional[PlatformAccount]
    ) -> Dict[str, Any]:
        """上架到 Ozon"""
        from app.core.config import settings
        config = PLATFORM_CONFIGS.get("ozon", {})
        if account:
            config = {
                "api_key": account.api_key,
                "client_id": account.store_name,
            }

        api_key = config.get("api_key", "")
        client_id = config.get("client_id", "")

        if not api_key:
            return {"status": "failed", "error": "Ozon 未配置 API Key"}

        # Ozon API v3 上架商品
        ozon_api_url = "https://api.ozon.ru/v3/product/create"

        payload = {
            "name": product.title,
            "description": product.description,
            "price": str(product.selling_price or product.original_price),
            "currency_code": "RUB",
            "images": [{"url": img} for img in product.images[:10]],
            "stock": str(product.stock or 1),
            "category_id": product.category_id or "",
        }

        async with httpx.AsyncClient(timeout=60) as client:
            try:
                resp = await client.post(
                    ozon_api_url,
                    json=payload,
                    headers={
                        "Api-Key": api_key,
                        "Client-Id": client_id,
                        "Content-Type": "application/json",
                    },
                )
                data = resp.json()
                if resp.status_code == 200 and data.get("id"):
                    return {
                        "status": "success",
                        "platform_product_id": str(data.get("id")),
                        "platform_url": f"https://www.ozon.ru/product/{data.get('id')}",
                    }
                return {"status": "failed", "error": str(data)}
            except Exception as e:
                return {"status": "failed", "error": str(e)}

    @staticmethod
    async def _list_to_joom(
        product: Product, account: Optional[PlatformAccount]
    ) -> Dict[str, Any]:
        """上架到 Joom"""
        from app.core.config import settings
        config = PLATFORM_CONFIGS.get("joom", {})
        if account:
            config = {"api_key": account.api_key}

        api_key = config.get("api_key", "")
        if not api_key:
            return {"status": "failed", "error": "Joom 未配置 API Key"}

        joom_api_url = "https://api.joom.com/v1/products"

        payload = {
            "name": product.title,
            "description": product.description,
            "price": {"amount": str(product.selling_price or product.original_price), "currency": "USD"},
            "images": [{"url": img, "is_default": i == 0} for i, img in enumerate(product.images[:10])],
            "inventory": [{"sku": f"AUTO-{product.id}", "quantity": product.stock or 1}],
        }

        async with httpx.AsyncClient(timeout=60) as client:
            try:
                resp = await client.post(
                    joom_api_url,
                    json=payload,
                    headers={"Authorization": f"Bearer {api_key}"},
                )
                data = resp.json()
                if resp.status_code in (200, 201):
                    return {
                        "status": "success",
                        "platform_product_id": str(data.get("id", "")),
                        "platform_url": f"https://www.joom.com/products/{data.get('id', '')}",
                    }
                return {"status": "failed", "error": str(data)}
            except Exception as e:
                return {"status": "failed", "error": str(e)}

    @staticmethod
    async def _list_to_shopify(
        product: Product, account: Optional[PlatformAccount]
    ) -> Dict[str, Any]:
        """上架到 Shopify"""
        from app.core.config import settings
        config = PLATFORM_CONFIGS.get("shopify", {})
        if account:
            config = {
                "store_name": account.store_name,
                "api_key": account.api_key,
                "password": account.api_secret,
            }

        store_name = config.get("store_name", "")
        api_key = config.get("api_key", "")
        password = config.get("password", "")

        if not store_name:
            return {"status": "failed", "error": "Shopify 未配置 Store Name"}

        # Shopify Admin API (REST)
        shopify_url = f"https://{api_key}:{password}@{store_name}.myshopify.com/admin/api/2024-01/products.json"
        if not password and api_key:
            shopify_url = f"https://{store_name}.myshopify.com/admin/api/2024-01/products.json"

        # Shopify GraphQL 也可以
        payload = {
            "product": {
                "title": product.title,
                "body_html": product.description,
                "images": [{"src": img} for img in product.images[:10]],
                "variants": [{
                    "price": str(product.selling_price or product.original_price),
                    "sku": f"AUTO-{product.id}",
                    "inventory_quantity": product.stock or 1,
                }],
                "status": "draft",
            }
        }

        async with httpx.AsyncClient(timeout=60) as client:
            try:
                headers = {"Content-Type": "application/json"}
                if password:
                    import base64
                    token = base64.b64encode(f"{api_key}:{password}".encode()).decode()
                    headers["Authorization"] = f"Basic {token}"

                resp = await client.post(shopify_url, json=payload, headers=headers)
                data = resp.json()

                if resp.status_code in (200, 201):
                    shopify_product = data.get("product", {})
                    return {
                        "status": "success",
                        "platform_product_id": str(shopify_product.get("id", "")),
                        "platform_url": f"https://{store_name}.myshopify.com/admin/products/{shopify_product.get('id', '')}",
                    }
                return {"status": "failed", "error": str(data)}
            except Exception as e:
                return {"status": "failed", "error": str(e)}

