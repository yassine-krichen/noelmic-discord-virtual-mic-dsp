namespace NoelMic.Core;

/// <summary>
/// Point-in-time snapshot of every meter the Settings UI polls, bundled into
/// one value type so a single poll allocates nothing on the heap.
/// </summary>
public readonly record struct MeteringSnapshot(
    float InputLevelDb,
    float OutputLevelDb,
    bool GateIsOpen,
    float GateReductionDb,
    float CompressorReductionDb,
    float DeEsserReductionDb);
