using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using System;
using System.Threading.Tasks;
using Oxide.Core.Plugins;
using System.Linq;
using System.Collections.Generic;
using Network;

namespace Oxide.Plugins
{
    [Info("StageLightningControl", "WlanKabL", "1.0.0")]
    public class StageLightningControl : RustPlugin
    {

        private BasePlayer? _tcpOwnerPlayer;
        private TcpListener? _tcpListener;
        private List<TcpClient> _clientList = new List<TcpClient>();
        private bool _isRunning = false;
        private int _port = 13377;

        void OnServerInitialized()
        {
            Puts("TCP StageLightningControl initialized.");
        }

        void OnPluginUnloaded(Plugin plugin)
        {
            if (_tcpListener != null)
            {
                Log("Stopping TCP Listener before plugin unload.");
                _tcpListener.Stop();
                _tcpListener = null;
            }
        }

        [ChatCommand("starttcp")]
        void StartTcpCommand(BasePlayer player, string command, string[] args)
        {
            if (args.Length > 0 && args[0] != null)
            {
                if (int.TryParse(args[0], out int port))
                {
                    if (port >= 1000 && port <= 65535)
                    {
                        // Der Port ist innerhalb des gültigen Bereichs
                        _port = port;
                        Log($"Valid port set to: {_port}", player);
                    } else
                    {
                        Log("Please choose a port between 1000 & 65535", player);
                        return;
                    }
                }
            } else
            {
                Log($"Command parameter has to be int to set port", player);
                return;
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
                StartTcpServer(); // Startet den TCP-Server
                Log("TCP server started.", player);
            }
            catch (Exception ex)
            {
                Log($"Error starting TCP server: {ex.ToString()}", player);
            }
        }

        [ChatCommand("stoptcp")]
        void StopTcpCommand(BasePlayer player)
        {
            if (_isRunning)
            {
                Log("TCP server is not running.", player);
            }

            try
            {
                StopTcpServer(player);
            }
            catch (Exception ex)
            {
                Log($"Error stopping TCP server: {ex.ToString()}", player);
            }
        }

        private void StartTcpServer(BasePlayer? player = null)
        {
            if (_tcpListener == null)
            {
                Log("TCP Instance not defined.", player);
                return;
            }
            _tcpListener.Start();
            _isRunning = true;

            // Start listening for client connections asynchronously
            Task.Run(() => ListenForClients());
        }

        private void StopTcpServer(BasePlayer? player = null)
        {
            Log("Try stopping TCP server...", player);

            if (_clientList != null && _clientList.Count > 0 && _clientList.Any(c => c.Connected))
            {
                Log("Closing connected client...", player);

                _clientList.ForEach(c =>
                {
                    c.GetStream().Close();
                    c.Close();
                    _clientList.Remove(c);
                });
                _clientList.Clear();
            }

            if (_tcpListener != null)
            {
                _tcpListener.Stop();
                _isRunning = false;
                Log("TCP server stopped.", player);
            }
        }

        private async Task ListenForClients()
        {
            Log("TCP server is now listening for clients...");
            while (_isRunning)
            {
                try
                {
                    if (_tcpListener == null || !_isRunning) break;

                    // Accept an incoming client connection
                    TcpClient client = await _tcpListener.AcceptTcpClientAsync();
                    _clientList.Add(client);

                    Log("Client connected.");

                    // Handle client communication
                    _ = Task.Run(() => HandleClientCommunication(client));
                }
                catch (Exception ex)
                {
                    Log($"Error accepting client: {ex.ToString()}");
                }
            }
        }

        private async Task HandleTcpCommand(string message)
        {
            Log("TCP Controller send following message: " + message);
            SmartSwitch[] smartswitches = BaseNetworkable.serverEntities.OfType<SmartSwitch>().ToArray() as SmartSwitch[];
            SmartSwitch? selectedSwitch = smartswitches.FirstOrDefault(smartSwitch =>
            {
                return smartSwitch.net.ID.ToString() == message;
            });

            if (selectedSwitch != null && !selectedSwitch.IsDestroyed)
            {
                Log("Smartswitch found: " + selectedSwitch.net.ID.ToString());
                Log(selectedSwitch.OwnerID.ToString());
                _ = Task.Run(() => SetSwitchState(selectedSwitch, !selectedSwitch.IsOn()));
            }
           
        }

