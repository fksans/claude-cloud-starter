// RoomInterior.cs
// 告诉天气脚本：你现在在屋里还是屋外（用来决定雨声闷不闷、门洞着色片画不画）。
// 挂在一个带 Box Collider（Is Trigger）的 GameObject 上，碰撞体正好贴着房间内墙。
// 这个 GameObject 一直启用，不要放进 RoomContent 里。

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RoomInterior : UdonSharpBehaviour
{
    public RoomWeather weather;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;
        if (weather != null) weather.SetPlayerInside(true);
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal) return;
        if (weather != null) weather.SetPlayerInside(false);
    }
}
