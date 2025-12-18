using System;

namespace BloxCord.Client.Services;

// Rich presence removed. This is a no-op shim to avoid breaking older code paths.
public sealed class DiscordRpcService : IDisposable
{
    public void Initialize() { }
    public void SetStatus(string details, string state, int? partySize = null, int? partyMax = null, DateTime? startTime = null) { }
    public void ClearStatus() { }
    public void Dispose() { }
}
