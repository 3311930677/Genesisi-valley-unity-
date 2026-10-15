using System;
using System.Collections.Generic;

namespace GenesisValley.Presentation.Blueprints
{
    /// <summary>Construction phase a piece belongs to (GDD §13: foundation → walls → roof → done).</summary>
    public enum BuildPhase
    {
        Foundation = 0,
        Walls = 1,
        Roof = 2,
        Finish = 3,
        /// <summary>Temporary: visible only while the site is under construction.</summary>
        Scaffold = 4,
        /// <summary>Always visible from the moment the site is placed (stakes, string lines).</summary>
        Site = 5,
    }

    /// <summary>Procedural primitive used when a blueprint needs simple geometry (scaffold poles, planks, logs).</summary>
    public enum PrimitiveShape
    {
        None = 0,
        Box = 1,
        Cylinder = 2,
    }

    /// <summary>
    /// One placed piece of a modular building, in building-local metres (origin = footprint centre at pad height,
    /// +Z = front/door side, +X = right of facing; Unity left-handed). Engine-free so blueprints can be unit-tested and
    /// previewed outside the editor.
    /// </summary>
    public struct PiecePlacement
    {
        /// <summary>Model key (file name without extension, e.g. "Wall_Plaster_Straight"). Empty for primitives.</summary>
        public string Model;
        public PrimitiveShape Shape;
        /// <summary>Material key for primitives: Wood, DarkWood, Stone, Rope, Soil, Straw, Cloth.</summary>
        public string Material;
        public float X, Y, Z;
        public float Yaw;
        /// <summary>Extra rotation about local X (lean / slope) and Z (roll); Unity Euler order Z→X→Y.</summary>
        public float Pitch, Roll;
        public float SX, SY, SZ;
        public BuildPhase Phase;
        /// <summary>Construction progress (0–1) at which the piece appears.</summary>
        public float RevealAt;
        /// <summary>Warm window glow at night when the building is occupied.</summary>
        public bool NightGlow;
        /// <summary>Smoke emitter point (chimney top).</summary>
        public bool Smoke;
    }

    public sealed class Blueprint
    {
        public readonly List<PiecePlacement> Pieces = new List<PiecePlacement>();
        public float Width, Depth, Height;
        public float SmokeX, SmokeY, SmokeZ;
        public bool HasSmoke;
        /// <summary>Points where builders stand while working (local XZ), spread around the site.</summary>
        public readonly List<float> WorkSpots = new List<float>();
    }

    /// <summary>
    /// Modular building layouts assembled from the Medieval Village MegaKit (walls 2 m × 3.12 m, exterior face on +Z,
    /// interior to −Z; gables "Roof_Front_Brick6" sized for 6 m fronts; roofs "Roof_RoundTiles_WxD").
    /// Layouts were checked in an offline renderer (tools/preview) before being used in game.
    /// </summary>
    public static class BuildingBlueprints
    {
        public const float WallHeight = 3.12f;
        public const float Module = 2f;

        private static Blueprint _bp;
        private static int _order;

        private static void Begin(float width, float depth)
        {
            _bp = new Blueprint { Width = width, Depth = depth };
            _order = 0;
        }

        private static void Add(string model, float x, float y, float z, float yaw, BuildPhase phase, float scale = 1f, bool glow = false)
        {
            _bp.Pieces.Add(new PiecePlacement { Model = model, X = x, Y = y, Z = z, Yaw = yaw, SX = scale, SY = scale, SZ = scale, Phase = phase, NightGlow = glow, RevealAt = _order++ });
        }

        /// <summary>Primitive: Box size = (sx, sy, sz); Cylinder = diameter sx/sz, length sy along its local Y.</summary>
        private static void Prim(PrimitiveShape shape, string mat, float x, float y, float z, float sx, float sy, float sz, float yaw, float pitch, BuildPhase phase, float roll = 0f)
        {
            _bp.Pieces.Add(new PiecePlacement { Shape = shape, Material = mat, X = x, Y = y, Z = z, SX = sx, SY = sy, SZ = sz, Yaw = yaw, Pitch = pitch, Roll = roll, Phase = phase, RevealAt = _order++ });
        }

