using SwbfHudEditor.Core.Hud;

namespace SwbfHudEditor.Core.Edits;

/// <summary>
/// A single undoable mutation to the HUD document. Apply mutates the model to its "done" state,
/// Revert restores it. Edits must be idempotent with respect to Apply/Revert pairs so Undo/Redo
/// can be chained without drift.
/// </summary>
public interface IHudEdit
{
    void Apply();
    void Revert();

    /// <summary>Block the edit targets. Used by the UI to refocus/reselect on undo/redo.</summary>
    HudBlock? AffectedBlock { get; }

    /// <summary>Short human-readable description. Shown in the status bar after undo/redo.</summary>
    string Description { get; }
}
