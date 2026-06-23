# Database
from pydantic_settings import BaseSettings
from typing import Dict, Optional, Any


class Settings(BaseSettings):
    DATABASE_URL: str = "sqlite:///./data/products.db"
    REDIS_URL: str = "redis://localhost:6379/0"
    SECRET_KEY: str = "your-secret-key-change-in-production"
    ALGORITHM: str = "HS256"
    ACCESS_TOKEN_EXPIRE_MINUTES: int = 1440
    
    DIANXIAOMI_BASE_URL: str = "https://www.dianxiaomi.com"
    DIANXIAOMI_API_KEY: str = ""
    DIANXIAOMI_API_SECRET: str = ""
    
    PLUGIN_AUTH_TOKEN: str = "plugin-token-change-in-production"

    class Config:
        env_file = ".env"
        env_file_encoding = "utf-8"
        extra = "ignore"


settings = Settings()
