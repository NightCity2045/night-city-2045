using System;
using System.Collections.Generic;

namespace Content.Server._NC.Forensics;

[RegisterComponent]
public sealed partial class NeuralPortBufferComponent : Component
{
    public readonly List<NeuralPortLogLine> Lines = new();

    [DataField("maxLines")]
    public int MaxLines = 10;

    public TimeSpan? TimeOfDeath;
    public string LastCriticalDamage = "Unknown";
}

public readonly record struct NeuralPortLogLine(TimeSpan Time, string Speaker, string Message, bool IsVictim);

