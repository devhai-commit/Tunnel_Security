using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace BackendV2.Services
{
    public class CameraRelayRegistry
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<WebSocket, bool>> _viewersByCamera = new();

        public void AddViewer(string cameraId, WebSocket socket)
        {
            var viewers = _viewersByCamera.GetOrAdd(cameraId, _ => new ConcurrentDictionary<WebSocket, bool>());
            viewers[socket] = true;
        }

        public void RemoveViewer(string cameraId, WebSocket socket)
        {
            if (_viewersByCamera.TryGetValue(cameraId, out var viewers))
            {
                viewers.TryRemove(socket, out _);
            }
        }

        public async Task BroadcastFrameAsync(string cameraId, byte[] frame, CancellationToken cancellationToken)
        {
            if (!_viewersByCamera.TryGetValue(cameraId, out var viewers))
            {
                return;
            }

            foreach (var socket in viewers.Keys)
            {
                if (socket.State != WebSocketState.Open)
                {
                    viewers.TryRemove(socket, out _);
                    continue;
                }

                try
                {
                    await socket.SendAsync(frame, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
                }
                catch (Exception)
                {
                    viewers.TryRemove(socket, out _);
                }
            }
        }
    }
}
