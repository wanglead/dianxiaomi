import datetime
from sqlalchemy import (
    Column, Integer, String, Text, Float, Boolean, 
    DateTime, ForeignKey, JSON, Enum as SAEnum, Index
)
from sqlalchemy.orm import relationship
from app.core.database import Base
import enum


class CollectionMethod(str, enum.Enum):
    SYSTEM_RECOMMEND = "system_recommend"  # 系统推荐
    URL_INPUT = "url_input"                # 输入链接
    BROWSER_PLUGIN = "browser_plugin"      # 浏览器插件


class ProductStatus(str, enum.Enum):
    COLLECTED = "collected"       # 已采集
    EDITED = "edited"             # 已编辑
    PROCESSING = "processing"     # 处理中
    PROCESSED = "processed"       # 已处理
    SYNC_PENDING = "sync_pending" # 待同步
    SYNCING = "syncing"           # 同步中
    LISTED = "listed"             # 已上架
    FAILED = "failed"             # 上架失败
    DELISTED = "delisted"         # 已下架


class PlatformName(str, enum.Enum):
    ALIEXPRESS = "aliexpress"  # 速卖通
    OZON = "ozon"              # Ozon
    JOOM = "joom"              # Joom
    SHOPIFY = "shopify"        # Shopify
    DIANXIAOMI = "dianxiaomi"  # 店小秘


class Product(Base):
    __tablename__ = "products"

    id = Column(Integer, primary_key=True, autoincrement=True)
    title = Column(String(500), nullable=False, index=True)
    description = Column(Text, default="")
    original_price = Column(Float, default=0.0)
    selling_price = Column(Float, default=0.0)
    currency = Column(String(10), default="CNY")
    
    # 商品图片（JSON 数组）
    images = Column(JSON, default=list)
    # 商品变体（如颜色、尺码）
    variants = Column(JSON, default=list)
    # 商品分类
    category = Column(String(200), default="")
    category_id = Column(String(100), default="")
    
    # 采集来源信息
    collection_method = Column(String(50), nullable=False)
    source_url = Column(Text, default="")        # 来源链接
    source_platform = Column(String(50), default="")  # 来源平台
    
    # 库存
    stock = Column(Integer, default=0)
    weight = Column(Float, default=0.0)
    
    # 营销字段
    keywords = Column(JSON, default=list)        # SEO 关键词
    marketing_copy = Column(Text, default="")    # 营销文案

    # 店小秘同步信息
    owner = Column(String(100), default="aa")
    target_store = Column(String(200), default="")
    image_progress = Column(JSON, default=dict)
    issue_message = Column(Text, default="等待开始处理")
    sync_payload = Column(JSON, default=dict)
    sync_status = Column(String(20), default="none", index=True)
    sync_error = Column(Text, default="")
    
    # 状态
    status = Column(String(20), default=ProductStatus.COLLECTED.value, index=True)
    
    # 上架信息
    listed_platform = Column(String(50), default="")
    listed_platform_id = Column(String(200), default="")  # 平台商品ID
    listed_at = Column(DateTime, nullable=True)
    
    created_at = Column(DateTime, default=datetime.datetime.utcnow, index=True)
    updated_at = Column(DateTime, default=datetime.datetime.utcnow, onupdate=datetime.datetime.utcnow)
    
    # 关联
    collection_records = relationship("CollectionRecord", back_populates="product", cascade="all, delete-orphan")
    listing_records = relationship("ListingRecord", back_populates="product", cascade="all, delete-orphan")
    
    __table_args__ = (
        Index("idx_product_status_platform", "status", "listed_platform"),
    )


class CollectionRecord(Base):
    """采集记录"""
    __tablename__ = "collection_records"
    
    id = Column(Integer, primary_key=True, autoincrement=True)
    product_id = Column(Integer, ForeignKey("products.id"), nullable=False)
    method = Column(String(50), nullable=False)  # 采集方式
    source_info = Column(JSON, default=dict)     # 采集源信息
    raw_data = Column(JSON, default=dict)        # 原始采集数据
    success = Column(Boolean, default=True)
    error_message = Column(Text, default="")
    collected_at = Column(DateTime, default=datetime.datetime.utcnow)
    
    product = relationship("Product", back_populates="collection_records")


