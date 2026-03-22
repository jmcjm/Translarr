using Microsoft.AspNetCore.SignalR;
using Translarr.Core.Api.Hubs;

namespace Translarr.Core.Api.Helpers;

public static class HubBroadcastHelper
{
    /// <summary>
    /// Executes an update under lock, takes a snapshot, and broadcasts it via SignalR.
    /// The update function should modify the status and return a snapshot, or null to skip broadcasting.
    /// </summary>
    public static void LockSnapshotBroadcast<TStatus>(
        Lock statusLock,
        Func<TStatus?> updateAndSnapshot,
        string eventName,
        IHubContext<ProgressHub> hubContext)
    {
        TStatus? snapshot;
        lock (statusLock)
        {
            snapshot = updateAndSnapshot();
        }
        if (snapshot is not null)
            _ = hubContext.Clients.All.SendAsync(eventName, snapshot);
    }
}
