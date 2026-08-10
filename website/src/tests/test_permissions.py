from src.core import models as data


def test_default_user_has_no_permissions(user_factory):
    user = user_factory()
    assert user.getPermission("premium") is False
    assert user.getPermission("admin") is False


def test_set_and_get_permission(user_factory):
    user = user_factory()
    user.setPermission("premium", True)
    assert user.getPermission("premium") is True
    user.setPermission("premium", False)
    assert user.getPermission("premium") is False


def test_admin_grants_all_permissions(user_factory):
    user = user_factory()
    user.setPermission("admin", True)
    assert user.getPermission("admin") is True
    assert user.getPermission("premium") is True
    assert user.getPermission("canWriteBlog") is True


def test_invalid_permission_raises(user_factory):
    import pytest

    user = user_factory()
    with pytest.raises(TypeError):
        user.getPermission("doesnotexist")
