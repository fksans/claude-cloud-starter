// RainRoomAssets.cs
// 生成材质、后处理 Profile，设置贴图导入。生成的资源放在 <包>/Generated/ 下。
// 已有的资源默认原样沿用（你进场景后调过的数不会被冲掉）；窗口里勾「重置材质和 Profile」才会按文档数值重写。
//
// 核对过的源码：
// - Particles/Standard Unlit 的 Rendering Mode：Unity 2022.3 StandardParticlesShaderGUI.SetupMaterialWithBlendMode
//   （internal，不能直接调，下面 SetupParticleBlend 照抄了 Fade / Additive 两种）。
// - Mochie/Glass：MochiesCode/Mochies-Unity-Shaders 的 Glass.shader 属性名、GlassEditor.SetKeywords / SetBlendMode
//   （MIT）。材质的 keyword 是 Mochie 的 ShaderGUI 设的，用脚本改属性不会自动更新，所以下面照抄了它的逻辑；
//   另外把 _MaterialResetCheck 设成 0，第一次在 Inspector 里打开材质时 Mochie 会自己再算一遍。
// - PPv2：PostProcessProfile.AddSettings<T>() 只把设置加进列表，子资源要自己 AssetDatabase.AddObjectToAsset，
//   和 PPv2 的 EffectListEditor.AddEffectOverride 一样。
// - 贴图：Cookie 的「Light Type = Spotlight」在 TextureImporterInspector.SetCookieLightTypeDefaults 里
//   就是 borderMipmap = true、Wrap = Clamp、Shape = Texture2D；m_CookieLightType 本身是 internal，只为了 Inspector 显示正确才写。

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using Object = UnityEngine.Object;

namespace RainRoomEditor
{
    internal class RainRoomAssetSet
    {
        public Texture2D doorCookie;
        public Texture2D windowCookie;
        public Texture2D rainStreak;
        public Material rainMaterial;
        public Material flashGlowMaterial;
        public Material leakGlassMaterial;       // shader 没编译成功时为 null
        public Material[] glassMaterials;        // 没装 Mochie 时为空数组
        public Material fallbackGlassMaterial;   // 没装 Mochie、又要建占位玻璃时用
        public PostProcessProfile roomProfile;
        public PostProcessProfile stormProfile;
    }

