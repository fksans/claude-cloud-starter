// RainRoomConfig.cs
// 一键搭建用到的所有数值，集中在这里，全部出自设计文档《这里有人 — 雨夜单人房技术指导》。
// 文档给的是区间的（比如 "1.5–3"），这里取中间值，并在注释里写明区间，进场景后凭眼睛再调。
// 标了【自定】的是文档没给具体数、由搭建脚本自己定的，可以随意改。

using UnityEngine;

namespace RainRoomEditor
{
    internal static class RainRoomConfig
    {
        // ───────── 层级名字（文档「脚本 → 层级结构」） ─────────
        public const string DefaultRootName = "Room_这里有人";
        public const string SuiteZoneName = "SuiteZone";
        public const string RoomInteriorName = "RoomInterior";
        public const string RoomWeatherName = "RoomWeather";
        public const string DoorMuffleName = "DoorMuffle";
        public const string StaticName = "Static";
        public const string ButtonsName = "Buttons";
        public const string RoomContentName = "RoomContent";
        public const string RoomPPName = "Room_PP";
        public const string RoomPPStormName = "Room_PP_Storm";
        public const string RoomPPLeakName = "Room_PP_Leak";
        public static readonly string[] RainNames = { "Rain_Light", "Rain_Heavy", "Rain_Storm" };
        public const string AudioLightRainName = "Audio_LightRain";
        public const string AudioHeavyRainName = "Audio_HeavyRain";
        public const string AudioThunderName = "Audio_Thunder";
        public const string FlashLightName = "FlashLight";
        public const string FlashGlowName = "FlashGlow";
        public const string LeakLightName = "Room_LeakLight";
        public const string LeakGlassName = "LeakGlass";

        // Static 下的占位物体：只在缺少时创建，重复运行永远不删（Static 里是你自己的墙、家具）
        public const string PlaceholderGlassName = "Window_Glass";
        public const string PlaceholderLampName = "Lamp_Light";
        public const string PlaceholderWindowAreaLightName = "Window_AreaLight";

        // ───────── 图层（文档「滤镜 → 一次性设置」「脚本 → 图层设置」、VRChat 图层说明） ─────────
        public const int PostProcessingLayer = 22;
        public const string PostProcessingLayerName = "PostProcessing";
        public const int PlayerZoneLayer = 23;
        public const string PlayerZoneLayerName = "PlayerZone";
        public const int PlayerLayer = 9;            // VRChat 内置：Player
        public const int PlayerLocalLayer = 10;      // VRChat 内置：PlayerLocal
        public const int MirrorReflectionLayer = 18; // VRChat 内置：MirrorReflection

        // ───────── 后处理 Volume（文档「滤镜」「开门：滤镜漏出来」） ─────────
        public const float RoomPriority = 10f, RoomBlend = 0f, RoomWeight = 1f;
        public const float StormPriority = 11f, StormWeight = 0f;
        public const float LeakPriority = 9f, LeakBlend = 1.5f, LeakWeight = 0f;
        public const float LeakExtraWidth = 0.4f; // 宽 = 门宽 + 0.4 m
        public const float LeakDepth = 1f;        // 深 1 m，贴着门外

        // Color Grading，Mode = High Definition Range，Tonemapping = ACES
        public struct GradingPreset
        {
            public float postExposure, temperature, tint, saturation, contrast;
            public bool overrideLift;
            public Vector4 lift;
        }

        // 低饱和冷色（推荐）
        public static readonly GradingPreset CoolPreset = new GradingPreset
        {
            postExposure = -0.4f, temperature = -18f, tint = 3f, saturation = -45f, contrast = 12f,
            // Lift「轻微偏蓝，亮度 -0.02」。PPv2 的 Lift 是 (r, g, b, 亮度偏移)，颜色部分只看相对亮度的偏差，
            // (0.96, 0.98, 1.06) 大约是蓝 +0.08、红 -0.02 的偏移。【偏多少是自定】
            overrideLift = true, lift = new Vector4(0.96f, 0.98f, 1.06f, -0.02f),
        };

