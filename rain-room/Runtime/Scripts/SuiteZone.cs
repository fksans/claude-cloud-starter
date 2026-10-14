// SuiteZone.cs
// 只在本地玩家走进这一户（合住的那一套）时，才启用你那间房的雨、声音、灯和后处理。
// 挂在一个带 Box Collider（Is Trigger）的 GameObject 上，碰撞体罩住整户：公共区域 + 两间房 + 门外那一块。
// 这个 GameObject 本身必须一直启用，不要放进 RoomContent 里。

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class SuiteZone : UdonSharpBehaviour
{
    [Tooltip("你那间房所有会动、会响、会发光的东西的父物体（默认关闭）")]
    public GameObject roomContent;

    [Tooltip("你那间房的天气脚本")]
    public RoomWeather weather;

    private void Start()
    {
        if (roomContent != null) roomContent.SetActive(false);
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;

        // 先开内容，再通知天气脚本（顺序不能反，粒子要先处于启用状态才能 Play）
        if (roomContent != null) roomContent.SetActive(true);
        if (weather != null) weather.SetSuiteActive(true);
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;

        if (weather != null) weather.SetSuiteActive(false);
        if (roomContent != null) roomContent.SetActive(false);
    }
}
