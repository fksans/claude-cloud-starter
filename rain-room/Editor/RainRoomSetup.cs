// RainRoomSetup.cs
// 搭建前的准备：找包的位置、给 UdonSharp 脚本生成 Program Asset、命名 Layer 22/23、设碰撞矩阵、检查后处理相机。
//
// 已对照的官方源码 / 文档（无法在这里跑 Unity，以下都是读源码核对的）：
// - UdonSharp 1.x（vrchat-community/UdonSharp，Packages/com.vrchat.UdonSharp/Editor）：
//     UdonSharpProgramAsset.sourceCsScript（public MonoScript）、ScriptVersion / CompiledVersion、
//     UdonSharpProgramVersion.CurrentVersion、UdonSharpProgramAsset.GetProgramAssetForClass(Type)、
//     UdonSharpProgramAsset.AnyUdonSharpScriptHasError()、UdonSharpProgramAsset.CompileAllCsPrograms(bool, bool)。
//     官方「新建 U# 脚本」按钮就是 CreateInstance<UdonSharpProgramAsset>() → 设 sourceCsScript → AssetDatabase.CreateAsset，
//     这里照做。新建的 Program Asset 导入后会被 UdonSharpUpgrader 在下一次 Editor Update 里升级版本号并编译，
//     版本号没到 CurrentVersion 之前 CopyProxyToUdon 会抛异常，所以第一次要等它编译完再点一次「搭建」。
//     注：较新的 VRChat Worlds SDK 已经把 UdonSharp 合进 com.vrchat.worlds 包里，命名空间和这些 API 没变。
// - Unity 2022.3（UnityCsReference）：PhysicsManagerInspector 自己就是用 Physics.IgnoreLayerCollision 改碰撞矩阵的，
//     这里也用它；Layer 名字写在 ProjectSettings/TagManager.asset 的 "layers" 数组里。
// - VRChat creator-docs：Layer 9 Player、10 PlayerLocal、18 MirrorReflection；22–30 是用户层，上传时不会被改写。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using Object = UnityEngine.Object;

namespace RainRoomEditor
{
    internal static class RainRoomSetup
    {
        // ───────── 包的位置 ─────────

