using System.Net;
using System.Net.Sockets;
using System.Text;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Oxide.Core.Plugins;
using Network;

namespace Oxide.Plugins
{
    [Info("StageLightningControl", "WlanKabL", "1.1.0")]
    public class StageLightningControl : RustPlugin
    {
        private BasePlayer? _tcpOwnerPlayer;
        private TcpListener? _tcpListener;
        private List<TcpClient> _clientList = new List<TcpClient>();
        private bool _isRunning = false;
        private int _port = 13377;

        void OnServerInitialized() => Puts("TCP StageLightningControl initialized.");
        void OnPluginUnloaded(Plugin plugin) => StopTcpServer();

        [ChatCommand("starttcp")]
        void StartTcpCommand(BasePlayer player, string command, string[] args)
        {
            if (args.Length > 0 && int.TryParse(args[0], out int port) && port >= 1000 && port <= 65535)
            {
                _port = port;
                Log($"Valid port set to: {_port}", player);
            }
            else
            {
                Log("Using default port " + _port, player);
            }

            if (_tcpListener == null)
            {
                _tcpListener = new TcpListener(IPAddress.Any, _port);
            }
            if (_isRunning)
            {
                Log("TCP server is already running!", player);
                return;
            }

            try
            {
                Log("Starting TCP server...", player);
                _tcpOwnerPlayer = player;
                StartTcpServer();
                Log("TCP server started.", player);
            }
            catch (Exception ex)
            {
                Log($"Error starting TCP server: {ex}", player);
            }
        }

        [ChatCommand("stoptcp")]
        void StopTcpCommand(BasePlayer player)
        {
            if (!_isRunning)
            {
                Log("TCP server is not running.", player);
                return;
            }

            try
            {
                StopTcpServer();
                Log("TCP server stopped.", player);
            }
            catch (Exception ex)
            {
                Log($"Error stopping TCP server: {ex}", player);
            }
        }

        private void StartTcpServer()
        {
            if (_tcpListener == null)
            {
                Log("TCP Instance not defined.");
                return;
            }
            _tcpListener.Start();
            _isRunning = true;

            Task.Run(() => ListenForClients());
        }

        private void StopTcpServer()
        {
            Log("Stopping TCP server...");

            _clientList.ForEach(c =>
            {
                if (c.Connected)
                {
                    c.GetStream().Close();
                    c.Close();
                }
            });
            _clientList.Clear();

            _tcpListener?.Stop();
            _tcpListener = null;
            _isRunning = false;
        }

        private async Task ListenForClients()
        {
            Log("TCP server is now listening for clients...");
            while (_isRunning)
            {
                try
                {
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync();
                    _clientList.Add(client);
                    Log("Client connected.");
                    _ = Task.Run(() => HandleClientCommunication(client));
                }
                catch (Exception ex)
                {
                    Log($"Error accepting client: {ex}");
                }
            }
        }

        private async Task HandleClientCommunication(TcpClient client)
        {
            NetworkStream clientStream = client.GetStream();
            byte[] buffer = new byte[1024];

            while (_isRunning && client.Connected)
            {
                try
                {
                    int bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        Log("Client disconnected.");
                        break;
                    }

                    string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Log($"Received: {receivedMessage}");

                    if (receivedMessage.StartsWith("{"))
                    {
                        try
                        {
                            var request = JsonConvert.DeserializeObject<SmartSwitchRequest>(receivedMessage);
                            if (request?.EntityId != null)
                            {
                                await HandleTcpCommand(request.EntityId, request.State);
                                var response = new SmartSwitchResponse(request.EntityId, true, "State updated successfully.");
                                string responseMessage = JsonConvert.SerializeObject(response);
                                byte[] responseBytes = Encoding.UTF8.GetBytes(responseMessage);
                                await clientStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                            }
                        }
                        catch (JsonException)
                        {
                            Log("Invalid JSON format for SmartSwitchRequest. Processing as a generic message.");
                        }
                    }
                    else
                    {
                        Log("Received non-JSON message. Processing as a generic string.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error communicating with client: {ex}");
                    break;
                }
            }

            client.Close();
            _clientList.Remove(client);
            Log("Client connection closed.");
        }

        [ChatCommand("send")]
        void SendEntityToTcp(BasePlayer player)
        {
            if (!_clientList.Any(c => c.Connected))
            {
                Log("No TCP client connected. Please connect first.", player);
                return;
            }

            var target = RaycastAll<BaseEntity>(player.eyes.HeadRay());
            if (target is BaseEntity targetEntity)
            {
                string targetId = targetEntity.net.ID.ToString();
                SmartSwitchRequest request = new SmartSwitchRequest
                {
                    EntityId = targetId,
                    State = true
                };

                string jsonRequest = JsonConvert.SerializeObject(request);
                Log($"Sending request to TCP client: {jsonRequest}", player);

                _clientList.ForEach(c =>
                {
                    if (c.Connected)
                    {
                        NetworkStream stream = c.GetStream();
                        byte[] messageBytes = Encoding.UTF8.GetBytes(jsonRequest);
                        stream.Write(messageBytes, 0, messageBytes.Length);
                        stream.Flush();
                    }
                });
            }
            else
            {
                Log("No valid SmartSwitch found.", player);
            }
        }

        private async Task HandleTcpCommand(string entityId, bool state)
        {
            Log($"Handling command to set SmartSwitch {entityId} to state {state}");
            SmartSwitch? selectedSwitch = BaseNetworkable.serverEntities
                .OfType<SmartSwitch>()
                .FirstOrDefault(sw => sw.net.ID.ToString() == entityId);

            if (selectedSwitch != null && !selectedSwitch.IsDestroyed)
            {
                Log($"SmartSwitch found: {selectedSwitch.net.ID}. Changing state...");
                await SetSwitchState(selectedSwitch, state);
            }
            else
            {
                Log($"SmartSwitch with ID {entityId} not found.");
            }
        }

        private async Task SetSwitchState(SmartSwitch smartSwitch, bool state)
        {
            try
            {
                Log("Toggling SmartSwitch...");
                smartSwitch.SetFlag(BaseEntity.Flags.On, state);
                smartSwitch.MarkDirty();
                Log("SmartSwitch state successfully changed.");
            }
            catch (Exception ex)
            {
                Log($"Error communicating with SmartSwitch: {ex}");
            }
        }

        private void Log(string message, BasePlayer? basePlayer = null)
        {
            Puts(message);
            if (basePlayer != null)
            {
                SendReply(basePlayer, message);
            }
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

        string GetMsg(string key, BasePlayer? player = null)
        {
            return lang.GetMessage(key, this, player == null ? null : player.UserIDString);
        }

        bool TryGetEntity<T>(BasePlayer player, out BaseEntity? entity) where T : BaseEntity
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

        private class SmartSwitchRequest
        {
            public string? EntityId { get; set; }
            public bool State { get; set; }
        }

        private class SmartSwitchResponse
        {
            public string EntityId { get; set; }
            public bool Success { get; set; }
            public string Message { get; set; }

            public SmartSwitchResponse(string entityId, bool success, string message)
            {
                EntityId = entityId;
                Success = success;
                Message = message;
            }
        }
    }
}
