from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from contextlib import asynccontextmanager

from app.core.database import init_db
from app.api.products import router as products_router
from app.api.listing import router as listing_router
from app.api.marketing import router as marketing_router
from app.api.inventory import router as inventory_router
from app.api.plugin import router as plugin_router
from app.pages import router as pages_router


@asynccontextmanager
async def lifespan(app: FastAPI):
    """应用启动/关闭生命周期"""
    init_db()
    import webbrowser
    webbrowser.open('http://127.0.0.1:8000/')
    yield


app = FastAPI(
    title="商品采集上架营销系统",
    description="支持多平台商品采集、上架、营销优化的综合管理系统",
    version="1.0.0",
    lifespan=lifespan,
)

# CORS 配置 - 允许前端跨域请求
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # 生产环境应限制具体域名
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# 注册路由
app.include_router(products_router)
app.include_router(listing_router)
app.include_router(marketing_router)
app.include_router(inventory_router)
app.include_router(plugin_router)
app.include_router(pages_router)


@app.get("/")
def root():
    return {
        "name": "商品采集上架营销系统",
        "version": "1.0.0",
        "docs": "/docs",
    }


@app.get("/health")
def health():
    return {"status": "ok"}



