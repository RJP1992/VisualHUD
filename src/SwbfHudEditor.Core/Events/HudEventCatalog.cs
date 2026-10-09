namespace SwbfHudEditor.Core.Events;

/// <summary>Where an event comes from.</summary>
public enum HudEventSource
{
    /// <summary>Registered by the game (<c>HUD::GameEvents::Open</c>).</summary>
    Stock,
    /// <summary>Published by BF2GameExt.</summary>
    GameExt,
    /// <summary>The output of a transform in the loaded files.</summary>
    Transform,
    /// <summary>Only named in the loaded files: nothing known sends it.</summary>
    Unknown,
}

public sealed record HudEventInfo(string Name, HudEventType Type, HudEventSource Source, string? Note = null);

/// <summary>
/// The events the game and BF2GameExt send. The stock list is the 173 names
/// <c>HUD::GameEvents::Open</c> registers on PC (BF2GameExt <c>docs/RE/HUDSystem.md</c>, "Built-in event
/// catalogue"); the GameExt list is from its <c>docs/RE/HUDAuthoring.md</c>, "GameExt events". Only
/// <c>player1</c> exists on PC. Names compare without case, as their hashes do.
/// </summary>
public static class HudEventCatalog
{
    private static readonly Lazy<Dictionary<string, HudEventInfo>> Known = new(Build);

    public static IReadOnlyCollection<HudEventInfo> All => Known.Value.Values;

    public static HudEventInfo? Find(string name) => Known.Value.TryGetValue(name, out var info) ? info : null;

