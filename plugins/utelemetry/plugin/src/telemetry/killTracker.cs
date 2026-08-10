using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace uTelemetry;

public class KillTracker
{
    public void Init()
    {
        UseableGun.onBulletHit += OnBulletHit;
    }

    public void Dispose()
    {
        UseableGun.onBulletHit -= OnBulletHit;
    }

    public static float CalculateBulletDrop(float gravityMultiplier, float distance, float velocity)
    {
        return CalculateBulletDrop(Physics.gravity.y, gravityMultiplier, distance, velocity);
    }

    public static float CalculateBulletDrop(float gravity, float gravityMultiplier, float distance, float velocity)
    {
        float time = distance / velocity;
        float drop = 0.5f * gravity * gravityMultiplier * time * time;
        return drop;
    }

    public void OnBulletHit(UseableGun gun, BulletInfo bullet, InputInfo hit, ref bool shouldAllow)
    {
        var player = gun.player;

        Vector3 origin = bullet.origin;
        Vector3 direction = (hit.point - origin).normalized;

        var equippedGunAsset = gun.equippedGunAsset;

        Transform barricade = GetFirstBarricade();
        if (barricade == null) return;
        Vector3 target = barricade.position;

        var dropOff = CalculateBulletDrop(
            equippedGunAsset.bulletGravityMultiplier,
            (origin - target).magnitude,
            equippedGunAsset.muzzleVelocity
        );

        Vector3 closestPoint = ClosestPointOnRay(origin, direction, target);
        Vector3 offset = target - closestPoint;

        Vector3 forward = direction;
        Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
        Vector3 up = Vector3.Cross(right, forward).normalized;

        float xOffset = Vector3.Dot(offset, right);
        float yOffset = Vector3.Dot(offset, up);

        Logger.Log($"{player.channel.owner.playerID.characterName} shot dummy -> X: {xOffset:F6}, Y: {yOffset:F6}");
    }

    private Vector3 ClosestPointOnRay(Vector3 origin, Vector3 dir, Vector3 point)
    {
        dir.Normalize();
        float t = Vector3.Dot(point - origin, dir);
        return origin + dir * t;
    }

    private Transform GetFirstBarricade()
    {
        foreach (var region in BarricadeManager.regions)
        {
            if (region == null) continue;

            foreach (var drop in region.drops)
            {
                if (drop.interactable == null) continue;

                return drop.model;
            }
        }

        return null;
    }
}
