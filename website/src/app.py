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
from src.dependencies import ActiveBan, LoginRedirect, load_user, templates
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


app = FastAPI(title="PeekSeek", dependencies=[Depends(load_user)], lifespan=lifespan)
app.add_middleware(
    SessionMiddleware,
    secret_key=settings.secret_key,
    https_only=settings.protocol == "https",
    max_age=60 * 60 * 24 * 30,
)
app.mount("/static", StaticFiles(directory=settings.public), name="static")

for module in (index, pages, auth, user, kits, admin, esports, gameserver, party):
    app.include_router(module.router)


@app.exception_handler(LoginRedirect)
def login_redirect(request: Request, exc: LoginRedirect):
    request.session["lastError"] = str(exc)
    return RedirectResponse("/", status_code=302)


@app.exception_handler(ActiveBan)
def banned_page(request: Request, exc: ActiveBan):
    """The ban page. 403 rather than 200: this is a refusal, and a crawler or a script
    reading the status code should see one."""
    return templates.TemplateResponse(
        request=request,
        name="pages/banned.html",
        context={"ban": exc.ban},
        status_code=403,
    )


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
