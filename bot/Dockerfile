# Built from the repository root, not from bot/, because the bot runs the website's
# own src.core.moderation rather than a copy of it. Sharing the module is what stops
# a ban issued from Discord meaning something different to one issued from the
# dashboard, so the image has to contain both trees.
FROM python:3.12-slim

WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential libpq-dev && rm -rf /var/lib/apt/lists/*

# The website's dependencies first: the bot imports its models, settings and database
# layer, so it needs everything they need.
COPY website/requirements.txt ./website-requirements.txt
COPY bot/requirements.txt ./bot-requirements.txt
RUN pip install --no-cache-dir -r website-requirements.txt -r bot-requirements.txt

# src/config.toml, mmconfig.json and kits.json all live here and are read on import,
# so this is the whole tree rather than just the packages the bot names.
COPY website/src ./website/src
COPY bot/src ./bot/src

# Where bot.py puts the website on sys.path. Set here rather than hardcoded so the
# layout can move without editing the source.
ENV WEBSITE_SRC=/app/website
ENV PYTHONUNBUFFERED=1

# No port and no healthcheck: this is a gateway client that dials out to Discord and
# never listens, so there is nothing to expose or probe.
CMD ["python", "bot/src/bot.py"]
