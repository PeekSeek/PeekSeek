FROM python:3.12-slim

WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential libpq-dev && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY src ./src
COPY public ./public

EXPOSE 8000

# --forwarded-allow-ips must NOT be "*": authorized_server() and the ban/IP-tracking
# logic in dependencies.py both trust request.client.host as the caller's real IP,
# and uvicorn's ProxyHeadersMiddleware rewrites that from X-Forwarded-For whenever the
# peer is in this list. "*" trusted it from every peer — including a client hitting
# this container directly — so anyone could set X-Forwarded-For to a known
# AuthorizedServers address and pass the /unt/* IP allowlist with no other access.
# Scoped to Docker's private bridge/overlay ranges, only Traefik's own hop is trusted.
CMD ["uvicorn", "src.app:app", "--host", "0.0.0.0", "--port", "8000", "--proxy-headers", "--forwarded-allow-ips=172.16.0.0/12,10.0.0.0/8,192.168.0.0/16"]
