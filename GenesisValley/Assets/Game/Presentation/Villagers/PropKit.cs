using System.Collections.Generic;
using UnityEngine;

namespace GenesisValley.Presentation
{
    public enum PropKind { None, Axe, Hammer, Rod, Bucket, Basket, Sickle, Log, Logs, Sack, WaterBucket, SeedBag }

    /// <summary>
    /// Small hand-held props built from primitives in the village palette (GDD §8: don't import a new style pack for a
    /// bucket). Grip at the origin, handle along +Y, so the same alignment works for every tool.
    /// </summary>
    public static class PropKit
    {
        public static GameObject Build(PropKind kind, Transform parent)
        {
            var root = new GameObject("Prop_" + kind);
            root.transform.SetParent(parent, false);
            var t = root.transform;
            var wood = MaterialLibrary.ForKey("Wood");
            var dark = MaterialLibrary.ForKey("DarkWood");
            var metal = MaterialLibrary.ForKey("Metal");
            var rope = MaterialLibrary.ForKey("Rope");
            var straw = MaterialLibrary.ForKey("Straw");
            var cloth = MaterialLibrary.ForKey("Cloth");
            var grain = MaterialLibrary.ForKey("EndGrain");
            switch (kind)
            {
                case PropKind.Axe:
                    Cyl(t, wood, grain, new Vector3(0, 0.22f, 0), Quaternion.identity, 0.035f, 0.78f);
                    Box(t, metal, new Vector3(0.07f, 0.56f, 0), Quaternion.identity, new Vector3(0.15f, 0.12f, 0.03f));
                    Box(t, metal, new Vector3(0.15f, 0.56f, 0), Quaternion.identity, new Vector3(0.03f, 0.17f, 0.035f));
                    Box(t, dark, new Vector3(-0.02f, 0.56f, 0), Quaternion.identity, new Vector3(0.05f, 0.08f, 0.05f));
                    break;
                case PropKind.Hammer:
                    Cyl(t, wood, grain, new Vector3(0, 0.12f, 0), Quaternion.identity, 0.03f, 0.42f);
                    Box(t, metal, new Vector3(0, 0.33f, 0), Quaternion.identity, new Vector3(0.15f, 0.06f, 0.06f));
                    break;
                case PropKind.Sickle:
                    Cyl(t, wood, grain, new Vector3(0, 0.06f, 0), Quaternion.identity, 0.03f, 0.22f);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i / 4f * 150f * Mathf.Deg2Rad;
                        var p = new Vector3(Mathf.Sin(a) * 0.13f, 0.18f + (1f - Mathf.Cos(a)) * 0.08f + Mathf.Sin(a) * 0.05f, 0);
                        Box(t, metal, p, Quaternion.Euler(0, 0, -a * Mathf.Rad2Deg * 0.9f), new Vector3(0.07f, 0.025f, 0.012f));
                    }
                    break;
                case PropKind.Rod:
                    Cyl(t, wood, grain, new Vector3(0, 0.95f, 0), Quaternion.identity, 0.025f, 2.2f);
                    Cyl(t, rope, rope, new Vector3(0, 0.05f, 0), Quaternion.identity, 0.04f, 0.18f);
                    var tip = new GameObject("Tip").transform; tip.SetParent(t, false); tip.localPosition = new Vector3(0, 2.05f, 0);
                    break;
                case PropKind.Bucket:
                case PropKind.WaterBucket:
                    // Bail at the grip; bucket hangs below.
                    Cyl(t, wood, dark, new Vector3(0, -0.24f, 0), Quaternion.identity, 0.26f, 0.28f);
                    Cyl(t, metal, metal, new Vector3(0, -0.16f, 0), Quaternion.identity, 0.27f, 0.025f);
                    Cyl(t, metal, metal, new Vector3(0, -0.32f, 0), Quaternion.identity, 0.265f, 0.025f);
                    Box(t, rope, new Vector3(0, -0.04f, 0), Quaternion.identity, new Vector3(0.26f, 0.015f, 0.015f));
                    Box(t, rope, new Vector3(0.125f, -0.1f, 0), Quaternion.identity, new Vector3(0.015f, 0.12f, 0.015f));
                    Box(t, rope, new Vector3(-0.125f, -0.1f, 0), Quaternion.identity, new Vector3(0.015f, 0.12f, 0.015f));
                    if (kind == PropKind.WaterBucket)
                    {
                        var water = MaterialLibrary.ForKey("Water");
                        Cyl(t, water, water, new Vector3(0, -0.14f, 0), Quaternion.identity, 0.235f, 0.012f);
                    }
                    break;
                case PropKind.Basket:
                    Cyl(t, straw, straw, new Vector3(0, -0.2f, 0), Quaternion.identity, 0.34f, 0.22f);
                    Box(t, straw, new Vector3(0, -0.02f, 0), Quaternion.identity, new Vector3(0.3f, 0.02f, 0.03f));
                    var food = MaterialLibrary.Lit(new Color(0.83f, 0.62f, 0.28f), 0.2f);
                    for (int i = 0; i < 4; i++) Sphere(t, i % 2 == 0 ? food : MaterialLibrary.Lit(new Color(0.62f, 0.24f, 0.16f), 0.3f), new Vector3((i - 1.5f) * 0.06f, -0.08f, (i % 2 - 0.5f) * 0.06f), 0.08f);
                    break;
                case PropKind.SeedBag:
                    Sphere(t, cloth, new Vector3(0, -0.14f, 0), 0.2f);
                    Cyl(t, rope, rope, new Vector3(0, -0.02f, 0), Quaternion.identity, 0.07f, 0.05f);
                    break;
                case PropKind.Sack:
                    Sphere(t, cloth, new Vector3(0, 0, 0), 0.42f, new Vector3(1f, 0.85f, 0.7f));
                    Cyl(t, rope, rope, new Vector3(0, 0.18f, 0), Quaternion.identity, 0.1f, 0.06f);
                    break;
                case PropKind.Log:
                    MeshKit.SpawnLog(t, Vector3.zero, Quaternion.identity, 1.5f, 0.22f, true);
                    break;
                case PropKind.Logs:
                    MeshKit.SpawnLog(t, new Vector3(-0.08f, 0, 0.05f), Quaternion.identity, 1.45f, 0.19f, true);
                    MeshKit.SpawnLog(t, new Vector3(0.09f, 0.02f, -0.06f), Quaternion.Euler(0, 4, 0), 1.55f, 0.18f, true);
                    MeshKit.SpawnLog(t, new Vector3(0.0f, 0.16f, 0.02f), Quaternion.Euler(0, -3, 0), 1.4f, 0.17f, true);
                    break;
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return root;
        }

        private static void Cyl(Transform p, Material side, Material cap, Vector3 pos, Quaternion rot, float d, float h)
            => MeshKit.Spawn(MeshKit.Cylinder, new[] { side, cap }, p, pos, rot, new Vector3(d, h, d), true, "Cyl");
        private static void Box(Transform p, Material m, Vector3 pos, Quaternion rot, Vector3 size)
            => MeshKit.Spawn(MeshKit.Box, m, p, pos, rot, size, true, "Box");
        private static void Sphere(Transform p, Material m, Vector3 pos, float d, Vector3? stretch = null)
        {
            var s = stretch ?? Vector3.one;
            MeshKit.Spawn(MeshKit.Sphere, m, p, pos, Quaternion.identity, new Vector3(d * s.x, d * s.y, d * s.z), true, "Sphere");
        }
    }
}
