//
// Trimmer Framework for Unity - https://sttz.ch/trimmer
// Copyright © 2017 Adrian Stutz
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

#if TRIMMER_CLIENT || UNITY_EDITOR

using UnityEngine;
using System;
using System.Net.Sockets;
using System.Net;
using System.Collections.Generic;
using System.Collections;

namespace sttz.Trimmer.Options
{

/// <summary>
/// Client API to access Trimmer configuration over the network.
/// </summary>
/// <remarks>
/// A client to connect to <see cref="TrimmerServer"/>.
/// 
/// If you use this class directly, make sure you call <see cref="Update"/>
/// regularily for the server to process commands.
/// 
/// > [!WARNING]
/// > The communication is unencrypted and doesn't use authentication.
/// > The use of this server is only intended for development.
/// </remarks>
public class TrimmerClient
{
    // ------ Configuration ------

    /// <summary>
    /// The port the server is listening on.
    /// </summary>
    public int ServerPort {
        get {
            return _serverPort;
        }
        set {
            if (IsConnected) throw new InvalidOperationException();
            _serverPort = value;
        }
    }
    int _serverPort = 21076;

    /// <summary>
    /// Hello sent to the server.
    /// </summary>
    /// <remarks>
    /// The format string can contain following placeholders:
    /// * {0}: Application.productName
    /// * {1}: Application.version
    /// * {2}: Application.unityVersion
    /// </remarks>
    public string ClientHelloFormat {
        get {
            return _clientHelloFormat;
        }
        set {
            if (IsConnected) throw new InvalidOperationException();
            _clientHelloFormat = value;
        }
    }
    string _clientHelloFormat = "TRIM {0} {1} {2}";

    /// <summary>
    /// Expected string to receive back from server for discoveries
    /// and connections.
    /// </summary>
    public string ServerHello {
        get {
            return _serverHello;
        }
        set {
            if (IsConnected) throw new InvalidOperationException();
            _serverHello = value;
        }
    }
    string _serverHello = "TRAM";

    // ------ API ------

    /// <summary>
    /// Wether the client is currently connected.
    /// </summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// The server address connected to.
    /// </summary>
    public IPAddress ServerAddress { get; private set; }

    /// <summary>
    /// Event triggered when a server has been found.
    /// </summary>
    public event Action<IPAddress, string> OnServerFound;

    /// <summary>
    /// Find discoverable servers on the local network.
    /// </summary>
    public void FindServers()
    {
        BeginListen();

        var endPoint = new IPEndPoint(IPAddress.Broadcast, ServerPort);
        var bytes = Common.Encode(clientHello);
        announcee.Send(bytes, bytes.Length, endPoint);
    }

    /// <summary>
    /// Connect to a server.
    /// </summary>
    public void Connect(IPAddress address, CommandResult onConnect)
    {
        if (IsConnected) {
            throw new InvalidOperationException();
        }

        IsConnected = true;
        ServerAddress = address;
        helloReceived = false;

        this.onConnect = onConnect;

        var localAddress = IPAddress.Any;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            localAddress = IPAddress.IPv6Any;

        client = new TcpClient(new IPEndPoint(localAddress, 0));
        client.BeginConnect(address, ServerPort, OnConnected, null);
    }

    /// <summary>
    /// Disconnect from the server.
    /// </summary>
    public void Disconnect()
    {
        if (!IsConnected) return;
        IsConnected = false;
        ServerAddress = null;

        onConnect = null;
        handlers.Clear();
        lock (((ICollection)replies).SyncRoot) {
            replies.Clear();
        }

        if (stream != null) {
            stream.Close();
            stream = null;
        }

        if (client != null) {
            client.Close();
            client = null;
        }
    }

    /// <summary>
    /// Call this method in a regular interval to process server messages.
    /// </summary>
    public void Update()
    {
        ProcessReplies();
    }

    /// <summary>
    /// Delegate receiving a command result.
    /// </summary>
    /// <param name="success">Wether the command was successful</param>
    /// <param name="message">The reply on success or the error on failure</param>
    public delegate void CommandResult(bool success, string message);

    /// <summary>
    /// Send a Ping command.
    /// </summary>
    public void Ping(CommandResult onPong)
    {
        SendCommand("PING", "", onPong);
    }

    /// <summary>
    /// Get an Option value.
    /// </summary>
    /// <param name="optionPath">Path of the Option</param>
    /// <param name="onValue">Handler to receive the value</param>
    public void GetOptionValue(string optionPath, CommandResult onResult)
    {
        SendCommand("GET", optionPath, onResult);
    }

    /// <summary>
    /// Set an Option value.
    /// </summary>
    /// <param name="optionPath">Path of the Option</param>
    /// <param name="onValue">Result callback</param>
    public void SetOptionValue(string optionPath, CommandResult onResult)
    {
        SendCommand("SET", optionPath, onResult);
    }