        /// <summary>Spreads the RevealAt order of each phase over its progress band.</summary>
        private static Blueprint Finish()
        {
            var bands = new Dictionary<BuildPhase, (float a, float b)>
            {
                { BuildPhase.Site, (0f, 0f) },
                { BuildPhase.Foundation, (0.0f, 0.28f) },
                { BuildPhase.Walls, (0.3f, 0.78f) },
                { BuildPhase.Roof, (0.8f, 0.97f) },
                { BuildPhase.Finish, (0.985f, 0.995f) },
                { BuildPhase.Scaffold, (0.06f, 0.3f) },
            };
            var counts = new Dictionary<BuildPhase, int>();
            var index = new Dictionary<BuildPhase, int>();
            foreach (var p in _bp.Pieces) { counts.TryGetValue(p.Phase, out int n); counts[p.Phase] = n + 1; }
            for (int i = 0; i < _bp.Pieces.Count; i++)
            {
                var p = _bp.Pieces[i];
                index.TryGetValue(p.Phase, out int k); index[p.Phase] = k + 1;
                var band = bands[p.Phase];
                int n = counts[p.Phase];
                p.RevealAt = n <= 1 ? band.a : band.a + (band.b - band.a) * k / (n - 1);
                _bp.Pieces[i] = p;
            }
            var bp = _bp; _bp = null;
            return bp;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Wall run along local X at depth z facing +Z (yaw 0) or −Z (yaw 180).</summary>
        private static void WallPiece(string wall, float x, float z, float yaw, float y, string window = null, string shutter = null)
        {
            Add(wall, x, y, z, yaw, BuildPhase.Walls);
            if (window != null) Add(window, x, y, z, yaw, BuildPhase.Finish, 1f, true);
            if (shutter != null) Add(shutter, x, y, z, yaw, BuildPhase.Finish);
        }

        private static void Floor(float w, float d, string tile)
        {
            for (float x = -w / 2 + 1; x < w / 2; x += Module)
            for (float z = -d / 2 + 1; z < d / 2; z += Module)
                Add(tile, x, 0.02f, z, 0, BuildPhase.Foundation);
        }

        private static void Stakes(float w, float d)
        {
            // Corner stakes + string line: the site is readable the moment it is planned.
            float hw = w / 2 + 0.3f, hd = d / 2 + 0.3f;
            foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                Prim(PrimitiveShape.Box, "Wood", sx * hw, 0.35f, sz * hd, 0.08f, 0.7f, 0.08f, 0, 0, BuildPhase.Site);
            Prim(PrimitiveShape.Box, "Rope", 0, 0.55f, hd, 2 * hw, 0.02f, 0.02f, 0, 0, BuildPhase.Site);
            Prim(PrimitiveShape.Box, "Rope", 0, 0.55f, -hd, 2 * hw, 0.02f, 0.02f, 0, 0, BuildPhase.Site);
            Prim(PrimitiveShape.Box, "Rope", hw, 0.55f, 0, 0.02f, 0.02f, 2 * hd, 0, 0, BuildPhase.Site);
            Prim(PrimitiveShape.Box, "Rope", -hw, 0.55f, 0, 0.02f, 0.02f, 2 * hd, 0, 0, BuildPhase.Site);
        }

        private static void Scaffold(float w, float d, float h)
        {
            float off = 0.9f;
            float hw = w / 2 + off, hd = d / 2 + off;
            // Vertical poles every ~2 m around the footprint.
            var poles = new List<(float x, float z)>();
            for (float x = -hw; x <= hw + 0.01f; x += Math.Max(1.8f, 2 * hw / Math.Max(1, (int)Math.Round(2 * hw / 2.2f))))
            {
                poles.Add((x, hd)); poles.Add((x, -hd));
            }
            for (float z = -hd + 2.2f; z < hd - 0.5f; z += 2.2f)
            {
                poles.Add((hw, z)); poles.Add((-hw, z));
            }
            foreach (var p in poles) Prim(PrimitiveShape.Cylinder, "Wood", p.x, h / 2, p.z, 0.11f, h, 0.11f, 0, 0, BuildPhase.Scaffold);
            // Ledgers (horizontal) at two heights on each side + planks.
            foreach (float y in new[] { 1.4f, 2.8f })
            {
                if (y > h - 0.3f) continue;
                Prim(PrimitiveShape.Cylinder, "Wood", 0, y, hd, 0.08f, 2 * hw + 0.3f, 0.08f, 0, 0, BuildPhase.Scaffold, 90);
                Prim(PrimitiveShape.Cylinder, "Wood", 0, y, -hd, 0.08f, 2 * hw + 0.3f, 0.08f, 0, 0, BuildPhase.Scaffold, 90);
                Prim(PrimitiveShape.Cylinder, "Wood", hw, y, 0, 0.08f, 2 * hd + 0.3f, 0.08f, 0, 90, BuildPhase.Scaffold);
                Prim(PrimitiveShape.Cylinder, "Wood", -hw, y, 0, 0.08f, 2 * hd + 0.3f, 0.08f, 0, 90, BuildPhase.Scaffold);
                Prim(PrimitiveShape.Box, "Plank", 0, y + 0.06f, hd - 0.35f, 2 * hw - 0.3f, 0.05f, 0.55f, 0, 0, BuildPhase.Scaffold);
                Prim(PrimitiveShape.Box, "Plank", hw - 0.35f, y + 0.06f, 0, 0.55f, 0.05f, 2 * hd - 0.3f, 0, 0, BuildPhase.Scaffold);
            }
            // A ladder leaning on the front.
            Prim(PrimitiveShape.Box, "Wood", hw - 0.9f, 1.5f, hd + 0.55f, 0.06f, 3.2f, 0.06f, 0, -16f, BuildPhase.Scaffold);
            Prim(PrimitiveShape.Box, "Wood", hw - 0.4f, 1.5f, hd + 0.55f, 0.06f, 3.2f, 0.06f, 0, -16f, BuildPhase.Scaffold);
        }

        private static void Work(params float[] xz) => _bp.WorkSpots.AddRange(xz);

        // ------------------------------------------------------------------ house (6 × 8 m)

        public static Blueprint House(int variant)
        {
            int v = ((variant % 3) + 3) % 3;
            const float w = 6f, d = 8f;
            Begin(w, d);
            bool brick = v == 1;
            bool twoStorey = v == 2;
            string wall = brick ? "Wall_UnevenBrick_Straight" : "Wall_Plaster_Straight";
            string winWall = brick ? "Wall_UnevenBrick_Window_Wide_Round" : "Wall_Plaster_Window_Wide_Flat";
            string win = brick ? "Window_Wide_Round1" : "Window_Wide_Flat1";
            string shut = brick ? "WindowShutters_Wide_Round_Open" : "WindowShutters_Wide_Flat_Open";
            string thinWall = brick ? "Wall_UnevenBrick_Window_Thin_Round" : "Wall_Plaster_Window_Thin_Round";
            string doorWall = brick ? "Wall_UnevenBrick_Door_Round" : "Wall_Plaster_Door_Flat";
            string door = brick ? "Door_2_Round" : "Door_1_Flat";
            string frame = brick ? "DoorFrame_Round_WoodDark" : "DoorFrame_Flat_WoodDark";

            Stakes(w, d);
            Floor(w, d, "Floor_WoodDark");
            // Doorstep + stone curb along the front.
            Add("Prop_ExteriorBorder_Straight1", 0, 0, d / 2, 0, BuildPhase.Foundation);
            foreach (float cx in new[] { -w / 2, w / 2 })
            foreach (float cz in new[] { -d / 2, d / 2 })
                Add("Corner_Exterior_Wood", cx, 0, cz, 0, BuildPhase.Foundation);

            Scaffold(w, d, twoStorey ? 6.2f : 3.6f);

            // Ground floor walls, ordered around the perimeter so the house "grows".
            WallPiece(winWall, 2, d / 2, 0, 0, win, shut);
            WallPiece(doorWall, 0, d / 2, 0, 0, null, null);
            WallPiece(v == 0 ? thinWall : winWall, -2, d / 2, 0, 0, v == 0 ? "Window_Thin_Round1" : win, v == 0 ? null : shut);
            for (int i = 0; i < 4; i++)
            {
                float z = d / 2 - 1 - i * Module;
                bool window = i == 1 || i == 2 && v != 1;
                WallPiece(window ? winWall : wall, -w / 2, z, 270, 0, window ? win : null, window ? shut : null);
            }
            WallPiece(wall, -2, -d / 2, 180, 0);
            WallPiece(thinWall, 0, -d / 2, 180, 0, "Window_Thin_Round1");
            WallPiece(wall, 2, -d / 2, 180, 0);
            for (int i = 0; i < 4; i++)
            {
                float z = -d / 2 + 1 + i * Module;
                bool window = i == 2 || i == 1 && v == 2;
                WallPiece(window ? winWall : wall, w / 2, z, 90, 0, window ? win : null, window ? shut : null);
            }
            float roofY = WallHeight;
            if (twoStorey)
            {
                // Upper storey in plaster with timber grid above a brick ground floor.
                for (int i = 0; i < 3; i++) WallPiece(i == 1 ? "Wall_Plaster_Window_Wide_Flat" : "Wall_Plaster_WoodGrid", 2 - 2 * i, d / 2, 0, WallHeight, i == 1 ? "Window_Wide_Flat1" : null, i == 1 ? "WindowShutters_Wide_Flat_Open" : null);
                for (int i = 0; i < 4; i++) WallPiece(i == 1 ? "Wall_Plaster_Window_Thin_Round" : "Wall_Plaster_Straight", -w / 2, d / 2 - 1 - i * Module, 270, WallHeight, i == 1 ? "Window_Thin_Round1" : null);
                for (int i = 0; i < 3; i++) WallPiece("Wall_Plaster_Straight", -2 + 2 * i, -d / 2, 180, WallHeight);
                for (int i = 0; i < 4; i++) WallPiece(i == 2 ? "Wall_Plaster_Window_Thin_Round" : "Wall_Plaster_Straight", w / 2, -d / 2 + 1 + i * Module, 90, WallHeight, i == 2 ? "Window_Thin_Round1" : null);
                foreach (float cx in new[] { -w / 2, w / 2 })
                foreach (float cz in new[] { -d / 2, d / 2 })
                    Add("Corner_Exterior_Wood", cx, WallHeight, cz, 0, BuildPhase.Walls);
                roofY = 2 * WallHeight;
            }

            Add("Roof_Front_Brick6", 0, roofY, d / 2, 0, BuildPhase.Roof);
            Add("Roof_Front_Brick6", 0, roofY, -d / 2, 180, BuildPhase.Roof);
            Add("Roof_RoundTiles_6x8", 0, roofY, 0, 0, BuildPhase.Roof);
            Add("Prop_Chimney", 1.6f, roofY + 1.4f, -2.2f, 0, BuildPhase.Roof);
            _bp.HasSmoke = true; _bp.SmokeX = 1.6f; _bp.SmokeY = roofY + 1.4f + 3.2f; _bp.SmokeZ = -2.2f;

            Add(frame, 0, 0, d / 2, 0, BuildPhase.Finish);
            Add(door, 0.53f, 0, d / 2, 0, BuildPhase.Finish);
            // Small garden touches.
            Add("Prop_Crate", -w / 2 - 0.9f, 0, d / 2 - 1.2f, 15, BuildPhase.Finish, 0.8f);
            if (v != 1) Add("Prop_WoodenFence_Single", w / 2 + 1.6f, 0, d / 2 - 0.2f, 90, BuildPhase.Finish);

            _bp.Height = roofY + 4.9f;
            Work(-w / 2 - 1.3f, 1f, w / 2 + 1.3f, -1f, 1.5f, d / 2 + 1.4f, -1.5f, -d / 2 - 1.3f, -w / 2 - 1.3f, -2.5f, w / 2 + 1.3f, 2.5f);
            return Finish();
        }

        // ------------------------------------------------------------------ storehouse (sim footprint 7 × 9; walls 6 × 8 + lean-to)

        public static Blueprint Storehouse(int variant)
        {
            const float w = 6f, d = 8f;
            Begin(7f, 9f);
            Stakes(7f, 9f);
            Floor(w, d, "Floor_Brick");
            foreach (float cx in new[] { -w / 2, w / 2 })
            foreach (float cz in new[] { -d / 2, d / 2 })
                Add("Corner_ExteriorWide_Wood", cx, 0, cz, 0, BuildPhase.Foundation);
            Add("Prop_ExteriorBorder_Straight2", 0, 0, d / 2, 0, BuildPhase.Foundation);
            Scaffold(w, d, 3.6f);

            WallPiece("Wall_UnevenBrick_Straight", 2, d / 2, 0, 0);
            WallPiece("Wall_UnevenBrick_Door_Round", 0, d / 2, 0, 0);
            WallPiece("Wall_UnevenBrick_Straight", -2, d / 2, 0, 0);
            for (int i = 0; i < 4; i++) WallPiece(i == 1 ? "Wall_UnevenBrick_Window_Thin_Round" : "Wall_UnevenBrick_Straight", -w / 2, d / 2 - 1 - i * Module, 270, 0, i == 1 ? "Window_Thin_Round1" : null);
            for (int i = 0; i < 3; i++) WallPiece("Wall_UnevenBrick_Straight", -2 + 2 * i, -d / 2, 180, 0);
            for (int i = 0; i < 4; i++) WallPiece(i == 2 ? "Wall_UnevenBrick_Window_Thin_Round" : "Wall_UnevenBrick_Straight", w / 2, -d / 2 + 1 + i * Module, 90, 0, i == 2 ? "Window_Thin_Round1" : null);

            Add("Roof_Front_Brick6", 0, WallHeight, d / 2, 0, BuildPhase.Roof);
            Add("Roof_Front_Brick6", 0, WallHeight, -d / 2, 180, BuildPhase.Roof);
            Add("Roof_RoundTiles_6x8", 0, WallHeight, 0, 0, BuildPhase.Roof);
            Add("DoorFrame_Round_WoodDark", 0, 0, d / 2, 0, BuildPhase.Finish);
            Add("Door_8_Round", 0.53f, 0, d / 2, 0, BuildPhase.Finish);
            Add("Prop_Crate", 2.2f, 0, d / 2 + 1.0f, 10, BuildPhase.Finish);
            Add("Prop_Crate", 2.4f, 1.0f, d / 2 + 0.9f, 35, BuildPhase.Finish, 0.85f);
            Add("Prop_Crate", -2.3f, 0, d / 2 + 0.9f, -12, BuildPhase.Finish, 0.9f);
            Add("Prop_Wagon", w / 2 + 1.9f, 0, 0.5f, 8, BuildPhase.Finish);
            _bp.Height = WallHeight + 4.9f;
            Work(-w / 2 - 1.3f, 1f, w / 2 + 1.3f, -1f, 1.5f, d / 2 + 1.4f, -1.5f, -d / 2 - 1.3f);
            return Finish();
        }

        // ------------------------------------------------------------------ fishing hut (4 × 4) + jetty toward −Z

        public static Blueprint FishingHut(int variant, float jettyLength)
        {
            const float w = 4f, d = 4f;
            Begin(w, d);
            Stakes(w, d);
            Floor(w, d, "Floor_WoodLight");
            foreach (float cx in new[] { -w / 2, w / 2 })
            foreach (float cz in new[] { -d / 2, d / 2 })
                Add("Corner_Exterior_Wood", cx, 0, cz, 0, BuildPhase.Foundation);
            Scaffold(w, d, 3.4f);
            WallPiece("Wall_Plaster_Door_Flat", -1, d / 2, 0, 0);
            WallPiece("Wall_Plaster_Window_Thin_Round", 1, d / 2, 0, 0, "Window_Thin_Round1");
            WallPiece("Wall_Plaster_WoodGrid", -w / 2, 1, 270, 0);
            WallPiece("Wall_Plaster_Window_Wide_Flat", -w / 2, -1, 270, 0, "Window_Wide_Flat1", "WindowShutters_Wide_Flat_Open");
            WallPiece("Wall_Plaster_Straight", -1, -d / 2, 180, 0);
            WallPiece("Wall_Plaster_Straight", 1, -d / 2, 180, 0);
            WallPiece("Wall_Plaster_Straight", w / 2, -1, 90, 0);
            WallPiece("Wall_Plaster_WoodGrid", w / 2, 1, 90, 0);
            Add("Roof_Front_Brick4", 0, WallHeight, d / 2, 0, BuildPhase.Roof);
            Add("Roof_Front_Brick4", 0, WallHeight, -d / 2, 180, BuildPhase.Roof);
            Add("Roof_RoundTiles_4x4", 0, WallHeight, 0, 0, BuildPhase.Roof);
            Add("DoorFrame_Flat_WoodDark", -1, 0, d / 2, 0, BuildPhase.Finish);
            Add("Door_1_Flat", -0.47f, 0, d / 2, 0, BuildPhase.Finish);
            // Jetty: planks from the back wall to the fishing spot, on posts.
            float len = Math.Max(2f, jettyLength);
            float z0 = -d / 2 - 0.2f;
            for (float s = 0.4f; s < len; s += 0.45f)
                Prim(PrimitiveShape.Box, "Plank", 0, 0.12f, z0 - s, 1.6f, 0.07f, 0.4f, 0, 0, BuildPhase.Foundation);
            for (float s = 1.2f; s < len + 0.2f; s += 1.8f)
            {
                Prim(PrimitiveShape.Cylinder, "DarkWood", -0.8f, -0.5f, z0 - s, 0.14f, 1.4f, 0.14f, 0, 0, BuildPhase.Foundation);
                Prim(PrimitiveShape.Cylinder, "DarkWood", 0.8f, -0.5f, z0 - s, 0.14f, 1.4f, 0.14f, 0, 0, BuildPhase.Foundation);
            }
            Add("Prop_Crate", 1.4f, 0, d / 2 + 0.8f, 20, BuildPhase.Finish, 0.7f);
            _bp.Height = WallHeight + 3.8f;
            Work(-w / 2 - 1.2f, 0, w / 2 + 1.2f, 0, 0, d / 2 + 1.3f);
            return Finish();
        }

        // ------------------------------------------------------------------ bridge (procedural deck between A and B)

        /// <summary>Bridge pieces in a local frame: origin at the midpoint, +Z along the span, deck length = length.</summary>
        public static Blueprint Bridge(float length, float rise)
        {
            Begin(2.6f, length);
            float half = length / 2;
            int planks = Math.Max(4, (int)(length / 0.42f));
            for (int i = 0; i < planks; i++)
            {
                float t = (i + 0.5f) / planks;
                float z = -half + t * length;
                float y = rise * 4f * t * (1 - t); // gentle arch
                float slope = (float)(Math.Atan(rise * 4f * (1 - 2 * t) / length) * 180.0 / Math.PI);
                Prim(PrimitiveShape.Box, i % 3 == 0 ? "DarkWood" : "Plank", 0, y + 0.05f, z, 2.4f, 0.08f, length / planks * 0.92f, 0, -slope, BuildPhase.Walls);
            }
            // Stringers + posts + rails.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = 0; k <= Math.Max(2, (int)(length / 2.5f)); k++)
                {
                    float t = k / (float)Math.Max(2, (int)(length / 2.5f));
                    float z = -half + t * length;
                    float y = rise * 4f * t * (1 - t);
                    Prim(PrimitiveShape.Cylinder, "DarkWood", side * 1.15f, y - 0.9f, z, 0.16f, 2.0f, 0.16f, 0, 0, BuildPhase.Foundation);
                    Prim(PrimitiveShape.Box, "Wood", side * 1.2f, y + 0.55f, z, 0.1f, 1.0f, 0.1f, 0, 0, BuildPhase.Roof);
                }
                for (int k = 0; k < 6; k++)
                {
                    float t0 = k / 6f, t1 = (k + 1) / 6f;
                    float za = -half + t0 * length, zb = -half + t1 * length;
                    float ya = rise * 4f * t0 * (1 - t0), yb = rise * 4f * t1 * (1 - t1);
                    float segLen = (float)Math.Sqrt((zb - za) * (zb - za) + (yb - ya) * (yb - ya));
                    float ang = (float)(Math.Atan2(yb - ya, zb - za) * 180.0 / Math.PI);
                    Prim(PrimitiveShape.Box, "Wood", side * 1.2f, (ya + yb) / 2 + 1.0f, (za + zb) / 2, 0.09f, 0.09f, segLen + 0.05f, 0, -ang, BuildPhase.Roof);
                    Prim(PrimitiveShape.Box, "DarkWood", side * 1.1f, (ya + yb) / 2 - 0.05f, (za + zb) / 2, 0.16f, 0.22f, segLen + 0.05f, 0, -ang, BuildPhase.Foundation);
                }
            }
            _bp.Height = rise + 1.2f;
            Work(0, -half - 0.8f, 0, half + 0.8f);
            return Finish();
        }
    }
}
