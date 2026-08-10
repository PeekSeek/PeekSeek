from src.core.settings import settings


def test_static_values_loaded_from_toml():
    assert settings.domain == "127.0.0.1"
    assert settings.protocol in {"http", "https"}
    assert "admin" in settings.permissions


def test_data_files_loaded():
    assert "gamemodes" in settings.mmconfig
    assert "kits" in settings.kits


def test_secrets_from_env():
    assert settings.db_url.startswith("sqlite")


def test_discord_ids_are_public_config():
    assert settings.discord_client_id
    assert settings.discord_invite_url.startswith("https://discord.gg/")


def test_derived_values():
    assert settings.base_url == f"{settings.protocol}://{settings.domain}"
    assert settings.profile_url(1) == f"{settings.base_url}/u/1"
    hidden = [
        gm for gm in settings.visible_mmconfig["gamemodes"] if gm.get("hideFromMM")
    ]
    assert hidden == []
