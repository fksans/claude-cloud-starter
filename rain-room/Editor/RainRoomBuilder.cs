// RainRoomBuilder.cs
// 按文档「脚本 → 层级结构」把整套东西建出来，并把脚本之间的引用全部接好。
//
//   Room_这里有人
//   ├── SuiteZone      Box Collider（Is Trigger），Layer 23，SuiteZone.cs
//   ├── RoomInterior   Box Collider（Is Trigger，贴着房间内墙），Layer 23，RoomInterior.cs
//   ├── RoomWeather    RoomWeather.cs —— 一直启用，负责同步
//   ├── DoorMuffle     DoorMuffle.cs（可选）
//   ├── Static         你自己的墙、地、家具、窗框、玻璃、台灯（重复运行时整个保留，只补缺的占位物体）
//   ├── Buttons        4 个按钮，各挂 WeatherButton.cs
//   └── RoomContent    默认关闭
//       ├── Room_PP / Room_PP_Storm / Room_PP_Leak   后处理 Volume（Layer 22）
//       ├── Rain_Light / Rain_Heavy / Rain_Storm      粒子，Play On Awake 关
//       ├── Audio_LightRain / Audio_HeavyRain         Loop 开、Play On Awake 开、Volume 0
//       ├── Audio_Thunder                             Play On Awake 关
//       ├── FlashLight / FlashGlow                    闪电窗光、窗外发光片
//       ├── Room_LeakLight                            门口漏光
//       └── LeakGlass                                 门洞着色片（可选）
//
// 重复运行：根物体下除了 Static 以外的子物体全部删掉重建；Buttons 下各按钮的位置、SuiteZone 的大小
// （如果这次没有另外指定整户碰撞体）会先记下来，重建后放回原处。整个搭建是一个 Undo 步骤（Ctrl+Z 一次撤销）。
// 生成的材质 / Profile / Program Asset 是资源文件，Undo 不会删它们（留着也没有影响）。
//
// UdonSharp 组件用 UdonSharpUndo.AddComponent<T>()（UdonSharp 1.x 的公开 API，内部是 Undo.AddComponent +
// 创建隐藏的 backing UdonBehaviour 并挂上 Program Asset）。字段直接写在 C# 组件（proxy）上；
// UdonSharp 在场景构建（进 Play / 上传）时会自动 CopyProxyToUdon，这里也手动同步一次。
// Interaction Text 是 backing UdonBehaviour 上的 interactText 字段（UdonSharpGUI 里就是这么改的）。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace RainRoomEditor
{
    internal class RainRoomOptions
    {
        public string rootName = RainRoomConfig.DefaultRootName;
        public Transform door;
        public BoxCollider interior;
        public BoxCollider suite;     // 可选
        public Renderer glass;        // 可选
        public bool slidingDoor;
        public float doorOpenAngle = RainRoomConfig.DefaultDoorOpenAngle;
        public float slideDistance = RainRoomConfig.DefaultSlideDistance;
        public bool syncAcrossPlayers = true;
        public bool monoPreset;
        public bool windowCookie3x2;
        public bool includeDoorMuffle = true;
        public bool includeLeakGlass = true;
        public bool resetAssets;
    }

    internal static class RainRoomBuilder
    {
        // ───────── 几何：一个带旋转的盒子 ─────────
        private struct Frame
        {
            public Vector3 center;
            public Quaternion rotation;
            public Vector3 size; // 世界尺度下、沿盒子自身三个轴的边长

            public static Frame FromCollider(BoxCollider c)
            {
                Transform t = c.transform;
                Vector3 s = t.lossyScale;
                s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                return new Frame { center = t.TransformPoint(c.center), rotation = t.rotation, size = Vector3.Scale(c.size, s) };
            }

            public Vector3 ToLocal(Vector3 world) => Quaternion.Inverse(rotation) * (world - center);
            public Vector3 ToWorld(Vector3 local) => center + rotation * local;
            public Vector3 Half => size * 0.5f;
            public Vector3 Up => rotation * Vector3.up;
        }

        // 墙上的一个开口（门或窗），全部用房间盒子的局部坐标
        private struct Opening
        {
            public int axis;       // 墙的法线沿哪个轴：0 = x，2 = z
            public float sign;     // +1 / -1：朝外
            public int tAxis;      // 沿墙的水平轴
            public float nIn, nOut; // 沿外法线方向的最里 / 最外（门板或玻璃的两个面）
            public float tc, yc;   // 开口中心（沿墙方向、高度）
            public float width, height, yMin, yMax;

            public Vector3 P(float n, float t, float y)
            {
                var v = Vector3.zero;
                v[axis] = sign * n;
                v[tAxis] = t;
                v.y = y;
                return v;
            }
        }

        private static readonly List<string> Warnings = new List<string>();

        public static bool Build(RainRoomOptions o, out string report)
        {
            Warnings.Clear();
            report = null;

            // ── 0. 检查输入 ──
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                report = "请先退出 Play 模式。";
                return false;
            }
            if (o.door == null || o.interior == null)
            {
                report = "需要指定门板和贴着房间内墙的 Box Collider。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(o.rootName)) o.rootName = RainRoomConfig.DefaultRootName;

            Type doorMuffleType = RainRoomSetup.FindDoorMuffleType();
            var udonTypes = new List<Type> { typeof(SuiteZone), typeof(RoomInterior), typeof(RoomWeather), typeof(WeatherButton) };
            if (doorMuffleType != null) udonTypes.Add(doorMuffleType);
            else if (o.includeDoorMuffle) Warnings.Add("没找到 DoorMuffle.cs，跳过隔门变闷（DoorMuffle 和三个 Audio Low Pass Filter）。");

            // ── 1. UdonSharp：Program Asset 齐了、编译完了才能接线 ──
            RainRoomSetup.UdonState udon = RainRoomSetup.CheckUdon(udonTypes, true, out string udonMessage);
            if (udon != RainRoomSetup.UdonState.Ready)
            {
                report = udonMessage;
                return false;
            }

            Scene scene = o.door.gameObject.scene;
            if (!scene.IsValid())
            {
                report = "门板要选场景里的物体（不是 Project 窗口里的 Prefab）。";
                return false;
            }
            Transform root = FindRoot(scene, o.rootName);

            // 门、玻璃不能在即将被删掉的子物体里
            if (root != null)
            {
                string bad = CheckNotInsideRebuiltChildren(root, o.door, "门板") ??
                             (o.glass != null ? CheckNotInsideRebuiltChildren(root, o.glass.transform, "玻璃") : null);
                if (bad != null)
                {
                    report = bad;
                    return false;
                }
            }

            // ── 2. 先把要用到的几何量都量好（重建会删掉旧物体，参考碰撞体可能就是旧的 RoomInterior） ──
            Frame room = Frame.FromCollider(o.interior);
            if (Vector3.Angle(room.Up, Vector3.up) > 1f)
                Warnings.Add("房间的 Box Collider 是斜的（Y 轴不朝上），下面按它自己的轴向摆放，结果可能不对。");

            Opening doorOp = MeasureDoor(room, o.door);
            bool haveGlass = o.glass != null;
            Opening winOp = haveGlass ? MeasureWindow(room, o.glass) : PlaceholderWindow(room, doorOp);

            Frame? suiteFrame = null;
            if (o.suite != null) suiteFrame = Frame.FromCollider(o.suite);

            // 重建前记下：按钮的位置、SuiteZone 的大小
            var keptPoses = new Dictionary<string, Pose>();
            if (root != null)
            {
                Transform oldButtons = root.Find(RainRoomConfig.ButtonsName);
                if (oldButtons != null)
                    foreach (Transform b in oldButtons)
                        keptPoses[b.name] = new Pose(b.position, b.rotation);

                if (suiteFrame == null)
                {
                    Transform oldSuite = root.Find(RainRoomConfig.SuiteZoneName);
                    BoxCollider oldSuiteBox = oldSuite != null ? oldSuite.GetComponent<BoxCollider>() : null;
                    if (oldSuiteBox != null) suiteFrame = Frame.FromCollider(oldSuiteBox);
                }
            }
            if (suiteFrame == null)
            {
                suiteFrame = EstimateSuite(room, doorOp);
                Warnings.Add("SuiteZone 是按「房间 + 门外 3 m」估的。按文档它要罩住整户（公共区 + 两间房 + 门外），请手动放大它的 Box Collider（重复搭建时会保留你改过的大小）。");
            }

            // ── 3. 资源（材质、Profile、贴图导入）——不进 Undo ──
            bool needPlaceholderGlass = !haveGlass && (root == null || FindStaticChild(root, RainRoomConfig.PlaceholderGlassName) == null);
            RainRoomAssetSet assets = RainRoomAssets.Build(o.monoPreset, o.windowCookie3x2, o.resetAssets, needPlaceholderGlass);
            if (assets.glassMaterials.Length == 0)
                Warnings.Add("没装 Mochie's Unity Shaders：三档玻璃材质跳过了，RoomWeather 的 Glass Materials 留空（脚本会跳过换材质）。装好后再搭建一次。");

            // ── 4. 场景（一个 Undo 组） ──
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("RainRoom 一键搭建");

            try
            {
                if (!RainRoomSetup.EnsureLayerNames(true))
                {
                    report = "已取消。";
                    Undo.CollapseUndoOperations(undoGroup);
                    return false;
                }
                RainRoomSetup.ApplyCollisionMatrix();

                if (root == null)
                {
                    var rootGo = new GameObject(o.rootName);
                    SceneManager.MoveGameObjectToScene(rootGo, scene);
                    rootGo.transform.SetPositionAndRotation(room.center, room.rotation);
                    Undo.RegisterCreatedObjectUndo(rootGo, "Create " + o.rootName);
                    root = rootGo.transform;
                }
                else
                {
                    // 先删旧的（Static 除外）
                    for (int i = root.childCount - 1; i >= 0; i--)
                    {
                        Transform child = root.GetChild(i);
                        if (child.name == RainRoomConfig.StaticName) continue;
                        Undo.DestroyObjectImmediate(child.gameObject);
                    }
                }

                if ((root.lossyScale - Vector3.one).sqrMagnitude > 1e-4f)
                    Warnings.Add($"{root.name} 或它的父物体有缩放，触发区和 Volume 的大小会跟着被缩放。建议把缩放改回 1 再搭建。");

                BuildHierarchy(o, root, room, doorOp, winOp, suiteFrame.Value, assets, doorMuffleType, keptPoses);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Undo.RevertAllDownToGroup(undoGroup); // 场景改动全部退回到搭建之前
                report = "搭建中途出错，场景改动已经自动撤销：" + e.Message + "\n详细报错在 Console。";
                return false;
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root.gameObject;
            EditorGUIUtility.PingObject(root.gameObject);

            // 参考用的 Box Collider（不是这次生成的那些）如果还开着，会罩住整间房：不是 Trigger 会挡住玩家，
            // 是 Trigger 也会挡住按钮的交互射线（VRChat 里大多数层都挡交互）。
            WarnLeftoverReference(o.interior, root, "房间内墙");
            WarnLeftoverReference(o.suite, root, "整户范围");

            string ppWarning = RainRoomSetup.CheckPostProcessCamera();
            if (ppWarning != null) Warnings.Add(ppWarning);

            report = "搭建完成。还有几步要手动做（Interact Passthrough、摆窗户、拖音频、烘焙），见 README「手动步骤」。";
            if (Warnings.Count > 0)
                report += "\n\n注意：\n• " + string.Join("\n• ", Warnings);

            foreach (string w in Warnings) Debug.LogWarning("[RainRoom] " + w);
            Debug.Log("[RainRoom] " + report.Split('\n')[0], root.gameObject);
            return true;
        }

        // ───────── 层级 ─────────

        private static void BuildHierarchy(RainRoomOptions o, Transform root, Frame room, Opening doorOp, Opening winOp, Frame suite,
                                           RainRoomAssetSet assets, Type doorMuffleType, Dictionary<string, Pose> keptPoses)
        {
            Vector3 upW = room.Up;
            Vector3 doorOutW = room.rotation * doorOp.P(1f, 0f, 0f);
            Vector3 winOutW = room.rotation * winOp.P(1f, 0f, 0f);
            float floorY = -room.Half.y;

            // ── SuiteZone / RoomInterior ──
            GameObject suiteGo = NewChild(RainRoomConfig.SuiteZoneName, root);
            ApplyTriggerBox(suiteGo, suite, RainRoomConfig.PlayerZoneLayer);

            GameObject interiorGo = NewChild(RainRoomConfig.RoomInteriorName, root);
            ApplyTriggerBox(interiorGo, room, RainRoomConfig.PlayerZoneLayer);

            GameObject weatherGo = NewChild(RainRoomConfig.RoomWeatherName, root);
            weatherGo.transform.SetPositionAndRotation(room.center, room.rotation);

            GameObject muffleGo = null;
            if (o.includeDoorMuffle && doorMuffleType != null)
            {
                muffleGo = NewChild(RainRoomConfig.DoorMuffleName, root);
                muffleGo.transform.SetPositionAndRotation(room.center, room.rotation);
            }

            // ── Static（只补缺的占位物体） ──
            Transform staticT = root.Find(RainRoomConfig.StaticName);
            if (staticT == null)
            {
                staticT = NewChild(RainRoomConfig.StaticName, root).transform;
                staticT.SetPositionAndRotation(room.center, room.rotation);
            }

            Renderer glass = o.glass;
            if (glass == null)
            {
                Transform placeholder = staticT.Find(RainRoomConfig.PlaceholderGlassName);
                if (placeholder == null)
                {
                    GameObject g = NewPrimitive(PrimitiveType.Quad, RainRoomConfig.PlaceholderGlassName, staticT);
                    g.transform.SetPositionAndRotation(room.ToWorld(winOp.P(winOp.nIn, winOp.tc, winOp.yc)),
                                                       Quaternion.LookRotation(winOutW, upW)); // Quad 正面朝屋里
                    g.transform.localScale = new Vector3(winOp.width, winOp.height, 1f);
                    // 会被脚本换材质：不要 Batching Static（文档「性能 → 其它建议」）
                    GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.OccludeeStatic);
                    placeholder = g.transform;
                    Warnings.Add("没指定玻璃，在 Static 下建了一块占位玻璃 Window_Glass（对着门的那面墙中间）。把它挪到真正的窗洞里，再搭建一次，雨、闪电、音源会重新对齐到它。");
                }
                glass = placeholder.GetComponent<Renderer>();
            }

            if (glass != null)
            {
                Material first = assets.glassMaterials.Length > 0 ? assets.glassMaterials[0] : assets.fallbackGlassMaterial;
                if (first != null && glass.sharedMaterial != first && (assets.glassMaterials.Length > 0 || glass.name == RainRoomConfig.PlaceholderGlassName))
                {
                    Undo.RecordObject(glass, "RainRoom 玻璃材质");
                    glass.sharedMaterial = first;
                }
            }

            if (staticT.Find(RainRoomConfig.PlaceholderLampName) == null)
            {
                GameObject lamp = NewChild(RainRoomConfig.PlaceholderLampName, staticT);
                lamp.transform.position = room.ToWorld(new Vector3(0f, floorY + 1f, 0f));
                Light l = lamp.AddComponent<Light>();
                l.type = LightType.Point;
                l.lightmapBakeType = LightmapBakeType.Baked;
                l.color = RainRoomConfig.LampColor;
                l.intensity = RainRoomConfig.LampIntensity;
                l.range = RainRoomConfig.LampRange;
                l.shadows = LightShadows.Soft;
                Warnings.Add("Static/Lamp_Light 是台灯的烘焙点光占位，放在房间中间 1 m 高。把它挪进灯罩里（之后重复搭建不会再动它）。");
            }

            if (staticT.Find(RainRoomConfig.PlaceholderWindowAreaLightName) == null)
            {
                GameObject area = NewChild(RainRoomConfig.PlaceholderWindowAreaLightName, staticT);
                area.transform.SetPositionAndRotation(room.ToWorld(winOp.P(winOp.nIn - 0.05f, winOp.tc, winOp.yc)),
                                                      Quaternion.LookRotation(-winOutW, upW)); // 面光源朝 +Z 发光：朝屋里
                Light l = area.AddComponent<Light>();
                l.type = LightType.Rectangle;
                l.lightmapBakeType = LightmapBakeType.Baked;
                l.areaSize = new Vector2(winOp.width, winOp.height);
                l.color = RainRoomConfig.WindowAreaColor;
                l.intensity = RainRoomConfig.WindowAreaIntensity;
            }

            // ── Buttons ──
            GameObject buttonsGo = NewChild(RainRoomConfig.ButtonsName, root);
            buttonsGo.transform.SetPositionAndRotation(room.center, room.rotation);
            var buttons = new WeatherButton[RainRoomConfig.ButtonLabels.Length];
            {
                float roomPlus = room.Half[doorOp.tAxis] - doorOp.tc;
                float roomMinus = room.Half[doorOp.tAxis] + doorOp.tc;
                float dir = roomPlus >= roomMinus ? 1f : -1f;
                float wallN = Mathf.Min(room.Half[doorOp.axis], doorOp.nIn) - RainRoomConfig.ButtonSize * 0.5f - 0.02f;

                for (int i = 0; i < buttons.Length; i++)
                {
                    string name = "Button_" + RainRoomConfig.ButtonLabels[i];
                    GameObject b = NewPrimitive(PrimitiveType.Cube, name, buttonsGo.transform, keepCollider: true);
                    float t = doorOp.tc + dir * (doorOp.width * 0.5f + 0.35f + i * RainRoomConfig.ButtonSpacing);
                    b.transform.SetPositionAndRotation(room.ToWorld(doorOp.P(wallN, t, floorY + RainRoomConfig.ButtonHeight)),
                                                       Quaternion.LookRotation(doorOutW, upW));
                    b.transform.localScale = Vector3.one * RainRoomConfig.ButtonSize;
                    if (keptPoses.TryGetValue(name, out Pose kept)) b.transform.SetPositionAndRotation(kept.position, kept.rotation);

                    var wb = UdonSharpUndo.AddComponent<WeatherButton>(b);
                    buttons[i] = wb;
                }
            }

            // ── RoomContent（最后再关掉） ──
            GameObject content = NewChild(RainRoomConfig.RoomContentName, root);
            content.transform.SetPositionAndRotation(room.center, room.rotation);

            // 后处理 Volume
            MakeVolume(RainRoomConfig.RoomPPName, content.transform, room, assets.roomProfile,
                       RainRoomConfig.RoomPriority, RainRoomConfig.RoomBlend, RainRoomConfig.RoomWeight);
            PostProcessVolume stormPP = MakeVolume(RainRoomConfig.RoomPPStormName, content.transform, room, assets.stormProfile,
                                                   RainRoomConfig.StormPriority, 0f, RainRoomConfig.StormWeight);

            var leakFrame = new Frame
            {
                center = room.ToWorld(doorOp.P(doorOp.nOut + RainRoomConfig.LeakDepth * 0.5f, doorOp.tc, doorOp.yc)),
                rotation = room.rotation,
            };
            var leakSize = Vector3.zero;
            leakSize[doorOp.axis] = RainRoomConfig.LeakDepth;
            leakSize[doorOp.tAxis] = doorOp.width + RainRoomConfig.LeakExtraWidth;
            leakSize.y = doorOp.height;
            leakFrame.size = leakSize;
            PostProcessVolume leakPP = MakeVolume(RainRoomConfig.RoomPPLeakName, content.transform, leakFrame, assets.roomProfile,
                                                  RainRoomConfig.LeakPriority, RainRoomConfig.LeakBlend, RainRoomConfig.LeakWeight);

            // 雨粒子
            var rain = new ParticleSystem[3];
            for (int i = 0; i < 3; i++)
                rain[i] = MakeRain(RainRoomConfig.RainNames[i], content.transform, i, room, winOp, upW, winOutW, assets.rainMaterial);

            // 音源
            AudioSource lightLoop = MakeAudio(RainRoomConfig.AudioLightRainName, content.transform,
                room.ToWorld(winOp.P(winOp.nIn - RainRoomConfig.RainAudioInside, winOp.tc, winOp.yc)),
                true, true, RainRoomConfig.RainAudioNear, RainRoomConfig.RainAudioFar);
            AudioSource heavyLoop = MakeAudio(RainRoomConfig.AudioHeavyRainName, content.transform,
                room.ToWorld(winOp.P(winOp.nIn - RainRoomConfig.RainAudioInside, winOp.tc, winOp.yc)),
                true, true, RainRoomConfig.RainAudioNear, RainRoomConfig.RainAudioFar);
            AudioSource thunder = MakeAudio(RainRoomConfig.AudioThunderName, content.transform,
                room.ToWorld(winOp.P(winOp.nOut + RainRoomConfig.ThunderOutside, winOp.tc, winOp.yc)),
                false, false, RainRoomConfig.ThunderNear, RainRoomConfig.ThunderFar);

            // 闪电窗光：窗外，斜向下 30° 往屋里照
            Vector3 winCenterW = room.ToWorld(winOp.P((winOp.nIn + winOp.nOut) * 0.5f, winOp.tc, winOp.yc));
            Vector3 flashPosW = room.ToWorld(winOp.P(winOp.nOut + RainRoomConfig.FlashOutside, winOp.tc,
                winOp.yc + RainRoomConfig.FlashOutside * Mathf.Tan(RainRoomConfig.FlashDownAngle * Mathf.Deg2Rad)));
            Light flash = MakeSpot(RainRoomConfig.FlashLightName, content.transform, flashPosW,
                Quaternion.LookRotation(winCenterW - flashPosW, upW), RainRoomConfig.FlashColor,
                RainRoomConfig.FlashRange, RainRoomConfig.FlashSpotAngle, assets.windowCookie);

            // 闪电发光片：雨粒子后面一块朝着窗户的大 Quad，Renderer 平时关着
            GameObject glowGo = NewPrimitive(PrimitiveType.Quad, RainRoomConfig.FlashGlowName, content.transform);
            glowGo.transform.SetPositionAndRotation(room.ToWorld(winOp.P(winOp.nOut + RainRoomConfig.GlowDistance, winOp.tc, winOp.yc)),
                                                    Quaternion.LookRotation(winOutW, upW)); // 正面朝屋里
            glowGo.transform.localScale = new Vector3(Mathf.Max(RainRoomConfig.GlowMinWidth, winOp.width * 10f),
                                                      Mathf.Max(RainRoomConfig.GlowMinHeight, winOp.height * 10f), 1f);
            var glowRenderer = glowGo.GetComponent<MeshRenderer>();
            ConfigureEffectRenderer(glowRenderer, assets.flashGlowMaterial);
            glowRenderer.enabled = false;

            // 门口漏光：屋里离门 1.75 m、高 2 m，对准门洞中心，落点在门外 1.75 m
            Vector3 leakLightLocal = doorOp.P(doorOp.nIn - RainRoomConfig.LeakLightBack, doorOp.tc, doorOp.yMin + RainRoomConfig.LeakLightHeight);
            Vector3 leakTargetLocal = doorOp.P(doorOp.nOut + RainRoomConfig.LeakLandOutside, doorOp.tc, doorOp.yMin);
            Vector3 leakLightW = room.ToWorld(leakLightLocal);
            Light leakLight = MakeSpot(RainRoomConfig.LeakLightName, content.transform, leakLightW,
                Quaternion.LookRotation(room.ToWorld(leakTargetLocal) - leakLightW, upW), RainRoomConfig.LeakColor,
                RainRoomConfig.LeakRange, DoorSpotAngle(doorOp, leakLightLocal, leakTargetLocal), assets.doorCookie);

            // 门洞着色片（可选）
            MeshRenderer leakGlass = null;
            if (o.includeLeakGlass && assets.leakGlassMaterial != null)
            {
                GameObject lg = NewPrimitive(PrimitiveType.Quad, RainRoomConfig.LeakGlassName, content.transform);
                lg.transform.SetPositionAndRotation(room.ToWorld(doorOp.P(doorOp.nIn - RainRoomConfig.LeakGlassInset, doorOp.tc, doorOp.yc)),
                                                    Quaternion.LookRotation(-doorOutW, upW)); // 正面朝门外
                lg.transform.localScale = new Vector3(doorOp.width, doorOp.height, 1f);
                leakGlass = lg.GetComponent<MeshRenderer>();
                ConfigureEffectRenderer(leakGlass, assets.leakGlassMaterial);
                leakGlass.enabled = false;
            }

            // 隔门变闷：三个音源各加一个 Audio Low Pass Filter
            AudioLowPassFilter[] filters = null;
            if (muffleGo != null)
            {
                filters = new[] { lightLoop, heavyLoop, thunder }.Select(a =>
                {
                    var f = a.gameObject.AddComponent<AudioLowPassFilter>();
                    f.cutoffFrequency = 22000f;
                    return f;
                }).ToArray();
            }

            // ── 脚本 ──
            var suiteZone = UdonSharpUndo.AddComponent<SuiteZone>(suiteGo);
            var roomInterior = UdonSharpUndo.AddComponent<RoomInterior>(interiorGo);
            var weather = UdonSharpUndo.AddComponent<RoomWeather>(weatherGo);
            UdonSharpBehaviour muffle = muffleGo != null ? UdonSharpUndo.AddComponent(muffleGo, doorMuffleType) : null;

            suiteZone.roomContent = content;
            suiteZone.weather = weather;
            roomInterior.weather = weather;

            weather.syncAcrossPlayers = o.syncAcrossPlayers;
            weather.rainParticles = rain;
            weather.glassRenderer = glass;
            weather.glassMaterials = assets.glassMaterials;
            weather.lightRainLoop = lightLoop;
            weather.heavyRainLoop = heavyLoop;
            weather.stormVolume = stormPP;
            weather.flashLight = flash;
            weather.flashPeak = RainRoomConfig.FlashPeak;
            weather.minGap = RainRoomConfig.MinGap;
            weather.maxGap = RainRoomConfig.MaxGap;
            weather.flashGlow = glowRenderer;
            weather.thunderSource = thunder;
            weather.thunderClips = new AudioClip[0]; // 音频栏位留空，见 README
            weather.minThunderDelay = RainRoomConfig.MinThunderDelay;
            weather.maxThunderDelay = RainRoomConfig.MaxThunderDelay;
            weather.door = o.door;
            weather.slidingDoor = o.slidingDoor;
            weather.doorOpenAngle = o.doorOpenAngle;
            weather.slideDistance = o.slideDistance;
            weather.doorSmoothing = RainRoomConfig.DoorSmoothing;
            weather.leakVolume = leakPP;
            weather.leakMaxWeight = RainRoomConfig.LeakMaxWeight;
            weather.leakLight = leakLight;
            weather.leakLightPeak = RainRoomConfig.LeakLightPeak;
            weather.leakGlass = leakGlass;
            weather.closedDoorVolume = RainRoomConfig.ClosedDoorVolume;
            weather.doorMuffle = muffle;

            if (muffle != null)
            {
                FieldInfo filtersField = doorMuffleType.GetField("filters", BindingFlags.Public | BindingFlags.Instance);
                if (filtersField != null && filtersField.FieldType == typeof(AudioLowPassFilter[])) filtersField.SetValue(muffle, filters);
                else Warnings.Add("DoorMuffle 里没找到 public AudioLowPassFilter[] filters 字段，Filters 需要手动拖。");
            }

            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].weather = weather;
                buttons[i].eventName = RainRoomConfig.ButtonEvents[i];

                UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(buttons[i]);
                if (backing != null)
                {
                    Undo.RecordObject(backing, "RainRoom 按钮文字");
                    backing.interactText = RainRoomConfig.ButtonLabels[i];
                }
            }

            // proxy → UdonBehaviour。UdonSharp 构建场景时也会自动做，这里提前做一次，Inspector / ClientSim 立即可见。
            foreach (UdonSharpBehaviour usb in root.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                EditorUtility.SetDirty(usb);
                try
                {
                    UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(usb);
                    if (backing != null) Undo.RecordObject(backing, "RainRoom 同步 Udon 变量");
                    UdonSharpEditorUtility.CopyProxyToUdon(usb);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RainRoom] {usb.name} 的变量暂时没能同步到 UdonBehaviour（进 Play 或上传时 UdonSharp 会自动同步）：{e.Message}", usb);
                }
            }

            content.SetActive(false); // 默认关闭，只有你在这一户时才打开
        }

        // ───────── 门、窗测量 ─────────

        private static Opening MeasureDoor(Frame room, Transform door)
        {
            Opening op = MeasureOpening(room, door, out bool measured);
            if (!measured)
            {
                Warnings.Add("门板上没有 Renderer / Collider，量不出门的大小，按 0.9 × 2.1 m 估算。");
                op.width = 0.9f;
                op.height = 2.1f;
                op.yMin = -room.Half.y;
                op.yMax = op.yMin + op.height;
                op.yc = op.yMin + op.height * 0.5f;
            }
            else if (op.width < 0.4f || op.width > 3f || op.height < 1.2f || op.height > 4f)
            {
                Warnings.Add($"量出来的门洞是 {op.width:0.00} × {op.height:0.00} m，不太像一扇门。搭建时门必须是关着的；选的应该是门板本身，不要选到门框或整面墙。");
            }
            return op;
        }

        private static Opening MeasureWindow(Frame room, Renderer glass)
        {
            Opening op = MeasureOpening(room, glass.transform, out bool measured);
            if (!measured || op.width < 0.1f || op.height < 0.1f)
            {
                Warnings.Add("量不出玻璃的大小，按 1.6 × 1.2 m 估算。");
                op.width = RainRoomConfig.PlaceholderWindowWidth;
                op.height = RainRoomConfig.PlaceholderWindowHeight;
                op.yMin = op.yc - op.height * 0.5f;
                op.yMax = op.yc + op.height * 0.5f;
            }
            return op;
        }

        // 没指定玻璃：放在门对面那面墙的中间
        private static Opening PlaceholderWindow(Frame room, Opening doorOp)
        {
            float floorY = -room.Half.y;
            var op = new Opening
            {
                axis = doorOp.axis,
                sign = -doorOp.sign,
                tAxis = doorOp.tAxis,
                width = RainRoomConfig.PlaceholderWindowWidth,
                height = RainRoomConfig.PlaceholderWindowHeight,
                tc = 0f,
                yMin = floorY + RainRoomConfig.PlaceholderWindowSill,
            };
            op.yMax = op.yMin + op.height;
            op.yc = (op.yMin + op.yMax) * 0.5f;
            op.nIn = op.nOut = room.Half[op.axis];
            return op;
        }

        private static Opening MeasureOpening(Frame room, Transform t, out bool measured)
        {
            measured = LocalExtents(room, t, out Vector3 min, out Vector3 max);
            Vector3 c = measured ? (min + max) * 0.5f : room.ToLocal(t.position);
            Vector3 half = room.Half;

            // 离哪面墙（x 面还是 z 面）最近
            float nx = half.x > 0f ? Mathf.Abs(c.x) / half.x : 0f;
            float nz = half.z > 0f ? Mathf.Abs(c.z) / half.z : 0f;
            var op = new Opening { axis = nx >= nz ? 0 : 2 };
            op.tAxis = op.axis == 0 ? 2 : 0;
            op.sign = c[op.axis] >= 0f ? 1f : -1f;

            if (Mathf.Max(nx, nz) < 0.7f)
                Warnings.Add($"「{t.name}」离房间内墙比较远（不在墙上？），按最近的那面墙算的方向，摆放结果请检查。");

            if (measured)
            {
                float a = op.sign * min[op.axis], b = op.sign * max[op.axis];
                op.nIn = Mathf.Min(a, b);
                op.nOut = Mathf.Max(a, b);
                op.tc = c[op.tAxis];
                op.width = max[op.tAxis] - min[op.tAxis];
                op.yMin = min.y;
                op.yMax = max.y;
                op.height = max.y - min.y;
                op.yc = c.y;
            }
            else
            {
                op.nIn = op.nOut = op.sign * c[op.axis];
                op.tc = c[op.tAxis];
                op.yc = c.y;
            }
            return op;
        }

        private static bool LocalExtents(Frame room, Transform t, out Vector3 min, out Vector3 max)
        {
            min = Vector3.positiveInfinity;
            max = Vector3.negativeInfinity;
            bool any = false;

            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                Bounds b = r.localBounds; // Unity 2021.2+
                if (b.size == Vector3.zero) continue;
                foreach (Vector3 corner in Corners(b))
                {
                    Vector3 p = room.ToLocal(r.transform.TransformPoint(corner));
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                any = true;
            }

            if (!any)
            {
                foreach (Collider col in t.GetComponentsInChildren<Collider>(true))
                {
                    if (!col.enabled) continue;
                    Bounds b = col.bounds; // 世界 AABB，门是斜的时会偏大
                    if (b.size == Vector3.zero) continue;
                    foreach (Vector3 corner in Corners(b))
                    {
                        Vector3 p = room.ToLocal(corner);
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }
                    any = true;
                }
            }
            return any;
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            Vector3 mn = b.min, mx = b.max;
            for (int i = 0; i < 8; i++)
                yield return new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
        }

        // 让 door_cookie.png 里的白色矩形（竖向占 83%）正好对上门洞的高：
        // Spot 的 cookie 铺满整个锥角，所以 tan(半锥角) × 0.834 = tan(门洞上 / 下边缘偏离光轴的角度)。
        private static float DoorSpotAngle(Opening d, Vector3 lightLocal, Vector3 targetLocal)
        {
            Vector3 axisDir = (targetLocal - lightLocal).normalized;
            float mid = (d.nIn + d.nOut) * 0.5f;
            float top = Vector3.Angle(axisDir, d.P(mid, d.tc, d.yMax) - lightLocal);
            float bottom = Vector3.Angle(axisDir, d.P(mid, d.tc, d.yMin) - lightLocal);
            float half = Mathf.Max(top, bottom) * Mathf.Deg2Rad;
            float spot = 2f * Mathf.Atan(Mathf.Tan(half) / RainRoomConfig.DoorCookieHalfHeightFrac) * Mathf.Rad2Deg;
            return Mathf.Clamp(spot, 10f, 150f);
        }

        private static Frame EstimateSuite(Frame room, Opening door)
        {
            Vector3 min = -room.Half, max = room.Half;
            float outside = Mathf.Max(door.nOut, room.Half[door.axis]) + RainRoomConfig.SuiteOutsideDoor;
            if (door.sign > 0f) max[door.axis] = outside;
            else min[door.axis] = -outside;

            var pad = new Vector3(RainRoomConfig.SuitePadding, 0.2f, RainRoomConfig.SuitePadding);
            min -= pad;
            max += pad;
            return new Frame { center = room.ToWorld((min + max) * 0.5f), rotation = room.rotation, size = max - min };
        }

        // ───────── 物体工厂 ─────────

        private static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject NewPrimitive(PrimitiveType type, string name, Transform parent, bool keepCollider = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider)
            {
                Collider col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
            }
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void ApplyTriggerBox(GameObject go, Frame f, int layer)
        {
            go.layer = layer;
            go.transform.SetPositionAndRotation(f.center, f.rotation);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = f.size;
        }

        private static PostProcessVolume MakeVolume(string name, Transform parent, Frame f, PostProcessProfile profile,
                                                    float priority, float blend, float weight)
        {
            GameObject go = NewChild(name, parent);
            ApplyTriggerBox(go, f, RainRoomConfig.PostProcessingLayer); // PPv2 的局部 Volume 需要同物体上的触发碰撞体
            var v = go.AddComponent<PostProcessVolume>();
            v.isGlobal = false;
            v.blendDistance = blend;
            v.priority = priority;
            v.weight = weight;
            v.sharedProfile = profile;
            return v;
        }

        private static int RealtimeCullingMask()
        {
            // 去掉 Player、PlayerLocal、MirrorReflection（文档「窗光」「冷光漏出来」）。
            // 文档要求"只勾房间几何 / 走廊地面所在的层"——你的场景用哪些层这里不知道，见 README 手动步骤。
            int mask = ~0;
            mask &= ~(1 << RainRoomConfig.PlayerLayer);
            mask &= ~(1 << RainRoomConfig.PlayerLocalLayer);
            mask &= ~(1 << RainRoomConfig.MirrorReflectionLayer);
            return mask;
        }

        private static Light MakeSpot(string name, Transform parent, Vector3 pos, Quaternion rot, Color color, float range, float spotAngle, Texture cookie)
        {
            GameObject go = NewChild(name, parent);
            go.transform.SetPositionAndRotation(pos, rot);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            l.color = color;
            l.intensity = 0f;                          // 平时 0，脚本控制
            l.range = range;
            l.spotAngle = spotAngle;
            l.cookie = cookie;
            l.renderMode = LightRenderMode.ForcePixel; // Important：cookie 只对逐像素光生效
            l.shadows = LightShadows.None;
            l.cullingMask = RealtimeCullingMask();
            l.enabled = false;                         // 组件默认关掉
            return l;
        }

        private static AudioSource MakeAudio(string name, Transform parent, Vector3 pos, bool loop, bool playOnAwake, float near, float far)
        {
            GameObject go = NewChild(name, parent);
            go.transform.position = pos;
            var a = go.AddComponent<AudioSource>();
            a.clip = null;            // 音频不下载，栏位留空
            a.loop = loop;
            a.playOnAwake = playOnAwake;
            a.volume = loop ? 0f : 1f; // 雨声 Volume 0（脚本淡入）；雷声用 PlayOneShot 的音量
            a.spatialBlend = 1f;
            AddSpatialAudio(go, near, far);
            return a;
        }

        // VRC Spatial Audio Source：用反射加，字段名按 VRChat SDK（Near / Far），读不到就提示手动设。
        private static void AddSpatialAudio(GameObject go, float near, float far)
        {
            Type t = RainRoomSetup.FindTypeByName("VRCSpatialAudioSource", typeof(Component));
            if (t == null)
            {
                Warnings.Add("没找到 VRCSpatialAudioSource 类型，音源上的 VRC Spatial Audio Source 需要手动加（Near / Far 见文档「音源摆放」）。");
                return;
            }

            Component c = go.GetComponent(t);
            if (c == null) c = go.AddComponent(t);
            if (!SetMember(c, "Near", near) | !SetMember(c, "Far", far))
                Warnings.Add($"{go.name}：VRC Spatial Audio Source 的 Near / Far 没能自动设置，请手动填 Near {near} m、Far {far} m。");
        }

        private static bool SetMember(object target, string name, object value)
        {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null && f.FieldType.IsInstanceOfType(value)) { f.SetValue(target, value); return true; }
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite && p.PropertyType.IsInstanceOfType(value)) { p.SetValue(target, value); return true; }
            return false;
        }

        private static void ConfigureEffectRenderer(Renderer r, Material m)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static ParticleSystem MakeRain(string name, Transform parent, int tier, Frame room, Opening win, Vector3 upW, Vector3 outW, Material mat)
        {
            GameObject go = NewChild(name, parent);
            // 盒子：宽 = 窗宽 + 1，厚 0.5，深 5（离玻璃 0.5 m 起往外），放在窗顶上方 1 m。
            // 物体的 +Z 朝下（Box 形状沿 +Z 发射），+Y 朝窗外。
            go.transform.SetPositionAndRotation(
                room.ToWorld(win.P(win.nOut + RainRoomConfig.RainBoxGap + RainRoomConfig.RainBoxDepth * 0.5f, win.tc,
                                   win.yMax + RainRoomConfig.RainAboveWindowTop)),
                Quaternion.LookRotation(-upW, outW));

            var ps = go.AddComponent<ParticleSystem>();
            float speed = RainRoomConfig.RainSpeed[tier];

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.prewarm = true; // 走进这一户时内容才被启用，不开 Prewarm 雨会从空中"开始下"
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.startSpeed = speed;
            main.startLifetime = (win.height + 1f) / speed; // 出了窗下沿就消失
            main.startSize = RainRoomConfig.RainStartSize;
            Color c = RainRoomConfig.RainColor;
            c.a = RainRoomConfig.RainAlpha[tier];
            main.startColor = c;
            main.maxParticles = RainRoomConfig.RainMaxParticles[tier];

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = RainRoomConfig.RainRate[tier];

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(win.width + RainRoomConfig.RainBoxExtraWidth, RainRoomConfig.RainBoxDepth, RainRoomConfig.RainBoxThickness);

            ParticleSystem.VelocityOverLifetimeModule vol = ps.velocityOverLifetime;
            float vx = RainRoomConfig.RainVelocityX[tier];
            vol.enabled = vx != 0f;
            vol.space = ParticleSystemSimulationSpace.Local; // 局部 X = 沿窗户的水平方向（被风吹斜）
            vol.x = new ParticleSystem.MinMaxCurve(vx);
            vol.y = new ParticleSystem.MinMaxCurve(0f); // 三个分量必须同一种模式
            vol.z = new ParticleSystem.MinMaxCurve(0f);

            // Collision、Lights、Trails、Sub Emitters 默认就是关的
            ParticleSystem.CollisionModule collision = ps.collision;
            collision.enabled = false;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = RainRoomConfig.RainLengthScale;
            r.velocityScale = RainRoomConfig.RainVelocityScale;
            ConfigureEffectRenderer(r, mat);

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        // ───────── 查找 ─────────

        private static Transform FindRoot(Scene scene, string name)
        {
            foreach (GameObject g in scene.GetRootGameObjects())
            {
                if (g.name == name) return g.transform;
                foreach (Transform t in g.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            }
            return null;
        }

        private static void WarnLeftoverReference(BoxCollider c, Transform root, string what)
        {
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) return; // 被重建删掉的旧碰撞体在这里是 null
            if (c.transform.IsChildOf(root) && c.transform.parent == root && c.gameObject.layer == RainRoomConfig.PlayerZoneLayer) return;
            Warnings.Add($"用来指定「{what}」的 Box Collider（{c.name}）还开着。搭建已经复制了它的大小，这个参考碰撞体请删掉或禁用，" +
                         "不然它会挡住玩家或挡住房里按钮的交互射线。");
        }

        private static Transform FindStaticChild(Transform root, string name)
        {
            Transform s = root.Find(RainRoomConfig.StaticName);
            return s != null ? s.Find(name) : null;
        }

        private static string CheckNotInsideRebuiltChildren(Transform root, Transform t, string what)
        {
            if (t == root) return $"{what}不能是根物体 {root.name} 本身。";
            foreach (Transform child in root)
            {
                if (child.name == RainRoomConfig.StaticName) continue;
                if (t == child || t.IsChildOf(child))
                    return $"{what}「{t.name}」在 {root.name}/{child.name} 下面，重新搭建会把它删掉。把它挪到 {root.name}/Static 下或者根物体外面再搭建。";
            }
            return null;
        }
    }
}
