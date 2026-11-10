using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RunCheckpoint;

internal static class MonoFields
{
    internal static readonly Action<ExpManager> FlushPendingCollectionGrants =
        AccessTools.MethodDelegate<Action<ExpManager>>(AccessTools.Method(typeof(ExpManager), "FlushPendingCollectionGrants"));
    internal static readonly Action<ScoreText> SnapDisplayToScore =
        AccessTools.MethodDelegate<Action<ScoreText>>(AccessTools.Method(typeof(ScoreText), "SnapDisplayToScore"));
    internal static readonly Action<EquippedWeaponPerkScreen> RefreshPerkScreen =
        AccessTools.MethodDelegate<Action<EquippedWeaponPerkScreen>>(AccessTools.Method(typeof(EquippedWeaponPerkScreen), "OnEnable"));
    internal static readonly Func<Enemy, bool> ShowBlightParticle =
        AccessTools.MethodDelegate<Func<Enemy, bool>>(AccessTools.Method(typeof(Enemy), "TryShowBlightParticle"));
    internal static readonly Func<Enemy, bool> ShowScourgeParticle =
        AccessTools.MethodDelegate<Func<Enemy, bool>>(AccessTools.Method(typeof(Enemy), "TryShowScourgeParticle"));

    internal static readonly AccessTools.FieldRef<Enemy, bool> EnemyDied = AccessTools.FieldRefAccess<Enemy, bool>("died");
    internal static readonly AccessTools.FieldRef<Enemy, Rigidbody> EnemyBody = AccessTools.FieldRefAccess<Enemy, Rigidbody>("rigidBody");
    internal static readonly AccessTools.FieldRef<WeaponManager, Rigidbody> PlayerBody = AccessTools.FieldRefAccess<WeaponManager, Rigidbody>("rb");
    internal static readonly AccessTools.FieldRef<stabSnareScript, Collider> SnareCollider = AccessTools.FieldRefAccess<stabSnareScript, Collider>("col");
    internal static readonly AccessTools.FieldRef<stabSnareScript, float> SnareColliderTimer = AccessTools.FieldRefAccess<stabSnareScript, float>("colliderEnableTimer");
    internal static readonly AccessTools.FieldRef<GameManager, List<GameObject>> EnemyPrefabs = AccessTools.FieldRefAccess<GameManager, List<GameObject>>("enemyList");
    internal static readonly AccessTools.FieldRef<GameManager, List<GameObject>> AdEnemyPrefabs = AccessTools.FieldRefAccess<GameManager, List<GameObject>>("adEnemyList");

