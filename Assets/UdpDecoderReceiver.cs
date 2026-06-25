using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UdpDecoderReceiver : MonoBehaviour
{
    public int listenPort = 9101;
    public int[] extraListenPorts = { 9100 };
    public bool verboseUdpLogging = false;

    private readonly List<UdpClient> udpClients = new List<UdpClient>();
    private readonly List<Thread> receiveThreads = new List<Thread>();
    private volatile bool isRunning = false;
    private readonly object lockObject = new object();
    private readonly Queue<ReceivedPacket> pendingPackets = new Queue<ReceivedPacket>();
    private readonly Dictionary<int, int> pendingClassesByTrial = new Dictionary<int, int>();
    private readonly Dictionary<int, string> pendingErrorsByTrial = new Dictionary<int, string>();
    private bool primaryPortListening = false;
    private bool selfTestReceived = false;
    private bool fallbackReceiverReady = false;

    private struct ReceivedPacket
    {
        public string message;
        public int port;

        public ReceivedPacket(string message, int port)
        {
            this.message = message;
            this.port = port;
        }
    }

    public bool IsReady
    {
        get { return (primaryPortListening && selfTestReceived) || fallbackReceiverReady; }
    }

    void Start()
    {
        StartReceiver();
    }

    void Update()
    {
        while (true)
        {
            ReceivedPacket packet;
            lock (lockObject)
            {
                if (pendingPackets.Count == 0)
                {
                    break;
                }

                packet = pendingPackets.Dequeue();
            }

            HandleMessage(packet.message, packet.port);
        }
    }

    void OnDestroy()
    {
        StopReceiver();
    }

    public void ClearTrialResult(int trialId)
    {
        lock (lockObject)
        {
            pendingClassesByTrial.Remove(trialId);
            pendingErrorsByTrial.Remove(trialId);
        }
    }

    public void QueueMessage(string message, int sourcePort)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (lockObject)
        {
            pendingPackets.Enqueue(new ReceivedPacket(message.Trim(), sourcePort));
        }
    }

    public void RegisterFallbackReceiver(int port)
    {
        fallbackReceiverReady = true;
        Debug.Log("[CHECK] Unity decoder-result fallback is ready on port " + port + ".");
    }

    public bool TryGetClassForTrial(int trialId, out int classId)
    {
        lock (lockObject)
        {
            if (pendingClassesByTrial.TryGetValue(trialId, out classId))
            {
                pendingClassesByTrial.Remove(trialId);
                Debug.Log("[CHECK] Unity consumed decoder output for trial " + trialId + ": class " + classId);
                return true;
            }
        }

        classId = -1;
        return false;
    }

    public bool TryGetErrorForTrial(int trialId, out string error)
    {
        lock (lockObject)
        {
            if (pendingErrorsByTrial.TryGetValue(trialId, out error))
            {
                pendingErrorsByTrial.Remove(trialId);
                return true;
            }
        }

        error = "";
        return false;
    }

    void StartReceiver()
    {
        HashSet<int> ports = new HashSet<int>();

        if (listenPort > 0)
        {
            ports.Add(listenPort);
        }

        if (extraListenPorts != null)
        {
            foreach (int port in extraListenPorts)
            {
                if (port > 0)
                {
                    ports.Add(port);
                }
            }
        }

        isRunning = true;
        primaryPortListening = false;
        selfTestReceived = false;

        foreach (int port in ports)
        {
            try
            {
                UdpClient client = new UdpClient(port);
                Thread thread = new Thread(() => ReceiveLoop(client, port));
                thread.IsBackground = true;

                udpClients.Add(client);
                receiveThreads.Add(thread);
                thread.Start();

                if (port == listenPort)
                {
                    primaryPortListening = true;
                }

                Debug.Log("[CHECK] UDP decoder receiver listening on port " + port + ".");
            }
            catch (Exception e)
            {
                Debug.LogError("Could not start UDP receiver on port " + port + ": " + e.Message);
            }
        }

        if (primaryPortListening)
        {
            SendLoopbackProbe();
        }
        else
        {
            Debug.LogError("[CHECK] Primary UDP decoder receiver is not listening on port " + listenPort + ".");
        }
    }

    void SendLoopbackProbe()
    {
        try
        {
            using (UdpClient probeClient = new UdpClient(AddressFamily.InterNetwork))
            {
                byte[] data = Encoding.UTF8.GetBytes("receiver_probe");
                probeClient.Send(data, data.Length, "127.0.0.1", listenPort);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[CHECK] Could not send UDP decoder receiver self-test: " + e.Message);
        }
    }

    void StopReceiver()
    {
        isRunning = false;

        foreach (UdpClient client in udpClients)
        {
            client.Close();
        }

        foreach (Thread thread in receiveThreads)
        {
            if (thread != null && thread.IsAlive)
            {
                thread.Join(200);
            }
        }

        udpClients.Clear();
        receiveThreads.Clear();
        primaryPortListening = false;
        selfTestReceived = false;
    }

    void ReceiveLoop(UdpClient client, int port)
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                byte[] data = client.Receive(ref remoteEndPoint);
                string message = Encoding.UTF8.GetString(data).Trim();
                QueueMessage(message, port);
            }
            catch (SocketException e)
            {
                if (isRunning)
                {
                    QueueMessage("receiver_error|" + e.Message, port);
                }
            }
            catch (ObjectDisposedException)
            {
                // Socket closes when exiting Play Mode.
            }
            catch (Exception e)
            {
                QueueMessage("receiver_error|" + e.Message, port);
            }
        }
    }

    void HandleMessage(string message, int sourcePort)
    {
        if (message == "receiver_probe")
        {
            selfTestReceived = true;
            Debug.Log("[CHECK] UDP decoder receiver self-test passed on port " + sourcePort + ".");
            return;
        }

        if (message.StartsWith("receiver_error|"))
        {
            Debug.LogWarning("[CHECK] UDP decoder receiver error on port " + sourcePort + ": "
                + message.Substring("receiver_error|".Length));
            return;
        }

        string[] parts = message.Split(':');
        if (parts.Length < 2)
        {
            return;
        }

        if (parts[0] == "armed")
        {
            if (verboseUdpLogging)
            {
                Debug.Log("[CHECK] Unity received decoder armed message for trial " + parts[1]
                    + " on port " + sourcePort + ".");
            }
            return;
        }

        if (!int.TryParse(parts[1], out int trialId))
        {
            return;
        }

        if (parts[0] == "error" && parts.Length >= 3)
        {
            string reason = string.Join(":", parts, 2, parts.Length - 2);
            lock (lockObject)
            {
                pendingErrorsByTrial[trialId] = reason;
            }
            Debug.LogWarning("[CHECK] Unity received decoder error for trial " + trialId
                + ": " + reason + ".");
            return;
        }

        if (parts[0] != "class" || parts.Length < 3)
        {
            return;
        }

        if (!int.TryParse(parts[2], out int classId))
        {
            return;
        }

        bool isNewResult;
        lock (lockObject)
        {
            isNewResult = !pendingClassesByTrial.ContainsKey(trialId);
            pendingClassesByTrial[trialId] = classId;
        }

        if (isNewResult)
        {
            Debug.Log("[CHECK] Unity stored decoder output for trial " + trialId + ": class "
                + classId + " from port " + sourcePort + ".");
        }
        else if (verboseUdpLogging)
        {
            Debug.Log("[CHECK] Unity refreshed decoder output for trial " + trialId + ": class "
                + classId + " from port " + sourcePort + ".");
        }
    }
}
