# rain-room — 给修 bug 会话的说明

VRChat 世界「这里有人」雨夜单人房的 Unity 包。环境：VRChat Worlds SDK（VCC）、Unity 2022.3、内置管线、UdonSharp、PPv2，只跑 PC。
整个包放在 `Assets/` 下用（不能放 `Packages/`：U# 要在脚本旁写 Program Asset）。没有 asmdef，脚本进 Assembly-CSharp，Editor 进 Assembly-CSharp-Editor。
设计文档不用读：所有数值已经搬进 `Editor/RainRoomConfig.cs`，每项注了出处；标【自定】的是文档没给、我们自己定的。
**从没在 Unity 里跑过**，只用 mcs 对 Unity 2021.1 引用 DLL + PPv2 源码 + 按 U# 源码写的桩编译通过。用户报的 bug 很可能落在下面「不确定」一节。

## 结构
- `Runtime/Scripts/`（附件原样，未改动；类在全局命名空间）
  - `SuiteZone.cs`：整户触发区。本地玩家进 → `roomContent.SetActive(true)` 再 `weather.SetSuiteActive(true)`（顺序不能反，粒子要先启用才能 Play）；出 → 反过来。Start 里关掉 roomContent。
  - `RoomInterior.cs`：房间内触发区 → `weather.SetPlayerInside(bool)`，决定隔门声音和着色片。
  - `RoomWeather.cs`：核心。`[UdonSynced] _mode`（0 小雨/1 大雨/2 雷暴）+ `FieldChangeCallback(Mode)`，Manual 同步；按钮入口 `SetLightRain/SetHeavyRain/SetStorm`（拿所有权→改→RequestSerialization）、`ToggleFlash`（仅本地）。`Update` 首行 `if (!_suiteActive) return`；每帧读门（转轴比角度/推拉比位移/门板隐藏=全开，加载时记关门姿态）→ 变了才 `ApplyDoor`（leakVolume.weight=k×0.6、漏光灯、着色片、隔门音量 0.15→1 按 √k、DoorMuffle）。闪电：每 2 s 的 `_LightningTick` 延迟事件，雷暴档 12–35 s 闪一次，0.8 s 曲线 `FlashShape`，雷声延迟 1–3.5 s。flashGlow/leakGlass 用 `renderer.material` 实例。
  - `WeatherButton.cs`：`Interact()` → `weather.SendCustomEvent(eventName)`。
  - `DoorMuffle.cs`（可选）：RoomWeather 用 `SetProgramVariable("open01")` + `SendCustomEvent("_Apply")` 驱动，低通 900 Hz↔22000 Hz 对数插值。
- `Runtime/Shaders/DoorFilterLeak.shader`：`RainRoom/DoorFilterLeak`，GrabPass 把门洞画面去饱和/染色，`_Strength` 由脚本写；Quad 正面朝门外。
- `Runtime/Textures/`：`door_cookie.png`（白矩形占宽 204/512、高 427/512）、`window_cookie_2x2/3x2.png`、`rain_streak.png`（32×256 竖条）。
- `Editor/`
  - `RainRoomBuilderWindow.cs`：菜单 `Tools/RainRoom/一键搭建雨夜单人房…`，收集参数，每 0.1 s 刷新 U# 状态。文件名必须等于类名（`PackageRoot` 靠它定位包根）。
  - `RainRoomBuilder.cs`：`Build(RainRoomOptions)`：检查 U# → 量门/窗 → 生成资源 → 一个 Undo 组里改 Layer、碰撞矩阵、删旧建新、接线。
  - `RainRoomAssets.cs`：贴图导入、材质、PPv2 Profile，输出到 `<包>/Generated/`。
  - `RainRoomSetup.cs`：包路径、U# Program Asset 检查/生成、Layer 命名、碰撞矩阵、后处理相机检查。
  - `RainRoomConfig.cs`：全部常量。

