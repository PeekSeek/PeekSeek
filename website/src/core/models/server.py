from __future__ import annotations

from datetime import datetime
from typing import Optional

from sqlalchemy import Column, DateTime, text
from sqlmodel import Field, SQLModel


class AuthorizedServer(SQLModel, table=True):
    __tablename__ = "AuthorizedServers"

    address: str = Field(primary_key=True)
    region: Optional[str] = None

    @staticmethod
    def bootstrap(servers: dict[str, str]) -> None:
        """Allow-list game-server IPs, so a fresh environment can accept /unt calls.

        There is no page or endpoint that adds one, and an unlisted server gets a
        401 from every /unt route. Driven by BOOTSTRAP_SERVERS so only the
        environments that set it are affected.
        """
        from src.core.db import open

        with open() as db:
            known = {server.address for server in db.query(AuthorizedServer).all()}
            for address, region in servers.items():
                if address not in known:
                    db.add(AuthorizedServer(address=address, region=region))


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
