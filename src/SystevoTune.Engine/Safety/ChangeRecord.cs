using System.Text.Json.Serialization;

namespace SystevoTune.Engine.Safety;

/// <summary>
/// One system change. Written to the run log BEFORE the change is applied, so a crash
/// mid-change still leaves an undo path behind.
/// </summary>
/// <remarks>
/// The field names and the time format are fixed by docs/05-safety-layer.md section 5.2.
/// Do not rename them: old log files must stay readable by new builds.
/// </remarks>
public sealed record ChangeRecord
{
    /// <summary>Unique id, <c>yyyy-MM-dd-NNN</c>. The sequence continues across runs on the same day.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Local time the record was written, to the second.</summary>
    [JsonPropertyName("time")]
    public required DateTime Time { get; init; }

    /// <summary>Engine module that owns the change, e.g. <c>PowerPlan</c>. Routes the undo.</summary>
    [JsonPropertyName("module")]
    public required string Module { get; init; }

    /// <summary>What was done, e.g. <c>SetActivePlan</c>.</summary>
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    /// <summary>What was changed — a registry value name, service name, or path.</summary>
    [JsonPropertyName("target")]
    public required string Target { get; init; }

    /// <summary>
    /// The value read from the live system just before the change. Undo restores exactly this.
    /// Never a Windows default, never assumed. <c>null</c> means the target did not exist.
    /// </summary>
    [JsonPropertyName("oldValue")]
    public string? OldValue { get; init; }

    /// <summary>The value the change writes. <c>null</c> means the target is removed.</summary>
    [JsonPropertyName("newValue")]
    public string? NewValue { get; init; }

    /// <summary>Set once the old value has been put back and the log rewritten.</summary>
    [JsonPropertyName("undone")]
    public bool Undone { get; init; }

    /// <summary>
    /// Whether this change can be put back at all. False only for deletions that are genuinely
    /// permanent, such as cleanup removing a temp file — the record still exists as an audit
    /// trail, and undo reports it as un-restorable instead of pretending it worked.
    /// </summary>
    /// <remarks>
    /// Not in the doc 5.2 example, and deliberately not <c>required</c>: a record written before
    /// this field existed reads back as undoable, which is what every record then was.
    /// </remarks>
    [JsonPropertyName("undoable")]
    public bool Undoable { get; init; } = true;
}
