from fastapi import APIRouter, Request
from fastapi.responses import HTMLResponse
from fastapi.templating import Jinja2Templates
from datetime import datetime

templates = Jinja2Templates(directory="app/templates")
router = APIRouter()

@router.get("/", response_class=HTMLResponse)
async def dashboard(request: Request):
    return templates.TemplateResponse(request, "index.html", {"active": "dashboard", "now": datetime.now().strftime("%Y-%m-%d %H:%M")})

@router.get("/products", response_class=HTMLResponse)
async def products_page(request: Request):
    return templates.TemplateResponse(request, "products.html", {"active": "products"})

@router.get("/collection", response_class=HTMLResponse)
async def collection_page(request: Request):
    return templates.TemplateResponse(request, "collection.html", {"active": "collection"})

@router.get("/listing", response_class=HTMLResponse)
async def listing_page(request: Request):
    return templates.TemplateResponse(request, "listing.html", {"active": "listing"})

@router.get("/marketing", response_class=HTMLResponse)
async def marketing_page(request: Request):
    return templates.TemplateResponse(request, "marketing.html", {"active": "marketing"})

@router.get("/inventory", response_class=HTMLResponse)
async def inventory_page(request: Request):
    return templates.TemplateResponse(request, "inventory.html", {"active": "inventory"})
