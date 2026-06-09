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

    private readonly List<UdpClient> udpClients = new List<UdpClient>();
    private readonly List<Thread> receiveThreads = new List<Thread>();
    private volatile bool isRunning = false;
    private readonly object lockObject = new object();

    private int pendingClass = -1;

    void Start()
    {
        StartReceiver();
    }

    void OnDestroy()
    {
        StopReceiver();
    }

    public bool TryGetLatestClass(out int classId)
    {
        lock (lockObject)
        {
            if (pendingClass >= 0)
            {
                classId = pendingClass;
                pendingClass = -1;
                return true;
            }
        }

        classId = -1;
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

                Debug.Log("UDP decoder receiver listening on port " + port);
            }
            catch (Exception e)
            {
                Debug.LogError("Could not start UDP receiver on port " + port + ": " + e.Message);
            }
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

                Debug.Log("Received UDP on port " + port + ": " + message);
                HandleMessage(message);
            }
            catch (SocketException)
            {
                // Socket closes when exiting Play Mode.
            }
            catch (ObjectDisposedException)
            {
                // Socket closes when exiting Play Mode.
            }
            catch (Exception e)
            {
                Debug.LogWarning("UDP receive error: " + e.Message);
            }
        }
    }

    void HandleMessage(string message)
    {
        string[] parts = message.Split(':');

        if (parts.Length < 3 || parts[0] != "class")
        {
            return;
        }

        if (!int.TryParse(parts[2], out int classId))
        {
            return;
        }

        lock (lockObject)
        {
            pendingClass = classId;
        }
    }
}
