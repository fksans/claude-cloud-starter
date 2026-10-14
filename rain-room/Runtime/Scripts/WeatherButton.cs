// WeatherButton.cs
// 房间里的按钮。物体上要有碰撞体；Interaction Text 填「小雨」「大雨」「雷暴」「关闪电」之类。

using UdonSharp;
using UnityEngine;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class WeatherButton : UdonSharpBehaviour
{
    public RoomWeather weather;

    [Tooltip("填其中一个：SetLightRain / SetHeavyRain / SetStorm / ToggleFlash")]
    public string eventName = "SetLightRain";

    public override void Interact()
    {
        if (weather != null) weather.SendCustomEvent(eventName);
    }
}
