using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class UdpMarkerSender : MonoBehaviour
{
    public string decoderHost = "127.0.0.1";
    public int decoderPort = 9099;
    public int lslBridgePort = 9098;

    private UdpClient udpClient;

    void Start()
    {
        udpClient = new UdpClient();
    }

    void OnDestroy()
    {
        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }
    }

    public void SendMarker(string marker, int trialId)
    {
        if (udpClient == null)
            return;

        double timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        string payload = marker + ":" + trialId + ":" + timestamp.ToString("F6");

        byte[] data = Encoding.UTF8.GetBytes(payload);
        udpClient.Send(data, data.Length, decoderHost, decoderPort);

        Debug.Log("Sent UDP marker: " + payload);
    }

    public void SendRawMarker(string marker)
    {
        SendRawMarkerToPort(marker, decoderPort);
    }

    public void SendRawMarkerToLslBridge(string marker)
    {
        SendRawMarkerToPort(marker, lslBridgePort);
    }

    void SendRawMarkerToPort(string marker, int port)
    {
        if (udpClient == null)
            return;

        byte[] data = Encoding.UTF8.GetBytes(marker);
        udpClient.Send(data, data.Length, decoderHost, port);

        Debug.Log("Sent UDP raw marker to port " + port + ": " + marker);
    }
}
