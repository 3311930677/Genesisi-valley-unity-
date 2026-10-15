using System.Collections.Generic;

namespace GenesisValley.Core
{
    public enum CommandType
    {
        TerrainBrush = 1,
        River = 2,
        Forest = 3,
        Summon = 4,
        Bridge = 5,
        Undo = 6,
        EndTransaction = 7,
    }

    public enum BrushMode
    {
        Raise = 0,
        Lower = 1,
        Flatten = 2,
    }

    /// <summary>
    /// Player intent. Carries command id / player id / client sequence (GDD §10) so the host can
    /// de-duplicate and stamp an authoritative sequence + tick later (multiplayer is stage D).
    /// </summary>
    public abstract class GameCommand
    {
        public int CommandId;
        public int PlayerId;
        public int ClientSequence;
        public long AuthoritativeSequence;
        public long AppliedTick;
        public abstract CommandType Type { get; }
    }

    /// <summary>One continuous edit (press → release) is one transaction (GDD §4); undo works per transaction.</summary>
    public abstract class TransactionalCommand : GameCommand
    {
        public int TransactionId;
    }

    public sealed class TerrainBrushCommand : TransactionalCommand
    {
        public override CommandType Type => CommandType.TerrainBrush;
        public BrushMode Mode;
        public float CenterX;
        public float CenterZ;
        public float Radius;
        /// <summary>Peak height change in metres for this command (always positive; Mode gives the sign).
        /// For Flatten it is the maximum per-command approach toward the locked target.</summary>
        public float Strength;
        /// <summary>Flatten only: target height in metres locked when the stroke started (GDD §4).</summary>
        public float TargetHeight;
    }

    public sealed class ForestCommand : TransactionalCommand
    {
        public override CommandType Type => CommandType.Forest;
        public float CenterX;
        public float CenterZ;
        public float Radius;
        /// <summary>-1 = choose by terrain (pines on higher ground, broadleaf near water).</summary>
        public int Species = -1;
    }

    public sealed class RiverCommand : TransactionalCommand
    {
        public override CommandType Type => CommandType.River;
        /// <summary>Polyline control points in world metres, x/z interleaved.</summary>
        public readonly List<float> Points = new List<float>();
        public float Width;
    }

    public sealed class SummonCommand : GameCommand
    {
        public override CommandType Type => CommandType.Summon;
        public float X;
        public float Z;
        public int Count;
    }

    public sealed class BridgeCommand : GameCommand
    {
        public override CommandType Type => CommandType.Bridge;
        public float AX, AZ, BX, BZ;
    }

    /// <summary>Undo the player's most recent terrain/vegetation/river transaction if it can be safely reversed.</summary>
    public sealed class UndoCommand : GameCommand
    {
        public override CommandType Type => CommandType.Undo;
    }

    /// <summary>Marks the end of a drag so the host can finalise the undo record and heavy follow-up work.</summary>
    public sealed class EndTransactionCommand : TransactionalCommand
    {
        public override CommandType Type => CommandType.EndTransaction;
    }

    public struct CommandResult
    {
        public bool Accepted;
        public string Reason;
        public static CommandResult Ok() => new CommandResult { Accepted = true };
        public static CommandResult Reject(string reason) => new CommandResult { Accepted = false, Reason = reason };
    }
}
