# The build context is the REPOSITORY ROOT, not website/, because the image also
# carries the documentation site from docs/. Build it with:
#
#     docker build -f website/Dockerfile .
#
# In Dokploy that is Build Context "." (or empty) with Dockerfile Path
# "website/Dockerfile". A context of website/ fails on the COPY lines below.

FROM python:3.12-slim AS base

WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential libpq-dev && rm -rf /var/lib/apt/lists/*

COPY website/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY website/src ./src
COPY website/public ./public

# The docs render the game-server API reference from the app's own OpenAPI schema, so it
# is generated here rather than committed. Importing the app constructs Settings, which
# requires DB; nothing connects, so any URL will do.
FROM base AS schema
COPY website/scripts/openapi.py ./scripts/openapi.py
RUN DB=sqlite:// python scripts/openapi.py /openapi.json

FROM node:22-slim AS docs
WORKDIR /docs
COPY docs/package.json docs/package-lock.json ./
RUN npm ci
COPY docs ./
COPY --from=schema /openapi.json ./openapi.json
# The contract page is imported from website/API.md at build time, so it lives at the
# path the importer expects relative to the docs project.
COPY website/API.md /website/API.md
RUN npm run build

FROM base

# Served at /docs by the app when this directory exists (see src/app.py).
COPY --from=docs /docs/dist ./docs

# Minify the stylesheets and scripts into the image. The build-only compressors are
# uninstalled in the same layer so they never reach the running container.
COPY website/minify.py .
RUN pip install --no-cache-dir csscompressor rjsmin \
    && python minify.py \
    && pip uninstall -y csscompressor rjsmin

EXPOSE 8000

# --forwarded-allow-ips must NOT be "*": authorized_server() and the ban/IP-tracking
# logic in dependencies.py both trust request.client.host as the caller's real IP,
# and uvicorn's ProxyHeadersMiddleware rewrites that from X-Forwarded-For whenever the
# peer is in this list. "*" trusted it from every peer — including a client hitting
# this container directly — so anyone could set X-Forwarded-For to a known
# AuthorizedServers address and pass the /unt/* IP allowlist with no other access.
# Scoped to Docker's private bridge/overlay ranges, only Traefik's own hop is trusted.
CMD ["uvicorn", "src.app:app", "--host", "0.0.0.0", "--port", "8000", "--proxy-headers", "--forwarded-allow-ips=172.16.0.0/12,10.0.0.0/8,192.168.0.0/16"]
