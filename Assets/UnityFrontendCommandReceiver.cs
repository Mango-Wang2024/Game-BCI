using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UnityFrontendCommandReceiver : MonoBehaviour
{
    public int listenPort = 9110;
    public BciGameTrialManager trialManager;
    public UdpDecoderReceiver decoderReceiver;

    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool isRunning = false;
    private readonly object lockObject = new object();
    private readonly Queue<string> pendingCommands = new Queue<string>();

    void Start()
    {
        if (trialManager == null)
        {
            trialManager = FindObjectOfType<BciGameTrialManager>();
        }

        if (decoderReceiver == null)
        {
            decoderReceiver = FindObjectOfType<UdpDecoderReceiver>();
        }

        if (trialManager == null)
        {
            Debug.LogError("[CHECK] Unity frontend command receiver has no BciGameTrialManager assigned.");
        }

        if (decoderReceiver == null)
        {
            Debug.LogError("[CHECK] Unity frontend command receiver has no UdpDecoderReceiver assigned.");
        }

        StartReceiver();
    }

    void Update()
    {
        while (true)
        {
            string command;
            lock (lockObject)
            {
                if (pendingCommands.Count == 0)
                {
                    break;
                }

                command = pendingCommands.Dequeue();
            }

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

            Debug.Log("[CHECK] Unity frontend command receiver listening on port " + listenPort + ".");
            if (decoderReceiver != null)
            {
                decoderReceiver.RegisterFallbackReceiver(listenPort);
            }
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
                    pendingCommands.Enqueue(message);
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
        string normalized = command.Trim().ToLowerInvariant();

        if (
            normalized.StartsWith("class:")
            || normalized.StartsWith("armed:")
            || normalized.StartsWith("error:")
        )
        {
            if (decoderReceiver == null)
            {
                Debug.LogWarning("[CHECK] Decoder result arrived on fallback port but no receiver is assigned: "
                    + command);
                return;
            }

            decoderReceiver.QueueMessage(command, listenPort);
            return;
        }

        Debug.Log("[CHECK] Unity frontend UDP command packet received: " + command);

        if (trialManager == null)
        {
            Debug.LogWarning("Unity frontend command ignored because Trial Manager is not assigned.");
            return;
        }

        Debug.Log("Unity frontend command received: " + normalized);

        if (normalized == "training" || normalized == "calibration")
        {
            trialManager.PrepareForMode(BciRunMode.Calibration, BciTestDisplayMode.OfflineNTrain);
            return;
        }

        if (
            normalized == "online_zero"
            || normalized == "online_zero_train"
            || normalized == "zero"
            || normalized == "zero_train"
            || normalized == "0train"
            || normalized == "0_train"
        )
        {
            trialManager.PrepareForMode(BciRunMode.OnlineGame, BciTestDisplayMode.ZeroTrain);
            return;
        }

        if (
            normalized == "online_n_train"
            || normalized == "online_ntrain"
            || normalized == "n_train"
            || normalized == "ntrain"
            || normalized == "calibrated_online"
        )
        {
            trialManager.PrepareForMode(BciRunMode.OnlineGame, BciTestDisplayMode.OnlineNTrain);
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
