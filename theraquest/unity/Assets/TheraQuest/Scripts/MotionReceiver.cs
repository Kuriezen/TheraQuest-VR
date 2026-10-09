using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace TheraQuest
{
    /// <summary>
    /// Receives joint-angle packets from tracker.py over UDP on a background thread.
    /// The newest packet is always available through <see cref="Latest"/>.
    /// </summary>
    public class MotionReceiver : MonoBehaviour
    {
        public int port = 5005;

        public MotionPacket Latest { get; private set; }
        public bool IsReceiving => Latest != null && Time.realtimeSinceStartup - lastPacketTime < 1.0f;
        public float PacketsPerSecond { get; private set; }

        UdpClient client;
        Thread thread;
        volatile bool running;
        readonly object gate = new object();
        string pendingJson;
        float lastPacketTime = -10f;
        int countThisSecond;
        float secondStart;

        void OnEnable()
        {
            try
            {
                client = new UdpClient(port);
                running = true;
                thread = new Thread(Listen) { IsBackground = true, Name = "TheraQuest UDP" };
                thread.Start();
                Debug.Log($"[TheraQuest] Listening for tracker on UDP port {port}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[TheraQuest] Could not open UDP port {port}: {e.Message}");
            }
        }

        void Listen()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] data = client.Receive(ref any);
                    string json = Encoding.UTF8.GetString(data);
                    lock (gate) pendingJson = json;
                }
                catch (SocketException) { /* socket closed on shutdown */ }
                catch (ObjectDisposedException) { break; }
            }
        }

        void Update()
        {
            string json = null;
            lock (gate)
            {
                if (pendingJson != null) { json = pendingJson; pendingJson = null; }
            }
            if (json != null)
            {
                try
                {
                    Latest = JsonUtility.FromJson<MotionPacket>(json);
                    lastPacketTime = Time.realtimeSinceStartup;
                    countThisSecond++;
                }
                catch (Exception e) { Debug.LogWarning("[TheraQuest] Bad packet: " + e.Message); }
            }
            if (Time.realtimeSinceStartup - secondStart >= 1f)
            {
                PacketsPerSecond = countThisSecond;
                countThisSecond = 0;
                secondStart = Time.realtimeSinceStartup;
            }
        }

        void OnDisable()
        {
            running = false;
            try { client?.Close(); } catch { }
            client = null;
        }
    }
}
