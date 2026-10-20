using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Mathematics;

namespace MphRead.Mods.Campaign;

public readonly record struct CampaignMapDetailRequest(string RoomName, string CollisionPath, int LayerMask, Vector3 NavOrigin);

/// <summary>
/// One pause opening's detached room snapshots. Only the existing Scene owner
/// submits their polygons. Closing/reopening cannot publish an older result.
/// </summary>
public sealed class CampaignMapDetailLoad : IDisposable
{
    public const int MaxRooms = 66;
    public const int MaxBatchVertices = 1048576;
    private static readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private bool _finished;
    private bool _retired;
    public Task<IReadOnlyDictionary<string, CampaignMapDetailResult>> Completion { get; }

    public CampaignMapDetailLoad(IReadOnlyList<CampaignMapDetailRequest> requests)
    {
        if (requests.Count > MaxRooms) throw new ArgumentOutOfRangeException(nameof(requests));
        var snapshot = requests.ToArray();
        Completion = Task.Run(async () =>
        {
            bool entered = false;
            try
            {
                CancellationToken token = _cancellation.Token;
                await _readGate.WaitAsync(token).ConfigureAwait(false);
                entered = true;
                var rooms = new Dictionary<string, CampaignMapDetailResult>(StringComparer.OrdinalIgnoreCase);
                int vertices = 0;
                foreach (var request in snapshot)
                {
                    token.ThrowIfCancellationRequested();
                    if (rooms.ContainsKey(request.RoomName) || vertices >= MaxBatchVertices) continue;
                    try
                    {
                        var collision = CampaignMapDetailSource.ReadCollision(request.CollisionPath, request.LayerMask, token);
                        var detail = CampaignMapDetail.Build(collision, request.RoomName, request.NavOrigin,
                            maximumOutputVertices: Math.Min(CampaignMapDetail.MaxOutputVertices, MaxBatchVertices - vertices));
                        token.ThrowIfCancellationRequested();
                        rooms.Add(request.RoomName, detail);
                        foreach (var polygon in detail.Polygons) vertices += polygon.Vertices.Count;
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
                        or OverflowException or ProgramException)
                    {
                        // This room keeps its NAV shape. No source paths or media
                        // are published through UI, networking or telemetry.
                    }
                }
                return (IReadOnlyDictionary<string, CampaignMapDetailResult>)
                    new ReadOnlyDictionary<string, CampaignMapDetailResult>(rooms);
            }
            finally
            {
                if (entered) _readGate.Release();
                lock (_gate)
                {
                    _finished = true;
                    _cancellation.Dispose();
                }
            }
        });
    }

    public bool TryGetRoom(string roomName, out CampaignMapDetailResult? detail)
    {
        detail = null;
        lock (_gate)
        {
            if (_retired) return false;
            if (Completion.IsFaulted) { _ = Completion.Exception; return false; }
            return Completion.IsCompletedSuccessfully
                && Completion.Result.TryGetValue(roomName, out detail) && detail.Ready;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _retired = true;
            if (!_finished) _cancellation.Cancel();
        }
    }
}