    internal static class RainRoomAssets
    {
        public static RainRoomAssetSet Build(bool useMonoPreset, bool useWindowCookie3x2, bool resetAssets, bool needFallbackGlass)
        {
            var set = new RainRoomAssetSet();
            string gen = RainRoomSetup.GeneratedFolder;

            // ── 贴图 ──
            set.doorCookie = ImportCookie(RainRoomSetup.FindPackageAsset("Runtime/Textures/door_cookie.png", "Texture2D"));
            set.windowCookie = ImportCookie(RainRoomSetup.FindPackageAsset(
                useWindowCookie3x2 ? "Runtime/Textures/window_cookie_3x2.png" : "Runtime/Textures/window_cookie_2x2.png", "Texture2D"));
            set.rainStreak = ImportRainStreak(RainRoomSetup.FindPackageAsset("Runtime/Textures/rain_streak.png", "Texture2D"));

            // ── 雨丝材质：Particles/Standard Unlit，Fade，贴图 rain_streak.png ──
            Shader particleUnlit = Shader.Find("Particles/Standard Unlit");
            if (particleUnlit == null)
            {
                Debug.LogError("[RainRoom] 找不到内置 shader「Particles/Standard Unlit」，雨丝和闪电发光片的材质没法建。");
            }
            else
            {
                set.rainMaterial = GetOrCreateMaterial(gen + "/Rain_Streak.mat", particleUnlit, resetAssets, m =>
                {
                    m.SetTexture("_MainTex", set.rainStreak);
                    m.SetColor("_Color", Color.white); // 颜色和透明度由粒子的 Start Color 给
                    SetupParticleBlend(m, ParticleBlend.Fade);
                });

                // ── 闪电发光片：Particles/Standard Unlit，Additive，Color 黑色 ──
                set.flashGlowMaterial = GetOrCreateMaterial(gen + "/FlashGlow.mat", particleUnlit, resetAssets, m =>
                {
                    m.SetColor("_Color", Color.black);
                    SetupParticleBlend(m, ParticleBlend.Additive);
                });
            }

            // ── 门洞着色片 ──
            Shader leakShader = Shader.Find(RainRoomConfig.DoorLeakShader);
            if (leakShader == null || !leakShader.isSupported)
            {
                Debug.LogWarning("[RainRoom] DoorFilterLeak.shader 没有编译成功（或当前平台不支持），跳过门洞着色片 LeakGlass。看 Console 里 shader 的报错。");
            }
            else
            {
                set.leakGlassMaterial = GetOrCreateMaterial(gen + "/LeakGlass.mat", leakShader, resetAssets, m =>
                {
                    m.SetColor("_Tint", RainRoomConfig.LeakGlassTint);
                    m.SetFloat("_Saturation", RainRoomConfig.LeakGlassSaturation);
                    m.SetFloat("_Brightness", RainRoomConfig.LeakGlassBrightness);
                    m.SetFloat("_Strength", 0f); // 由 RoomWeather 写入
                });
            }

            // ── 玻璃：Mochie/Glass，三档 ──
            Shader mochie = Shader.Find(RainRoomConfig.MochieGlassShader);
            if (mochie == null)
            {
                set.glassMaterials = new Material[0];
                Debug.LogWarning("[RainRoom] 没找到 Mochie/Glass（Mochie's Unity Shaders 没装），跳过三档玻璃材质。" +
                                 "装好以后再点一次「搭建」会补上。安装见 README。");
            }
            else
            {
                set.glassMaterials = new Material[3];
                for (int i = 0; i < 3; i++)
                {
                    int tier = i;
                    set.glassMaterials[i] = GetOrCreateMaterial(gen + "/" + RainRoomConfig.GlassMaterialNames[i] + ".mat", mochie, resetAssets,
                        m => SetupMochieGlass(m, mochie, tier));
                }
            }

            if (mochie == null && needFallbackGlass)
            {
                Shader standard = Shader.Find("Standard");
                if (standard != null)
                {
                    set.fallbackGlassMaterial = GetOrCreateMaterial(gen + "/Room_Glass_Fallback.mat", standard, resetAssets, m =>
                    {
                        m.SetColor("_Color", new Color(0.55f, 0.58f, 0.64f, 0.15f));
                        m.SetFloat("_Glossiness", 0.95f);
                        SetupStandardTransparent(m);
                    });
                }
            }

            // ── 后处理 Profile ──
            RainRoomConfig.GradingPreset preset = useMonoPreset ? RainRoomConfig.MonoPreset : RainRoomConfig.CoolPreset;
            set.roomProfile = GetOrCreateProfile(gen + (useMonoPreset ? "/Room_PP_Profile_Mono.asset" : "/Room_PP_Profile.asset"), resetAssets, p =>
            {
                var cg = AddSettings<ColorGrading>(p);
                cg.gradingMode.Override(GradingMode.HighDefinitionRange);
                cg.tonemapper.Override(Tonemapper.ACES);
                cg.postExposure.Override(preset.postExposure);
                cg.temperature.Override(preset.temperature);
                cg.tint.Override(preset.tint);
                cg.saturation.Override(preset.saturation);
                cg.contrast.Override(preset.contrast);
                if (preset.overrideLift) cg.lift.Override(preset.lift);

                var vg = AddSettings<Vignette>(p);
                vg.intensity.Override(RainRoomConfig.VignetteIntensity);
                vg.smoothness.Override(RainRoomConfig.VignetteSmoothness);
                vg.color.Override(Color.black);
            });

            set.stormProfile = GetOrCreateProfile(gen + "/Room_PP_Storm_Profile.asset", resetAssets, p =>
            {
                // 只勾两项：高优先级 Volume 是覆盖不是叠加，-0.7 就是最终值
                var cg = AddSettings<ColorGrading>(p);
                cg.postExposure.Override(RainRoomConfig.StormPostExposure);
                cg.saturation.Override(RainRoomConfig.StormSaturation);
            });

            AssetDatabase.SaveAssets();
            return set;
        }

