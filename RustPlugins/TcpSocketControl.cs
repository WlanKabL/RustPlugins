using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using System;
using System.Threading.Tasks;
using Oxide.Core.Plugins;
using System.Linq;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("TcpSocketControl", "WlanKabL", "1.0.0")]
    class TcpSocketControl : RustPlugin
    {

        private BasePlayer _tcpOwnerPlayer;
        private TcpListener _tcpListener;
        List<TcpClient> _clientsList = new List<TcpClient>();
        private bool _isRunning;

        [ChatCommand("starttcp")]
        void StartTcpCommand(BasePlayer player)
        {
            if (_isRunning)
            {
                SendReply(player, "TCP server is already running!");
                return;
            }

            try
            {
                SendReply(player, "Starting TCP server...");
                StartTcpServer(); // Startet den TCP-Server
                _tcpOwnerPlayer = player;
                SendReply(player, "TCP server started.");
            }
            catch (Exception ex)
            {
                Puts($"Error starting TCP server: {ex.Message}");
                SendReply(player, $"Error starting TCP server: {ex.Message}");
            }
        }

        [ChatCommand("stoptcp")]
        void StopTcpCommand(BasePlayer player)
        {
            if (_isRunning)
            {
                SendReply(player, "TCP server is not running.");
                return;
            }

            try
            {
                SendReply(player, "Stopping TCP server...");

                if (_clientsList != null && _clientsList.Count > 0 && _clientsList.Any(c => c.Connected))
                {
                    Puts("Closing connected client...");

                    _clientsList.ForEach(c =>
                    {
                        c.GetStream().Close();
                        c.Close();
                        _clientsList.Remove(c);
                    });
                }
                _clientsList = null;

                StopTcpServer(); // Stoppt den TCP-Server
                SendReply(player, "TCP server stopped.");
            }
            catch (Exception ex)
            {
                Puts($"Error stopping TCP server: {ex.Message}");
                SendReply(player, $"Error stopping TCP server: {ex.Message}");
            }
        }

        private void StartTcpServer()
        {
            _tcpListener = new TcpListener(IPAddress.Any, 13376);
            _tcpListener.Start();
            _isRunning = true;

            // Start listening for client connections asynchronously
            Task.Run(() => ListenForClients());
        }

        private void StopTcpServer()
        {
            if (_tcpListener != null)
            {
                _tcpListener.Stop();
                _tcpListener = null;
                _isRunning = false;
            }
        }

        private async Task ListenForClients()
        {
            Puts("TCP server is now listening for clients...");
            while (_isRunning)
            {
                try
                {
                    // Accept an incoming client connection
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync();
                    _clientsList.Add(client);

                    Puts("Client connected.");

                    // Handle client communication
                    Task.Run(() => HandleClientCommunication(client));
                }
                catch (Exception ex)
                {
                    Puts($"Error accepting client: {ex.Message}");
                }
            }
        }

        private async Task HandleClientCommunication(TcpClient client)
        {
            NetworkStream clientStream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            while (_isRunning && client.Connected)
            {
                try
                {
                    bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        // Client has disconnected
                        Puts("Client disconnected.");
                        break;
                    }

                    // Convert received bytes to a string message
                    string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Puts($"Received: {receivedMessage}");

                    // Process the message and send a response (here we just echo it back)
                    string responseMessage = "connected";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(responseMessage);
                    await clientStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    Puts($"Sent: {responseMessage}");
                }
                catch (Exception ex)
                {
                    Puts($"Error communicating with client: {ex.Message}");
                    break;
                }
            }

            // Close client connection
            client.Close();
            _clientsList.Remove(client);
            Puts("Client connection closed.");
        }

        #region Raycast Logic

        object RaycastAll<T>(Vector3 position, Vector3 aim) where T : BaseEntity
        {
            var hits = Physics.RaycastAll(position, aim);
            GamePhysics.Sort(hits);
            var distance = 100f;
            object target = false;
            foreach (var hit in hits)
            {
                var ent = hit.GetEntity();
                if (ent is T && hit.distance < distance)
                {
                    target = ent;
                    break;
                }
            }

            return target;
        }

        object RaycastAll<T>(Ray ray) where T : BaseEntity
        {
            var hits = Physics.RaycastAll(ray);
            GamePhysics.Sort(hits);
            var distance = 100f;
            object target = false;
            foreach (var hit in hits)
            {
                var ent = hit.GetEntity();
                if (ent is T && hit.distance < distance)
                {
                    target = ent;
                    break;
                }
            }

            return target;
        }

        string GetMsg(string key, BasePlayer player = null)
        {
            return lang.GetMessage(key, this, player == null ? null : player.UserIDString);
        }

        bool TryGetEntity<T>(BasePlayer player, out BaseEntity entity) where T : BaseEntity
        {
            entity = null;

            var target = RaycastAll<BaseEntity>(player.eyes.HeadRay());

            if (target is T)
            {
                entity = target as T;
                return true;
            }

            return false;
        }

        #endregion

        [ChatCommand("send")]
        void SendEntityToTcp(BasePlayer player)
        {
            // Prüfen, ob ein TCP-Client verbunden ist
            if (_clientsList == null || !_clientsList.Any(c => c.Connected))
            {
                SendReply(player, "Fehler: Kein TCP-Client verbunden. Bitte zuerst eine Verbindung herstellen.");
                return;
            }

            // Raycast durchführen, um eine Entität zu erfassen
            var target = RaycastAll<BaseEntity>(player.eyes.HeadRay());
            if (target is bool)
            {
                SendReply(player, GetMsg("Target: None", player));
                return;
            }
            if (target is BaseEntity)
            {
                var targetEntity = target as BaseEntity;
                string targetId = targetEntity.net.ID.ToString();
                string resultJson = $"{{\"entityId\":\"{targetId}\"}}";
                SendReply(player, $"{{entityId:{resultJson}}}");

                // Senden der Nachricht an den verbundenen TCP-Client
                try
                {
                    _clientsList.ForEach(c => {
                        if (c.Connected)
                        {
                            NetworkStream stream = c.GetStream();
                            byte[] messageBytes = Encoding.UTF8.GetBytes(resultJson);
                            stream.Write(messageBytes, 0, messageBytes.Length);
                            stream.Flush();
                        }
                    });
                }
                catch (Exception ex)
                {
                    SendReply(player, $"Fehler beim Senden der Nachricht: {ex.Message}");
                }
                return;
            }
        }
    }
}
