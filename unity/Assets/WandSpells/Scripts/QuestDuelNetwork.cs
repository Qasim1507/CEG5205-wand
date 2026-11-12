using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>Small LAN host/join link for two duel clients on the same local network.</summary>
public sealed class QuestDuelNetwork : MonoBehaviour {
    const int Port = 5060;

    [Serializable]
    public sealed class Snapshot {
        public Vector3 rigPosition;
        public Quaternion rigRotation;
        public Vector3 headPosition;
        public Quaternion headRotation;
        public float playerOneHealth;
        public float playerTwoHealth;
    }

    [Serializable]
    public sealed class SpellEvent {
        public int player;
        public int spell;
        public Vector3 origin;
        public Vector3 target;
        public bool landed;
    }

    [Serializable]
    sealed class Packet {
        public string type;
        public Snapshot state;
        public SpellEvent shot;
    }

    sealed class Received {
        public string json;
        public IPEndPoint endpoint;
    }

    readonly ConcurrentQueue<Received> incoming = new ConcurrentQueue<Received>();
    UdpClient socket;
    Thread reader;
    volatile bool listening;
    bool host;
    bool connected;
    float nextDiscoveryAt;
    IPEndPoint peer;

    public int LocalPlayerId { get; private set; }
    public bool IsHost => host;
    public bool IsConnected => connected;
    public bool IsActive => LocalPlayerId != 0;
    public event Action<int> SessionReady;
    public event Action<Snapshot> StateReceived;
    public event Action<SpellEvent> SpellReceived;
    public event Action<SpellEvent> SpellRequested;

    public void StartHosting() {
        if (IsActive) return;
        try {
            socket = new UdpClient(Port);
            socket.EnableBroadcast = true;
            host = true;
            LocalPlayerId = 1;
            StartReader();
            SessionReady?.Invoke(LocalPlayerId);
        } catch (Exception exception) {
            Debug.LogError("Could not host Quest duel on UDP " + Port + ": " + exception.Message);
        }
    }

    public void JoinNearbyHost() {
        if (IsActive) return;
        try {
            socket = new UdpClient(0);
            socket.EnableBroadcast = true;
            host = false;
            LocalPlayerId = 2;
            StartReader();
            nextDiscoveryAt = 0f;
        } catch (Exception exception) {
            LocalPlayerId = 0;
            Debug.LogError("Could not search for a Quest duel host: " + exception.Message);
        }
    }

    void StartReader() {
        listening = true;
        reader = new Thread(ReadPackets) { IsBackground = true, Name = "Quest duel LAN" };
        reader.Start();
    }

    void ReadPackets() {
        while (listening && socket != null) {
            try {
                IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = socket.Receive(ref endpoint);
                incoming.Enqueue(new Received {
                    json = Encoding.UTF8.GetString(bytes), endpoint = endpoint
                });
            } catch (SocketException) {
                if (!listening) return;
            } catch (ObjectDisposedException) {
                return;
            } catch (Exception) {
                // Receive runs off the Unity thread; ignore transient socket errors here.
            }
        }
    }

    void Update() {
        if (!host && LocalPlayerId == 2 && !connected && Time.unscaledTime >= nextDiscoveryAt) {
            nextDiscoveryAt = Time.unscaledTime + 1f;
            SendTo(new Packet { type = "discover" }, new IPEndPoint(IPAddress.Broadcast, Port));
        }

        Received message;
        while (incoming.TryDequeue(out message)) Handle(message);
    }

    void Handle(Received message) {
        Packet packet;
        try { packet = JsonUtility.FromJson<Packet>(message.json); }
        catch { return; }
        if (packet == null || string.IsNullOrEmpty(packet.type)) return;

        if (host && packet.type == "discover") {
            if (peer == null) peer = message.endpoint;
            if (peer.Address.Equals(message.endpoint.Address)) {
                connected = true;
                SendTo(new Packet { type = "welcome" }, peer);
            }
            return;
        }
        if (!host && packet.type == "welcome") {
            peer = message.endpoint;
            if (!connected) {
                connected = true;
                SessionReady?.Invoke(LocalPlayerId);
            }
            return;
        }
        if (peer == null || !peer.Address.Equals(message.endpoint.Address)) return;
        if (packet.type == "state" && packet.state != null)
            StateReceived?.Invoke(packet.state);
        else if (packet.type == "spell" && packet.shot != null)
            SpellReceived?.Invoke(packet.shot);
        else if (host && packet.type == "request" && packet.shot != null)
            SpellRequested?.Invoke(packet.shot);
    }

    public void SendState(Snapshot state) {
        if (!connected || peer == null || state == null) return;
        SendTo(new Packet { type = "state", state = state }, peer);
    }

    public void SendSpell(SpellEvent spell) {
        if (!host || !connected || peer == null || spell == null) return;
        SendTo(new Packet { type = "spell", shot = spell }, peer);
    }

    public void RequestSpell(SpellEvent spell) {
        if (host || !connected || peer == null || spell == null) return;
        SendTo(new Packet { type = "request", shot = spell }, peer);
    }

    void SendTo(Packet packet, IPEndPoint destination) {
        if (socket == null || destination == null) return;
        try {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet));
            socket.Send(bytes, bytes.Length, destination);
        } catch (Exception) {
            // A dropped LAN packet is recovered by the next state update.
        }
    }

    void OnDestroy() {
        listening = false;
        if (socket != null) socket.Close();
        if (reader != null && reader.IsAlive) reader.Join(300);
        socket = null;
    }
}
