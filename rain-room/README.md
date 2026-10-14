# RainRoom — 雨夜单人房（VRChat 世界组件）

「这里有人」雨夜单人房的 Unity 包：UdonSharp 脚本、门洞 shader、cookie 和雨丝贴图，加一个**一键搭建**的编辑器工具。
所有数值以设计文档《这里有人 — 雨夜单人房技术指导》为准，集中写在 `Editor/RainRoomConfig.cs` 里，每一项都标了出处。

环境：VRChat Worlds SDK（VCC）· Unity 2022.3 · 内置管线 · UdonSharp · Post Processing Stack v2 · 只跑 PC。

> ⚠️ 这个包是在**没有 Unity 的环境里**写的，没有在 Unity 里实际跑过。下面「未实测 / 不确定的地方」一节列出了所有不确定的点，第一次用请先在一个测试场景里搭一遍。

---

## 目录

```
rain-room/
├── README.md
├── Runtime/
│   ├── Scripts/    SuiteZone.cs  RoomInterior.cs  RoomWeather.cs  WeatherButton.cs  DoorMuffle.cs（可选）
│   ├── Shaders/    DoorFilterLeak.shader（可选的门洞着色片）
│   └── Textures/   door_cookie.png  window_cookie_2x2.png  window_cookie_3x2.png  rain_streak.png
└── Editor/
    ├── RainRoomBuilderWindow.cs   菜单 + 窗口
    ├── RainRoomBuilder.cs         按层级搭建场景、接线
    ├── RainRoomAssets.cs          生成材质、后处理 Profile，设置贴图导入
    ├── RainRoomSetup.cs           UdonSharp Program Asset、Layer 命名、碰撞矩阵、检查
    └── RainRoomConfig.cs          所有数值（出自设计文档）
```

仓库里**没有** `.mat` / `.prefab` / `.unity` / `.asset` / `.meta`：材质、Profile、UdonSharp Program Asset 都由一键搭建生成，`.meta` 由 Unity 导入时生成。

---

## 安装

