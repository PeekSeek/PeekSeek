using Rocket.Core.Plugins;
using SDG.Unturned;
using System;
using UnityEngine;
using System.Reflection;
using Logger = Rocket.Core.Logging.Logger;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace uTelemetry;

public class DataPacket
{
    public List<int> x;
}