using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.World;

namespace GenesisValley.Simulation
{
    public enum UndoKind
    {
        Terrain = 0,
        Forest = 1,
        River = 2,
    }

    public struct TreeSnapshot
    {
        public int Id;
        public TreeSpecies Species;
        public int Variant;
        public float X, Z, Rotation, Scale, Growth;
        public int Wood;
        public int PlantedBy;
        public long FelledTick;
    }

    /// <summary>One reversible player transaction (press → release). Only terrain, vegetation and rivers are undoable (GDD §4).</summary>
    public sealed class UndoRecord
    {
        public int PlayerId;
        public int TransactionId;
        public UndoKind Kind;
        public long Tick;
        public string Label;
        public readonly HeightEditRecorder Heights = new HeightEditRecorder();
        public readonly Dictionary<int, float> After = new Dictionary<int, float>();
        public readonly List<EntityId> PlantedTrees = new List<EntityId>();
        public readonly List<TreeSnapshot> RemovedTrees = new List<TreeSnapshot>();
        public int RiverId;
        public bool Finalized;
    }

    /// <summary>Per-player LIFO of reversible transactions. Not saved: history starts fresh after loading.</summary>
    public sealed class UndoHistory
    {
        public readonly List<UndoRecord> Records = new List<UndoRecord>();
        public int Depth = 24;

        public UndoRecord Open(int playerId, int transactionId, UndoKind kind, long tick, string label, HeightField h)
        {
            for (int i = Records.Count - 1; i >= 0; i--)
            {
                var r = Records[i];
                if (!r.Finalized && r.PlayerId == playerId && r.TransactionId == transactionId && r.Kind == kind) return r;
            }
            // A new transaction implicitly closes this player's previous open one.
            foreach (var r in Records) if (!r.Finalized && r.PlayerId == playerId) Finalize(r, h);
            var rec = new UndoRecord { PlayerId = playerId, TransactionId = transactionId, Kind = kind, Tick = tick, Label = label };
            Records.Add(rec);
            while (Records.Count > Depth) Records.RemoveAt(0);
            return rec;
        }

        public void Finalize(UndoRecord r, HeightField h)
        {
            if (r.Finalized) return;
            r.Finalized = true;
            if (h == null) return;
            foreach (var kv in r.Heights.Before) r.After[kv.Key] = h.Heights[kv.Key];
        }

        public void FinalizeAll(int playerId, HeightField h)
        {
            foreach (var r in Records) if (!r.Finalized && r.PlayerId == playerId) Finalize(r, h);
        }

        public UndoRecord Latest(int playerId)
        {
            for (int i = Records.Count - 1; i >= 0; i--) if (Records[i].PlayerId == playerId) return Records[i];
            return null;
        }

        public int CountFor(int playerId)
        {
            int n = 0;
            foreach (var r in Records) if (r.PlayerId == playerId) n++;
            return n;
        }

        public void Clear() => Records.Clear();
    }
}
