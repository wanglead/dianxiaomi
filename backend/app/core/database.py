from sqlalchemy import create_engine, inspect, text
from sqlalchemy.orm import sessionmaker, DeclarativeBase
from app.core.config import settings
import os

# Ensure data directory exists
os.makedirs("data", exist_ok=True)

engine = create_engine(
    settings.DATABASE_URL,
    connect_args={"check_same_thread": False} if "sqlite" in settings.DATABASE_URL else {},
    echo=True,
)

SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)


class Base(DeclarativeBase):
    pass


def get_db():
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()


def init_db():
    """Create all tables."""
    Base.metadata.create_all(bind=engine)

    if engine.dialect.name != "sqlite":
        return

    existing = {column["name"] for column in inspect(engine).get_columns("products")}
    columns = {
        "owner": "VARCHAR(100) DEFAULT 'aa'",
        "target_store": "VARCHAR(200) DEFAULT ''",
        "image_progress": "JSON DEFAULT '{}'",
        "issue_message": "TEXT DEFAULT '等待开始处理'",
        "sync_payload": "JSON DEFAULT '{}'",
        "sync_status": "VARCHAR(20) DEFAULT 'none'",
        "sync_error": "TEXT DEFAULT ''",
    }
    with engine.begin() as connection:
        for name, definition in columns.items():
            if name not in existing:
                connection.execute(text(f"ALTER TABLE products ADD COLUMN {name} {definition}"))
        connection.execute(
            text("CREATE INDEX IF NOT EXISTS ix_products_sync_status ON products (sync_status)")
        )