class ListingRecord(Base):
    """上架记录"""
    __tablename__ = "listing_records"
    
    id = Column(Integer, primary_key=True, autoincrement=True)
    product_id = Column(Integer, ForeignKey("products.id"), nullable=False)
    platform = Column(String(50), nullable=False)  # 目标平台
    platform_product_id = Column(String(200), default="")
    platform_url = Column(Text, default="")
    via_dianxiaomi = Column(Boolean, default=False)  # 是否通过店小秘上架
    status = Column(String(20), default="pending")   # pending/success/failed
    response_data = Column(JSON, default=dict)
    error_message = Column(Text, default="")
    listed_at = Column(DateTime, nullable=True)
    created_at = Column(DateTime, default=datetime.datetime.utcnow)
    
    product = relationship("Product", back_populates="listing_records")


class PlatformAccount(Base):
    """平台账号配置"""
    __tablename__ = "platform_accounts"
    
    id = Column(Integer, primary_key=True, autoincrement=True)
    platform = Column(String(50), nullable=False)  # 平台名称
    account_name = Column(String(200), nullable=False)
    api_key = Column(String(500), default="")
    api_secret = Column(String(500), default="")
    store_name = Column(String(200), default="")
    is_active = Column(Boolean, default=True)
    extra_config = Column(JSON, default=dict)
    created_at = Column(DateTime, default=datetime.datetime.utcnow)
    updated_at = Column(DateTime, default=datetime.datetime.utcnow, onupdate=datetime.datetime.utcnow)


class CollectionTask(Base):
    """批量采集任务"""
    __tablename__ = "collection_tasks"
    
    id = Column(Integer, primary_key=True, autoincrement=True)
    task_name = Column(String(200), nullable=False)
    method = Column(String(50), nullable=False)
    urls = Column(JSON, default=list)   # 待采集URL列表
    total = Column(Integer, default=0)
    completed = Column(Integer, default=0)
    failed = Column(Integer, default=0)
    status = Column(String(20), default="pending")  # pending/running/completed/failed
    created_at = Column(DateTime, default=datetime.datetime.utcnow)
    completed_at = Column(DateTime, nullable=True)



class InventoryRecord(Base):
    """库存变动记录"""
    __tablename__ = "inventory_records"
    
    id = Column(Integer, primary_key=True, autoincrement=True)
    product_id = Column(Integer, ForeignKey("products.id"), nullable=False, index=True)
    change_type = Column(String(20), nullable=False)  # inbound/outbound/adjust/return
    quantity = Column(Integer, nullable=False)  # 正数=入库，负数=出库
    before_stock = Column(Integer, default=0)
    after_stock = Column(Integer, default=0)
    remark = Column(String(500), default="")
    operator = Column(String(100), default="system")
    created_at = Column(DateTime, default=datetime.datetime.utcnow)
    
    product = relationship("Product", backref="inventory_records")


class InventoryAlert(Base):
    """库存预警配置"""
    __tablename__ = "inventory_alerts"
    
    id = Column(Integer, primary_key=True, autoincrement=True)
    product_id = Column(Integer, ForeignKey("products.id"), nullable=False, index=True)
    min_stock = Column(Integer, default=10)  # 最低库存预警
    max_stock = Column(Integer, default=0)   # 最高库存预警（0=不限制）
    enabled = Column(Boolean, default=True)
    notify_method = Column(String(50), default="")  # email/sms 等
    created_at = Column(DateTime, default=datetime.datetime.utcnow)
    updated_at = Column(DateTime, default=datetime.datetime.utcnow, onupdate=datetime.datetime.utcnow)
    
    product = relationship("Product", backref="inventory_alert")

class SystemRecommendConfig(Base):
    """系统推荐配置"""
    __tablename__ = "system_recommend_configs"
    
    id = Column(Integer, primary_key=True, autoincrement=True)
    name = Column(String(200), nullable=False)
    source_platform = Column(String(50), nullable=False)  # 推荐来源平台
    category = Column(String(200), default="")
    keywords = Column(String(500), default="")
    min_price = Column(Float, default=0)
    max_price = Column(Float, default=0)
    is_active = Column(Boolean, default=True)
    schedule = Column(String(100), default="")  # cron 表达式
    last_run = Column(DateTime, nullable=True)
    created_at = Column(DateTime, default=datetime.datetime.utcnow)

