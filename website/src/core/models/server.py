from __future__ import annotations

from datetime import datetime
from typing import Optional

from sqlalchemy import Column, DateTime, text
from sqlmodel import Field, SQLModel


class AuthorizedServer(SQLModel, table=True):
    __tablename__ = "AuthorizedServers"

    address: str = Field(primary_key=True)
    region: Optional[str] = None


class ServerRequest(SQLModel, table=True):
    """Audit log for every /unt request received from a game server."""

    __tablename__ = "ServerRequests"

    id: Optional[int] = Field(default=None, primary_key=True)
    address: str = Field(foreign_key="AuthorizedServers.address", nullable=False)
    path: str = Field(nullable=False)
    method: str = Field(nullable=False)
    match_id: Optional[int] = Field(default=None, nullable=True)
    created_at: Optional[datetime] = Field(
        default=None,
        sa_column=Column(
            DateTime(timezone=True), server_default=text("CURRENT_TIMESTAMP")
        ),
    )