## 关键设计决定
- 层级固定（名字在 Config）：根 `Room_这里有人`（窗口可改名）下 SuiteZone、RoomInterior、RoomWeather、DoorMuffle、Static、Buttons、RoomContent（默认关；含 Room_PP/_Storm/_Leak、Rain_*、Audio_*、FlashLight、FlashGlow、Room_LeakLight、LeakGlass）。
- 重跑：删根下除 `Static` 外的全部子物体再建；`Static` 永不删，占位物体（Window_Glass、Lamp_Light、Window_AreaLight）只在缺时建。按钮世界姿态、SuiteZone 盒子（未另指定时）先记下再恢复。几何量都在删除**之前**量（参考碰撞体可能就是旧的 RoomInterior）。门/玻璃若在被删的子物体里 → 报错中止。
- Undo：整次一个组；异常时 `Undo.RevertAllDownToGroup`。资源文件不进 Undo。
- 资源默认「已存在就原样沿用」，勾「重置材质和 Profile」才重写（保护用户手调的数）。
- 雷暴 Profile 按预设分两份：纯黑白版（`_Mono`）只覆盖 Post-exposure，不覆盖 Saturation（-55 会盖掉 -100，一打雷就返色）。
- 几何：参考 Box Collider → `Frame`（中心/旋转/世界尺寸）；门、窗按「离哪面内墙最近（x 或 z 面）」定外法线，用 `Renderer.localBounds` 角点转到房间局部坐标量宽高与内外面（`Opening.nIn/nOut`）。没 Renderer 退到 Collider 世界 AABB，再退到默认 0.9×2.1。
- 漏光 Spot Angle 按 cookie 白矩形高 0.834 反算：`2·atan(tan(门上下沿偏轴角)/0.834)`。
- 雨粒子物体旋转 `LookRotation(-up, out)`：+Z 朝下（Box 沿 +Z 发射），+Y 朝窗外；shape.scale=(窗宽+1, 5, 0.5)；Velocity over Lifetime 用 Local，三分量都给常数（Unity 要求同模式）。
- Quad 朝向：可见面法线是 -forward。玻璃/发光片 forward=窗外（从屋里看），LeakGlass forward=屋里（从门外看）；面光源朝 +Z 发光，forward=屋里。
- DoorMuffle、VRCSpatialAudioSource、VRC_SceneDescriptor 都用反射找（`FindTypeByName`），删掉 DoorMuffle.cs 或 SDK 改名不会让编辑器代码编译失败。DoorMuffle 类型查找有静态缓存。
- 实时灯 Culling Mask 只去掉 9 Player / 10 PlayerLocal / 18 MirrorReflection；Light 组件建好后 `enabled=false`、intensity 0。
- Interact Passthrough、后处理相机、音频、烘焙、门牌是 README 里的手动步骤，工具只检查后处理相机并提示，不改。

## 已核对的 API 结论
- **UdonSharp 1.1.9**（命名空间：`UdonSharp` 放运行时和 ProgramAsset，`UdonSharpEditor` 放编辑器工具；新版 SDK 把 U# 合进 com.vrchat.worlds，API 不变）
  - 加组件：`UdonSharpUndo.AddComponent<T>(go)` = `Undo.AddComponent` + `RunBehaviourSetupWithUndo`（建隐藏 backing `UdonBehaviour`，挂 programSource）。Program Asset 为 null 时会 NRE，所以必须先有 Program Asset。
  - 字段写在 proxy（C# 组件）上；`UdonSharpEditorUtility.CopyProxyToUdon(proxy)` 同步到 backing。U# 的 `[PostProcessScene]` 在进 Play/上传时也会自动同步。版本号未到 `UdonSharpProgramVersion.CurrentVersion` 时 CopyProxyToUdon 抛异常。
  - Interaction Text：`UdonSharpEditorUtility.GetBackingUdonBehaviour(b).interactText`（UdonSharpGUI 就这么改）。
  - Program Asset：`CreateInstance<UdonSharpProgramAsset>()`，设 `sourceCsScript`，`AssetDatabase.CreateAsset(.cs 同目录同名 .asset)`（与官方「新建 U# 脚本」一致）。导入后 postprocessor 排队，`UdonSharpUpgrader` 在下次 EditorUpdate 设 ScriptVersion 并 CompileSync → 所以第一次搭建要点两次。查询：`UdonSharpProgramAsset.GetProgramAssetForClass(Type)`、`AnyUdonSharpScriptHasError()`、`CompileAllCsPrograms(bool)`。`ResetCaches` 是 internal。
