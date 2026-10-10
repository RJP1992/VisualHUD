using System.Numerics;

namespace SwbfHudEditor.Core.Layout;

/// <summary>
/// What the editor shows in the elements the game fills from its state: objectives, messages, a
/// vehicle's seats, targets on the screen, and the map's posts and units. Typical values, so their
/// layout can be judged; none is read from a game.
/// </summary>
public static class CodeSamples
{
    /// <summary>The team colours (<c>Team::GetColorByAffiliation</c>, P <c>00775C90</c>); neutral is taken to be white.</summary>
    public static readonly Vector3 Friend = new Vector3(1, 86, 213) / 255f;
    public static readonly Vector3 Enemy = new Vector3(223, 32, 32) / 255f;
    public static readonly Vector3 Neutral = Vector3.One;

    /// <summary>An objective as <c>AddMissionObjective</c> adds it, oldest first; only active and completed ones show.</summary>
    public sealed record Objective(string Text, bool Completed);

    public static readonly IReadOnlyList<Objective> Objectives = new[]
    {
        new Objective("Destroy the shield generator", Completed: true),
        new Objective("Capture the command post", Completed: false),
        new Objective("Defend the gate until reinforcements arrive", Completed: false),
    };

    /// <summary>A message, oldest first; a colour sent before it with <c>EventColor</c>, else the format's.</summary>
    public sealed record Message(string Text, Vector3? Color = null);

    public static readonly IReadOnlyList<Message> Messages = new[]
    {
        new Message("Player 2 captured the Main Gate"),
        new Message("Reinforcements: 80 remaining", Friend),
        new Message("You were killed by an AT-TE"),
    };

    public static readonly IReadOnlyList<Message> Tooltips = new[]
    {
        new Message("Press [Use] to enter the vehicle"),
    };

    /// <summary>A vehicle seat: 1 empty, 2 the player, 3 another player, 4 the AI (<c>SeatingState</c>).</summary>
    public sealed record Seat(int State, string Name);

    public static readonly IReadOnlyList<Seat> Seats = new[]
    {
        new Seat(2, "Player"),
        new Seat(4, "CT-7567"),
        new Seat(1, "Passenger"),
    };

    /// <summary>
    /// A marker the Target element draws: its template, where the target projects (x right, y down, -1
    /// to 1; an off-screen one already pinned to the 0.9 square), how far it is, and whose it is.
    /// </summary>
    public sealed record TargetMarker(string Template, Vector2 Ndc, float Distance, bool Common, bool Friendly);

    public static readonly IReadOnlyList<TargetMarker> Targets = new[]
    {
        new TargetMarker("ObjectiveOnScreen", new Vector2(-0.3f, -0.1f), 80f, Common: true, Friendly: false),
        new TargetMarker("FlagOnScreenBehind", new Vector2(0.2f, 0.15f), 300f, Common: true, Friendly: false),
        new TargetMarker("AttackerOffScreen", new Vector2(0.9f, 0.3f), 150f, Common: false, Friendly: false),
        new TargetMarker("LockedOnScreen", new Vector2(0.1f, 0.05f), 250f, Common: false, Friendly: false),
        new TargetMarker("HintOnScreen", new Vector2(-0.5f, 0.2f), 120f, Common: false, Friendly: true),
    };

    /// <summary>A command post on the map: its number, colour, and place in the world (x, z), with z toward the bottom.</summary>
    public sealed record Post(int Number, Vector3 Color, Vector2 World);

    public static readonly IReadOnlyList<Post> Posts = new[]
    {
        new Post(1, Friend, new Vector2(-60f, -40f)),
        new Post(2, Enemy, new Vector2(90f, 70f)),
        new Post(3, Neutral, new Vector2(-300f, 150f)),
    };

    /// <summary>A unit on the map: whose, where, which way it faces (degrees), and whether it is firing.</summary>
    public sealed record Unit(bool Friendly, Vector2 World, float Heading, bool Firing);

    public static readonly IReadOnlyList<Unit> Units = new[]
    {
        new Unit(true, new Vector2(20f, -30f), 45f, Firing: false),
        new Unit(false, new Vector2(-40f, 60f), 180f, Firing: true),
    };

    /// <summary>The player, at the world's origin facing north; the level's map 800 m across around it.</summary>
    public const float MapRadius = 400f;

    /// <summary>What the minimap shows around the player on foot (<c>MapViewMin</c>, P <c>00501AE0</c>).</summary>
    public const float MiniMapRadius = 125f;
}
