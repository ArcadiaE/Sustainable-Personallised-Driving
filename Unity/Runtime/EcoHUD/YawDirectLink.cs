using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class YawDirectLink : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Device/emulator IP. Leave EMPTY to discover via UDP broadcast.")]
    public string deviceIp = "";
    public int udpPort = 50010;
    public int tcpPort = 50020;
    public string gameName = "SustainableDriving";

    [Header("Streaming")]
    public Transform motionSource;
    public float sendHz = 50f;
    public bool autoConnectOnPlay = false;

    [Header("Device safety limits (sent on connect)")]
    [Tooltip("Hard pitch limits enforced BY THE DEVICE (deg). Slightly above YawMotion's ±12 so the mapping clamp, not the device, shapes the motion.")]
    public int tiltLimitPitchFwdDeg = 15;
    public int tiltLimitPitchBackDeg = 15;
    [Tooltip("Hard roll limit enforced by the device (deg).")]
    public int tiltLimitRollDeg = 12;
    [Tooltip("Device-side hard limit on yaw either side of home (deg). 0 = unlimited continuous yaw — the Yaw3 will spin freely and the headset/wheel cables cannot take that. Keep this ABOVE YawMotion.maxYawDeg so the mapping, not the device, shapes normal motion; it is a backstop that should never engage.")]
    public int yawLimitDeg = 30;

    [Tooltip("Seconds after START before the VR view is recentered. The platform rises to its operating height when the motors engage, carrying the headset above the origin captured while it was parked - recentering once it settles puts the eyes back at the driver anchor. 0 = off (F9 stays available).")]
    public float recenterAfterStartDelay = 3f;   // s

    [Header("Dock ramp (comfort at Play start/stop)")]
    [Tooltip("Pitch the seat settles to when the motors release, nose-down positive. Play-end ramps the seat TO this angle BEFORE releasing (no drop under the rider), Play-start rises FROM it back to level (no jerk) - . Set it to whatever angle the seat sags to with a rider when stopped.")]
    public float parkPitchDeg = 12f;   // deg
    [Tooltip("Play-end ramp duration. Blocks the editor for this long when leaving Play. 0 = old instant release.")]
    public float parkRampS = 1.5f;     // s
    [Tooltip("Play-start rise duration from the parked pitch to level.")]
    public float riseRampS = 2.0f;     // s

    [Header("State (read-only)")]
    public string status = "idle";

    UdpClient _udp;
    TcpClient _tcp;
    NetworkStream _stream;
    IPEndPoint _motionEndpoint;
    float _sendTimer;
    float _riseUntil;
    bool _streaming;

    void Start()
    {
        if (motionSource == null)
        {
            var rig = FindFirstObjectByType<YawMotion>();
            if (rig != null) motionSource = rig.transform;
        }
        if (autoConnectOnPlay) Connect();
    }

    public async void Connect()
    {
        try
        {
            status = "discovering";
            string ip = deviceIp;
            if (string.IsNullOrEmpty(ip)) ip = await DiscoverAsync();
            if (string.IsNullOrEmpty(ip))
            {
                status = "no device found (broadcast timeout)";
                Debug.LogWarning("[YawDirectLink] " + status + " - if the chair is on and reachable, " +
                                 "inbound UDP to the editor is likely firewalled; set deviceIp to skip discovery.");
                return;
            }

            status = "connecting " + ip;
            _tcp = new TcpClient();
            try
            {
                await _tcp.ConnectAsync(ip, tcpPort);
            }
            catch (Exception e) when (!string.IsNullOrEmpty(deviceIp))
            {
                Debug.LogWarning($"[YawDirectLink] fixed IP {ip}:{tcpPort} failed ({e.Message}) - retrying via broadcast.");
                _tcp.Close();
                ip = await DiscoverAsync();
                if (string.IsNullOrEmpty(ip))
                { status = "no device found (fixed IP dead, broadcast timeout)"; Debug.LogWarning("[YawDirectLink] " + status); return; }
                status = "connecting " + ip;
                _tcp = new TcpClient();
                await _tcp.ConnectAsync(ip, tcpPort);
            }
            _stream = _tcp.GetStream();

            int listenPort = 28067;
            byte[] name = Encoding.ASCII.GetBytes(gameName);
            byte[] checkIn = new byte[1 + 4 + name.Length];
            checkIn[0] = 0x30;
            checkIn[1] = (byte)(listenPort >> 24);
            checkIn[2] = (byte)(listenPort >> 16);
            checkIn[3] = (byte)(listenPort >> 8);
            checkIn[4] = (byte)listenPort;
            Buffer.BlockCopy(name, 0, checkIn, 5, name.Length);
            await _stream.WriteAsync(checkIn, 0, checkIn.Length);

            byte[] ans = new byte[256];
            int n = await _stream.ReadAsync(ans, 0, ans.Length);
            Debug.Log($"[YawDirectLink] check-in answer: {(n > 0 ? ans[0].ToString("X2") : "none")} " +
                      Encoding.ASCII.GetString(ans, Mathf.Min(1, n), Mathf.Max(0, n - 1)));

            await _stream.WriteAsync(Cmd(0x40, tiltLimitPitchFwdDeg, tiltLimitPitchBackDeg, tiltLimitRollDeg), 0, 13);  // SET_TILT_LIMITS
            await _stream.WriteAsync(Cmd(0x70, yawLimitDeg), 0, 5);                                                     // SET_YAW_LIMIT

            await _stream.WriteAsync(new byte[] { 0xA1 }, 0, 1);   // START

            _udp = new UdpClient();
            _motionEndpoint = new IPEndPoint(IPAddress.Parse(ip), udpPort);
            _riseUntil = Time.time + riseRampS;
            _streaming = true;
            status = "streaming to " + ip;
            Debug.Log("[YawDirectLink] STARTED — streaming motion.");

            if (recenterAfterStartDelay > 0f)
            {
                await Task.Delay((int)(recenterAfterStartDelay * 1000f));
                if (Application.isPlaying && _streaming)
                    VRRecenter.RecenterNow();
            }
        }
        catch (Exception e)
        {
            status = "error: " + e.Message;
            Debug.LogWarning("[YawDirectLink] connect failed: " + e.Message);
        }
    }

    async Task<string> DiscoverAsync()
    {
        using var disc = new UdpClient { EnableBroadcast = true };
        byte[] call = Encoding.ASCII.GetBytes("YAW_CALLING");
        var recvTask = disc.ReceiveAsync();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await disc.SendAsync(call, call.Length, new IPEndPoint(IPAddress.Broadcast, udpPort));
            if (await Task.WhenAny(recvTask, Task.Delay(1500)) != recvTask) continue;
            var res = recvTask.Result;
            string msg = Encoding.ASCII.GetString(res.Buffer);
            if (!msg.StartsWith("YAWDEVICE"))
            {
                Debug.LogWarning("[YawDirectLink] unexpected reply: " + msg);
                recvTask = disc.ReceiveAsync();
                continue;
            }
            var parts = msg.Split(';');
            if (parts.Length >= 4 && int.TryParse(parts[3], out int p)) tcpPort = p;
            string ip = res.RemoteEndPoint.Address.ToString();
            Debug.Log($"[YawDirectLink] found {msg} at {ip}");
            return ip;
        }
        return null;
    }

    static byte[] Cmd(byte op, params int[] args)
    {
        byte[] b = new byte[1 + 4 * args.Length];
        b[0] = op;
        for (int i = 0; i < args.Length; i++)
        {
            b[1 + 4 * i] = (byte)(args[i] >> 24);
            b[2 + 4 * i] = (byte)(args[i] >> 16);
            b[3 + 4 * i] = (byte)(args[i] >> 8);
            b[4 + 4 * i] = (byte)args[i];
        }
        return b;
    }

    void Update()
    {
        if (!_streaming || motionSource == null || _udp == null) return;
        _sendTimer += Time.deltaTime;
        if (_sendTimer < 1f / Mathf.Max(1f, sendHz)) return;
        _sendTimer = 0f;

        Vector3 e = motionSource.rotation.eulerAngles;
        float y = Mathf.DeltaAngle(0f, e.y);
        float pDeg = Mathf.DeltaAngle(0f, e.x);
        float r = Mathf.DeltaAngle(0f, e.z);
        if (Time.time < _riseUntil)
        {
            float t = 1f - (_riseUntil - Time.time) / Mathf.Max(0.01f, riseRampS);
            t = t * t * (3f - 2f * t);
            y = Mathf.Lerp(0f, y, t);
            pDeg = Mathf.Lerp(parkPitchDeg, pDeg, t);
            r = Mathf.Lerp(0f, r, t);
        }
        SendPose(y, pDeg, r);
    }

    void SendPose(float yawDeg, float pitchDeg, float rollDeg)
    {
        if (_udp == null || _motionEndpoint == null) return;
        string msg = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "Y[{0:000.00}]P[{1:000.00}]R[{2:000.00}]",
            yawDeg < 0f ? yawDeg + 360f : yawDeg,
            pitchDeg < 0f ? pitchDeg + 360f : pitchDeg,
            rollDeg < 0f ? rollDeg + 360f : rollDeg);
        byte[] b = Encoding.ASCII.GetBytes(msg);
        try { _udp.Send(b, b.Length, _motionEndpoint); } catch {  }
    }

    void ParkRamp()
    {
        if (!_streaming || _udp == null || _motionEndpoint == null || parkRampS < 0.05f) return;
        float y0 = 0f, p0 = 0f, r0 = 0f;
        if (motionSource != null)
        {
            Vector3 e = motionSource.rotation.eulerAngles;
            y0 = Mathf.DeltaAngle(0f, e.y);
            p0 = Mathf.DeltaAngle(0f, e.x);
            r0 = Mathf.DeltaAngle(0f, e.z);
        }
        int steps = Mathf.Max(2, (int)(parkRampS * 25f));
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            t = t * t * (3f - 2f * t);
            SendPose(Mathf.Lerp(y0, 0f, t), Mathf.Lerp(p0, parkPitchDeg, t), Mathf.Lerp(r0, 0f, t));
            System.Threading.Thread.Sleep(Mathf.Max(1, (int)(parkRampS * 1000f / steps)));
        }
    }

    public void DisconnectFromDevice()
    {
        try
        {
            ParkRamp();
            _streaming = false;
            if (_stream != null)
            {
                _stream.Write(new byte[] { 0xA2 }, 0, 1);   // STOP
                _stream.Write(new byte[] { 0xA3 }, 0, 1);   // EXIT
            }
        }
        catch { }
        _stream?.Dispose(); _tcp?.Close(); _udp?.Close();
        _stream = null; _tcp = null; _udp = null;
        status = "idle";
    }

    void OnDestroy() => DisconnectFromDevice();
}
