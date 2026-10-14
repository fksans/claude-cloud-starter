// RoomWeather.cs
// 这里有人 —— 雨夜单人房
// 负责：三档天气（0 小雨 / 1 大雨 / 2 雷暴）、窗外闪电、远雷、雨声淡入淡出、雷暴压暗滤镜，
//       以及开门时的"漏出来"：门口冷光、门口的局部滤镜、隔门的雨声、可选的门洞着色片。
// 本脚本所在的 GameObject 必须一直保持启用（不要放进 RoomContent 里），它负责联网同步。

using UdonSharp;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class RoomWeather : UdonSharpBehaviour
{
    // ───────── 同步状态 ─────────
    [Tooltip("默认天气：0 小雨，1 大雨，2 雷暴")]
    [SerializeField, UdonSynced, FieldChangeCallback(nameof(Mode))]
    private int _mode = 0;

    public int Mode
    {
        get => _mode;
        set
        {
            _mode = Mathf.Clamp(value, 0, 2);
            ApplyMode(false);
        }
    }

    [Tooltip("勾上：在这间房里的人看到同一种天气。不勾：每个人自己切自己的。")]
    public bool syncAcrossPlayers = true;

    // ───────── 雨 ─────────
    [Header("雨粒子（顺序：小雨、大雨、雷暴）Play On Awake 要关")]
    public ParticleSystem[] rainParticles;

    [Header("玻璃（材质顺序：小雨、大雨、雷暴）")]
    public Renderer glassRenderer;
    public Material[] glassMaterials;

    // ───────── 雨声 ─────────
    [Header("雨声循环（Loop 开、Play On Awake 开、Volume 0）")]
    public AudioSource lightRainLoop;
    public AudioSource heavyRainLoop;
    [Tooltip("小雨层在三档下的音量：小雨、大雨、雷暴")]
    public float[] lightRainVolumes = { 0.5f, 0.15f, 0.1f };
    [Tooltip("大雨层在三档下的音量：小雨、大雨、雷暴")]
    public float[] heavyRainVolumes = { 0f, 0.6f, 0.75f };
    [Tooltip("音量每秒变化多少（0.5 = 两秒从 0 到 1）")]
    public float fadeSpeed = 0.5f;

    // ───────── 雷暴压暗 ─────────
    [Header("雷暴压暗用的 Post Process Volume（可留空）")]
    public PostProcessVolume stormVolume;
    public float stormFadeSpeed = 0.5f;

    // ───────── 闪电 ─────────
    [Header("闪电窗光（Spot Light，组件默认关掉）")]
    public Light flashLight;
    [Tooltip("闪电峰值强度，建议 2–3，别再高")]
    public float flashPeak = 2.5f;
    [Tooltip("两次闪电之间的最短 / 最长间隔（秒）")]
    public float minGap = 12f;
    public float maxGap = 35f;

    [Header("窗外闪电发光片（Particles/Standard Unlit，Additive，可留空）")]
    public Renderer flashGlow;
    public Color glowFlashColor = new Color(0.45f, 0.50f, 0.60f, 1f);

    // ───────── 雷声 ─────────
    [Header("雷声")]
    public AudioSource thunderSource;
    public AudioClip[] thunderClips;
    public float minThunderDelay = 1f;
    public float maxThunderDelay = 3.5f;

    // ───────── 门 ─────────
    [Header("门：只读它的角度或位置，不用改门本身的脚本。世界加载时门必须是关着的。")]
    [Tooltip("门板（转轴门拖会转的那个物体，推拉门拖会动的那个物体）")]
    public Transform door;
    [Tooltip("推拉门勾上；转轴门不勾")]
    public bool slidingDoor = false;
    [Tooltip("转轴门全开时转了多少度")]
    public float doorOpenAngle = 90f;
    [Tooltip("推拉门全开时移动了多少米")]
    public float slideDistance = 1f;
    [Tooltip("开门程度每秒最多变化多少（让瞬间开关的门也有一点过渡）")]
    public float doorSmoothing = 4f;

    // ───────── 门口漏出来 ─────────
    [Header("门口漏出来")]
    [Tooltip("门外那个局部 Post Process Volume")]
    public PostProcessVolume leakVolume;
    [Tooltip("门全开时门口 Volume 的 weight")]
    public float leakMaxWeight = 0.6f;
    [Tooltip("屋里朝门外照的冷色 Spot Light（组件默认关掉）")]
    public Light leakLight;
    public float leakLightPeak = 1.2f;
    [Tooltip("可选：用 DoorFilterLeak.shader 的门洞着色片")]
    public Renderer leakGlass;
    public float leakGlassMax = 0.85f;

    // ───────── 隔门的声音 ─────────
    [Header("隔门的声音")]
    [Tooltip("人在屋外、门关着时，雨声剩原来的多少")]
    public float closedDoorVolume = 0.15f;
    [Tooltip("可选：挂了 DoorMuffle.cs 的物体，用来让门关着时的声音变闷")]
    public UdonSharpBehaviour doorMuffle;

    // ───────── 内部状态（全部本地） ─────────
    private bool _suiteActive;
    private bool _playerInside;
    private bool _flashEnabled = true;
    private bool _flashing;
    private float _flashTime;
    private bool _fading;
    private bool _audioDirty;
    private float _lightVol;
    private float _heavyVol;
    private float _stormWeight;
    private float _doorOpen;
    private float _occlusion = 1f;
    private Vector3 _doorClosedPos;
    private Quaternion _doorClosedRot;
    private Material _glowMat;
    private Material _leakMat;

    private const float FLASH_LENGTH = 0.8f;

    private void Start()
    {
        if (flashLight != null)
        {
            flashLight.intensity = 0f;
            flashLight.enabled = false;
        }

        if (leakLight != null)
        {
            leakLight.intensity = 0f;
            leakLight.enabled = false;
        }

        // 用 material（实例）而不是 sharedMaterial，免得在编辑器里测试时改坏材质资源
        if (flashGlow != null)
        {
            _glowMat = flashGlow.material;
            _glowMat.SetColor("_Color", Color.black);
            flashGlow.enabled = false;
        }

        if (leakGlass != null)
        {
            _leakMat = leakGlass.material;
            _leakMat.SetFloat("_Strength", 0f);
            leakGlass.enabled = false;
        }

        // 记下门关着时的样子
        if (door != null)
        {
            _doorClosedPos = door.localPosition;
            _doorClosedRot = door.localRotation;
        }

        // 闪电循环：整个生命周期只启动这一次，之后自己续下去
        SendCustomEventDelayedSeconds(nameof(_LightningTick), 2f);
    }

    // ───────── 区域开关（由 SuiteZone / RoomInterior 调用） ─────────
    public void SetSuiteActive(bool active)
    {
        _suiteActive = active;
        if (active)
        {
            _doorOpen = ReadDoor();   // 进门时直接到位，不淡入
            ApplyMode(true);
            ApplyDoor();
        }
        else
        {
            _playerInside = false;
            _fading = false;
            EndFlash();
            if (leakLight != null) leakLight.enabled = false;
            if (leakGlass != null) leakGlass.enabled = false;
        }
    }

    public void SetPlayerInside(bool inside)
    {
        _playerInside = inside;
        if (_suiteActive) ApplyDoor();
    }

    // ───────── 按钮入口 ─────────
    public void SetLightRain() { RequestMode(0); }
    public void SetHeavyRain() { RequestMode(1); }
    public void SetStorm() { RequestMode(2); }

    // 只影响自己：关掉闪电的光，雷声照常
    public void ToggleFlash()
    {
        _flashEnabled = !_flashEnabled;
        if (!_flashEnabled) EndFlash();
    }

    private void RequestMode(int m)
    {
        if (syncAcrossPlayers && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        Mode = m;   // 走属性的 setter，本地立刻生效

        if (syncAcrossPlayers)
        {
            RequestSerialization();
        }
    }

    // ───────── 应用天气 ─────────
    private void ApplyMode(bool instant)
    {
        if (!_suiteActive) return;   // 不在这一户：只记住档位，进来时再应用

        // 雨粒子：选中的 Play，其它 Stop（默认是停止发射，空中的雨丝会自然落完）
        if (rainParticles != null)
        {
            for (int i = 0; i < rainParticles.Length; i++)
            {
                ParticleSystem ps = rainParticles[i];
                if (ps == null) continue;
                if (i == _mode) ps.Play();
                else ps.Stop();
            }
        }

        // 玻璃材质
        if (glassRenderer != null && glassMaterials != null && _mode < glassMaterials.Length)
        {
            Material m = glassMaterials[_mode];
            if (m != null) glassRenderer.sharedMaterial = m;
        }

        if (instant)
        {
            _lightVol = lightRainVolumes[_mode];
            _heavyVol = heavyRainVolumes[_mode];
            _stormWeight = _mode == 2 ? 1f : 0f;
            if (stormVolume != null) stormVolume.weight = _stormWeight;
            _fading = false;
            _audioDirty = true;
        }
        else
        {
            _fading = true;
        }

        if (_mode != 2) EndFlash();
    }

    private void Update()
    {
        if (!_suiteActive) return;

        // 门：只有门的状态变了才去改灯、滤镜和声音
        float raw = ReadDoor();
        if (raw != _doorOpen)
        {
            _doorOpen = Mathf.MoveTowards(_doorOpen, raw, doorSmoothing * Time.deltaTime);
            ApplyDoor();
        }

        if (_fading) UpdateFade();
        if (_audioDirty) ApplyVolumes();
        if (_flashing) UpdateFlash();
    }

    private void UpdateFade()
    {
        float step = fadeSpeed * Time.deltaTime;
        float tl = lightRainVolumes[_mode];
        float th = heavyRainVolumes[_mode];
        float tw = _mode == 2 ? 1f : 0f;

        _lightVol = Mathf.MoveTowards(_lightVol, tl, step);
        _heavyVol = Mathf.MoveTowards(_heavyVol, th, step);
        _stormWeight = Mathf.MoveTowards(_stormWeight, tw, stormFadeSpeed * Time.deltaTime);
        if (stormVolume != null) stormVolume.weight = _stormWeight;
        _audioDirty = true;

        if (_lightVol == tl && _heavyVol == th && _stormWeight == tw) _fading = false;
    }

    private void ApplyVolumes()
    {
        if (lightRainLoop != null) lightRainLoop.volume = _lightVol * _occlusion;
        if (heavyRainLoop != null) heavyRainLoop.volume = _heavyVol * _occlusion;
        _audioDirty = false;
    }

    // ───────── 门 ─────────
    // 返回 0（关）到 1（全开）
    private float ReadDoor()
    {
        if (door == null) return 0f;

        // 有的门"开门"就是直接把门板隐藏
        if (!door.gameObject.activeInHierarchy) return 1f;

        if (slidingDoor)
        {
            if (slideDistance <= 0f) return 0f;
            return Mathf.Clamp01(Vector3.Distance(_doorClosedPos, door.localPosition) / slideDistance);
        }

        if (doorOpenAngle <= 0f) return 0f;
        return Mathf.Clamp01(Quaternion.Angle(_doorClosedRot, door.localRotation) / doorOpenAngle);
    }

    private void ApplyDoor()
    {
        float k = _doorOpen;

        // 1. 门口的局部滤镜：门开多少，weight 就是多少（相机走进那个区域才看得到）
        if (leakVolume != null) leakVolume.weight = k * leakMaxWeight;

        // 2. 冷光漏出来：门关上时直接关掉灯组件
        if (leakLight != null)
        {
            bool on = k > 0.01f;
            leakLight.enabled = on;
            leakLight.intensity = on ? k * leakLightPeak : 0f;
        }

        // 3. 可选的门洞着色片：只在门开着、而且人在屋外时才画（它要抓屏）
        if (leakGlass != null)
        {
            bool show = !_playerInside && k > 0.01f;
            leakGlass.enabled = show;
            if (show) _leakMat.SetFloat("_Strength", k * leakGlassMax);
        }

        // 4. 声音：门缝一开声音就漏得很明显，所以开方；人在屋里就是原声
        float open = _playerInside ? 1f : Mathf.Sqrt(k);
        _occlusion = Mathf.Lerp(closedDoorVolume, 1f, open);
        _audioDirty = true;

        if (doorMuffle != null)
        {
            doorMuffle.SetProgramVariable("open01", open);
            doorMuffle.SendCustomEvent("_Apply");
        }
    }

    // ───────── 闪电 ─────────
    // 每 2 秒检查一次；只有在这一户里、且是雷暴档时才真的闪
    public void _LightningTick()
    {
        if (_suiteActive && _mode == 2)
        {
            StartFlash();
            SendCustomEventDelayedSeconds(nameof(_LightningTick), Random.Range(minGap, maxGap));
        }
        else
        {
            SendCustomEventDelayedSeconds(nameof(_LightningTick), 2f);
        }
    }

    private void StartFlash()
    {
        if (_flashEnabled && (flashLight != null || flashGlow != null))
        {
            _flashing = true;
            _flashTime = 0f;
            if (flashLight != null)
            {
                flashLight.intensity = 0f;
                flashLight.enabled = true;
            }
            if (flashGlow != null) flashGlow.enabled = true;
        }

        // 雷声晚一到三秒半
        SendCustomEventDelayedSeconds(nameof(_PlayThunder), Random.Range(minThunderDelay, maxThunderDelay));
    }

    private void UpdateFlash()
    {
        _flashTime += Time.deltaTime;
        float k = FlashShape(_flashTime);

        if (flashLight != null) flashLight.intensity = flashPeak * k;
        if (_glowMat != null)
        {
            Color c = glowFlashColor * k;
            c.a = 1f;
            _glowMat.SetColor("_Color", c);
        }

        if (_flashTime >= FLASH_LENGTH) EndFlash();
    }

    // 一次闪电的亮度曲线（0–1）：冲上去、掉下来、停一下、再亮一下、慢慢退
    private float FlashShape(float t)
    {
        if (t < 0.05f) return t / 0.05f;                                   // 0 → 1
        if (t < 0.10f) return 1f - (t - 0.05f) / 0.05f * 0.8f;             // 1 → 0.2
        if (t < 0.18f) return 0.2f;                                         // 停一下
        if (t < 0.22f) return 0.2f + (t - 0.18f) / 0.04f * 0.5f;           // 0.2 → 0.7
        if (t < FLASH_LENGTH) return 0.7f * (1f - (t - 0.22f) / (FLASH_LENGTH - 0.22f)); // 0.7 → 0
        return 0f;
    }

    private void EndFlash()
    {
        _flashing = false;
        if (flashLight != null)
        {
            flashLight.intensity = 0f;
            flashLight.enabled = false;
        }
        if (_glowMat != null) _glowMat.SetColor("_Color", Color.black);
        if (flashGlow != null) flashGlow.enabled = false;
    }

    // ───────── 雷声 ─────────
    public void _PlayThunder()
    {
        if (!_suiteActive) return;
        if (thunderSource == null || thunderClips == null || thunderClips.Length == 0) return;

        AudioClip clip = thunderClips[Random.Range(0, thunderClips.Length)];
        if (clip == null) return;

        thunderSource.pitch = Random.Range(0.9f, 1.05f);
        thunderSource.PlayOneShot(clip, Random.Range(0.6f, 1f) * _occlusion);
    }
}
