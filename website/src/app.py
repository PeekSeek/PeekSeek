"""The PeekSeek FastAPI app — session middleware, routers, and error pages.

One app serves everything: server-rendered pages, the browser JSON endpoints the
site's own scripts call, and the `/server/v1/*` API the game servers run on.
"""

import asyncio
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, Request
from fastapi.responses import JSONResponse, RedirectResponse
from fastapi.staticfiles import StaticFiles
from starlette.exceptions import HTTPException as StarletteHTTPException
from starlette.middleware.sessions import SessionMiddleware

from src.core.notify import keep_player_counter_current
from src.core.settings import settings
from src.dependencies import LoginRedirect, load_user, templates
from src.routes import admin, auth, esports, gameserver, index, kits, pages, party, user


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Background work that lives as long as the app does.

    Only the Discord player counter so far. It no-ops without a BOT_TOKEN, so an
    environment that has none still starts the task and simply never posts.
    """
    counter = asyncio.create_task(keep_player_counter_current())
    yield
    counter.cancel()


# /docs is the documentation site (mounted below), so the generated API explorer
# moves to /api. Both read from the same /openapi.json, which is also what the docs
# build renders its route reference from.
app = FastAPI(
    title="PeekSeek",
    dependencies=[Depends(load_user)],
    lifespan=lifespan,
    docs_url="/api",
    redoc_url=None,
)
app.add_middleware(
    SessionMiddleware,
    secret_key=settings.secret_key,
    https_only=settings.protocol == "https",
    max_age=60 * 60 * 24 * 30,
)
app.mount("/static", StaticFiles(directory=settings.public), name="static")

# The docs are a static Starlight build served from this app so the handbook and the
# route reference share the site's domain. Mounted only when built: a checkout that
# has not run the docs build still has to start, and StaticFiles raises on a missing
# directory.
if settings.docs:
    app.mount("/docs", StaticFiles(directory=settings.docs, html=True), name="docs")

for module in (index, pages, auth, user, kits, admin, esports, gameserver, party):
    app.include_router(module.router)


@app.exception_handler(LoginRedirect)
def login_redirect(request: Request, exc: LoginRedirect):
    request.session["lastError"] = str(exc)
    return RedirectResponse("/", status_code=302)


@app.exception_handler(StarletteHTTPException)
def error_response(request: Request, exc: StarletteHTTPException):
    wants_html = "text/html" in request.headers.get("accept", "")
    if exc.status_code == 404 and wants_html:
        return templates.TemplateResponse(
            request=request,
            name="pages/error.html",
            context={"error": "404", "message": str(exc.detail)},
            status_code=404,
        )
    return JSONResponse({"error": str(exc.detail)}, status_code=exc.status_code)