        /// <summary>包的根目录（Editor 文件夹的上一级），例如 "Assets/RainRoom"。</summary>
        public static string PackageRoot
        {
            get
            {
                string[] guids = AssetDatabase.FindAssets("RainRoomBuilderWindow t:MonoScript");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileName(path) != "RainRoomBuilderWindow.cs") continue;
                    string editorDir = Path.GetDirectoryName(path);
                    string root = Path.GetDirectoryName(editorDir);
                    return root?.Replace('\\', '/');
                }
                return "Assets/RainRoom";
            }
        }

        public static string GeneratedFolder
        {
            get
            {
                string root = PackageRoot;
                string folder = root + "/Generated";
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder(root, "Generated");
                return folder;
            }
        }

        /// <summary>按包内相对路径找资源；找不到（比如文件夹被改名了）就按文件名在全工程里找。</summary>
        public static string FindPackageAsset(string relativePath, string typeFilter)
        {
            string path = PackageRoot + "/" + relativePath;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) return path;

            string fileName = Path.GetFileNameWithoutExtension(relativePath);
            foreach (string guid in AssetDatabase.FindAssets(fileName + " t:" + typeFilter))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(p) == Path.GetFileName(relativePath)) return p;
            }
            return null;
        }

        // ───────── 类型 ─────────

        /// <summary>DoorMuffle 是可选的：如果 UdonSharp 提示 AudioLowPassFilter 没开放给 Udon，文档让你直接删掉它。
        /// 所以这里不直接写 typeof(DoorMuffle)，删掉脚本后编辑器代码照样能编译。</summary>
        public static Type FindDoorMuffleType()
        {
            // 扫全部程序集比较慢，窗口每次重绘都会问，所以缓存。删掉 DoorMuffle.cs 会触发脚本重新编译、静态字段清空。
            if (!_doorMuffleSearched)
            {
                _doorMuffleType = FindTypeByName("DoorMuffle", typeof(UdonSharpBehaviour));
                _doorMuffleSearched = true;
            }
            return _doorMuffleType;
        }

        private static bool _doorMuffleSearched;
        private static Type _doorMuffleType;

        public static Type FindTypeByName(string name, Type baseType)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (Type t in types)
                {
                    if (t.Name == name && (baseType == null || baseType.IsAssignableFrom(t)) && !t.IsAbstract)
                        return t;
                }
            }
            return null;
        }

        public static MonoScript FindMonoScript(Type type)
        {
            foreach (string guid in AssetDatabase.FindAssets(type.Name + " t:MonoScript"))
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() == type) return script;
            }
            return null;
        }

        // ───────── UdonSharp Program Asset ─────────

        public enum UdonState { Ready, Preparing, Error }

        /// <summary>
        /// 确认每个 U# 脚本都有 Program Asset 并且已经编译到当前版本。
        /// createMissing = true 时会补建缺的 Program Asset（放在 .cs 旁边，同名 .asset，和 U# 自己新建脚本时一样）。
        /// </summary>
        public static UdonState CheckUdon(IList<Type> types, bool createMissing, out string message)
        {
            message = null;
            bool created = false;
            var programs = new List<UdonSharpProgramAsset>();

            foreach (Type type in types)
            {
                UdonSharpProgramAsset program = UdonSharpProgramAsset.GetProgramAssetForClass(type);
                if (program == null)
                {
                    if (!createMissing)
                    {
                        message = $"{type.Name} 还没有 UdonSharp Program Asset，点「搭建」会自动生成。";
                        return UdonState.Preparing;
                    }

                    MonoScript script = FindMonoScript(type);
                    if (script == null)
                    {
                        message = $"找不到 {type.Name}.cs（文件名要和类名一致）。";
                        return UdonState.Error;
                    }

                    string assetPath = Path.ChangeExtension(AssetDatabase.GetAssetPath(script), ".asset");
                    if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                    {
                        message = $"{assetPath} 已经存在，但不是 {type.Name} 的 UdonSharp Program Asset。请检查这个文件，或者手动选中 {type.Name}.cs 在 Inspector 里点 Create Program Asset。";
                        return UdonState.Error;
                    }

                    var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                    asset.sourceCsScript = script;
                    AssetDatabase.CreateAsset(asset, assetPath);
                    created = true;
                    Debug.Log($"[RainRoom] 已生成 UdonSharp Program Asset：{assetPath}", asset);
                    continue;
                }

                programs.Add(program);
            }

            if (created)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                message = "已为 UdonSharp 脚本生成 Program Asset。UdonSharp 会在后台升级并编译它们（几秒钟），" +
                          "等右下角的进度条结束、这个窗口的状态变成「就绪」，再点一次「搭建」。";
                return UdonState.Preparing;
            }

            if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
            {
                message = "UdonSharp 编译有错误，先看 Console 里的报错。\n" +
                          "如果报错是 DoorMuffle 里的 AudioLowPassFilter 没有开放给 Udon：按文档删掉 DoorMuffle.cs 和旁边的 DoorMuffle.asset，" +
                          "其它功能不受影响（隔门的雨声只会变小、不会变闷）。";
                return UdonState.Error;
            }

            foreach (UdonSharpProgramAsset program in programs)
            {
                if (program.ScriptVersion < UdonSharpProgramVersion.CurrentVersion ||
                    program.CompiledVersion < UdonSharpProgramVersion.CurrentVersion)
                {
                    if (createMissing && !EditorApplication.isCompiling)
                        UdonSharpProgramAsset.CompileAllCsPrograms(true);

                    message = $"UdonSharp 还在升级 / 编译 {program.name}。等进度条结束再点「搭建」。" +
                              "如果一直停在这里：菜单 VRChat SDK → Udon Sharp → Force Upgrade（菜单位置按 SDK 版本可能不同），或者重启 Unity。";
                    return UdonState.Preparing;
                }
            }

            return UdonState.Ready;
        }

        // ───────── Layer 22 / 23 命名 ─────────

        /// <summary>给 Layer 22、23 起名。已经有别的名字时先问。返回 false 表示用户取消。</summary>
        public static bool EnsureLayerNames(bool interactive)
        {
            Object tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
            if (tagManager == null)
            {
                Debug.LogError("[RainRoom] 读不到 ProjectSettings/TagManager.asset");
                return false;
            }

            var so = new SerializedObject(tagManager);
            SerializedProperty layers = so.FindProperty("layers");
            if (layers == null || !layers.isArray || layers.arraySize < 32)
            {
                Debug.LogError("[RainRoom] TagManager 里找不到 layers 数组");
                return false;
            }

            var wanted = new[]
            {
                (index: RainRoomConfig.PostProcessingLayer, name: RainRoomConfig.PostProcessingLayerName),
                (index: RainRoomConfig.PlayerZoneLayer, name: RainRoomConfig.PlayerZoneLayerName),
            };

            foreach (var w in wanted)
            {
                SerializedProperty element = layers.GetArrayElementAtIndex(w.index);
                string current = element.stringValue;
                if (current == w.name) continue;

                if (!string.IsNullOrEmpty(current))
                {
                    bool overwrite = !interactive || EditorUtility.DisplayDialog(
                        "RainRoom：Layer 名字冲突",
                        $"Layer {w.index} 现在叫「{current}」，文档要求叫「{w.name}」。\n\n" +
                        "如果别的东西（比如清吧已有的后处理）在用这一层，改名不会影响它们的功能，只是名字变了。\n是否改名？",
                        "改名", "取消搭建");
                    if (!overwrite) return false;
                }

                element.stringValue = w.name;
            }

            so.ApplyModifiedProperties(); // 带 Undo
            return true;
        }

        // ───────── 碰撞矩阵 ─────────

        /// <summary>
        /// Layer 22（PostProcessing）：这一行全部取消勾选。
        /// Layer 23（PlayerZone）：只勾 PlayerLocal。
        /// 用 Physics.IgnoreLayerCollision，和 Unity 自己的 Physics 设置面板同一个入口，会写进 ProjectSettings/DynamicsManager.asset。
        /// </summary>
        public static void ApplyCollisionMatrix()
        {
            Object dynamicsManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset").FirstOrDefault();
            if (dynamicsManager != null) Undo.RecordObject(dynamicsManager, "RainRoom 碰撞矩阵");

            for (int other = 0; other < 32; other++)
            {
                Physics.IgnoreLayerCollision(RainRoomConfig.PostProcessingLayer, other, true);
                Physics.IgnoreLayerCollision(RainRoomConfig.PlayerZoneLayer, other, other != RainRoomConfig.PlayerLocalLayer);
            }

            if (dynamicsManager != null) EditorUtility.SetDirty(dynamicsManager);
        }

        // ───────── 检查 ─────────

        /// <summary>
        /// 检查 VRC Scene Descriptor 的 Reference Camera 上有没有 Post-process Layer，以及它的 Volume Layer 有没有包含 22。
        /// 只提示、不改：清吧可能已经有自己的全局后处理相机（文档「一次性设置」）。
        /// VRC_SceneDescriptor / ReferenceCamera 用反射读，名字以 VRChat SDK 为准，读不到就跳过检查。
        /// </summary>
        public static string CheckPostProcessCamera()
        {
            Type descriptorType = FindTypeByName("VRC_SceneDescriptor", typeof(Component));
            if (descriptorType == null) return "没找到 VRC_SceneDescriptor 类型（VRChat SDK 没装？），跳过后处理相机检查。";

            Object descriptor = Object.FindObjectsOfType(descriptorType).FirstOrDefault();
            if (descriptor == null) return "场景里没有 VRC Scene Descriptor，跳过后处理相机检查。";

            FieldInfo camField = descriptorType.GetField("ReferenceCamera", BindingFlags.Public | BindingFlags.Instance);
            GameObject camObject = camField?.GetValue(descriptor) as GameObject;
            if (camObject == null)
                return "VRC Scene Descriptor 的 Reference Camera 是空的：滤镜在 VRChat 里不会生效。按 README「手动步骤 1」设置。";

            PostProcessLayer ppLayer = camObject.GetComponent<PostProcessLayer>();
            if (ppLayer == null)
                return $"Reference Camera「{camObject.name}」上没有 Post-process Layer：滤镜不会生效。按 README「手动步骤 1」设置。";

            if ((ppLayer.volumeLayer.value & (1 << RainRoomConfig.PostProcessingLayer)) == 0)
                return $"Reference Camera 的 Post-process Layer → Volume blending → Layer 没有勾 {RainRoomConfig.PostProcessingLayerName}（Layer 22）：房间的 Volume 不会生效。";

            return null;
        }
    }
}
