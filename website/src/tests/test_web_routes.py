def test_index_anonymous_renders(client):
    r = client.get("/")
    assert r.status_code == 200


def test_play_redirects_anonymous(client):
    r = client.get("/play")
    assert r.status_code == 302
    assert r.headers["location"] == "/"


def test_index_redirects_when_logged_in(client, login, user_factory):
    login(client, user_factory())
    r = client.get("/")
    assert r.status_code == 302
    assert "/play" in r.headers["location"]


def test_settings_page_requires_login(client):
    assert client.get("/settings").status_code == 302


def test_settings_page_rendered_when_logged_in(client, login, user_factory):
    login(client, user_factory())
    assert client.get("/settings").status_code == 200


def test_admin_requires_admin_permission(client, login, user_factory):
    login(client, user_factory())
    r = client.get("/admin/")
    assert r.status_code == 302


def test_admin_allowed_for_admin_user(client, login, user_factory):
    from sqlmodel import Session
    from src.core import db as core_db
    from src.core import models as data

    user = user_factory()
    with Session(core_db.engine) as s:
        s.query(data.User).filter(data.User.steam64 == user.steam64).update(
            {"permissions": 1}
        )
        s.commit()

    login(client, user)
    assert client.get("/admin/").status_code == 200


def test_logout_clears_session(client, login, user_factory):
    login(client, user_factory())
    r = client.get("/auth/logout")
    assert r.status_code == 302
    assert client.get("/settings").status_code == 302  # anonymous again


def test_404_renders_error_page(client):
    r = client.get("/this-route-does-not-exist", headers={"accept": "text/html"})
    assert r.status_code == 404
    assert "text/html" in r.headers["content-type"]
