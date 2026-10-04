"""Dump the game-server API's OpenAPI schema to a file for the docs build.

The docs site renders the `/server/v1/*` reference from this schema rather than from a
hand-kept copy, so the route detail cannot drift from the code. It is generated at build
time and deliberately not committed: a stale schema in git is worse than no schema.

Only `/server/v1/*` is included. The rest of the app is pages and the browser endpoints
the site's own scripts call, which are nobody's contract and would bury the 9 routes a
game server actually implements under 50 it must never touch.

    python scripts/openapi.py ../docs/openapi.json
"""

import json
import sys
from pathlib import Path

# Run as a script rather than a module, so the package root is not already importable.
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from src.app import app  # noqa: E402

PUBLIC = "/server/v1"

schema = app.openapi()
schema["paths"] = {p: v for p, v in schema["paths"].items() if p.startswith(PUBLIC)}
schema["info"] = {
    "title": "PeekSeek game-server API",
    "version": schema["info"].get("version", "1.0.0"),
    "description": (
        "Everything a game server needs to run PeekSeek matches, and nothing else. "
        "Auth is the caller's source IP, allow-listed in `AuthorizedServers`.\n\n"
        "The conventions every route here follows, and the thinking behind the "
        "lifecycle, are in [the API contract](/docs/reference/api/)."
    ),
}

# FastAPI derives operationId from the function name plus the whole path and method
# ("gamemodes_server_v1_gamemodes_get"), and the docs site uses it for both the page
# slug and the sidebar label. Cut it back to the handler's own name, which is what the
# route is actually called in gameserver.py, keeping the method only to break a tie.
seen: dict[str, str] = {}
for path, methods in schema["paths"].items():
    for method, operation in methods.items():
        if "operationId" not in operation:
            continue
        short = operation["operationId"].split(
            f"_{PUBLIC.strip('/').replace('/', '_')}"
        )[0]
        if seen.get(short) not in (None, f"{path}:{method}"):
            short = f"{short}_{method}"
        seen[short] = f"{path}:{method}"
        operation["operationId"] = short

out = Path(sys.argv[1] if len(sys.argv) > 1 else "openapi.json")
out.write_text(json.dumps(schema, indent=1), "utf-8")
print(f"wrote {out} with {len(schema['paths'])} paths")