        // 纯黑白（备选）：Lift 不动
        public static readonly GradingPreset MonoPreset = new GradingPreset
        {
            postExposure = -0.3f, temperature = 0f, tint = 0f, saturation = -100f, contrast = 20f,
            overrideLift = false, lift = new Vector4(1f, 1f, 1f, 0f),
        };

        public const float VignetteIntensity = 0.28f, VignetteSmoothness = 0.45f;

        // 雷暴压暗 Profile 只勾两项。
        // 纯黑白预设下只勾 Post-exposure：高优先级 Volume 是覆盖不是叠加，再勾 Saturation -55 会把 -100 的黑白盖掉，一打雷画面反而返色。
        public const float StormPostExposure = -0.7f, StormSaturation = -55f;

        // ───────── 灯（文档「场景与灯光 → 灯」「闪电与雷声 → 窗光」「冷光漏出来」） ─────────
        public static readonly Color FlashColor = Hex(0xDC, 0xE6, 0xFF);
        public const float FlashSpotAngle = 70f;   // 60–80
        public const float FlashRange = 7f;        // 6–8 m，刚好够到对面墙
        public const float FlashDownAngle = 30f;   // 斜向下 30°
        public const float FlashOutside = 1.5f;    // 【自定】灯离玻璃多远（窗外）

        public static readonly Color LeakColor = Hex(0x8F, 0xA6, 0xC8);
        public const float LeakRange = 5f;
        public const float LeakLightBack = 1.75f;  // 离门 1.5–2 m（屋里）
        public const float LeakLightHeight = 2f;   // 高约 2 m
        public const float LeakLandOutside = 1.75f; // 落点在门外 1.5–2 m
        // door_cookie.png 里白色矩形占贴图的比例（实测：宽 204/512，高 427/512），用来反算 Spot Angle
        public const float DoorCookieHalfWidthFrac = 204f / 512f;
        public const float DoorCookieHalfHeightFrac = 427f / 512f;

        public static readonly Color LampColor = new Color(255f / 255f, 170f / 255f, 90f / 255f); // 约 2700K
        public const float LampIntensity = 2f;     // 1.5–3
        public const float LampRange = 3f;

        public static readonly Color WindowAreaColor = Hex(0x6F, 0x7F, 0x99);
        public const float WindowAreaIntensity = 0.45f; // 0.3–0.6

        // ───────── 雨粒子（文档「雨 → 窗外雨丝」） ─────────
        public static readonly float[] RainSpeed = { 5f, 8f, 9f };
        public static readonly float[] RainRate = { 100f, 300f, 380f };
        public static readonly int[] RainMaxParticles = { 150, 400, 500 };
        public static readonly float[] RainVelocityX = { 0f, -0.3f, -0.8f };
        public static readonly float[] RainAlpha = { 0.25f, 0.35f, 0.4f };
        public static readonly Color RainColor = new Color(0.75f, 0.80f, 0.88f); // 浅灰蓝【具体色值自定】
        public const float RainStartSize = 0.02f;
        public const float RainBoxExtraWidth = 1f;  // 宽 = 窗宽 + 1 m
        public const float RainBoxThickness = 0.5f; // 厚 0.5 m
        public const float RainBoxDepth = 5f;       // 深 5 m（从玻璃往外）
        public const float RainBoxGap = 0.5f;       // 离玻璃 0.5–6 m 的盒子：从 0.5 m 开始
        public const float RainAboveWindowTop = 1f; // 放在窗顶上方 1 m
        public const float RainLengthScale = 0f, RainVelocityScale = 0.05f;

        // ───────── 闪电发光片（文档「闪电发光片」） ─────────
        public const float GlowDistance = 8f;       // 离窗 6–10 m
        public const float GlowMinWidth = 16f;      // 【自定】比从屋里看出去的视野大一圈
        public const float GlowMinHeight = 12f;