    private static Dictionary<string, HudEventInfo> Build()
    {
        var map = new Dictionary<string, HudEventInfo>(StringComparer.OrdinalIgnoreCase);
        void Add(HudEventType type, HudEventSource source, string note, params string[] names)
        {
            foreach (var name in names) map.TryAdd(name, new HudEventInfo(name, type, source, note.Length > 0 ? note : null));
        }
        void Stock(HudEventType type, params string[] names) => Add(type, HudEventSource.Stock, string.Empty, names);
        void StockNote(HudEventType type, string note, params string[] names) => Add(type, HudEventSource.Stock, note, names);

        const HudEventType B = HudEventType.Bool, I = HudEventType.Int, U = HudEventType.Uint, F = HudEventType.Float;
        const HudEventType M = HudEventType.Model, T = HudEventType.Texture, C = HudEventType.Color, S = HudEventType.String;
        const HudEventType V = HudEventType.Vector3;

        // Global.
        Stock(F, "objectivetimer", "victorytimer", "defeattimer", "time");
        Stock(B, "objectivetimer.disable", "victorytimer.disable", "defeattimer.disable", "hintPopup.disable",
            "objectivePopup.disable", "selectionPopup.disable", "levelHintText");
        StockNote(B, "Sent as the HUD starts", "initialize");
        Stock(U, "hintPopup", "objectivePopup", "selectionPopup", "targetResetCommon");
        Stock(S, "hintPopup.pageNumber");

        // Per player.
        const string p = "player1.";
        Stock(B, p + "spawn", p + "die", p + "healthDisable", p + "healthInVehicleDisable", p + "hero.healthDisable",
            p + "vehicle.healthDisable", p + "jetDisable", p + "energyDisable", p + "objectivelist.enable",
            p + "objectivelist.disable", p + "objectivesUpdated", p + "objectivesUpdated.disable", p + "objectiveDetails",
            p + "objectiveDetails.disable", p + "hintsAvailable", p + "hintsAvailable.disable", p + "pressSelectToReturn",
            p + "pressSelectToReturn.disable", p + "spaceAssaultStatus", p + "spaceAssaultStatus.disable",
            p + "vehicle.hackingTimeDisable", p + "vehicle.hackedTimeDisable", p + "weaponsOverheat", p + "weaponsEnable",
            p + "weaponsDisable", p + "map.hideCPs", p + "map.modeToggle", p + "map.enable", p + "map.disable", p + "map.spawn",
            p + "map.spawnLarge", p + "map.spawnLargeDisable", p + "spawnDisplay.enable", p + "spawnDisplay.disable",
            p + "tooltips.disable", p + "message.disable", p + "statistic.changed", p + "statistic.disable",
            p + "heroSelect.disable", p + "commandPost.disable", p + "commandPost.disputeEnable", p + "commandPost.disputeDisable",
            p + "lockOnDisable", p + "lockOnDisableShieldName", p + "lockOnDirectionDisable", p + "lockOnFlagCarrier",
            p + "lockOnFlagCarrierDisable", p + "missileLock", p + "missileLockDisable", p + "flag.player.carried",
            p + "flag.player.dropped", p + "flag.player.disable");
        Stock(U, p + "health", p + "healthInVehicle", p + "hero.health", p + "vehicle.health", p + "map.refreshTarget",
            p + "map.refreshPost", p + "map.refreshMarker", p + "targetResetPlayer", p + "spawnDisplay.message", p + "tooltips",
            p + "lockOnHealth", p + "missileLockDistance");
        Stock(F, p + "healthFraction", p + "bonusHealthFraction", p + "healthRegenPulseRate", p + "healthInVehicleFraction",
            p + "hero.healthFraction", p + "vehicle.healthFraction", p + "jetFuelFraction", p + "jetFuelWarning",
            p + "jetFuelThreshold", p + "energy", p + "energyFraction", p + "energyOverburn", p + "energyRegenPulseRate",
            p + "vehicle.hackingTime", p + "vehicle.hackedTime", p + "heroSelect.timerFraction", p + "heroSelect.timer",
            p + "commandPost.charge", p + "lockOnHealthFraction", p + "lockOnDistance", p + "reticule.alpha");
        StockNote(S, "A String despite the name: a bar ignores it", p + "vehicle.hackingTimeFraction", p + "vehicle.hackedTimeFraction");
        Stock(S, p + "spawnDisplay.vehicle", p + "spawnDisplay.spawninfo", p + "message", p + "heroSelect.message",
            p + "lockOnName", p + "lockOnClassName", p + "lockOnShieldName");
        Stock(I, p + "map.mode", p + "index");
        Stock(M, p + "vehicle.seatingMesh");
        Stock(C, p + "message.color", p + "commandPost.color", p + "commandPost.disputeColor", p + "lockOnTeamColor");
        Stock(V, p + "lockOnDirection");
        foreach (var side in new[] { "friend", "enemy" })
            foreach (var state in new[] { "carried", "dropped" })
            {
                var flag = $"{p}flag.{side}.{state}";
                Stock(B, flag, flag + ".disable", flag + ".number.disable");
                Stock(I, flag + ".number");
            }

        // Per weapon.
        for (var w = 1; w <= 2; w++)
        {
            var q = $"{p}weapon{w}.";
            StockNote(U, "The weapon's name hash; TransformNameMesh picks its model", q + "change");
            Stock(B, q + "disable", q + "ammoInfinite", q + "target.disable", q + "target.disableShieldName", q + "target.hit",
                q + "target.hitCritical", q + "reticule.disable", q + "lockOnDisable");
            StockNote(U, "0xFFFFFFFF when infinite", q + "totalAmmoBullets", q + "totalClipBullets");
            Stock(F, q + "totalAmmoFraction", q + "totalClipFraction", q + "heat", q + "charge", q + "refire",
                q + "target.healthFraction");
            Stock(S, q + "name", q + "target.name", q + "target.className", q + "target.shieldName");
            Stock(U, q + "target.health");
            Stock(C, q + "target.teamColor", q + "target.teamColorBright", q + "target.hitColor");
            StockNote(V, "Screen fractions of the aim point", q + "reticule.position", q + "lockOnPosition");
        }

        // Per team.
        for (var t = 1; t <= 2; t++)
        {
            var q = $"{p}team{t}.";
            Stock(I, q + "points", q + "reinforcements");
            Stock(B, q + "pointsDisable", q + "reinforcementsDisable", q + "textureDisable");
            Stock(S, q + "pointsText");
            Stock(F, q + "reinforcementsFraction", q + "bleedRate");
            Stock(T, q + "texture");
        }

        // Statistics (the two the stock HUDs use).
        foreach (var stat in new[] { "kills", "points" })
        {
            Stock(I, $"{p}statistic.{stat}");
            Stock(S, $"{p}statistic.{stat}Delta");
        }

        // GameExt.
        void Ext(HudEventType type, string note, params string[] names) => Add(type, HudEventSource.GameExt, note, names);
        Ext(U, "Texture name hash of the unit's icon for its stance", p + "unit.healthTexture");
        Ext(B, "", p + "unit.healthTextureDisable", p + "vehicle.healthTextureDisable");
        Ext(U, "0 stand, 1 crouch, 2 prone, 3 ball", p + "unit.stance");
        Ext(U, "Texture name hash of the vehicle seat's icon", p + "vehicle.healthTexture");
        foreach (var s in new[] { "sprint", "jump", "fall", "roll", "jet", "hover", "tumble", "land" })
            Ext(F, "1 while it lasts", $"{p}unit.state.{s}");
        Ext(V, "(0, 0, degrees) to level the reticule", p + "reticule.horizonRotation");
        Ext(U, "Slots in use", p + "commandPosts.count");
        for (var w = 1; w <= 2; w++)
        {
            var q = $"{p}weapon{w}.";
            Ext(U, "Texture name hash of the weapon's icon", q + "iconTexture");
            Ext(B, "", q + "iconTextureDisable");
            foreach (var s in new[] { "firing", "charging", "reloading", "overheated", "blocking", "shot" })
                Ext(F, "1 while it lasts", $"{q}state.{s}");
            Ext(V, "Screen fractions of the target's anchor", q + "target.position");
            Ext(F, "Metres; FLT_MAX with no target", q + "target.distance");
        }
        for (var n = 1; n <= 16; n++)
        {
            var q = $"{p}commandPost{n}.";
            Ext(U, "The owning team's icon", q + "icon");
            Ext(B, "", q + "iconDisable", q + "disable", q + "onScreen", q + "offScreen");
            Ext(C, "", q + "color", q + "captureColor");
            Ext(F, "", q + "capture", q + "distance");
            Ext(V, "Screen fractions of the post's marker", q + "position");
            Ext(V, "(0, 0, degrees) toward the post", q + "direction");
        }
        return map;
    }
}