    /// <summary>
    /// Send a raw command to the server.
    /// </summary>
    /// <param name="name">Name of the command</param>
    /// <param name="arguments">Arguments of the command (if any)</param>
    /// <param name="onResult">Result callback</param>
    public void RawCommand(string name, string arguments, CommandResult onResult)
    {
        SendCommand(name, arguments, onResult);
    }

    // ------ Common ------

    public TrimmerClient()
    {
        // Done here becuase callbacks are called on another thread
        clientHello = string.Format(ClientHelloFormat + "\n",
            Application.productName,
            Application.version,
            Application.unityVersion
        );
    }

    // ------ Discover ------

    UdpClient announcee;

    void BeginListen()
    {
        if (announcee != null)
            return;

        announcee = new UdpClient(0);
        announcee.BeginReceive(OnAnnouncement, null);
    }

    void StopListen()
    {
        if (announcee != null) {
            announcee.Close();
            announcee = null;
        }
    }

    void OnAnnouncement(IAsyncResult ar)
    {
        if (announcee == null)
            return;
        
        try {
            var endPoint = new IPEndPoint(IPAddress.IPv6Any, 0);
            var message = Common.Decode(announcee.EndReceive(ar, ref endPoint));

            if (message.StartsWith(ServerHello) && OnServerFound != null) {
                var hello = message.Substring(ServerHello.Length).Trim();
                OnServerFound(endPoint.Address, hello);
            }

            announcee.BeginReceive(OnAnnouncement, null);
        } catch (ObjectDisposedException) {
            // Client was closed
        } catch (Exception e) {
            Debug.LogError("Announce listen error: " + e);
        }
    }

    // ------ Client ------

    TcpClient client;
    bool helloReceived;
    string clientHello;
    NetworkStream stream;
    byte[] readBuffer = new byte[2048];

    CommandResult onConnect;
    Queue<CommandResult> handlers = new Queue<CommandResult>();
    List<string> replies = new List<string>();

    void HandleConnectionError(string error)
    {
        if (onConnect != null) {
            onConnect(false, error);
        } else {
            Debug.LogError(error);
        }
        Disconnect();
    }

    void HandleError(Exception e)
    {
        Debug.LogError("Client error: " + e);
        Disconnect();
    }

    void OnConnected(IAsyncResult ar)
    {
        if (client == null)
            return;
        
        try {
            client.EndConnect(ar);
            stream = client.GetStream();

            // Send client hello
            var data = Common.Encode(clientHello);
            stream.Write(data, 0, data.Length);

            stream.BeginRead(readBuffer, 0, readBuffer.Length, OnDataReceived, null);
        } catch (ObjectDisposedException) {
            // Client was closed
        } catch (Exception e) {
            HandleConnectionError(e.Message);
        }
    }

    void OnDataReceived(IAsyncResult ar)
    {
        if (client == null || stream == null)
            return;
        
        try {
            var message = Common.Decode(readBuffer, stream.EndRead(ar));

            if (!helloReceived) {
                if (!message.StartsWith(ServerHello)) {
                    // Server sent wrong hello
                    HandleConnectionError("Got invalid hello from server: " + message);
                    return;
                }

                helloReceived = true;
                if (onConnect != null) {
                    onConnect(true, message.Substring(ServerHello.Length + 1));
                    onConnect = null;
                } else {
                    Debug.Log("Server hello: " + message);
                }

            } else {
                if (message.Trim().Length == 0) {
                    Debug.Log("Server disconnected");
                    Disconnect();
                    return;
                }

                lock (((ICollection)replies).SyncRoot) {
                    replies.Add(message);
                }
            }

            stream.BeginRead(readBuffer, 0, readBuffer.Length, OnDataReceived, null);
        } catch (ObjectDisposedException) {
            // Client was closed
        } catch (Exception e) {
            HandleError(e);
        }
    }

    void SendCommand(string command, string arguments, CommandResult handler)
    {
        if (stream == null) {
            throw new InvalidOperationException();
        }

        try {
            var data = Common.Encode(command + " " + arguments);
            stream.Write(data, 0, data.Length);
            handlers.Enqueue(handler);
        } catch (Exception e) {
            HandleError(e);
        }
    }

    void ProcessReplies()
    {
        List<string> temp = null;

        lock (((ICollection)replies).SyncRoot) {
            if (replies.Count == 0)
                return;
            temp = new List<string>(replies);
            replies.Clear();
        }

        if (temp == null)
            return;

        foreach (var reply in temp) {
            var success = true;
            var message = reply;
            if (reply.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) {
                success = false;
                message = reply.Substring(6);
            }

            if (handlers.Count == 0) {
                Debug.LogError("No handler for message: " + reply);
                break;
            } else {
                var handler = handlers.Dequeue();
                handler(success, message);
            }
        }
    }
}

}

#endif