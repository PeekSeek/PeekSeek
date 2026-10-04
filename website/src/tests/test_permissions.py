def test_default_user_has_no_permissions(user_factory):
    user = user_factory()
    assert user.get_permission("premium") is False
    assert user.get_permission("admin") is False


def test_set_and_get_permission(user_factory):
    user = user_factory()
    user.set_permission("premium", True)
    assert user.get_permission("premium") is True
    user.set_permission("premium", False)
    assert user.get_permission("premium") is False


def test_admin_grants_all_permissions(user_factory):
    user = user_factory()
    user.set_permission("admin", True)
    assert user.get_permission("admin") is True
    assert user.get_permission("premium") is True
    assert user.get_permission("canWriteBlog") is True


def test_invalid_permission_raises(user_factory):
    import pytest

    user = user_factory()
    with pytest.raises(TypeError):
        user.get_permission("doesnotexist")