    internal static readonly AccessTools.FieldRef<ButtonControl, GameObject> Border = AccessTools.FieldRefAccess<ButtonControl, GameObject>("border");
    internal static readonly AccessTools.FieldRef<Enemy, bool> CanShoot = AccessTools.FieldRefAccess<Enemy, bool>("canShoot");
    internal static readonly AccessTools.FieldRef<Enemy, float> CurrentMovementSpeed = AccessTools.FieldRefAccess<Enemy, float>("currentMovementSpeed");
    internal static readonly AccessTools.FieldRef<Enemy, float> MaxHealth = AccessTools.FieldRefAccess<Enemy, float>("maxHealth");
    internal static readonly AccessTools.FieldRef<Enemy, List<Enemy>> ShockDamageList = AccessTools.FieldRefAccess<Enemy, List<Enemy>>("shockDamageList");
    internal static readonly AccessTools.FieldRef<Enemy, GameObject> ShockTemp = AccessTools.FieldRefAccess<Enemy, GameObject>("shockTemp");
    internal static readonly AccessTools.FieldRef<ExpManager, int> CollectionParticleCount = AccessTools.FieldRefAccess<ExpManager, int>("collectionParticleCount");
    internal static readonly AccessTools.FieldRef<ExpManager, int> OrbCount = AccessTools.FieldRefAccess<ExpManager, int>("orbCount");
    internal static readonly AccessTools.FieldRef<GameManager, GameObject> HeartDrop = AccessTools.FieldRefAccess<GameManager, GameObject>("heartDrop");
    internal static readonly AccessTools.FieldRef<Mainmenu, Image> Black = AccessTools.FieldRefAccess<Mainmenu, Image>("black");
    internal static readonly AccessTools.FieldRef<Mainmenu, GameObject> Blackfadeinscreen = AccessTools.FieldRefAccess<Mainmenu, GameObject>("blackfadeinscreen");
    internal static readonly AccessTools.FieldRef<Mainmenu, GameObject> Flashinglightswarningscreen = AccessTools.FieldRefAccess<Mainmenu, GameObject>("flashinglightswarningscreen");
    internal static readonly AccessTools.FieldRef<Mainmenu, Button> LeaderboardsButton = AccessTools.FieldRefAccess<Mainmenu, Button>("leaderboardsButton");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> BoltSnarePool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("boltSnarePool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> ClonedSentryPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("clonedSentryPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> CombustionPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("combustionPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> ConcealedSpiralPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("concealedSpiralPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> CosmicCraterPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("cosmicCraterPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> DragonsBreathFirePool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("dragonsBreathFirePool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> DragonsBreathPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("dragonsBreathPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, FuelScript> ExpDrop = AccessTools.FieldRefAccess<ObjectPool, FuelScript>("expDrop");
    internal static readonly AccessTools.FieldRef<ObjectPool, FuelScript> ExpDrop2 = AccessTools.FieldRefAccess<ObjectPool, FuelScript>("expDrop2");
    internal static readonly AccessTools.FieldRef<ObjectPool, FuelScript> ExpDrop3 = AccessTools.FieldRefAccess<ObjectPool, FuelScript>("expDrop3");
    internal static readonly AccessTools.FieldRef<ObjectPool, FuelScript> ExpDrop4 = AccessTools.FieldRefAccess<ObjectPool, FuelScript>("expDrop4");
    internal static readonly AccessTools.FieldRef<ObjectPool, FuelScript> ExpDrop5 = AccessTools.FieldRefAccess<ObjectPool, FuelScript>("expDrop5");
    internal static readonly AccessTools.FieldRef<ObjectPool, FuelScript> ExpDrop6 = AccessTools.FieldRefAccess<ObjectPool, FuelScript>("expDrop6");
    internal static readonly AccessTools.FieldRef<ObjectPool, FuelScript> ExpDrop7 = AccessTools.FieldRefAccess<ObjectPool, FuelScript>("expDrop7");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> FirePool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("firePool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> GuidedLaserPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("guidedLaserPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> LightMeteorPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("lightMeteorPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, GameObject> MutationsPickupPrefab = AccessTools.FieldRefAccess<ObjectPool, GameObject>("mutationsPickupPrefab");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> OrbitalMortarParticlePool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("orbitalMortarParticlePool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> PhoenixMeteorPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("phoenixMeteorPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> PlasmaBeamPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("plasmaBeamPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> PlasmaCoilPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("plasmaCoilPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> PsychicEruptionPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("psychicEruptionPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> PulseAnomalyPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("pulseAnomalyPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> RingOfFirePool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("ringOfFirePool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> SingularityPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("singularityPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> SingularitySlingshotPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("singularitySlingshotPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, GameObject> TeslaCurrentPrefab = AccessTools.FieldRefAccess<ObjectPool, GameObject>("teslaCurrentPrefab");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> TitanicAsteroidPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("titanicAsteroidPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> TracerRoundPool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("tracerRoundPool");
    internal static readonly AccessTools.FieldRef<ObjectPool, List<GameObject>> WildFireParticlePool = AccessTools.FieldRefAccess<ObjectPool, List<GameObject>>("wildFireParticlePool");
    internal static readonly AccessTools.FieldRef<PlayerStats, HashSet<Enemy>> BlightDecayTrackedEnemies = AccessTools.FieldRefAccess<PlayerStats, HashSet<Enemy>>("blightDecayTrackedEnemies");
    internal static readonly AccessTools.FieldRef<Projectile, List<Enemy>> ExecutionSawsList = AccessTools.FieldRefAccess<Projectile, List<Enemy>>("executionSawsList");
    internal static readonly AccessTools.FieldRef<Projectile, List<Enemy>> FrostSawsList = AccessTools.FieldRefAccess<Projectile, List<Enemy>>("frostSawsList");
    internal static readonly AccessTools.FieldRef<Projectile, HashSet<Collider>> LaserDamageList = AccessTools.FieldRefAccess<Projectile, HashSet<Collider>>("laserDamageList");
    internal static readonly AccessTools.FieldRef<Projectile, List<GameObject>> PierceList = AccessTools.FieldRefAccess<Projectile, List<GameObject>>("pierceList");
    internal static readonly AccessTools.FieldRef<ScoreText, int> Kills = AccessTools.FieldRefAccess<ScoreText, int>("kills");
    internal static readonly AccessTools.FieldRef<SideUpgrade, Image> BackgroundImage = AccessTools.FieldRefAccess<SideUpgrade, Image>("backgroundImage");
    internal static readonly AccessTools.FieldRef<SideUpgradesDisplayHandling, int> MutationUpgradeCounter = AccessTools.FieldRefAccess<SideUpgradesDisplayHandling, int>("mutationUpgradeCounter");
    internal static readonly AccessTools.FieldRef<SideUpgradesDisplayHandling, int> WeaponUpgradeCounter = AccessTools.FieldRefAccess<SideUpgradesDisplayHandling, int>("weaponUpgradeCounter");
    internal static readonly AccessTools.FieldRef<Upgrade, PlayerStats> PS = AccessTools.FieldRefAccess<Upgrade, PlayerStats>("pS");
    internal static readonly AccessTools.FieldRef<WeaponManager, GameObject> ClonedSentryObj = AccessTools.FieldRefAccess<WeaponManager, GameObject>("clonedSentryObj");
    internal static readonly AccessTools.FieldRef<WeaponManager, stabSnareScript> FirstSnare = AccessTools.FieldRefAccess<WeaponManager, stabSnareScript>("firstSnare");
    internal static readonly AccessTools.FieldRef<WeaponManager, GameObject> MissileObj = AccessTools.FieldRefAccess<WeaponManager, GameObject>("missileObj");
    internal static readonly AccessTools.FieldRef<WeaponManager, GameObject> Player = AccessTools.FieldRefAccess<WeaponManager, GameObject>("player");
    internal static readonly AccessTools.FieldRef<WeaponManager, List<Enemy>> RadiationDamageList = AccessTools.FieldRefAccess<WeaponManager, List<Enemy>>("radiationDamageList");
    internal static readonly AccessTools.FieldRef<WeaponManager, int> RemainingSkillPoints = AccessTools.FieldRefAccess<WeaponManager, int>("remainingSkillPoints");
    internal static readonly AccessTools.FieldRef<weaponselect, GameObject> StartButton = AccessTools.FieldRefAccess<weaponselect, GameObject>("startButton");
}