        private async Task SetSwitchState(SmartSwitch smartSwitch, bool state)
        {
            try
            {
                
                //smartSwitch.SendIONetworkUpdate();

                Log("Try enabling smartswitch");

                // Prüfe, ob der SmartSwitch Eingänge hat
                if (smartSwitch.inputs == null || smartSwitch.inputs.Length == 0)
                {
                    Log("SmartSwitch has no inputs.");
                    return;
                }

                // Prüfe den ersten Eingang und die Verbindung
                var inputConnection = smartSwitch.inputs[0].connectedTo.Get();
                if (inputConnection == null)
                {
                    Log("SmartSwitch is not connected to any input source.");
                    //return;
                }

                // Logge den Typ der Verbindung, um sicherzustellen, dass sie korrekt ist
                //Log($"SmartSwitch connected to: {inputConnection.GetType().Name}");

                // Wenn der Eingang korrekt verbunden ist, schalte den SmartSwitch um
                //if (state == false)
                //    smartSwitch.UpdateHasPower(0, 0);
                //else
                //    smartSwitch.UpdateHasPower(200, 0);
                smartSwitch.SetFlag(BaseEntity.Flags.On, state);
                //smartSwitch.SendNetworkUpdate_Flags();
                //smartSwitch.SetFlag(BaseEntity.Flags.InUse, false);
                //smartSwitch.SetFlag(BaseEntity.Flags.Disabled, state);
                //smartSwitch.SetFlag(BaseEntity.Flags.Busy, false); 
                //smartSwitch.SetFlag(BaseEntity.Flags.Reserved7, false); // short circuit
                //smartSwitch.SetFlag(BaseEntity.Flags.Reserved8, state);  //has power
                //smartSwitch.SetFlag(BaseEntity.Flags.On, b: state);  //has power
                //smartSwitch.SetSwitch(state);
                smartSwitch.MarkDirty();

                //smartSwitch.SendNetworkUpdateImmediate();

                Log("SmartSwitch state successfully changed.");
            }
            catch (Exception ex)
            {
                Log($"Error communicating with switch: {ex.ToString()}");
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
                    if (!clientStream.CanRead) continue;
                    bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        // Client has disconnected
                        Log("Client disconnected.");
                        break;
                    }

                    // Convert received bytes to a string message
                    string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Log($"Received: {receivedMessage}");

                    await HandleTcpCommand(receivedMessage);

                    // Process the message and send a response (here we just echo it back)
                    string responseMessage = "connected";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(responseMessage);
                    await clientStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    Log($"Sent ({responseBytes.Length} bytes): {responseMessage}");


                    if (receivedMessage == "stop")
                    {
                        Log("Receive stop command from TCP.");
                        StopTcpServer();
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error communicating with client: {ex.ToString()}");
                    break;
                }
            }

            // Close client connection
            client.Close();
            _clientList.Remove(client);
            Log("Client connection closed.");
        }

        private void Log(string message, BasePlayer? basePlayer = null)
        {
            if (basePlayer == null && _tcpOwnerPlayer != null)
            {
                basePlayer = _tcpOwnerPlayer;
            }

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
            if (_clientList == null || !_clientList.Any(c => c.Connected))
            {
                Log("Fehler: Kein TCP-Client verbunden. Bitte zuerst eine Verbindung herstellen.", player);
                return;
            }

            // Raycast durchführen, um eine Entität zu erfassen
            var target = RaycastAll<BaseEntity>(player.eyes.HeadRay());
            if (target is bool)
            {
                Log(GetMsg("Target: None", player), player);
                return;
            }

            if (target is BaseEntity)
            {
                var targetEntity = target as BaseEntity;

                if (targetEntity == null) return;

                if (targetEntity is SmartSwitch)
                {
                    Log("Selected entity is smartswitch");
                }
                string targetId = targetEntity.net.ID.ToString();
                string resultJson = $"{{\"entityId\":\"{targetId}\"}}";
                Log($"{resultJson}", player);

                // Senden der Nachricht an den verbundenen TCP-Client
                try
                {
                    _clientList.ForEach(c =>
                    {
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
                    Log($"Fehler beim Senden der Nachricht: {ex.ToString()}", player);
                }
                return;
            }
        }
    }
}
