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
    for gamemode in settings.visible_mmconfig["gamemodes"]:
        assert (
            settings.mmconfig["gamemodes"][gamemode["bit"]]["name"] == gamemode["name"]
        )


def test_gamemodes_are_only_ever_added_at_the_end():
    """A gamemode is its position in mmconfig: the queue bitmask, every stored
    Match.gamemode and the bits a running lobby fetched on load all count from it.
    Inserting one above the rest moved every mode over by one, and a lobby still
    holding the old bits queued players for 1v1 into a 2v2 that never filled."""
    shapes = [
        (gm["name"], gm["teams"], gm["playersPerTeam"])
        for gm in settings.mmconfig["gamemodes"]
    ]
    assert shapes[:10] == [
        ("Arena 1v1", 2, 1),
        ("Arena Drums 1v1", 2, 1),
        ("Arena Sniper 1v1", 2, 1),
        ("Casual Duo", 2, 2),
        ("Casual", 4, 1),
        ("Rush", 2, 1),
        ("Augewehr", 2, 1),
        ("RAID 1v1", 2, 1),
        ("Arena 2v2", 2, 2),
        ("RAID 2v2", 2, 2),
    ]


def test_the_menu_lists_the_queueable_modes_in_menu_order():
    menu = [(gm["name"], gm["bit"]) for gm in settings.visible_mmconfig["gamemodes"]]
    assert menu == [
        ("RAID 2v2", 9),
        ("RAID 1v1", 7),
        ("Arena 2v2", 8),
        ("Arena 1v1", 0),
        ("Arena Sniper 1v1", 2),
        ("Arena Drums 1v1", 1),
    ]
