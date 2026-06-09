using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UnityFrontendCommandReceiver : MonoBehaviour
{
    public int listenPort = 9110;
    public BciGameTrialManager trialManager;

    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool isRunning = false;
    private readonly object lockObject = new object();
    private string pendingCommand = "";

    void Start()
    {
        StartReceiver();
    }

    void Update()
    {
        string command = "";

        lock (lockObject)
        {
            if (!string.IsNullOrEmpty(pendingCommand))
            {
                command = pendingCommand;
                pendingCommand = "";
            }
        }

        if (!string.IsNullOrEmpty(command))
        {
            ApplyCommand(command);
        }
    }

    void OnDestroy()
    {
        StopReceiver();
    }

    void StartReceiver()
    {
        try
        {
            udpClient = new UdpClient(listenPort);
            isRunning = true;

            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            Debug.Log("Unity frontend command receiver listening on port " + listenPort);
        }
        catch (Exception e)
        {
            Debug.LogError("Could not start Unity frontend command receiver: " + e.Message);
        }
    }

    void StopReceiver()
    {
        isRunning = false;

        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(200);
            receiveThread = null;
        }
    }

    void ReceiveLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                string message = Encoding.UTF8.GetString(data).Trim();

                lock (lockObject)
                {
                    pendingCommand = message;
                }
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
                Debug.LogWarning("Unity frontend command receive error: " + e.Message);
            }
        }
    }

    void ApplyCommand(string command)
    {
        if (trialManager == null)
        {
            Debug.LogWarning("Unity frontend command ignored because Trial Manager is not assigned.");
            return;
        }

        string normalized = command.Trim().ToLowerInvariant();
        Debug.Log("Unity frontend command received: " + normalized);

        if (normalized == "training" || normalized == "calibration")
        {
            trialManager.PrepareForMode(BciRunMode.Calibration);
            return;
        }

        if (normalized == "online" || normalized == "onlinegame")
        {
            trialManager.PrepareForMode(BciRunMode.OnlineGame);
            return;
        }

        Debug.LogWarning("Unknown Unity frontend command: " + command);
    }
}
