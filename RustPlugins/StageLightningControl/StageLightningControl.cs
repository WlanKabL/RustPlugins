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
using Newtonsoft.Json.Linq;

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
            _tcpListener?.Stop();

            _clientList.ForEach(c =>
            {
                if (c.Connected)
                {
                    c.GetStream().Close();
                    c.Close();
                }
            });
            _clientList.Clear();

            
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
            StringBuilder messageBuilder = new StringBuilder();  // Zum Speichern von unvollständigen Nachrichten

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

                    // Füge die empfangenen Daten zum StringBuilder hinzu
                    string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    messageBuilder.Append(receivedData);

                    // Verarbeite nur komplette Nachrichten, getrennt durch Semikolon
                    string fullData = messageBuilder.ToString();
                    string[] messages = fullData.Split("^"); // Trennzeichen: Semikolon

                    // Die letzte Nachricht könnte unvollständig sein, also behalten wir sie für das nächste Lesen
                    messageBuilder.Clear();
                    if (!fullData.EndsWith("^"))
                    {
                        messageBuilder.Append(messages[^1]); // Die letzte unvollständige Nachricht behalten
                        messages = messages.SkipLast(1).ToArray(); // Verarbeite alle anderen Nachrichten
                    }

                    foreach (string message in messages)
                    {
                        if (string.IsNullOrWhiteSpace(message)) continue; // Leere Nachrichten ignorieren

                        Log($"Received: {message}");

                        if (message.StartsWith("{"))
                        {
                            try
                            {
                                TcpMessage<object> request = JsonConvert.DeserializeObject<TcpMessage<object>>(message);
                                switch (request.Type)
                                {
                                    case "control-smartswitch":
                                        TcpMessage<SmartSwitchRequest> castedRequest = JsonConvert.DeserializeObject<TcpMessage<SmartSwitchRequest>>(message);

                                        if (castedRequest == null)
                                        {
                                            TcpMessage<SmartSwitchResponse> errorResponseMessage = new TcpMessage<SmartSwitchResponse>
                                            {
                                                Type = "error",
                                                Message = "Invalid request payload",
                                            };
                                            await SendTcpMessage(clientStream, errorResponseMessage);
                                            return;
                                        }

                                        SmartSwitchRequest? payload = castedRequest.Payload;

                                        if (payload?.EntityId == null)
                                        {
                                            TcpMessage<SmartSwitchResponse> errorResponseMessage = new TcpMessage<SmartSwitchResponse>
                                            {
                                                Type = "error",
                                                Message = "EntityId is null",
                                            };
                                            await SendTcpMessage(clientStream, errorResponseMessage);
                                            return;
                                        }

                                        bool setSwitchResult = HandleSmartSwitchChange(payload.EntityId, payload.State);
                                        string setSwitchResultText = setSwitchResult ? "State updated successfully" : "Error while setting switch state";
                                        SmartSwitchResponse response = new SmartSwitchResponse(payload.EntityId, setSwitchResult, setSwitchResultText, payload.State);
                                        TcpMessage<SmartSwitchResponse> responseMessage = new TcpMessage<SmartSwitchResponse>
                                        {
                                            Type = "control-success",
                                            Payload = response
                                        };
                                        await SendTcpMessage(clientStream, responseMessage);

                                        break;
                                    default:
                                        TcpMessage<SmartSwitchResponse> defaultReponseMessage = new TcpMessage<SmartSwitchResponse>
                                        {
                                            Type = "error",
                                            Message = "Unknown request type"
                                        };
                                        await SendTcpMessage(clientStream, defaultReponseMessage);
                                        break;
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

        private async Task SendTcpMessage<T>(NetworkStream clientStream, TcpMessage<T> message)
        {
            string messageString = JsonConvert.SerializeObject(message);
            byte[] responseBytes = Encoding.UTF8.GetBytes(messageString + '^');
            await clientStream.WriteAsync(responseBytes, 0, responseBytes.Length);
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
                    State = false
                };
                TcpMessage<SmartSwitchRequest> message = new TcpMessage<SmartSwitchRequest>
                {
                    Type = "send-smartswitch",
                    Payload = request
                };
                

                _clientList.ForEach(async (c) =>
                {
                    if (c.Connected)
                    {
                        NetworkStream stream = c.GetStream();
                        await SendTcpMessage(stream, message);
                    }
                });
            }
            else
            {
                Log("No valid SmartSwitch found.", player);
            }
        }

        private bool HandleSmartSwitchChange(string entityId, bool state)
        {
            Log($"Handling command to set SmartSwitch {entityId} to state {state}");
            SmartSwitch? selectedSwitch = BaseNetworkable.serverEntities
                .OfType<SmartSwitch>()
                .FirstOrDefault(sw => sw.net.ID.ToString() == entityId);

            if (selectedSwitch != null && !selectedSwitch.IsDestroyed)
            {
                Log($"SmartSwitch found: {selectedSwitch.net.ID}. Changing state...");
                return SetSwitchState(selectedSwitch, state);
            }
            else
            {
                Log($"SmartSwitch with ID {entityId} not found.");
                return false;
            }
        }

        private bool SetSwitchState(SmartSwitch smartSwitch, bool state)
        {
            try
            {
                Log("Toggling SmartSwitch...");
                smartSwitch.SetFlag(BaseEntity.Flags.On, state);
                smartSwitch.MarkDirty();
                Log("SmartSwitch state successfully changed.");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error communicating with SmartSwitch: {ex}");
                return false;
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

        private class TcpMessage<T>
        {
            public string Type { get; set; }
            public T? Payload { get; set; }
            public string? Message { get; set; }
        }

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
            public bool SwitchState { get; set; }

            public SmartSwitchResponse(string entityId, bool success, string message, bool switchState)
            {
                EntityId = entityId;
                Success = success;
                Message = message;
                SwitchState = switchState;
            }
        }
    }
}
