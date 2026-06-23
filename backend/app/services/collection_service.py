from datetime import datetime
import httpx
import re
import logging
from typing import List, Dict, Any, Optional
from bs4 import BeautifulSoup
from urllib.parse import urlparse
from sqlalchemy.orm import Session

from app.models.models import Product, CollectionRecord, CollectionTask
from app.schemas.schemas import ProductCreate

logger = logging.getLogger(__name__)


class CollectionService:
    """商品采集服务"""

    PLATFORM_PATTERNS = {
        "aliexpress": [
            r"(?:www\.)?aliexpress\.(?:com|ru)",
            r"(?:www\.)?aliexpress\.us",
        ],
        "amazon": [
            r"(?:www\.)?amazon\.[a-z.]+",
        ],
        "taobao": [
            r"(?:www\.)?taobao\.com",
            r"(?:www\.)?tmall\.com",
        ],
        "1688": [
            r"(?:www\.)?1688\.com",
        ],
        "shopify": [
            r"(?:[\w-]+\.)?myshopify\.com",
        ],
        "ozon": [
            r"(?:www\.)?ozon\.[a-z.]+",
        ],
        "joom": [
            r"(?:www\.)?joom\.com",
        ],
    }

    @staticmethod
    def detect_platform(url: str) -> str:
        """从 URL 检测来源平台"""
        parsed = urlparse(url)
        domain = parsed.netloc.lower()
        for platform, patterns in CollectionService.PLATFORM_PATTERNS.items():
            for pattern in patterns:
                if re.search(pattern, domain):
                    return platform
        return "unknown"

    @staticmethod
    async def scrape_url(url: str) -> Dict[str, Any]:
        """通用 URL 采集 - 抓取页面基本信息"""
        platform = CollectionService.detect_platform(url)
        logger.info(f"Scraping URL: {url}, detected platform: {platform}")

        headers = {
            "User-Agent": (
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                "AppleWebKit/537.36 (KHTML, like Gecko) "
                "Chrome/120.0.0.0 Safari/537.36"
            ),
            "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.8",
        }

        async with httpx.AsyncClient(follow_redirects=True, timeout=30) as client:
            try:
                resp = await client.get(url, headers=headers)
                resp.raise_for_status()
                soup = BeautifulSoup(resp.text, "lxml")

                # 提取基本信息
                title = ""
                if soup.title:
                    title = soup.title.get_text(strip=True)

                # 尝试提取 meta 信息
                description = ""
                meta_desc = soup.find("meta", attrs={"name": "description"})
                if meta_desc and meta_desc.get("content"):
                    description = meta_desc["content"]

                # 提取图片
                images = []
                for img in soup.find_all("img", limit=20):
                    src = img.get("src") or img.get("data-src", "")
                    if src and not src.startswith("data:"):
                        if src.startswith("//"):
                            src = "https:" + src
                        elif src.startswith("/"):
                            src = f"{parsed.scheme}://{parsed.netloc}{src}"
                        images.append(src)

                # 提取价格（通用尝试）
                price = 0.0
                price_patterns = [
                    r'["\']?price["\']?\s*[:=]\s*["\']?([\d.]+)',
                    r'¥\s*([\d.,]+)',
                    r'\$\s*([\d.,]+)',
                    r'€\s*([\d.,]+)',
                ]
                for pattern in price_patterns:
                    match = re.search(pattern, resp.text[:5000])
                    if match:
                        try:
                            price = float(match.group(1).replace(",", ""))
                            break
                        except ValueError:
                            continue

                return {
                    "title": title,
                    "description": description,
                    "images": images[:10],  # 最多10张
                    "original_price": price,
                    "source_url": url,
                    "source_platform": platform,
                    "raw_html_snippet": resp.text[:2000],  # 保存原始片段供后续解析
                }

            except httpx.HTTPError as e:
                logger.error(f"HTTP error scraping {url}: {e}")
                return {"error": str(e), "source_url": url, "source_platform": platform}

    @staticmethod
    async def scrape_aliexpress(url: str) -> Dict[str, Any]:
        """速卖通专用采集器"""
        basic = await CollectionService.scrape_url(url)
        if "error" in basic:
            return basic

        # 速卖通特定的解析逻辑可以在这里扩展
        # 例如通过 API 接口、JSON-LD 等提取更详细的数据
        async with httpx.AsyncClient(follow_redirects=True, timeout=30) as client:
            try:
                resp = await client.get(url)
                soup = BeautifulSoup(resp.text, "lxml")

                # 尝试提取 JSON-LD 结构化数据
                scripts = soup.find_all("script", type="application/ld+json")
                for script in scripts:
                    try:
                        import json
                        data = json.loads(script.string)
                        if isinstance(data, dict):
                            if data.get("name"):
                                basic["title"] = data["name"]
                            if data.get("description"):
                                basic["description"] = data["description"]
                            if data.get("image"):
                                imgs = data["image"]
                                if isinstance(imgs, list):
                                    basic["images"] = imgs
                                else:
                                    basic["images"] = [imgs]
                            if data.get("offers"):
                                offers = data["offers"]
                                if isinstance(offers, dict):
                                    basic["original_price"] = float(
                                        offers.get("price", basic["original_price"])
                                    )
                    except (json.JSONDecodeError, AttributeError):
                        continue

            except Exception as e:
                logger.warning(f"Aliexpress specific scrape failed: {e}")

        return basic

    @staticmethod
    def get_scraper_for_platform(platform: str):
        """根据平台获取专用采集器"""
        scrapers = {
            "aliexpress": CollectionService.scrape_aliexpress,
        }
        return scrapers.get(platform, CollectionService.scrape_url)

    @classmethod
    async def collect_by_urls(
        cls, urls: List[str], source_platform: str, db: Session
    ) -> CollectionTask:
        """批量 URL 采集入库"""
        task = CollectionTask(
            task_name=f"URL采集 - {len(urls)} 个商品",
            method="url_input",
            urls=urls,
            total=len(urls),
            status="running",
        )
        db.add(task)
        db.commit()
        db.refresh(task)

        for url in urls:
            try:
                platform = cls.detect_platform(url)
                scraper = cls.get_scraper_for_platform(platform)
                data = await scraper(url)

                if "error" in data:
                    task.failed += 1
                    logger.warning(f"Failed to scrape {url}: {data['error']}")
                else:
                    product = Product(
                        title=data.get("title", "未命名商品"),
                        description=data.get("description", ""),
                        original_price=data.get("original_price", 0.0),
                        images=data.get("images", []),
                        source_url=url,
                        source_platform=data.get("source_platform", platform),
                        collection_method="url_input",
                        status="collected",
                    )
                    db.add(product)
                    db.flush()

                    record = CollectionRecord(
                        product_id=product.id,
                        method="url_input",
                        source_info={"url": url, "detected_platform": platform},
                        raw_data=data,
                    )
                    db.add(record)
                    task.completed += 1

            except Exception as e:
                task.failed += 1
                logger.exception(f"Error collecting {url}: {e}")

            db.commit()

        task.status = "completed"
        task.completed_at = datetime.utcnow()
        db.commit()
        db.refresh(task)
        return task

    @staticmethod
    def import_from_plugin(
        products_data: List[Dict[str, Any]], db: Session
    ) -> tuple[List[Product], List[str]]:
        """导入浏览器插件采集的数据"""
        imported = []
        errors = []

        for idx, item in enumerate(products_data):
            try:
                product = Product(
                    title=item.get("title", "未命名商品"),
                    description=item.get("description", ""),
                    original_price=float(item.get("original_price", 0)),
                    selling_price=float(item.get("selling_price", 0)),
                    currency=item.get("currency", "CNY"),
                    images=item.get("images", []),
                    variants=item.get("variants", []),
                    category=item.get("category", ""),
                    category_id=item.get("category_id", ""),
                    source_url=item.get("source_url", ""),
                    source_platform=item.get("source_platform", ""),
                    stock=int(item.get("stock", 0)),
                    weight=float(item.get("weight", 0)),
                    collection_method="browser_plugin",
                    status="collected",
                )
                db.add(product)
                db.flush()

                record = CollectionRecord(
                    product_id=product.id,
                    method="browser_plugin",
                    source_info={"plugin_data": item},
                    raw_data=item,
                )
                db.add(record)
                imported.append(product)

            except Exception as e:
                errors.append(f"Item #{idx}: {e}")
                logger.exception(f"Plugin import error at item #{idx}")

        db.commit()
        return imported, errors

    @staticmethod
    async def system_recommend(
        source_platform: str,
        category: str = "",
        keywords: str = "",
        min_price: float = 0,
        max_price: float = 0,
        limit: int = 20,
    ) -> List[Dict[str, Any]]:
        """系统推荐商品（通过平台 API 或热销榜单）"""
        recommended = []

        if source_platform == "aliexpress":
            # 速卖通热销推荐逻辑
            # 可通过速卖通开放平台 API 获取热销商品
            recommended = await CollectionService._recommend_aliexpress(
                category, keywords, limit
            )
        elif source_platform == "1688":
            recommended = await CollectionService._recommend_1688(
                category, keywords, limit
            )

        # 价格过滤
        if min_price > 0 or max_price > 0:
            filtered = []
            for item in recommended:
                price = float(item.get("price", 0) or 0)
                if min_price > 0 and price < min_price:
                    continue
                if max_price > 0 and price > max_price:
                    continue
                filtered.append(item)
            recommended = filtered[:limit]

        return recommended

    @staticmethod
    async def _recommend_aliexpress(
        category: str = "", keywords: str = "", limit: int = 20
    ) -> List[Dict[str, Any]]:
        """速卖通推荐算法（模拟 - 实际应接入速卖通联盟API或开放平台）"""
        import json

        # 模拟推荐数据
        # 实际应调用速卖通开放平台 /aliexpress/marketplace/recommend
        mock_items = []
        for i in range(min(limit, 10)):
            mock_items.append({
                "title": f"速卖通热销商品 {i+1} - {keywords or category or '热门'}",
                "description": "速卖通平台推荐商品描述",
                "original_price": 9.99 + i * 5,
                "currency": "USD",
                "images": [f"https://ae01.alicdn.com/kf/example_{i}.jpg"],
                "source_platform": "aliexpress",
                "source_url": f"https://www.aliexpress.com/item/100500{i}.html",
                "category": category or "Home & Garden",
                "sales_count": 1000 + i * 200,
                "rating": 4.5 + (i % 5) * 0.1,
            })
        return mock_items

    @staticmethod
    async def _recommend_1688(
        category: str = "", keywords: str = "", limit: int = 20
    ) -> List[Dict[str, Any]]:
        """1688推荐算法"""
        mock_items = []
        for i in range(min(limit, 10)):
            mock_items.append({
                "title": f"1688热销商品 {i+1} - {keywords or category or '爆款'}",
                "description": "1688平台批发商品描述",
                "original_price": 5.0 + i * 3,
                "currency": "CNY",
                "images": [f"https://cbu01.alicdn.com/img/example_{i}.jpg"],
                "source_platform": "1688",
                "source_url": f"https://detail.1688.com/offer/{i}.html",
                "category": category or "日用百货",
                "sales_count": 5000 + i * 500,
            })
        return mock_items



