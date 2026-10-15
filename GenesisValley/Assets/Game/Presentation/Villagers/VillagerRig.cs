using System;
using UnityEngine;

namespace GenesisValley.Presentation
{
    /// <summary>Animator state names shared by the runtime and the editor's controller builder.</summary>
    public static class VillagerAnim
    {
        public const string Idle = "Idle";
        public const string Locomotion = "Locomotion";   // blend tree on "Gait": 0 walk, 1 jog, 2 sprint
        public const string Talk = "Talk";
        public const string Chop = "Chop";
        public const string Hammer = "Hammer";
        public const string Kneel = "Kneel";
        public const string Harvest = "Harvest";
        public const string Fish = "Fish";
        public const string Interact = "Interact";
        public const string PickUp = "PickUp";
        public const string Sit = "Sit";
        public const string SitTalk = "SitTalk";
        public const string Lie = "Lie";
        public const string Crouch = "Crouch";
        public const string Wave = "Wave";
        public const string Cheer = "Cheer";

        public const string GaitParam = "Gait";
        public const string SpeedParam = "MoveSpeed";

        /// <summary>Root-motion stride of the UAL clips (metres per cycle / cycle seconds).</summary>
        public const float WalkDistance = 1.3f, WalkCycle = 1.333f, JogDistance = 5.0f, JogCycle = 0.933f, SprintDistance = 5.5f, SprintCycle = 0.667f;

        public static float VisualSpeed(float gait)
        {
            if (gait <= 1f) return Mathf.Lerp(WalkDistance, JogDistance, gait) / Mathf.Lerp(WalkCycle, JogCycle, gait);
            float g = gait - 1f;
            return Mathf.Lerp(JogDistance, SprintDistance, g) / Mathf.Lerp(JogCycle, SprintCycle, g);
        }

        /// <summary>Gait blend that makes the feet match <paramref name="speed"/> (m/s) at animator speed 1.</summary>
        public static float SolveGait(float speed)
        {
            if (speed <= VisualSpeed(0f)) return 0f;
            float lo = 0f, hi = 2f;
            for (int i = 0; i < 18; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (VisualSpeed(mid) < speed) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }
    }

    /// <summary>
    /// Appearance + attachment data on the villager prefab (filled by the editor's character builder). The runtime
    /// picks one option from each part group by AppearanceSeed and tints skin/hair/cloth with property blocks, so 50
    /// villagers share a handful of materials.
    /// </summary>
    public sealed class VillagerRig : MonoBehaviour
    {
        [Serializable]
        public sealed class PartGroup
        {
            public string name;
            public GameObject[] options = new GameObject[0];
            [Range(0, 1)] public float noneChance;
            public bool hair;   // tint with hair colour
            public bool cloth;  // tint with cloth variation
            [Tooltip("Hide this whole group when any active part's name contains this text (e.g. hair under a hood).")]
            public string suppressedBy = "";
        }

        public Animator animator;
        public PartGroup[] groups = new PartGroup[0];
        public Renderer[] skinRenderers = new Renderer[0];
        public Renderer[] alwaysOnRenderers = new Renderer[0];
        [Tooltip("Uniform scale applied to reach the GDD's 1.65–1.8 m adult height.")]
        public float heightScale = 1f;

        [Header("Hand-held prop alignment (relative to the bind pose)")]
        public Vector3 rightHandOffset = new Vector3(0.07f, -0.02f, 0.02f);
        public Vector3 leftHandOffset = new Vector3(-0.07f, -0.02f, 0.02f);
        public Vector3 propEulerAdjust = Vector3.zero;

        [NonSerialized] public Transform RightHand, LeftHand, Head, Chest, Hips, RightUpperArm, Neck;
        [NonSerialized] public Quaternion RightHandBindToProp = Quaternion.identity, LeftHandBindToProp = Quaternion.identity;
        [NonSerialized] public Vector3 RightHandLocalOffset, LeftHandLocalOffset;
        [NonSerialized] public bool BonesFound;

        private static readonly Color[] SkinTones =
        {
            new Color(1.00f, 0.86f, 0.74f), new Color(0.96f, 0.78f, 0.64f), new Color(0.88f, 0.68f, 0.52f),
            new Color(0.76f, 0.56f, 0.42f), new Color(0.62f, 0.44f, 0.32f), new Color(0.98f, 0.82f, 0.70f),
        };
        private static readonly Color[] HairColors =
        {
            new Color(0.16f, 0.11f, 0.08f), new Color(0.30f, 0.19f, 0.11f), new Color(0.45f, 0.29f, 0.16f),
            new Color(0.62f, 0.42f, 0.22f), new Color(0.78f, 0.62f, 0.38f), new Color(0.55f, 0.25f, 0.12f),
            new Color(0.08f, 0.07f, 0.07f), new Color(0.62f, 0.60f, 0.58f),
        };
        private static readonly Color[] ClothTints =
        {
            Color.white, new Color(0.92f, 0.96f, 1.0f), new Color(1.0f, 0.94f, 0.86f), new Color(0.9f, 1.0f, 0.9f),
            new Color(1.0f, 0.9f, 0.9f), new Color(0.95f, 0.92f, 1.0f),
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Must be called on a freshly instantiated rig at identity rotation (bind pose) – see VillagerView.</summary>
        public void CaptureBindPose()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                RightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                LeftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Head = animator.GetBoneTransform(HumanBodyBones.Head);
                Neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                Chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
                Hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                RightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            }
            if (RightHand == null) RightHand = FindDeep(transform, "hand_r", "Hand_R", "RightHand", "mixamorig:RightHand");
            if (LeftHand == null) LeftHand = FindDeep(transform, "hand_l", "Hand_L", "LeftHand", "mixamorig:LeftHand");
            if (Head == null) Head = FindDeep(transform, "Head", "head");
            if (Neck == null) Neck = FindDeep(transform, "neck_01", "Neck");
            if (Chest == null) Chest = FindDeep(transform, "spine_03", "Spine2", "Chest", "spine_02");
            BonesFound = RightHand != null && LeftHand != null && Head != null;
            // In the bind (T) pose a tool gripped in the fist points forward (+Z world); the prop's own axis is +Y.
            var propBind = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(propEulerAdjust);
            if (RightHand != null)
            {
                RightHandBindToProp = Quaternion.Inverse(RightHand.rotation) * propBind;
                RightHandLocalOffset = Quaternion.Inverse(RightHand.rotation) * (transform.rotation * rightHandOffset) / Mathf.Max(0.0001f, RightHand.lossyScale.x);
            }
            if (LeftHand != null)
            {
                LeftHandBindToProp = Quaternion.Inverse(LeftHand.rotation) * propBind;
                LeftHandLocalOffset = Quaternion.Inverse(LeftHand.rotation) * (transform.rotation * leftHandOffset) / Mathf.Max(0.0001f, LeftHand.lossyScale.x);
            }
        }

