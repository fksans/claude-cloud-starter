// DoorMuffle.cs（可选）
// 门关着、人在屋外时，让雨声和雷声变"闷"（高频被墙和门挡掉）。
// 用法：在雨声、雷声的 AudioSource 上各加一个 Audio Low Pass Filter，把它们拖进 filters，
//       再把挂这个脚本的物体拖到 RoomWeather 的 doorMuffle 栏。
// 如果编译时提示 AudioLowPassFilter 没有开放给 Udon：直接删掉这个脚本，
//       其它功能不受影响，只是隔门的雨声只会变小、不会变闷。

using UdonSharp;
using UnityEngine;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DoorMuffle : UdonSharpBehaviour
{
    public AudioLowPassFilter[] filters;

    [Tooltip("门关着时的截止频率（越低越闷）")]
    public float closedCutoff = 900f;

    [Tooltip("门全开或人在屋里时的截止频率（22000 = 不过滤）")]
    public float openCutoff = 22000f;

    // 由 RoomWeather 写入：0 = 门关着且人在外面，1 = 门全开或人在屋里
    [HideInInspector] public float open01 = 1f;

    public void _Apply()
    {
        if (filters == null) return;

        // 按对数插值，听起来才是均匀变化的
        float cutoff = closedCutoff * Mathf.Pow(openCutoff / closedCutoff, Mathf.Clamp01(open01));

        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i] != null) filters[i].cutoffFrequency = cutoff;
        }
    }
}
