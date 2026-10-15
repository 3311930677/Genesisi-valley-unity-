using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenesisValley.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace GenesisValley.EditorTools
{
    /// <summary>
    /// Builds the two villager prefabs from Quaternius' Universal Base Characters + Modular Fantasy Outfits:
    /// the full body is cut down to head + neck (the outfits replace the rest, as the pack's readme recommends),
    /// every outfit/hair part is re-bound onto the base humanoid skeleton and grouped for <see cref="VillagerRig"/>
    /// so each villager gets its own outfit, hair, beard and hood from its appearance seed.
    /// Also builds the shared animator controller from the Universal Animation Library.
    /// </summary>
    public static class GVCharacterBuilder
    {
        public const string ControllerPath = GVPaths.CharacterOut + "/GV_Villager.controller";
        private const float TargetHeight = 1.74f;
        private static readonly HashSet<string> HeadBones = new HashSet<string> { "Head", "neck_01", "head", "neck" };

        // ------------------------------------------------------------------ animator

        private static AnimationClip[] LibraryClips()
        {
            var path = GVPaths.Animations + "/UAL1_Standard.fbx";
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
        }

        private static AnimationClip Clip(AnimationClip[] all, string name)
        {
            foreach (var c in all) if (c.name == name) return c;
            foreach (var c in all) if (c.name.EndsWith("|" + name) || c.name.EndsWith(name)) return c;
            return null;
        }

        public static AnimatorController BuildController(List<string> log)
        {
            var clips = LibraryClips();
            if (clips.Length == 0) { log.Add("✖ 动画库 UAL1_Standard.fbx 未导入或没有动画片段"); return null; }
            GVPaths.EnsureFolder(GVPaths.CharacterOut);
            if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
            var c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            c.AddParameter(VillagerAnim.GaitParam, AnimatorControllerParameterType.Float);
            c.AddParameter(VillagerAnim.SpeedParam, AnimatorControllerParameterType.Float);
            var ps = c.parameters;
            foreach (var p in ps) if (p.name == VillagerAnim.SpeedParam) p.defaultFloat = 1f;
            c.parameters = ps;
            var sm = c.layers[0].stateMachine;

            AnimatorState Add(string state, string clip, float speed = 1f, string fallback = null)
            {
                var m = Clip(clips, clip) ?? (fallback != null ? Clip(clips, fallback) : null);
                if (m == null) log.Add($"⚠ 动画片段缺失：{clip}（状态 {state}）");
                var s = sm.AddState(state);
                s.motion = m; s.speed = speed; s.writeDefaultValues = true;
                return s;
            }

            var idle = Add(VillagerAnim.Idle, "Idle_Loop");
            sm.defaultState = idle;

            var loco = c.CreateBlendTreeInController(VillagerAnim.Locomotion, out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = VillagerAnim.GaitParam;
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip(clips, "Walk_Loop"), 0f);
            tree.AddChild(Clip(clips, "Jog_Fwd_Loop"), 1f);
            tree.AddChild(Clip(clips, "Sprint_Loop"), 2f);
            loco.speedParameterActive = true;
            loco.speedParameter = VillagerAnim.SpeedParam;
            loco.writeDefaultValues = true;

            Add(VillagerAnim.Talk, "Idle_Talking_Loop");
            Add(VillagerAnim.Chop, "Sword_Attack", 0.85f);
            Add(VillagerAnim.Hammer, "Sword_Attack", 1.15f);
            Add(VillagerAnim.Kneel, "Fixing_Kneeling");
            Add(VillagerAnim.Harvest, "PickUp_Table", 0.8f, "Fixing_Kneeling");
            Add(VillagerAnim.Fish, "Spell_Simple_Idle_Loop", 0.6f, "Idle_Loop");
            Add(VillagerAnim.Interact, "Interact");
            Add(VillagerAnim.PickUp, "PickUp_Table");
            Add(VillagerAnim.Sit, "Sitting_Idle_Loop");
            Add(VillagerAnim.SitTalk, "Sitting_Talking_Loop", 1f, "Sitting_Idle_Loop");
            Add(VillagerAnim.Lie, "Death01", 0.7f);
            Add(VillagerAnim.Crouch, "Crouch_Idle_Loop");
            Add(VillagerAnim.Wave, "Idle_Talking_Loop", 1.25f);
            Add(VillagerAnim.Cheer, "Dance_Loop");
            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            return c;
        }

        // ------------------------------------------------------------------ villagers

        private sealed class Part { public string File; public Dictionary<string, string> Swap; }

        private static Part P(string file, Dictionary<string, string> swap = null) => new Part { File = file, Swap = swap };

        public static GameObject BuildVillager(bool female, AnimatorController controller, Dictionary<string, Material> mats, List<string> log)
        {
            string g = female ? "Female" : "Male";
            string basePath = $"{GVPaths.Characters}/Base/Superhero_{g}_FullBody.fbx";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            if (src == null) { log.Add($"✖ 缺少角色模型 {basePath}"); return null; }
            var root = Object.Instantiate(src);
            root.name = "Villager_" + g;
            try
            {
                var anim = root.GetComponent<Animator>();
                if (anim == null) anim = root.AddComponent<Animator>();
                if (anim.avatar == null || !anim.avatar.isHuman) log.Add($"⚠ {g} 角色的 Avatar 不是有效的人形骨骼，动画可能无法重定向");
                anim.runtimeAnimatorController = controller;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                var bones = new Dictionary<string, Transform>();
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;

                var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var body = smrs.OrderByDescending(s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0).First();
                float height = MeasureHeight(body);
                var head = CutHead(body, $"{GVPaths.CharacterOut}/{g}_HeadOnly.asset", log);
                if (head != null) body.sharedMesh = head;
                body.name = "Head";
                var always = smrs.Where(s => s != body).Cast<Renderer>().ToList();
                foreach (var r in smrs) Tidy(r);

                var groups = new List<VillagerRig.PartGroup>();
                var outfits = new GameObject("Outfits").transform; outfits.SetParent(root.transform, false);
                string O(string part) => $"{GVPaths.Characters}/Outfits/{g}_{part}.fbx";
                var peasant = new[] { P(O("Peasant_Body")), P(O("Peasant_Arms")), P(O("Peasant_Legs")), P(O("Peasant_Feet")) };
                var swapPeasant = new Dictionary<string, string> { { "MI_Peasant", "MI_Peasant_2" } };
                var swapRanger = new Dictionary<string, string> { { "MI_Ranger", "MI_Ranger_3" } };
                string feet = female ? "Ranger_Feet" : "Ranger_Feet_Boots";
                string pauldron = female ? "Ranger_Acc_Pauldrons" : "Ranger_Acc_Pauldron";
                var outfitOptions = new List<GameObject>
                {
                    Assemble("Outfit_Peasant", outfits, bones, peasant, mats, log),
                    Assemble("Outfit_Peasant_Dark", outfits, bones, peasant.Select(p => P(p.File, swapPeasant)).ToArray(), mats, log),
                    Assemble("Outfit_Peasant_Patched", outfits, bones, new[] { P(O("Peasant_Body")), P(O("Peasant_Arms"), swapPeasant), P(O("Peasant_Legs"), swapPeasant), P(O("Peasant_Feet")) }, mats, log),
                    Assemble("Outfit_Ranger", outfits, bones, new[] { P(O("Ranger_Body")), P(O("Ranger_Arms")), P(O("Ranger_Legs")), P(O(feet)), P(O(pauldron)) }, mats, log),
                    Assemble("Outfit_Ranger_Moss", outfits, bones, new[] { P(O("Ranger_Body"), swapRanger), P(O("Ranger_Arms"), swapRanger), P(O("Ranger_Legs"), swapRanger), P(O(feet), swapRanger) }, mats, log),
                };
                groups.Add(new VillagerRig.PartGroup { name = "Outfit", options = outfitOptions.Where(o => o != null).ToArray(), cloth = true });

                var hoods = new GameObject("Hoods").transform; hoods.SetParent(root.transform, false);
                var hood = Assemble("Hood_Ranger", hoods, bones, new[] { P(O("Ranger_Head_Hood")) }, mats, log);
                var hoodMoss = Assemble("Hood_Ranger_Moss", hoods, bones, new[] { P(O("Ranger_Head_Hood"), swapRanger) }, mats, log);
                groups.Add(new VillagerRig.PartGroup { name = "Hood", options = new[] { hood, hoodMoss }.Where(o => o != null).ToArray(), noneChance = 0.84f, cloth = true });

                var hairRoot = new GameObject("Hair").transform; hairRoot.SetParent(root.transform, false);
                string H(string n) => $"{GVPaths.Characters}/Hair/{n}.fbx";
                var hairNames = female ? new[] { "Hair_Long", "Hair_Buns", "Hair_SimpleParted", "Hair_BuzzedFemale", "Hair_Long" } : new[] { "Hair_Buzzed", "Hair_SimpleParted", "Hair_SimpleParted", "Hair_Long" };
                var hairOptions = new List<GameObject>();
                var made = new Dictionary<string, GameObject>();
                foreach (var hn in hairNames.Distinct())
                {
                    var h = Assemble(hn, hairRoot, bones, new[] { P(H(hn)) }, mats, log);
                    if (h != null) made[hn] = h;
                }
                foreach (var hn in hairNames) if (made.TryGetValue(hn, out var h)) hairOptions.Add(h); // duplicates weight the pick
                groups.Add(new VillagerRig.PartGroup { name = "Hair", options = hairOptions.ToArray(), noneChance = female ? 0f : 0.08f, hair = true, suppressedBy = "Hood" });
                if (!female)
                {
                    var beard = Assemble("Hair_Beard", hairRoot, bones, new[] { P(H("Hair_Beard")) }, mats, log);
                    if (beard != null) groups.Add(new VillagerRig.PartGroup { name = "Beard", options = new[] { beard }, noneChance = 0.55f, hair = true });
                }

                var rig = root.GetComponent<VillagerRig>() ?? root.AddComponent<VillagerRig>();
                rig.animator = anim;
                rig.groups = groups.ToArray();
                rig.skinRenderers = new Renderer[] { body };
                rig.alwaysOnRenderers = always.ToArray();
                rig.heightScale = height > 0.5f ? TargetHeight / height : 1f;

                string prefabPath = $"{GVPaths.CharacterOut}/Villager_{g}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                log.Add($"✔ 村民预制体 {g}：服装 {outfitOptions.Count(o => o != null)} 套，发型 {made.Count} 种，身高 {height:0.00} m → 缩放 {rig.heightScale:0.000}");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Tidy(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            if (r is SkinnedMeshRenderer s) { s.updateWhenOffscreen = false; s.skinnedMotionVectors = false; s.quality = SkinQuality.Auto; }
        }

        private static float MeasureHeight(SkinnedMeshRenderer body)
        {
            var baked = new Mesh();
            body.BakeMesh(baked);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in baked.vertices)
            {
                float y = body.transform.TransformPoint(v).y;
                lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
            }
            Object.DestroyImmediate(baked);
            return hi > lo ? hi - lo : 0f;
        }

        /// <summary>Copy of the body mesh keeping only triangles whose vertices are driven mostly by the head/neck.</summary>
        private static Mesh CutHead(SkinnedMeshRenderer body, string path, List<string> log)
        {
            var src = body.sharedMesh;
            if (src == null) return null;
            var bones = body.bones;
            var keepBone = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++) keepBone[i] = bones[i] != null && HeadBones.Contains(bones[i].name);
            if (!keepBone.Any(k => k)) { log.Add("⚠ 身体网格里找不到 Head/neck_01 骨骼，保留完整身体"); return null; }
            var weights = src.boneWeights;
            if (weights == null || weights.Length != src.vertexCount) { log.Add("⚠ 身体网格没有蒙皮权重，保留完整身体"); return null; }
            var keepVert = new bool[weights.Length];
            for (int v = 0; v < weights.Length; v++)
            {
                var w = weights[v];
                float head = 0f;
                if (w.boneIndex0 < keepBone.Length && keepBone[w.boneIndex0]) head += w.weight0;
                if (w.boneIndex1 < keepBone.Length && keepBone[w.boneIndex1]) head += w.weight1;
                if (w.boneIndex2 < keepBone.Length && keepBone[w.boneIndex2]) head += w.weight2;
                if (w.boneIndex3 < keepBone.Length && keepBone[w.boneIndex3]) head += w.weight3;
                keepVert[v] = head >= 0.5f;
            }

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh();
            mesh.Clear();
            // Compact the vertex set so the head mesh only skins what it draws.
            var remap = new int[weights.Length];
            int count = 0;
            for (int v = 0; v < weights.Length; v++) remap[v] = -1;
            var subTris = new List<int[]>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tris = src.GetTriangles(s);
                var kept = new List<int>(tris.Length / 4);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    if (!(keepVert[a] && keepVert[b] && keepVert[c])) continue;
                    foreach (var idx in new[] { a, b, c })
                    {
                        if (remap[idx] < 0) remap[idx] = count++;
                        kept.Add(remap[idx]);
                    }
                }
                subTris.Add(kept.ToArray());
            }
            var order = new int[count];
            for (int v = 0; v < remap.Length; v++) if (remap[v] >= 0) order[remap[v]] = v;
            T[] Pick<T>(T[] arr) => arr != null && arr.Length == weights.Length ? order.Select(i => arr[i]).ToArray() : null;

            mesh.name = Path.GetFileNameWithoutExtension(path);
            mesh.indexFormat = count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = Pick(src.vertices);
            var n = Pick(src.normals); if (n != null) mesh.normals = n;
            var t = Pick(src.tangents); if (t != null) mesh.tangents = t;
            var uv = Pick(src.uv); if (uv != null) mesh.uv = uv;
            var uv2 = Pick(src.uv2); if (uv2 != null) mesh.uv2 = uv2;
            var col = Pick(src.colors); if (col != null) mesh.colors = col;
            mesh.boneWeights = Pick(weights);
            mesh.bindposes = src.bindposes;
            mesh.subMeshCount = subTris.Count;
            for (int s = 0; s < subTris.Count; s++) mesh.SetTriangles(subTris[s], s);
            mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            log.Add($"✔ {mesh.name}: 顶点 {src.vertexCount} → {count}");
            return mesh;
        }

        /// <summary>Instantiates each part FBX, re-binds its skinned meshes to <paramref name="bones"/> and groups them.</summary>
        private static GameObject Assemble(string name, Transform parent, Dictionary<string, Transform> bones, Part[] parts, Dictionary<string, Material> mats, List<string> log)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            int meshes = 0;
            foreach (var part in parts)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(part.File);
                if (src == null) { log.Add($"⚠ 缺少部件 {part.File}"); continue; }
                var inst = Object.Instantiate(src);
                try
                {
                    foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (smr.sharedMesh == null) continue;
                        var mapped = new Transform[smr.bones.Length];
                        int missing = 0;
                        for (int i = 0; i < mapped.Length; i++)
                        {
                            var b = smr.bones[i];
                            if (b != null && bones.TryGetValue(b.name, out var t)) mapped[i] = t; else missing++;
                        }
                        if (missing > 0) log.Add($"⚠ {Path.GetFileName(part.File)}: {missing} 根骨骼无法对应");
                        var go = new GameObject(smr.name);
                        go.transform.SetParent(holder.transform, false);
                        var r = go.AddComponent<SkinnedMeshRenderer>();
                        r.sharedMesh = smr.sharedMesh;
                        r.bones = mapped;
                        r.rootBone = smr.rootBone != null && bones.TryGetValue(smr.rootBone.name, out var rb) ? rb : (bones.TryGetValue("pelvis", out var pel) ? pel : null);
                        r.localBounds = smr.localBounds;
                        var shared = smr.sharedMaterials;
                        for (int i = 0; i < shared.Length; i++)
                        {
                            var m = shared[i];
                            if (m == null) continue;
                            if (part.Swap != null && part.Swap.TryGetValue(m.name, out var swapName) && mats.TryGetValue(swapName, out var sm)) shared[i] = sm;
                            else if (mats.TryGetValue(m.name, out var pm)) shared[i] = pm;
                        }
                        r.sharedMaterials = shared;
                        Tidy(r);
                        meshes++;
                    }
                }
                finally { Object.DestroyImmediate(inst); }
            }
            if (meshes == 0) { Object.DestroyImmediate(holder); return null; }
            holder.SetActive(false);
            return holder;
        }
    }
}
