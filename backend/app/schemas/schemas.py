from pydantic import BaseModel, Field
from typing import Optional, List, Dict, Any
from datetime import datetime


# ===== Product Schemas =====

class ProductVariant(BaseModel):
    name: str              # 如 "颜色", "尺码"
    value: str             # 如 "红色", "XL"
    price: Optional[float] = None
    stock: Optional[int] = None
    image: Optional[str] = None


class ProductCreate(BaseModel):
    title: str = Field(..., max_length=500)
    description: Optional[str] = ""
    original_price: Optional[float] = 0.0
    selling_price: Optional[float] = 0.0
    currency: Optional[str] = "CNY"
    images: Optional[List[str]] = []
    variants: Optional[List[Dict[str, Any]]] = []
    category: Optional[str] = ""
    category_id: Optional[str] = ""
    collection_method: str
    source_url: Optional[str] = ""
    source_platform: Optional[str] = ""
    stock: Optional[int] = 0
    weight: Optional[float] = 0.0
    keywords: Optional[List[str]] = []
    marketing_copy: Optional[str] = ""


class ProductUpdate(BaseModel):
    title: Optional[str] = None
    description: Optional[str] = None
    selling_price: Optional[float] = None
    currency: Optional[str] = None
    images: Optional[List[str]] = None
    variants: Optional[List[Dict[str, Any]]] = None
    category: Optional[str] = None
    category_id: Optional[str] = None
    stock: Optional[int] = None
    weight: Optional[float] = None
    keywords: Optional[List[str]] = None
    marketing_copy: Optional[str] = None


class ProductResponse(BaseModel):
    id: int
    title: str
    description: str
    original_price: float
    selling_price: float
    currency: str
    images: List[str]
    variants: List[Dict[str, Any]]
    category: str
    category_id: str
    collection_method: str
    source_url: str
    source_platform: str
    stock: int
    weight: float
    keywords: List[str]
    marketing_copy: str
    owner: str
    target_store: str
    image_progress: Dict[str, Any]
    issue_message: str
    sync_payload: Dict[str, Any]
    sync_status: str
    sync_error: str
    status: str
    listed_platform: str
    listed_platform_id: str
    listed_at: Optional[datetime] = None
    created_at: datetime
    updated_at: datetime

    class Config:
        from_attributes = True


class ProductListResponse(BaseModel):
    items: List[ProductResponse]
    total: int
    page: int
    page_size: int


class ProductProcessRequest(BaseModel):
    product_ids: List[int]
    mode: str = "image_fill"


class ProductProcessResponse(BaseModel):
    updated_count: int
    results: List[Dict[str, Any]]


class DianxiaomiPrepareRequest(BaseModel):
    product_ids: List[int]
    target_store: str


class DianxiaomiPrepareResponse(BaseModel):
    prepared_count: int
    items: List[Dict[str, Any]]


class DianxiaomiPendingResponse(BaseModel):
    items: List[Dict[str, Any]]
    total: int


class DianxiaomiProgressRequest(BaseModel):
    product_id: int
    stage: str
    message: str
    done: int = 0
    total: int = 6


class DianxiaomiResultRequest(BaseModel):
    product_id: int
    success: bool
    platform_product_id: Optional[str] = ""
    message: Optional[str] = ""
    details: Optional[Dict[str, Any]] = Field(default_factory=dict)


# ===== Collection Schemas =====

class CollectByUrlRequest(BaseModel):
    urls: List[str] = Field(..., description="商品链接列表")
    source_platform: Optional[str] = ""


class CollectByUrlResponse(BaseModel):
    task_id: int
    message: str
    urls_count: int


class PluginCollectRequest(BaseModel):
    token: str
    products: List[Dict[str, Any]] = Field(..., description="浏览器插件采集的商品数据")
    plugin_version: Optional[str] = "1.0.0"


class PluginCollectResponse(BaseModel):
    success: bool
    imported_count: int
    errors: List[str]


class SystemRecommendRequest(BaseModel):
    source_platform: str
    category: Optional[str] = ""
    keywords: Optional[str] = ""
    min_price: Optional[float] = 0
    max_price: Optional[float] = 0
    limit: Optional[int] = 20


# ===== Listing Schemas =====

class ListingRequest(BaseModel):
    product_ids: List[int]
    platform: str
    via_dianxiaomi: bool = False
    account_id: Optional[int] = None


class ListingResponse(BaseModel):
    task_id: Optional[int] = None
    results: List[Dict[str, Any]]


# ===== Platform Account Schemas =====

class PlatformAccountCreate(BaseModel):
    platform: str
    account_name: str
    api_key: Optional[str] = ""
    api_secret: Optional[str] = ""
    store_name: Optional[str] = ""
    extra_config: Optional[Dict[str, Any]] = {}


class PlatformAccountResponse(BaseModel):
    id: int
    platform: str
    account_name: str
    api_key: str
    api_secret: str
    store_name: str
    is_active: bool
    extra_config: dict
    created_at: datetime

    class Config:
        from_attributes = True


# ===== Marketing Schemas =====

class MarketingGenerateRequest(BaseModel):
    product_id: int
    style: Optional[str] = "professional"  # professional / social / concise
    target_platform: Optional[str] = ""


class MarketingGenerateResponse(BaseModel):
    marketing_copy: str
    keywords: List[str]
    optimized_title: Optional[str] = None


# ===== Task Schemas =====

class TaskResponse(BaseModel):
    id: int
    task_name: str
    method: str
    urls: List[str]
    total: int
    completed: int
    failed: int
    status: str
    created_at: datetime

    class Config:
        from_attributes = True