        private static Transform FindDeep(Transform t, params string[] names)
        {
            foreach (var n in names) if (t.name == n) return t;
            foreach (Transform c in t) { var r = FindDeep(c, names); if (r != null) return r; }
            return null;
        }

        public void Apply(int seed, int gender)
        {
            var rng = new System.Random(seed * 7919 + 17);
            var block = new MaterialPropertyBlock();
            var skin = SkinTones[rng.Next(SkinTones.Length)];
            var hairC = HairColors[rng.Next(HairColors.Length)];
            if (rng.NextDouble() < 0.12) hairC = Color.Lerp(hairC, new Color(0.75f, 0.74f, 0.72f), 0.6f); // a few grey-haired elders
            var cloth = ClothTints[rng.Next(ClothTints.Length)];

            var picks = new int[groups.Length];
            for (int gi = 0; gi < groups.Length; gi++)
            {
                var g = groups[gi];
                picks[gi] = -1;
                if (g == null || g.options == null || g.options.Length == 0) continue;
                picks[gi] = rng.NextDouble() < g.noneChance ? -1 : rng.Next(g.options.Length);
            }
            // Second pass: groups suppressed by another active part (hair under a hood, beard on a hooded ranger is fine).
            for (int gi = 0; gi < groups.Length; gi++)
            {
                var g = groups[gi];
                if (g == null || string.IsNullOrEmpty(g.suppressedBy) || picks[gi] < 0) continue;
                for (int oj = 0; oj < groups.Length && picks[gi] >= 0; oj++)
                {
                    if (oj == gi || picks[oj] < 0 || groups[oj] == null) continue;
                    var other = groups[oj].options[picks[oj]];
                    if (other != null && other.name.IndexOf(g.suppressedBy, StringComparison.OrdinalIgnoreCase) >= 0) picks[gi] = -1;
                }
            }
            for (int gi = 0; gi < groups.Length; gi++)
            {
                var g = groups[gi];
                if (g == null || g.options == null) continue;
                for (int i = 0; i < g.options.Length; i++)
                {
                    var o = g.options[i];
                    if (o == null) continue;
                    bool on = picks[gi] >= 0 && o == g.options[picks[gi]];
                    o.SetActive(on);
                    if (!on) continue;
                    foreach (var r in o.GetComponentsInChildren<Renderer>(true))
                        TintParts(r, skin, g.hair ? hairC : (Color?)null, g.cloth ? cloth : (Color?)null, block);
                }
            }
            foreach (var r in skinRenderers) if (r != null) TintParts(r, skin, hairC, null, block);
            foreach (var r in alwaysOnRenderers) if (r != null) TintParts(r, skin, hairC, null, block);
            if (heightScale > 0f)
            {
                float h = heightScale * (0.96f + 0.08f * (float)rng.NextDouble()) * (gender == 1 ? 0.97f : 1f);
                transform.localScale = new Vector3(h, h, h);
            }
        }

        private static bool IsSkin(string n) => n.Contains("skin") || n.Contains("body") || n.Contains("superhero") || n.Contains("regular") || n.Contains("face") || n.Contains("head");
        private static bool IsHair(string n) => n.Contains("hair") || n.Contains("brow") || n.Contains("beard");

        /// <summary>
        /// Tints each sub-material by what it depicts: skin-like materials get the skin tone, hair the hair colour (the
        /// shipped hair textures are neutral grey, so the colour is boosted to compensate) and everything else the cloth
        /// variation. Eyes and mouths keep their texture colour.
        /// </summary>
        private static void TintParts(Renderer r, Color skin, Color? hair, Color? cloth, MaterialPropertyBlock block)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                string n = m.name.ToLowerInvariant();
                if (n.Contains("eye") || n.Contains("mouth") || n.Contains("teeth")) continue;
                Color tint;
                if (IsHair(n)) { if (hair == null) continue; var h = hair.Value; tint = new Color(Mathf.Min(1f, h.r * 1.75f), Mathf.Min(1f, h.g * 1.75f), Mathf.Min(1f, h.b * 1.75f), 1f); }
                else if (IsSkin(n)) tint = skin;
                else if (cloth != null) tint = cloth.Value;
                else continue;
                var baseC = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : (m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white);
                var c = new Color(baseC.r * tint.r, baseC.g * tint.g, baseC.b * tint.b, baseC.a);
                r.GetPropertyBlock(block, i);
                block.SetColor(BaseColorId, c);
                block.SetColor(ColorId, c);
                r.SetPropertyBlock(block, i);
            }
        }
    }
}