        // ───────── 音源（文档「音频 → 音源摆放」） ─────────
        public const float RainAudioInside = 0.3f;  // 窗户内侧中间【离玻璃多远自定】
        public const float RainAudioNear = 3f, RainAudioFar = 15f;
        public const float ThunderOutside = 3f;     // 窗外 3 m
        public const float ThunderNear = 8f, ThunderFar = 40f;

        // ───────── 玻璃 Mochie/Glass（文档「雨 → 玻璃」） ─────────
        public const string MochieGlassShader = "Mochie/Glass";
        public static readonly Color GrabpassTint = Hex(0x8A, 0x93, 0xA3);
        public const float GlassRoughness = 0f;
        // Normal Refraction：按 shader 默认值的倍数给（Mochie 的 _Refraction 默认 5）。
        // 设计文档原来写的 0.3–0.5 是在不知道 Mochie 量程时估的，只有默认值的十分之一左右，雨滴主要靠折射显出来，太弱会几乎看不见。
        // 先用默认值，进场景觉得扭曲太强再往下调这个倍数。
        public const float GlassRefractionMul = 1f;
        public const float GlassRefractionFallback = 5f; // 读不到 shader 默认值时用
        public const float GlassReflectionStrength = 0.3f; // Environment Reflections 0.3
        // Droplets：按"默认值的倍数"给（小雨、大雨、雷暴）
        public static readonly float[] DropletStrengthMul = { 0.5f, 1f, 1.2f };
        public static readonly float[] DropletSpeedMul = { 0.6f, 1f, 1.3f };
        // Dynamic Droplets：文档只说"多一点 / 默认 / 少一点"，倍数【自定】
        public static readonly float[] DynamicDropletsMul = { 1.3f, 1f, 0.7f };
        public static readonly string[] GlassMaterialNames = { "Room_Glass_Light", "Room_Glass_Heavy", "Room_Glass_Storm" };

        // ───────── 门洞着色片（文档「门洞着色片」） ─────────
        public const string DoorLeakShader = "RainRoom/DoorFilterLeak";
        public static readonly Color LeakGlassTint = new Color(0.80f, 0.86f, 0.97f, 1f); // 冷蓝白（shader 默认值）
        public const float LeakGlassSaturation = 0.55f, LeakGlassBrightness = 0.8f;
        public const float LeakGlassInset = 0.025f; // 往屋里缩进 2–3 cm

        // ───────── 门（文档「门怎么读」） ─────────
        public const float DefaultDoorOpenAngle = 90f;
        public const float DefaultSlideDistance = 1f;
        public const float DoorSmoothing = 4f;

        // ───────── RoomWeather 其它（与脚本默认值一致，搭建时再写一次） ─────────
        public const float FlashPeak = 2.5f;        // 2–3
        public const float MinGap = 12f, MaxGap = 35f;
        public const float MinThunderDelay = 1f, MaxThunderDelay = 3.5f;
        public const float LeakMaxWeight = 0.6f;
        public const float LeakLightPeak = 1.2f;
        public const float ClosedDoorVolume = 0.15f;

        // ───────── 按钮（文档「脚本 → 按钮」） ─────────
        public static readonly string[] ButtonLabels = { "小雨", "大雨", "雷暴", "关闪电" };
        public static readonly string[] ButtonEvents = { "SetLightRain", "SetHeavyRain", "SetStorm", "ToggleFlash" };
        public const float ButtonSize = 0.12f;      // 【自定】
        public const float ButtonHeight = 1.2f;     // 【自定】离地高度
        public const float ButtonSpacing = 0.2f;    // 【自定】

        // ───────── 占位物体（没指定玻璃时） ─────────
        public const float PlaceholderWindowWidth = 1.6f;  // 【自定】
        public const float PlaceholderWindowHeight = 1.2f; // 【自定】
        public const float PlaceholderWindowSill = 0.9f;   // 【自定】窗台离地

        // ───────── 整户触发区（没指定时的估算） ─────────
        public const float SuiteOutsideDoor = 3f;   // 【自定】门外多罩 3 m
        public const float SuitePadding = 0.5f;     // 【自定】四周多罩 0.5 m

        private static Color Hex(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);
    }
}
