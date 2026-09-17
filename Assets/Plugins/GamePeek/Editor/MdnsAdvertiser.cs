using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamePeek
{
    public sealed class MdnsAdvertiser : IDisposable
    {
        private readonly int _port;
        private readonly string _machineName;
        private readonly string _localIp;

        private UdpClient _client;
        private CancellationTokenSource _cts;

        private static readonly IPAddress MulticastAddress =
            IPAddress.Parse("224.0.0.251");

        private const int MdnsPort = 5353;

        private const ushort DNS_TYPE_A = 1;
        private const ushort DNS_TYPE_PTR = 12;
        private const ushort DNS_TYPE_TXT = 16;
        private const ushort DNS_TYPE_SRV = 33;

        private const ushort DNS_CLASS_IN = 1;
        private const ushort DNS_CLASS_FLUSH = 0x8001;

        // Dev-friendly TTLs (change to 4500 later if you want)
        private const uint PTR_TTL = 120;
        private const uint TXT_TTL = 120;
        private const uint SRV_TTL = 120;
        private const uint A_TTL = 120;

        public MdnsAdvertiser(int port, string localIp, string displayName = null)
        {
            _port = port;
            _localIp = localIp;
            _machineName = (string.IsNullOrWhiteSpace(displayName)
                    ? Environment.MachineName
                    : displayName)
                .ToLowerInvariant()
                .Replace(" ", "-");
        }

        // ─────────────────────────────────────────────
        // START
        // ─────────────────────────────────────────────
        public void Start()
        {
            _cts = new CancellationTokenSource();

            try
            {
                _client = new UdpClient(AddressFamily.InterNetwork);

                _client.Client.ExclusiveAddressUse = false;
                _client.Client.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true);

                _client.Client.Bind(
                    new IPEndPoint(IPAddress.Any, MdnsPort));

                // CRITICAL: join on correct interface
                _client.JoinMulticastGroup(
                    MulticastAddress,
                    IPAddress.Parse(_localIp));

                // CRITICAL: set the SEND interface too.
                // JoinMulticastGroup only controls which interface *receives* multicast.
                // Without this, on Windows with multiple NICs (WiFi + Ethernet + VPN),
                // outbound mDNS packets go out the default-route interface, not _localIp,
                // so devices on the LAN never see the announcements.
                _client.Client.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.MulticastInterface,
                    IPAddress.Parse(_localIp).GetAddressBytes());

                _client.MulticastLoopback = true;
                _client.Ttl = 255;
            }
            catch (Exception e)
            {
                GamePeekConstants.LogError("[mDNS] Socket error: " + e.Message);
                return;
            }

            _ = ListenLoop(_cts.Token);
            _ = AnnounceLoop(_cts.Token);

            GamePeekConstants.Log($"[mDNS] Advertising {_machineName}._unipeek._tcp.local on {_localIp}:{_port}");
        }

        // ─────────────────────────────────────────────
        // STOP (synchronous — guaranteed before return)
        // ─────────────────────────────────────────────
        public void Stop()
        {
            // Cancel background loops first so AnnounceLoop can't race with our goodbye.
            try { _cts?.Cancel(); } catch { }

            var client = _client;
            _client = null;

            if (client != null)
            {
                // Send goodbye (TTL=0) synchronously — three back-to-back packets,
                // no delays, so the socket is guaranteed closed before we return.
                try
                {
                    byte[] goodbye = BuildPacket(ttlOverride: 0);
                    var endpoint   = new IPEndPoint(MulticastAddress, MdnsPort);
                    for (int i = 0; i < 3; i++)
                        client.Send(goodbye, goodbye.Length, endpoint);
                }
                catch { }

                try { client.Close(); client.Dispose(); } catch { }
            }

            GamePeekConstants.Log("[mDNS] Stopped");
        }

        public void Dispose() => Stop();

        // ─────────────────────────────────────────────
        // ANNOUNCE LOOP
        // ─────────────────────────────────────────────
        private async Task AnnounceLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    byte[] packet = BuildPacket();

                    await _client.SendAsync(
                        packet,
                        packet.Length,
                        new IPEndPoint(MulticastAddress, MdnsPort));
                }
                catch (Exception e)
                {
                    GamePeekConstants.LogWarning("[mDNS] Announce error: " + e.Message);
                }

                try { await Task.Delay(5000, ct); }
                catch { break; }
            }
        }

        // ─────────────────────────────────────────────
        // LISTEN LOOP
        // ─────────────────────────────────────────────
        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;

                try { result = await _client.ReceiveAsync(); }
                catch { break; }

                try { HandleQuery(result.Buffer, result.RemoteEndPoint); }
                catch (Exception e) { GamePeekConstants.LogWarning("[mDNS] HandleQuery error: " + e.Message); }
            }
        }

        private void HandleQuery(byte[] data, IPEndPoint remote)
        {
            if (data.Length < 12) return;

            ushort flags = ReadUInt16(data, 2);
            bool isQuery = (flags & 0x8000) == 0;
            if (!isQuery) return;

            ushort qdCount = ReadUInt16(data, 4);
            int offset = 12;

            for (int i = 0; i < qdCount; i++)
            {
                string qname = ReadName(data, ref offset);
                ushort qtype = ReadUInt16(data, offset); offset += 2;
                ushort qclass = ReadUInt16(data, offset); offset += 2;

                bool relevant =
                    qname.Contains("_unipeek") ||
                    qname.Contains("_services._dns-sd");

                if (!relevant) continue;

                bool unicastRequested = (qclass & 0x8000) != 0;

                IPEndPoint target = unicastRequested
                    ? remote
                    : new IPEndPoint(MulticastAddress, MdnsPort);

                byte[] packet = BuildPacket();

                _client.Send(packet, packet.Length, target);
            }
        }

        // ─────────────────────────────────────────────
        // PACKET BUILDER
        // ─────────────────────────────────────────────
        private byte[] BuildPacket(uint ttlOverride = uint.MaxValue)
        {
            uint ttl(uint normal) =>
                ttlOverride == uint.MaxValue ? normal : ttlOverride;

            string instance = $"{_machineName}._unipeek._tcp.local.";
            string serviceType = "_unipeek._tcp.local.";
            string meta = "_services._dns-sd._udp.local.";
            string host = $"{_machineName}.local.";

            var buf = new List<byte>();

            // Header
            WriteUInt16(buf, 0);
            WriteUInt16(buf, 0x8400);

            WriteUInt16(buf, 0); // QDCOUNT
            WriteUInt16(buf, 2); // ANCOUNT
            WriteUInt16(buf, 0); // NSCOUNT
            WriteUInt16(buf, 3); // ARCOUNT

            // ─── ANSWERS ───

            var metaRdata = new List<byte>();
            WriteName(metaRdata, serviceType);
            WriteResource(buf, meta, DNS_TYPE_PTR, DNS_CLASS_IN, ttl(PTR_TTL), metaRdata);

            var ptrRdata = new List<byte>();
            WriteName(ptrRdata, instance);
            WriteResource(buf, serviceType, DNS_TYPE_PTR, DNS_CLASS_IN, ttl(PTR_TTL), ptrRdata);

            // ─── ADDITIONAL ───

            var txt = new List<byte>();
            WriteTxt(txt, $"version={GamePeekConstants.Version}");
            // Wire protocol version (PROTOCOL.md §2) — lets the app gate on v2
            // before dialing when it discovers the editor via browse instead of
            // the QR code.
            WriteTxt(txt, $"v={GamePeekConstants.ProtoVersion}");
            WriteResource(buf, instance, DNS_TYPE_TXT, DNS_CLASS_FLUSH, ttl(TXT_TTL), txt);

            var srv = new List<byte>();
            WriteUInt16(srv, 0);
            WriteUInt16(srv, 0);
            WriteUInt16(srv, (ushort)_port);
            WriteName(srv, host);
            WriteResource(buf, instance, DNS_TYPE_SRV, DNS_CLASS_FLUSH, ttl(SRV_TTL), srv);

            var a = new List<byte>(IPAddress.Parse(_localIp).GetAddressBytes());
            WriteResource(buf, host, DNS_TYPE_A, DNS_CLASS_FLUSH, ttl(A_TTL), a);

            return buf.ToArray();
        }

        // ─────────────────────────────────────────────
        // DNS HELPERS
        // ─────────────────────────────────────────────
        private static void WriteResource(
            List<byte> buf,
            string name,
            ushort type,
            ushort cls,
            uint ttl,
            List<byte> rdata)
        {
            WriteName(buf, name);
            WriteUInt16(buf, type);
            WriteUInt16(buf, cls);
            WriteUInt32(buf, ttl);
            WriteUInt16(buf, (ushort)rdata.Count);
            buf.AddRange(rdata);
        }

        private static void WriteName(List<byte> buf, string name)
        {
            foreach (var label in name.TrimEnd('.').Split('.'))
            {
                var bytes = Encoding.ASCII.GetBytes(label);
                buf.Add((byte)bytes.Length);
                buf.AddRange(bytes);
            }
            buf.Add(0);
        }

        private static void WriteTxt(List<byte> buf, string txt)
        {
            var bytes = Encoding.UTF8.GetBytes(txt);
            buf.Add((byte)bytes.Length);
            buf.AddRange(bytes);
        }

        private static void WriteUInt16(List<byte> buf, ushort value)
        {
            buf.Add((byte)(value >> 8));
            buf.Add((byte)value);
        }

        private static void WriteUInt32(List<byte> buf, uint value)
        {
            buf.Add((byte)(value >> 24));
            buf.Add((byte)(value >> 16));
            buf.Add((byte)(value >> 8));
            buf.Add((byte)value);
        }

        private static ushort ReadUInt16(byte[] data, int offset)
            => (ushort)((data[offset] << 8) | data[offset + 1]);

        private static string ReadName(byte[] data, ref int offset)
        {
            var labels = new List<string>();

            while (offset < data.Length && data[offset] != 0)
            {
                // DNS pointer compression: high two bits are 0xC0
                if ((data[offset] & 0xC0) == 0xC0)
                {
                    // Skip the 2-byte pointer; don't follow it (we only need the name for filtering)
                    offset += 2;
                    return string.Join(".", labels);
                }

                int len = data[offset++];
                if (offset + len > data.Length) break;
                labels.Add(Encoding.ASCII.GetString(data, offset, len));
                offset += len;
            }

            if (offset < data.Length) offset++; // consume terminating zero
            return string.Join(".", labels);
        }
    }
}