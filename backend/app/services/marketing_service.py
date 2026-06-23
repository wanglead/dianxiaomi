import logging
from typing import List, Optional
from sqlalchemy.orm import Session

from app.models.models import Product

logger = logging.getLogger(__name__)


class MarketingService:
    """营销优化服务"""

    @staticmethod
    def generate_marketing(
        product: Product,
        style: str = "professional",
        target_platform: Optional[str] = None,
    ) -> dict:
        """生成营销文案和关键词"""
        
        # 基础关键词生成
        keywords = MarketingService._generate_keywords(product)
        
        # 营销文案生成
        marketing_copy = MarketingService._generate_copy(product, style, target_platform)
        
        # 标题优化
        optimized_title = MarketingService._optimize_title(product, keywords)
        
        return {
            "marketing_copy": marketing_copy,
            "keywords": keywords,
            "optimized_title": optimized_title,
        }

    @staticmethod
    def _generate_keywords(product: Product) -> List[str]:
        """基于商品信息生成 SEO 关键词"""
        title_words = product.title.split()
        
        # 从标题提取关键词（中文分词 + 英文单词）
        keywords = []
        for word in title_words:
            word = word.strip(" ,.!?（）()【】[]《》")
            if word and len(word) > 1:
                keywords.append(word)
        
        # 添加分类相关词
        if product.category:
            cat_parts = product.category.replace("/", " ").split()
            for part in cat_parts:
                if part not in keywords:
                    keywords.append(part)
        
        # 添加价格相关词
        if product.selling_price:
            price_range = "budget" if product.selling_price < 20 else \
                          "mid-range" if product.selling_price < 100 else "premium"
            keywords.append(price_range)
        
        return keywords[:20]  # 限制20个

    @staticmethod
    def _generate_copy(
        product: Product,
        style: str,
        target_platform: Optional[str] = None,
    ) -> str:
        """生成营销文案"""
        platform_note = f" for {target_platform}" if target_platform else ""
        
        templates = {
            "professional": f"""【Product Highlights】
• {product.title}
• High quality material, durable and long-lasting
• Perfect for daily use and special occasions
• Competitive pricing at great value

【Product Description】
{product.description or 'Premium quality product designed to meet your needs.'}

【Shipping & Returns】
• Fast shipping worldwide
• 30-day money-back guarantee
• Customer support available 24/7""",

            "social": f"""✨ JUST IN: {product.title} ✨

🔥 Don't miss out on this amazing find!
💰 Price: {product.currency} {product.selling_price or product.original_price:.2f}

👉 Perfect for:
• Daily essentials
• Gift giving
• Personal use

⚡ Limited stock available!
#shopping #musthave #newarrival""",

            "concise": f"""{product.title}

{product.description or 'Premium quality product.'}

Price: {product.currency} {product.selling_price or product.original_price:.2f}
Stock: {product.stock} units

Fast shipping & easy returns.""",
        }

        return templates.get(style, templates["professional"])

    @staticmethod
    def _optimize_title(product: Product, keywords: List[str]) -> str:
        """优化商品标题（添加关键词）"""
        title = product.title
        
        # 如果标题太短，追加关键词
        if len(title) < 30 and keywords:
            extra = " | ".join(keywords[:3])
            title = f"{title} - {extra}"
        
        # 如果标题太长，截断
        if len(title) > 200:
            title = title[:197] + "..."
        
        return title

    @staticmethod
    def calculate_optimal_price(
        original_price: float,
        platform: str = "",
        margin_percent: float = 1.3,
    ) -> dict:
        """计算最优定价"""
        import math
        
        # 各平台费率参考
        platform_fees = {
            "aliexpress": 0.08,  # 8% 佣金
            "ozon": 0.10,        # 10%
            "joom": 0.15,        # 15%
            "shopify": 0.02,     # 2%（交易费）
        }
        
        fee_rate = platform_fees.get(platform, 0.05)
        
        # 计算建议售价
        cost = original_price
        suggested_price = cost * margin_percent / (1 - fee_rate)
        
        # 取整到 .99
        suggested_price = math.floor(suggested_price * 100) / 100 + 0.99
        
        return {
            "original_price": original_price,
            "suggested_price": round(suggested_price, 2),
            "platform_fee_rate": fee_rate,
            "estimated_profit": round(suggested_price * (1 - fee_rate) - cost, 2),
            "margin": margin_percent,
        }