- **PPv2**：`profile.AddSettings<T>()` 只加进列表并设 HideFlags，子资源要自己 `AssetDatabase.AddObjectToAsset(settings, profile)`（同 PPv2 EffectListEditor）；重置时 `DestroyImmediate(s, true)` 后 `settings.Clear()`。参数用 `.Override(v)`。Lift 是 Vector4(r,g,b,亮度偏移)，`ColorToLift` 只看颜色相对亮度的偏差。局部 Volume 需要同物体触发碰撞体。
- **Mochie/Glass**（2026-10 主分支）：文档名 → 属性：Transparency→`_BlendMode`(0 Grabpass/1 Premultiplied/2 Opaque)，Roughness→`_Roughness`，Normal Refraction→`_Refraction`(默认 5)，Grabpass Tint→`_GrabpassTint`，Environment Reflections→`_ReflectionsToggle`+`_ReflectionStrength`，SSR→`_SSRToggle`，Rain Mode→`_RainMode`(1 Droplets)，Droplets 的 Strength→`_Strength`(默认 0.3)、Speed→`_Speed`(60)、X/Y Scale→`_XScale/_YScale`、Dynamic Droplets→`_DynamicDroplets`(0.5)。keyword 由 Mochie 的 ShaderGUI 设，脚本改属性不会更新 → `RainRoomAssets` 照抄了 `GlassEditor.SetKeywords/SetBlendMode`，并把 `_MaterialResetCheck` 设 0 让 Inspector 首次打开时重算。
- **Unity 2022.3**：Particles/Standard Unlit 的混合设置抄自 `StandardParticlesShaderGUI.SetupMaterialWithBlendMode`（`_Mode` Fade=2、Additive=4）。Cookie「Spotlight」= borderMipmap+Clamp+Texture2D（`m_CookieLightType` 是 internal，只为 Inspector 显示）。碰撞矩阵用 `Physics.IgnoreLayerCollision`（Unity 自己的 PhysicsManagerInspector 也这样做，会写进 ProjectSettings）；Layer 名在 TagManager 的 `layers`。`LightType.Rectangle`、`Light.areaSize`、`Renderer.localBounds` 均存在。
- **VRChat**：Layer 9 Player、10 PlayerLocal、18 MirrorReflection；22–30 是用户层，名字和碰撞矩阵上传时不会被改写；用户层默认挡交互射线（所以需要 Interact Passthrough 22/23）。AudioLowPassFilter、PostProcessVolume/Layer 在世界组件白名单里。

## 不确定（优先怀疑这些）
1. `AudioLowPassFilter.cutoffFrequency` 是否开放给 Udon。不行就删 DoorMuffle.cs 和 .asset。
2. DoorFilterLeak.shader 从没编译过（SPS-I 宏看着对）。shader 没编译成功时工具会跳过 LeakGlass（`ShaderUtil.ShaderHasError` + `isSupported` 双重检查）。
3. Stretched Billboard 下 rain_streak.png 的哪条轴沿速度方向，没查到官方说明。如果显示成横的，就把贴图转 90°。
4. Mochie `_Refraction` 现在 = shader 默认值 × `GlassRefractionMul`（×1，默认 5；文档原来的 0.3–0.5 太弱，已改），观感没实测。Dynamic Droplets 的倍数 ×1.3/×1/×0.7、Lift (0.96,0.98,1.06,-0.02) 都是自定的。
5. VRCSpatialAudioSource 的 `Near`/`Far` 字段名、VRC_SceneDescriptor 的 `ReferenceCamera` 字段名：反射读写，失败只提示。
6. 首次搭建的两段式流程（生成 Program Asset → 等升级编译）没实测耗时。卡住时用 VRChat SDK → Udon Sharp → Force Upgrade，或重启 Unity。
7. 门洞漏光的 Spot Angle 反算，假设 Spot cookie 铺满整个锥角。
8. 摆放的前提：参考盒子竖直、搭建时门是关着的、选的是门板本身。不满足时会出警告，但结果可能偏。

## 改代码时
- 改数值改 `RainRoomConfig.cs`。改层级名要同时考虑重跑时的查找（`root.Find(名字)`）。
- 本地编译检查：把 `Renderer.localBounds` 临时换成 `bounds`（2021.1 DLL 里没有），并且不要用 C# 7.2 以上语法（mcs 只支持到 7.x）。Unity 2022.3 本身支持 C# 9。
