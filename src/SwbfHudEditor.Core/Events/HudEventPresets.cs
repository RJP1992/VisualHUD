using System.Numerics;

namespace SwbfHudEditor.Core.Events;

/// <summary>
/// Ready-made scenarios: the events the game would have sent by a given moment, in order, so a HUD can
/// be seen as it plays. The values are typical, not read from a mission.
/// </summary>
public static class HudEventPresets
{
    /// <summary>
    /// Spawned on foot with two weapons, full health and energy, mid-match: what most of a HUD shows.
    /// The weapons are the first names the HUD's TransformNameMesh tables know, so their models show.
    /// </summary>
    public static IReadOnlyList<(string Name, HudEventValue Value)> OnFoot(HudTransformGraph transforms)
    {
        string Weapon(string changeEvent, string prefer, string fallback)
        {
            var rows = transforms.Nodes.OfType<NameMeshNode>()
                .Where(n => string.Equals(n.Input, changeEvent, StringComparison.OrdinalIgnoreCase))
                .SelectMany(n => n.Rows.Select(r => r.Source)).ToList();
            return rows.FirstOrDefault(r => r.Contains(prefer, StringComparison.OrdinalIgnoreCase)) ?? rows.FirstOrDefault() ?? fallback;
        }

        var sends = new List<(string, HudEventValue)>
        {
            ("initialize", HudEventValue.FromBool(true)),
            ("time", HudEventValue.FromFloat(0f)),
            ("player1.spawn", HudEventValue.FromBool(true)),
            ("player1.weaponsEnable", HudEventValue.FromBool(true)),
            ("player1.health", HudEventValue.FromUint(300)),
            ("player1.healthFraction", HudEventValue.FromFloat(1f)),
            ("player1.bonusHealthFraction", HudEventValue.FromFloat(0f)),
            ("player1.energy", HudEventValue.FromFloat(100f)),
            ("player1.energyFraction", HudEventValue.FromFloat(1f)),
            ("player1.weapon1.change", HudEventValue.FromName(Weapon("player1.weapon1.change", "rifle", "rep_weap_inf_rifle"))),
            ("player1.weapon1.name", HudEventValue.FromString("Blaster Rifle")),
            ("player1.weapon1.totalClipBullets", HudEventValue.FromUint(30)),
            ("player1.weapon1.totalClipFraction", HudEventValue.FromFloat(1f)),
            ("player1.weapon1.totalAmmoBullets", HudEventValue.FromUint(200)),
            ("player1.weapon1.totalAmmoFraction", HudEventValue.FromFloat(1f)),
            ("player1.weapon1.heat", HudEventValue.FromFloat(0f)),
            ("player1.weapon1.reticule.position", HudEventValue.FromVector(new Vector3(0.5f, 0.5f, 0f))),
            ("player1.weapon2.change", HudEventValue.FromName(Weapon("player1.weapon2.change", "thermal", "rep_weap_inf_thermaldetonator"))),
            ("player1.weapon2.name", HudEventValue.FromString("Thermal Detonator")),
            ("player1.weapon2.totalAmmoBullets", HudEventValue.FromUint(4)),
            ("player1.weapon2.totalAmmoFraction", HudEventValue.FromFloat(1f)),
        };
        for (var team = 1; team <= 2; team++)
        {
            var t = $"player1.team{team}.";
            sends.Add((t + "reinforcements", HudEventValue.FromInt(150)));
            sends.Add((t + "reinforcementsFraction", HudEventValue.FromFloat(1f)));
            sends.Add((t + "points", HudEventValue.FromInt(0)));
            sends.Add((t + "pointsText", HudEventValue.FromString("0")));
        }
        return sends;
    }
}
