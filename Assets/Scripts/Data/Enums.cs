// Ported from ArmedConflict data/. Enum ORDER matters — these are serialized by index in
// ScriptableObject assets, so reordering silently rewrites existing data.
namespace ArmedConflict.Data
{
    public enum ProjectileType { Bullet, Rocket, Grenade, Shell }
    public enum BulletVariant { Standard, MachineGun, Sniper }
    public enum SilhouetteStyle { Mountains, Forest, Ocean, Desert, Winter, City }
    public enum AmmoType { Standard, Incendiary, AP, Cluster }
    public enum CosmeticSet { Olive, Desert, Urban, Arctic }
    public enum ConsumableType { Airstrike, EarlyReinforcements, TraumaKit, SmokeScreen, OverwatchFlare }
    public enum TierAxis { HP, DAMAGE }
}