1. **依赖**
   - VRChat Worlds SDK（VCC 建的世界项目自带 UdonSharp；新版 SDK 已经把 UdonSharp 合进 `com.vrchat.worlds`）。
   - Package Manager → Unity Registry → **Post Processing**（PPv2，`com.unity.postprocessing`）。
   - 可选：[Mochie's Unity Shaders](https://github.com/MochiesCode/Mochies-Unity-Shaders)（玻璃雨滴用 `Mochie/Glass`），安装见它的 [Installation and Setup](https://github.com/MochiesCode/Mochies-Unity-Shaders/wiki/Installation-and-Setup)。**没装也能搭建**，只是跳过三档玻璃材质，装好后再搭建一次就会补上。
2. 把整个 `rain-room` 文件夹复制到项目的 `Assets/` 下（建议改名为 `Assets/RainRoom`，名字随意）。
   - 必须放在 `Assets/` 里，不要放进 `Packages/`：UdonSharp 要在脚本旁边生成 Program Asset，`Packages/` 里的包是只读的。
   - 不需要 asmdef：脚本进 `Assembly-CSharp`，编辑器工具进 `Assembly-CSharp-Editor`，PPv2 / UdonSharp / VRChat SDK 的程序集都是自动引用的。
3. 等 Unity 编译完。

---

## 一键搭建

菜单 **Tools → RainRoom → 一键搭建雨夜单人房…**

| 窗口里的项 | 说明 |
| --- | --- |
| **门板**（必填） | 会转 / 会动的那块门板。打开窗口时默认填当前选中的物体，也可以点「用选中的」。**搭建时门必须是关着的**（脚本在世界加载时记下门关着的样子，搭建工具也按关着的门量门洞大小）。 |
| **房间内墙 Box Collider**（必填） | 大小正好贴着房间内墙的 Box Collider。工具复制它的位置、朝向、大小，**不会改它**。Y 轴要朝上。 |
| 推拉门 / 全开角度 / 全开移动 | 写进 RoomWeather 的 slidingDoor / doorOpenAngle（默认 90）/ slideDistance（默认 1）。 |
| 玻璃 Renderer（可选） | 窗户玻璃。雨粒子、闪电窗光、发光片、音源都按它的位置摆。留空就在 `Static` 下建一块占位玻璃。 |
| 整户范围 Box Collider（可选） | 罩住整户（公共区 + 两间房 + 门外）。留空就按「房间 + 门外 3 m」估一个。 |
| 根物体名字 | 默认 `Room_这里有人`。室友也用这个工具时改个名字，避免互相覆盖。 |
| 滤镜 | 低饱和冷色（推荐）/ 纯黑白，两套参数照文档。 |
| 窗框 cookie | 2×2（田字格）或 3×2，要和你们的窗棂格数一致。 |
| 天气联网同步 | 对应 RoomWeather 的 syncAcrossPlayers。 |
| 隔门变闷 DoorMuffle | 建 DoorMuffle 并给三个音源加 Audio Low Pass Filter。 |
| 门洞着色片 LeakGlass | 建可选的门洞着色片（多一次 GrabPass）。 |
| 重置材质和 Profile | 默认不勾：已经生成过的材质 / Profile 原样沿用（你调过的数不会被冲掉）。勾上就按文档数值重写。 |

### 第一次点「搭建」要点两次

UdonSharp 的每个脚本都需要一个 **UdonSharp Program Asset**。第一次点「搭建」时工具会在每个 `.cs` 旁边生成同名 `.asset`（做法和 UdonSharp 自己「新建 U# 脚本」一样：`CreateInstance<UdonSharpProgramAsset>()` → 设 `sourceCsScript` → `AssetDatabase.CreateAsset`），然后 UdonSharp 会在后台升级、编译它们。**等窗口里的状态变成「UdonSharp：就绪」再点一次「搭建」**。在那之前没法接线（UdonSharp 的 `CopyProxyToUdon` 会因为版本号没到而报错）。

如果状态一直停在「还在升级 / 编译」：试试菜单 *VRChat SDK → Udon Sharp → Force Upgrade*（菜单位置随 SDK 版本可能不同），或者重启 Unity。
如果状态是「编译有错误」：看 Console。若报错是 DoorMuffle 的 `AudioLowPassFilter` 没有开放给 Udon，按文档删掉 `DoorMuffle.cs` 和 `DoorMuffle.asset`，其它功能不受影响（隔门的雨声只会变小、不会变闷）；编辑器工具用反射找 DoorMuffle，删掉它不会让工具编译失败。

### 它做了什么

**项目设置**
- Layer 22 命名为 `PostProcessing`，Layer 23 命名为 `PlayerZone`（已经有别的名字时会先问）。
- 碰撞矩阵：Layer 22 这一行全部取消；Layer 23 只勾 PlayerLocal（Layer 10）。用的是 `Physics.IgnoreLayerCollision`，和 Unity 自己的 Physics 设置面板同一个入口。

**场景**（文档「脚本 → 层级结构」）

```
Room_这里有人
├── SuiteZone      Box Collider（Is Trigger），Layer 23，SuiteZone.cs
├── RoomInterior   Box Collider（Is Trigger，= 你指定的内墙盒子），Layer 23，RoomInterior.cs
├── RoomWeather    RoomWeather.cs（一直启用，Manual 同步）
├── DoorMuffle     DoorMuffle.cs（可选）
├── Static         你的墙、地、家具……（工具只在缺少时补：Window_Glass 占位玻璃、Lamp_Light 台灯烘焙点光、Window_AreaLight 窗口冷光）
├── Buttons        Button_小雨 / 大雨 / 雷暴 / 关闪电（Cube + Box Collider + WeatherButton.cs，Interaction Text 已填）
└── RoomContent    默认关闭
    ├── Room_PP          Layer 22，Box Collider = 内墙，Is Global 关，Blend 0，Priority 10，Weight 1
    ├── Room_PP_Storm    Layer 22，同样大小，Priority 11，Weight 0，Profile 只勾 Post-exposure -0.7、Saturation -55（纯黑白时只勾 Post-exposure）
    ├── Room_PP_Leak     Layer 22，宽 = 门宽 + 0.4、高 = 门高、深 1 m，贴着门外，Blend 1.5，Priority 9，Weight 0
    ├── Rain_Light / Rain_Heavy / Rain_Storm   Box 发射器，速度 5/8/9，Rate 100/300/380，Max 150/400/500，
    │                                          风 X 0/-0.3/-0.8，Alpha 0.25/0.35/0.4，Prewarm，World，Stretched Billboard
    ├── Audio_LightRain / Audio_HeavyRain      Loop、Play On Awake、Volume 0，Spatial Blend 1，VRC Spatial Audio Near 3 / Far 15
    ├── Audio_Thunder                          Play On Awake 关，窗外 3 m，Near 8 / Far 40
    ├── FlashLight     Spot，#DCE6FF，窗外斜向下 30°，Spot Angle 70，Range 7，窗框 cookie，Important，无阴影，组件关
    ├── FlashGlow      离窗 8 m 的大 Quad，Particles/Standard Unlit Additive，黑色，Renderer 关
    ├── Room_LeakLight Spot，#8FA6C8，屋里离门 1.75 m、高 2 m，落点门外 1.75 m，Range 5，门形 cookie，Important，无阴影，组件关
    └── LeakGlass      门洞里往屋里缩 2.5 cm 的 Quad，DoorFilterLeak 材质，正面朝门外，Renderer 关（可选）
```

脚本之间的引用全部接好：SuiteZone → RoomContent / RoomWeather；RoomInterior → RoomWeather；按钮 → RoomWeather + eventName；RoomWeather 的粒子、玻璃、音源、Volume、灯、发光片、门、漏光、着色片、DoorMuffle；DoorMuffle → 三个 Low Pass Filter。音频栏位（AudioSource 的 Clip、Thunder Clips）留空。

**生成的资源**（`<包>/Generated/`）
- `Room_PP_Profile.asset`（或纯黑白的 `Room_PP_Profile_Mono.asset`）：Color Grading（HDR、ACES、EV -0.4、Temperature -18、Tint +3、Saturation -45、Contrast +12、Lift 轻微偏蓝 -0.02）+ Vignette（0.28 / 0.45 / 黑）。房间和门口的 Volume 共用这一份。
- `Room_PP_Storm_Profile.asset`（或纯黑白的 `Room_PP_Storm_Profile_Mono.asset`，只勾 Post-exposure：再勾 Saturation -55 会盖掉黑白的 -100，一打雷就返色）
- `Rain_Streak.mat`（Particles/Standard Unlit，Fade，rain_streak.png）、`FlashGlow.mat`（Additive，黑色）、`LeakGlass.mat`（Tint / Saturation 0.55 / Brightness 0.8）
- `Room_Glass_Light / Heavy / Storm.mat`（装了 Mochie 才有）：Grabpass、Roughness 0、Normal Refraction = shader 默认值（Mochie 是 5）、Grabpass Tint #8A93A3、Reflections 0.3、SSR 关、Rain = Droplets；Strength ×0.5/×1/×1.2，Speed ×0.6/×1/×1.3，Dynamic Droplets ×1.3/×1/×0.7（相对 shader 默认值）
- `Room_Glass_Fallback.mat`：没装 Mochie、又建了占位玻璃时用的普通透明材质
- 贴图导入：三张 cookie 设为 Cookie / Spotlight / From Gray Scale / Clamp / Crunch；雨丝设为 Alpha Is Transparency / Clamp

**重复运行**：根物体下除 `Static` 以外的子物体全部删掉重建。按钮的位置、SuiteZone 的大小（如果这次没另外指定整户碰撞体）会保留。`Static` 下的东西永远不删，占位物体只在缺少时才建。
**Undo**：整次搭建（含 Layer 命名和碰撞矩阵）是一个 Undo 步骤，Ctrl+Z 一次撤销；搭建中途出错会自动撤销。生成的资源文件不会被 Undo 删除（留着没有影响）。

---

## 手动步骤

一键搭建之后还要手动做这些（按顺序）：

1. **后处理相机（全图只做一次，清吧可能已经有了）**
   VRC Scene Descriptor 的 Reference Camera 上加 **Post-process Layer**：Volume blending → Trigger 选这台相机自己，Layer 勾 `PostProcessing`；Anti-aliasing 选 No Anti-aliasing。清吧已经有全局后处理就沿用它的相机，只要确认 Layer 里勾了 `PostProcessing`。搭建完成时工具会检查并提示。
2. **Interact Passthrough**（不做的话房里按钮点不到）
   VRC Scene Descriptor → **Interact Passthrough** 勾上 Layer 22 和 23。用户层默认会挡住交互射线，罩着整间房的触发器会把按钮挡住。
3. **删掉参考碰撞体**：你指定的「房间内墙 Box Collider」（和整户范围的那个）工具只是复制了大小，它本身如果还开着会挡住玩家或按钮，搭建后请删掉或禁用（工具会提示）。
4. **SuiteZone 的大小**：没指定整户范围时，把 `SuiteZone` 的 Box Collider 放大到罩住整户（公共区 + 两间房 + 门外那一块）。重复搭建会保留你改的大小。
5. **摆窗户**
   - 把玻璃放进真正的窗洞（没指定玻璃时就是 `Static/Window_Glass`），在窗口里指定它，**再点一次搭建**：雨粒子、闪电窗光、发光片、音源会重新对齐到玻璃。多扇窗时用一个大盒子罩住所有窗（手动改 Rain_* 的 Shape）。
   - `Static/Window_AreaLight` 只在第一次建，窗户挪了要手动跟着挪，尺寸等于窗洞。
   - 玻璃材质的 Droplets **X / Y Scale** 调到水珠看起来 3–8 mm（取决于玻璃的 UV，工具算不了）。
   - 窗框 cookie 的格数不对就换另一张，或者在图里改。
6. **实时灯的 Culling Mask**：工具只去掉了 Player、PlayerLocal、MirrorReflection。按文档，FlashLight 只勾房间几何所在的层，Room_LeakLight 只勾走廊地面和门框所在的层。
7. **门口漏光**：看一下 cookie 里的白色矩形是否正好对上门洞（Spot Angle 是按门高反算的，门很宽或很窄时要微调）。想要门半开时光带变窄，开 Hard Shadows + Low Resolution。
8. **拖音频**（文档「音频」）
   - 用 Audacity 剪好：小雨 / 大雨两段无缝循环，3–5 段远雷（低通 2000–3000 Hz）。
   - 导入设置：循环 Force To Mono、Compressed In Memory、Vorbis 50–60%；雷声 Force To Mono、Decompress On Load、Vorbis 60%。
   - 小雨循环拖到 `Audio_LightRain` 的 AudioClip，大雨循环拖到 `Audio_HeavyRain`，3–5 段雷拖进 `RoomWeather` 的 **Thunder Clips**。
   - 用到 CC BY / CC BY-SA 素材时，在世界里放致谢牌。
9. **台灯**：把 `Static/Lamp_Light`（烘焙点光，2700K 左右，强度 2，范围 3 m）挪进灯罩；灯泡给发光材质（Emission 约 2）。
10. **烘焙**
    - 墙、地、家具勾 Static；玻璃、FlashGlow、LeakGlass 不要勾 Batching Static。
    - 加 **Light Probe Group** 铺满房间；加一个 **Reflection Probe**（Baked、Box Projection、范围等于房间）。
    - 这间房的 Lightmap Resolution 10–20 texel/m。
    - 烘焙遮挡剔除（Window → Rendering → Occlusion Culling），墙和门设成 Occluder Static。
    - 最后 Generate Lighting。
11. **门牌和告示**（TextMeshPro + 霞鹜文楷，只打包用到的字），见文档「门牌与告示」。
12. 按文档「上线前测试清单」先用 ClientSim 测，再上传私人世界戴头显测，最后两人测同步。

---

## 未实测 / 不确定的地方

没有 Unity 可跑，所有 API 都是对照官方源码核对的，编辑器代码用 `mcs` 对着 Unity 2021.1 的 UnityEngine/UnityEditor 引用程序集 + PPv2 源码 + 按 UdonSharp 源码写的桩编译通过（`Renderer.localBounds` 是 2021.2 才加的，已在 Unity 2022.3 源码里确认存在）。具体：

| 位置 | 内容 | 依据 / 风险 |
| --- | --- | --- |
| UdonSharp 组件 | 用 `UdonSharpUndo.AddComponent<T>()` 添加，字段写在 proxy 上，再 `UdonSharpEditorUtility.CopyProxyToUdon()`；按钮文字写 backing `UdonBehaviour.interactText`。 | 对照 UdonSharp 1.1.9 源码（`UdonSharpUndo.cs`、`UdonSharpEditorUtility.cs`、`UdonSharpGUI.cs`）。UdonSharp 在场景构建时也会自动 CopyProxyToUdon。 |
| Program Asset | 工具自己生成 `.asset`，第一次要等 UdonSharp 升级 + 编译后再点一次。 | `UdonSharpProgramAssetPostprocessor` 会在导入时排队升级，`UdonSharpUpgrader` 在下一次 Editor Update 里把版本号升到当前并 CompileSync。没实测过整个流程要多久。 |
| DoorMuffle | `AudioLowPassFilter.cutoffFrequency` 是否开放给 Udon。 | **不确定**（文档原本就标了）。AudioLowPassFilter 在 VRChat 世界组件白名单里，但 Udon 是否暴露这个属性没查到。不行就删掉 DoorMuffle。 |
| DoorFilterLeak.shader | 单通道立体（SPS-I）下的 GrabPass。 | 原样沿用，**没编译测试过**（文档原本就标了）。宏的用法（`UNITY_DECLARE_SCREENSPACE_TEXTURE` + `UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX`）看起来是对的。shader 没编译成功时工具会跳过 LeakGlass。 |
| 雨丝方向 | Stretched Billboard 下 `rain_streak.png`（竖条）沿速度方向拉伸后是不是竖着的。 | **没查到官方说明贴图哪条轴沿速度方向**。如果看到的是横着的短片，把 rain_streak.png 转 90° 再试。 |
| Mochie/Glass | 属性名 `_BlendMode` `_Roughness` `_Refraction` `_GrabpassTint` `_ReflectionsToggle` `_ReflectionStrength` `_SSRToggle` `_RainMode` `_Strength` `_Speed` `_DynamicDroplets`，keyword 逻辑照抄 `GlassEditor.SetKeywords / SetBlendMode`。 | 对照 2026-10 的 Mochie 主分支。版本不同属性名可能变，找不到的属性会在 Console 警告并跳过；材质的 `_MaterialResetCheck` 设成 0，第一次在 Inspector 打开时 Mochie 会自己重算 keyword。 |
| Normal Refraction | 用 shader 默认值 × `GlassRefractionMul`（默认 ×1，Mochie 默认是 5）。 | 设计文档原来写的 0.3–0.5 是不知道 Mochie 量程时估的，只有默认的十分之一左右，雨滴会几乎看不见，已改。进场景觉得扭曲太强，把倍数往下调。 |
| Dynamic Droplets | 文档只写「多一点 / 默认 / 少一点」，取 ×1.3 / ×1 / ×0.7。 | 自定。 |
| Lift | 文档「轻微偏蓝，亮度 -0.02」，取 (0.96, 0.98, 1.06, -0.02)。 | 自定；PPv2 的 `ColorUtilities.ColorToLift` 只看颜色相对亮度的偏差。 |
| VRC Spatial Audio Source | 按类型名 `VRCSpatialAudioSource` 反射查找，设 `Near` / `Far`。 | 字段名按 VRChat 文档的属性名；设不上会提示手动填。 |
| 后处理相机检查 | 反射读 `VRC_SceneDescriptor.ReferenceCamera`。 | 只提示、不修改；读不到就跳过。 |
| Cookie 导入 | Light Type = Spotlight 在 Unity 里就是 borderMipmap + Clamp + Texture2D；`m_CookieLightType` 是 internal 字段，只为 Inspector 显示。 | 对照 Unity 2022.3 `TextureImporterInspector.SetCookieLightTypeDefaults`。 |
| 碰撞矩阵 | `Physics.IgnoreLayerCollision` 会写进 ProjectSettings。 | Unity 2022.3 自己的 `PhysicsManagerInspector` 就是这么改的。VRChat 文档：22–30 层的名字和碰撞矩阵上传时不会被改写。 |
| Spot Angle（门口漏光） | 按 door_cookie.png 里白色矩形的实测比例（高 427/512）反算。 | 假设 Spot cookie 铺满整个锥角；需要进场景看一眼。 |
| 摆放 | 门 / 窗的朝向取「离哪面内墙最近」，大小按 Renderer 的 localBounds 在房间坐标系里量。 | 门必须关着、选的是门板本身；房间盒子要竖直。 |
| Interact Passthrough | 没有自动设置（按要求写成手动步骤）。 | — |

脚本（`Runtime/Scripts`）和 shader 是附件原样，检查过没有发现需要改的问题。

---

## 素材授权

- 脚本、shader、cookie、雨丝贴图：自制，随便用。
- 玻璃 shader：Mochie's Unity Shaders（MIT），本包只引用、不包含；`RainRoomAssets.cs` 里照抄了它 `GlassEditor` 的 keyword 设置逻辑，版权与许可声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
- 音频、台灯模型、字体等的授权见设计文档「素材清单」。
