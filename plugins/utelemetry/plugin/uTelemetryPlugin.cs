using Rocket.Core.Plugins;
using SDG.Unturned;
using System;
using UnityEngine;
using System.Reflection;
using Logger = Rocket.Core.Logging.Logger;
using System.Threading.Tasks;

namespace uTelemetry;

public class uTelemetryPlugin : RocketPlugin<uTelemetryConfiguration>
{
    public static uTelemetryPlugin Instance { get; private set; }

    protected override void Load()
    {
        var _killTracker = new KillTracker();
        _killTracker.Init();
        Logger.Log("KillTracker loaded.", ConsoleColor.Yellow);

        Logger.Log($"miauw has been loaded!", ConsoleColor.Yellow);

        Task.Run(async () =>
        {
            await DiscordWebhookExample.SendMessage("hello world");
        });
    }


}