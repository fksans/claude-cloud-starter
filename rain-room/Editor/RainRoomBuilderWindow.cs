// RainRoomBuilderWindow.cs
// 菜单：Tools → RainRoom → 一键搭建雨夜单人房…
// 选中门板，再指定一个贴着房间内墙的 Box Collider，点「搭建」。

using UnityEditor;
using UnityEngine;

namespace RainRoomEditor
{
    public class RainRoomBuilderWindow : EditorWindow
    {
        [SerializeField] private string rootName = RainRoomConfig.DefaultRootName;
        [SerializeField] private Transform door;
        [SerializeField] private BoxCollider interior;
        [SerializeField] private Renderer glass;
        [SerializeField] private BoxCollider suite;
        [SerializeField] private bool slidingDoor;
        [SerializeField] private float doorOpenAngle = RainRoomConfig.DefaultDoorOpenAngle;
        [SerializeField] private float slideDistance = RainRoomConfig.DefaultSlideDistance;
        [SerializeField] private bool syncAcrossPlayers = true;
        [SerializeField] private int filterPreset;   // 0 低饱和冷色，1 纯黑白
        [SerializeField] private int windowCookie;   // 0 田字格 2×2，1 3×2
        [SerializeField] private bool includeDoorMuffle = true;
        [SerializeField] private bool includeLeakGlass = true;
        [SerializeField] private bool resetAssets;
        [SerializeField] private string lastReport;
        [SerializeField] private Vector2 scroll;

        private static readonly string[] FilterPresets = { "低饱和冷色（推荐）", "纯黑白" };
        private static readonly string[] WindowCookies = { "window_cookie_2x2（田字格）", "window_cookie_3x2" };

        [MenuItem("Tools/RainRoom/一键搭建雨夜单人房…")]
        private static void Open()
        {
            var w = GetWindow<RainRoomBuilderWindow>(false, "RainRoom 搭建", true);
            w.minSize = new Vector2(380, 520);
            if (Selection.activeTransform != null && w.door == null) w.door = Selection.activeTransform;
            w.Show();
        }

        private void OnInspectorUpdate()
        {
            Repaint(); // 刷新 UdonSharp 编译状态
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("必填", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                door = (Transform)EditorGUILayout.ObjectField(new GUIContent("门板", "会转 / 会动的那块门板。搭建时门必须是关着的。"), door, typeof(Transform), true);
                if (GUILayout.Button("用选中的", GUILayout.Width(70)) && Selection.activeTransform != null)
                    door = Selection.activeTransform;
            }
            interior = (BoxCollider)EditorGUILayout.ObjectField(
                new GUIContent("房间内墙 Box Collider", "大小正好贴着房间内墙的 Box Collider。搭建会复制它的位置和大小，不会改它。"),
                interior, typeof(BoxCollider), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("门", EditorStyles.boldLabel);
            slidingDoor = EditorGUILayout.Toggle(new GUIContent("推拉门", "推拉门勾上；转轴门不勾"), slidingDoor);
            if (slidingDoor)
                slideDistance = EditorGUILayout.FloatField(new GUIContent("全开移动（米）"), slideDistance);
            else
                doorOpenAngle = EditorGUILayout.FloatField(new GUIContent("全开角度（度）"), doorOpenAngle);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("可选", EditorStyles.boldLabel);
            glass = (Renderer)EditorGUILayout.ObjectField(
                new GUIContent("玻璃 Renderer", "窗户玻璃。留空会在 Static 下建一块占位玻璃；雨、闪电、音源都按玻璃的位置摆。"),
                glass, typeof(Renderer), true);
            suite = (BoxCollider)EditorGUILayout.ObjectField(
                new GUIContent("整户范围 Box Collider", "罩住整户（公共区 + 两间房 + 门外）。留空就按房间 + 门外 3 m 估一个，之后手动放大。"),
                suite, typeof(BoxCollider), true);
            rootName = EditorGUILayout.TextField(new GUIContent("根物体名字", "室友也用这个工具时，改个名字避免互相覆盖"), rootName);
            filterPreset = EditorGUILayout.Popup("滤镜", filterPreset, FilterPresets);
            windowCookie = EditorGUILayout.Popup(new GUIContent("窗框 cookie", "窗棂格数要和你们的窗一致"), windowCookie, WindowCookies);
            syncAcrossPlayers = EditorGUILayout.Toggle(new GUIContent("天气联网同步", "不勾：每个人自己切自己的"), syncAcrossPlayers);
            includeDoorMuffle = EditorGUILayout.Toggle(new GUIContent("隔门变闷 DoorMuffle", "三个音源各加 Audio Low Pass Filter"), includeDoorMuffle);
            includeLeakGlass = EditorGUILayout.Toggle(new GUIContent("门洞着色片 LeakGlass", "多一次 GrabPass，shader 未实测"), includeLeakGlass);
            resetAssets = EditorGUILayout.Toggle(new GUIContent("重置材质和 Profile", "勾上：生成的材质 / 后处理 Profile 按文档数值重写（会冲掉你调过的数）"), resetAssets);

            EditorGUILayout.Space();
            DrawStatus();

            using (new EditorGUI.DisabledScope(door == null || interior == null || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button(new GUIContent("搭建（可重复运行，Ctrl+Z 可撤销）"), GUILayout.Height(32)))
                    RunBuild();
            }

            if (!string.IsNullOrEmpty(lastReport))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(lastReport, MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawStatus()
        {
            var types = new System.Collections.Generic.List<System.Type> { typeof(SuiteZone), typeof(RoomInterior), typeof(RoomWeather), typeof(WeatherButton) };
            System.Type muffle = RainRoomSetup.FindDoorMuffleType();
            if (muffle != null) types.Add(muffle);

            RainRoomSetup.UdonState state = RainRoomSetup.CheckUdon(types, false, out string msg);
            if (state == RainRoomSetup.UdonState.Ready)
                EditorGUILayout.HelpBox("UdonSharp：就绪", MessageType.Info);
            else
                EditorGUILayout.HelpBox("UdonSharp：" + msg, state == RainRoomSetup.UdonState.Error ? MessageType.Error : MessageType.Warning);

            if (Shader.Find(RainRoomConfig.MochieGlassShader) == null)
                EditorGUILayout.HelpBox("没装 Mochie's Unity Shaders：玻璃材质会跳过（其它照常搭建）。", MessageType.Warning);
        }

        private void RunBuild()
        {
            var o = new RainRoomOptions
            {
                rootName = rootName,
                door = door,
                interior = interior,
                glass = glass,
                suite = suite,
                slidingDoor = slidingDoor,
                doorOpenAngle = doorOpenAngle,
                slideDistance = slideDistance,
                syncAcrossPlayers = syncAcrossPlayers,
                monoPreset = filterPreset == 1,
                windowCookie3x2 = windowCookie == 1,
                includeDoorMuffle = includeDoorMuffle,
                includeLeakGlass = includeLeakGlass,
                resetAssets = resetAssets,
            };

            bool ok = RainRoomBuilder.Build(o, out string report);
            lastReport = report;
            EditorUtility.DisplayDialog(ok ? "RainRoom：搭建完成" : "RainRoom：没有搭建", report, "好");
            GUIUtility.ExitGUI(); // 弹窗之后避免 IMGUI 布局报错
        }
    }
}