        // ───────── 贴图导入 ─────────

        private static Texture2D ImportCookie(string path)
        {
            if (path == null)
            {
                Debug.LogError("[RainRoom] 找不到 cookie 贴图（Runtime/Textures 下的 PNG）。");
                return null;
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                var s = new TextureImporterSettings();
                importer.ReadTextureSettings(s);

                // Cookie / Spotlight / From Gray Scale / Clamp（文档「窗光」）
                bool needs = s.textureType != TextureImporterType.Cookie ||
                             s.textureShape != TextureImporterShape.Texture2D ||
                             !s.borderMipmap ||
                             s.wrapMode != TextureWrapMode.Clamp ||
                             s.alphaSource != TextureImporterAlphaSource.FromGrayScale ||
                             !importer.crunchedCompression;
                if (needs)
                {
                    s.textureType = TextureImporterType.Cookie;
                    s.textureShape = TextureImporterShape.Texture2D;
                    s.borderMipmap = true;
                    s.wrapMode = TextureWrapMode.Clamp;
                    s.alphaSource = TextureImporterAlphaSource.FromGrayScale;
                    importer.SetTextureSettings(s);
                    importer.crunchedCompression = true; // 文档「性能」：开 Crunch Compression

                    // Inspector 上的 Light Type 下拉框（0 = Spotlight）。字段是 internal，找不到就算了，不影响效果。
                    var so = new SerializedObject(importer);
                    SerializedProperty cookieType = so.FindProperty("m_CookieLightType");
                    if (cookieType != null)
                    {
                        cookieType.intValue = 0;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }

                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D ImportRainStreak(string path)
        {
            if (path == null)
            {
                Debug.LogError("[RainRoom] 找不到 rain_streak.png。");
                return null;
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null &&
                (importer.textureType != TextureImporterType.Default || !importer.alphaIsTransparency ||
                 importer.wrapMode != TextureWrapMode.Clamp || !importer.mipmapEnabled))
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ───────── 材质 ─────────

        private static Material GetOrCreateMaterial(string path, Shader shader, bool reset, Action<Material> setup)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (!reset)
            {
                return mat;
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            setup(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private enum ParticleBlend { Fade, Additive }

        // 照抄 Unity 2022.3 StandardParticlesShaderGUI.SetupMaterialWithBlendMode（BlendMode 枚举：Fade = 2，Additive = 4）
        private static void SetupParticleBlend(Material m, ParticleBlend blend)
        {
            m.SetFloat("_Mode", blend == ParticleBlend.Fade ? 2f : 4f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_BlendOp", (float)BlendOp.Add);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", blend == ParticleBlend.Fade ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        // 照抄 Unity Standard shader 的 Transparent 模式（StandardShaderGUI，_Mode = 3）
        private static void SetupStandardTransparent(Material m)
        {
            m.SetFloat("_Mode", 3f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        // ───────── Mochie/Glass ─────────

        private static void SetupMochieGlass(Material m, Shader shader, int tier)
        {
            // 三档共用（文档「雨 → 玻璃」）
            SetFloatIfExists(m, "_BlendMode", 0f);       // Transparency = Grabpass
            SetFloatIfExists(m, "_Roughness", RainRoomConfig.GlassRoughness);
            SetFloatIfExists(m, "_Refraction", RainRoomConfig.GlassRefraction);
            // 注意：Mochie 的 _Refraction 默认值是 5，文档给的 Normal Refraction 是 0.3–0.5。
            // 如果实际看着雨滴几乎不扭曲窗外，就往上调这一项（Mochie 没有公开取值范围，文档也是按观感给的）。
            if (m.HasProperty("_GrabpassTint")) m.SetColor("_GrabpassTint", RainRoomConfig.GrabpassTint);
            SetFloatIfExists(m, "_ReflectionsToggle", 1f);
            SetFloatIfExists(m, "_ReflectionStrength", RainRoomConfig.GlassReflectionStrength);
            SetFloatIfExists(m, "_SSRToggle", 0f);       // Screen Space Reflections 关
            SetFloatIfExists(m, "_RainMode", 1f);        // Rain → Droplets

            // Droplets：按 shader 默认值的倍数
            SetFloatIfExists(m, "_Strength", Default(shader, "_Strength", 0.3f) * RainRoomConfig.DropletStrengthMul[tier]);
            SetFloatIfExists(m, "_Speed", Default(shader, "_Speed", 60f) * RainRoomConfig.DropletSpeedMul[tier]);
            SetFloatIfExists(m, "_DynamicDroplets", Mathf.Clamp01(Default(shader, "_DynamicDroplets", 0.5f) * RainRoomConfig.DynamicDropletsMul[tier]));
            // X / Y Scale（水珠 3–8 mm）取决于玻璃的 UV 大小，没法自动算：留默认值，见 README 手动步骤。

            MochieSetBlendMode(m);
            MochieSetKeywords(m);
            SetFloatIfExists(m, "_MaterialResetCheck", 0f); // 第一次在 Inspector 打开时让 Mochie 自己再设一遍 keyword
        }

        private static float Default(Shader shader, string name, float fallback)
        {
            int index = shader.FindPropertyIndex(name);
            if (index < 0) return fallback;
            ShaderPropertyType type = shader.GetPropertyType(index);
            if (type != ShaderPropertyType.Float && type != ShaderPropertyType.Range) return fallback;
            return shader.GetPropertyDefaultFloatValue(index);
        }

        private static void SetFloatIfExists(Material m, string name, float value)
        {
            if (m.HasProperty(name)) m.SetFloat(name, value);
            else Debug.LogWarning($"[RainRoom] 材质 {m.name} 的 shader 没有属性 {name}（Mochie 版本不同？），跳过。");
        }

        private static int GetIntSafe(Material m, string name, int fallback) => m.HasProperty(name) ? m.GetInt(name) : fallback;

        private static void SetKeyword(Material m, string keyword, bool on)
        {
            if (on) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }

        // 照抄 Mochie GlassEditor.SetBlendMode（MIT），只保留 Grabpass / Premultiplied / Opaque 三种
        private static void MochieSetBlendMode(Material m)
        {
            int blendMode = GetIntSafe(m, "_BlendMode", 0);
            int queueOffset = GetIntSafe(m, "_QueueOffset", 0);
            switch (blendMode)
            {
                case 0: // Grabpass
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetInt("_SrcBlend", (int)BlendMode.One);
                    m.SetInt("_DstBlend", (int)BlendMode.Zero);
                    m.SetInt("_ZWrite", 0);
                    m.renderQueue = (int)RenderQueue.Transparent + queueOffset;
                    m.SetShaderPassEnabled("GrabPass", true);
                    SetKeyword(m, "_GRABPASS_ON", true);
                    SetKeyword(m, "_PREMULTIPLIED_ON", false);
                    break;
                case 1: // Premultiplied
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.SetInt("_SrcBlend", (int)BlendMode.One);
                    m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0);
                    m.renderQueue = (int)RenderQueue.Transparent + queueOffset;
                    m.SetShaderPassEnabled("GrabPass", false);
                    SetKeyword(m, "_GRABPASS_ON", false);
                    SetKeyword(m, "_PREMULTIPLIED_ON", true);
                    break;
                case 2: // Opaque
                    m.SetOverrideTag("RenderType", "Opaque");
                    m.SetInt("_SrcBlend", (int)BlendMode.One);
                    m.SetInt("_DstBlend", (int)BlendMode.Zero);
                    m.SetInt("_ZWrite", 1);
                    m.renderQueue = (int)RenderQueue.Geometry + queueOffset;
                    m.SetShaderPassEnabled("GrabPass", false);
                    SetKeyword(m, "_GRABPASS_ON", false);
                    SetKeyword(m, "_PREMULTIPLIED_ON", false);
                    break;
            }
        }

        // 照抄 Mochie GlassEditor.SetKeywords（MIT）
        private static void MochieSetKeywords(Material m)
        {
            bool isEmissive = (m.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0;
            bool isTwoPass = m.shader.name.Contains("(Two Pass)");
            int rainMode = GetIntSafe(m, "_RainMode", 0);
            int blurMode = GetIntSafe(m, "_BlurQuality", 1);
            SetKeyword(m, "_EMISSION_ON", isEmissive);
            SetKeyword(m, "_WORKFLOW_PACKED_ON", GetIntSafe(m, "_Workflow", 0) == 1);
            SetKeyword(m, "_STOCHASTIC_SAMPLING_ON", GetIntSafe(m, "_SamplingMode", 0) == 1);
            SetKeyword(m, "_NORMALMAP_ON", m.HasProperty("_NormalMap") && m.GetTexture("_NormalMap") != null);
            SetKeyword(m, "_RAIN_ON", rainMode > 0);
            SetKeyword(m, "_RAINMODE_RIPPLE", rainMode == 2);
            SetKeyword(m, "_RAINMODE_AUTO", rainMode == 3);
            SetKeyword(m, "_BLURQUALITY_LOW", blurMode == 0);
            SetKeyword(m, "_BLURQUALITY_MED", blurMode == 1);
            SetKeyword(m, "_BLURQUALITY_HIGH", blurMode == 2);
            SetKeyword(m, "_BLURQUALITY_ULTRA", blurMode == 3);
            SetKeyword(m, "_AREALIT_ON", GetIntSafe(m, "_AreaLitToggle", 0) == 1);
            SetKeyword(m, "LTCGI", GetIntSafe(m, "_LTCGI", 0) == 1);
            SetKeyword(m, "_REFLECTIONS_ON", GetIntSafe(m, "_ReflectionsToggle", 1) == 1);
            SetKeyword(m, "_SPECULAR_HIGHLIGHTS_ON", GetIntSafe(m, "_SpecularToggle", 1) == 1);
            SetKeyword(m, "_SSR_ON", GetIntSafe(m, "_SSRToggle", 0) == 1 && GetIntSafe(m, "_BlendMode", 0) == 0 && !isTwoPass);
            SetKeyword(m, "BAKERY_SH", GetIntSafe(m, "_BakeryMode", 0) == 1);
            SetKeyword(m, "BAKERY_RNM", GetIntSafe(m, "_BakeryMode", 0) == 2);
            SetKeyword(m, "BAKERY_MONOSH", GetIntSafe(m, "_BakeryMode", 0) == 3);
            SetKeyword(m, "BAKERY_LMSPEC", GetIntSafe(m, "_BAKERY_LMSPEC", 0) == 1);
        }

        // ───────── PPv2 Profile ─────────

        private static PostProcessProfile GetOrCreateProfile(string path, bool reset, Action<PostProcessProfile> setup)
        {
            var profile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PostProcessProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            else if (!reset)
            {
                return profile;
            }
            else
            {
                // 重置：删掉旧的效果子资源
                foreach (PostProcessEffectSettings s in profile.settings)
                {
                    if (s != null) Object.DestroyImmediate(s, true);
                }
                profile.settings.Clear();
            }

            setup(profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T AddSettings<T>(PostProcessProfile profile) where T : PostProcessEffectSettings
        {
            T settings = profile.AddSettings<T>();
            AssetDatabase.AddObjectToAsset(settings, profile);
            return settings;
        }
    }
}
