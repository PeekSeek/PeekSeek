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
# The lock rather than requirements.txt, so this image and the site's resolve the same
# versions - two containers running the same moderation code on different SQLModel
# builds would be a hard fault to see.
COPY website/requirements.lock ./website-requirements.lock
COPY bot/requirements.txt ./bot-requirements.txt
RUN pip install --no-cache-dir --require-hashes -r website-requirements.lock \
    && pip install --no-cache-dir -r bot-requirements.txt

# The site installed as a package, so `from src.core import moderation` resolves the
# ordinary way. It used to be a sys.path.insert() in bot.py pointed at WEBSITE_SRC, which
# was a path hack standing in for a dependency. config.toml, mmconfig.json and kits.json
# are read on import and travel with the package (see website/pyproject.toml).
COPY website ./website
RUN pip install --no-cache-dir --no-deps ./website

COPY bot/src ./bot/src

ENV PYTHONUNBUFFERED=1

# Not root. The bot dials out to Discord and reads a database; neither needs uid 0, and
# a container process running as root is one container escape away from being root.
RUN useradd --create-home --uid 10001 peekseek && chown -R peekseek /app
USER peekseek

# No port and no healthcheck: this is a gateway client that dials out to Discord and
# never listens, so there is nothing to expose or probe.
CMD ["python", "bot/src/bot.py"]
