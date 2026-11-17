using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace cpu_net.Services
{
    // 只包含本服务构造的固定原因，可由日志安全显示。
    public sealed class PhysicalNetworkException : IOException
    {
        public PhysicalNetworkException(string message) : base(message) { }
    }

    public static class PhysicalNetworkService
    {
        // ws2ipdef.h 中 IP_UNICAST_IF/IPV6_UNICAST_IF 均为 31，.NET 枚举未公开命名项。
        private const SocketOptionName UnicastInterface = (SocketOptionName)31;
        public static readonly HttpRequestOptionsKey<Action<string>> RouteReportKey = new("PhysicalRouteReport");
        public static readonly HttpRequestOptionsKey<Action> TcpConnectedKey = new("PhysicalTcpConnected");

        private sealed record Adapter(string Name, uint Ipv4Index, uint Ipv6Index,
            IPAddress[] Addresses, IPAddress[] DnsServers)
        {
            public uint IndexFor(AddressFamily family) => family == AddressFamily.InterNetwork ? Ipv4Index : Ipv6Index;
        }

        private sealed record Route(Adapter Adapter, uint InterfaceIndex, IPAddress Source,
            IPAddress Destination, byte PrefixLength, ulong Metric);

        public static SocketsHttpHandler CreateHandler(DecompressionMethods decompression = DecompressionMethods.None)
            => new()
            {
                UseProxy = false,
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = decompression,
                // 每个请求重新选择出口，避免连接池复用切网前的 socket。
                PooledConnectionLifetime = TimeSpan.Zero,
                ConnectCallback = ConnectAsync
            };

        private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            context.InitialRequestMessage.Options.TryGetValue(RouteReportKey, out var report);
            var adapters = ReadPhysicalAdapters();
            IPAddress[] destinations;
            if (IPAddress.TryParse(context.DnsEndPoint.Host, out var address)) destinations = new[] { address };
            else destinations = await ResolveAsync(context.DnsEndPoint.Host, adapters, report, token).ConfigureAwait(false);
            var route = SelectRoute(adapters, destinations);
            report?.Invoke($"物理出口：{route.Adapter.Name}；接口={route.InterfaceIndex}；本地={route.Source}；目标={route.Destination}");
            var socket = CreateBoundSocket(route, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(route.Destination, context.DnsEndPoint.Port), token).ConfigureAwait(false);
                if (context.InitialRequestMessage.Options.TryGetValue(TcpConnectedKey, out var connected))
                    connected();
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static Adapter[] ReadPhysicalAdapters()
        {
            var adapters = new List<Adapter>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var properties = adapter.GetIPProperties();
                uint ipv4Index = adapter.Supports(NetworkInterfaceComponent.IPv4)
                    ? (uint)(properties.GetIPv4Properties()?.Index ?? 0) : 0;
                uint ipv6Index = adapter.Supports(NetworkInterfaceComponent.IPv6)
                    ? (uint)(properties.GetIPv6Properties()?.Index ?? 0) : 0;
                var native = new InterfaceRow { Index = ipv4Index != 0 ? ipv4Index : ipv6Index };
                // Windows 的 HardwareInterface 标志排除改名后的 TUN、TAP 和虚拟以太网卡。
                if (native.Index == 0 || GetIfEntry2(ref native) != 0 || (native.Flags & 3) != 1) continue;
                var addresses = properties.UnicastAddresses.Select(item => item.Address)
                    .Where(IsUsableAddress).ToArray();
                if (addresses.Length == 0) continue;
                adapters.Add(new(adapter.Name, ipv4Index, ipv6Index, addresses,
                    properties.DnsAddresses.Where(IsUsableAddress).ToArray()));
            }
            if (adapters.Count == 0)
                throw new PhysicalNetworkException("未找到具有可用地址的活动物理网卡，请检查 Wi-Fi 或网线连接。");
            return adapters.ToArray();
        }

        private static bool IsUsableAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
            if (address.AddressFamily == AddressFamily.InterNetworkV6) return !address.IsIPv6Multicast;
            byte[] bytes = address.GetAddressBytes();
            return address.AddressFamily == AddressFamily.InterNetwork && bytes[0] < 224 &&
                bytes[0] != 0 && !(bytes[0] == 169 && bytes[1] == 254);
        }

        private static Route? FindRoute(Adapter adapter, IPAddress destination)
        {
            if (!IsUsableAddress(destination)) return null;
            uint index = adapter.IndexFor(destination.AddressFamily);
            if (index == 0) return null;
            var remote = NativeAddress.From(destination);
            if (GetBestRoute2(IntPtr.Zero, index, IntPtr.Zero, in remote, 0, out var row, out var source) != 0)
                return null;
            var localAddress = source.ToAddress();
            if (row.InterfaceIndex != index || !adapter.Addresses.Contains(localAddress)) return null;
            var ipInterface = new IpInterfaceRow { Family = remote.Family, Index = index };
            if (GetIpInterfaceEntry(ref ipInterface) != 0) return null;
            return new(adapter, index, localAddress, destination, row.PrefixLength, (ulong)row.Metric + ipInterface.Metric);
        }

        private static Route SelectRoute(IEnumerable<Adapter> adapters, IEnumerable<IPAddress> destinations)
            => adapters.SelectMany(adapter => destinations.Select(destination => FindRoute(adapter, destination)))
                .OfType<Route>().OrderByDescending(route => route.PrefixLength).ThenBy(route => route.Metric)
                .ThenBy(route => route.InterfaceIndex).FirstOrDefault()
                ?? throw new PhysicalNetworkException("物理网卡没有到目标地址的可用路由，本次未建立连接。");

        private static Socket CreateBoundSocket(Route route, SocketType type, ProtocolType protocol)
        {
            var socket = new Socket(route.Destination.AddressFamily, type, protocol);
            try
            {
                bool ipv4 = route.Destination.AddressFamily == AddressFamily.InterNetwork;
                int interfaceValue = ipv4 ? IPAddress.HostToNetworkOrder((int)route.InterfaceIndex) : (int)route.InterfaceIndex;
                socket.SetSocketOption(ipv4 ? SocketOptionLevel.IP : SocketOptionLevel.IPv6,
                    UnicastInterface, interfaceValue);
                socket.Bind(new IPEndPoint(route.Source, 0));
                if (type == SocketType.Stream) socket.NoDelay = true;
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static async Task<IPAddress[]> ResolveAsync(string host, Adapter[] adapters, Action<string>? report, CancellationToken token)
        {
            // 只向物理适配器配置的 DNS 发送查询，不使用系统缓存或 TUN 的 DNS 出口。
            var resolverRoute = adapters.SelectMany(adapter => adapter.DnsServers.Select(server => FindRoute(adapter, server)))
                .OfType<Route>().OrderBy(route => route.Metric).ThenBy(route => route.InterfaceIndex).FirstOrDefault()
                ?? throw new PhysicalNetworkException("物理网卡未配置可达的 DNS 服务器，无法解析探测域名。");
            report?.Invoke($"物理 DNS：{resolverRoute.Adapter.Name}；接口={resolverRoute.InterfaceIndex}；服务器={resolverRoute.Destination}");
            var addresses = await QueryDnsAsync(host, 1, resolverRoute, token).ConfigureAwait(false);
            if (!adapters.Any(adapter => addresses.Any(address => FindRoute(adapter, address) != null)))
                addresses = await QueryDnsAsync(host, 28, resolverRoute, token).ConfigureAwait(false);
            if (addresses.Length == 0) throw new PhysicalNetworkException("物理网卡 DNS 未返回可用的域名地址。");
            return addresses;
        }

        private static async Task<IPAddress[]> QueryDnsAsync(string host, ushort type, Route route, CancellationToken token, int aliasDepth = 0)
        {
            if (aliasDepth >= 8) throw new PhysicalNetworkException("物理网卡 DNS 别名链过长，已停止本次解析。");
            var query = PhysicalDnsMessage.Create(host, type);
            using var socket = CreateBoundSocket(route, SocketType.Dgram, ProtocolType.Udp);
            await socket.ConnectAsync(new IPEndPoint(route.Destination, 53), token).ConfigureAwait(false);
            await socket.SendAsync(query, SocketFlags.None, token).ConfigureAwait(false);
            var buffer = new byte[65535];
            int length = await socket.ReceiveAsync(buffer, SocketFlags.None, token).ConfigureAwait(false);
            byte[] response = buffer.AsSpan(0, length).ToArray();
            if (PhysicalDnsMessage.IsTruncated(response, query))
            {
                // DNS 截断响应按协议改用 TCP，仍绑定同一个物理接口和服务器。
                using var tcp = CreateBoundSocket(route, SocketType.Stream, ProtocolType.Tcp);
                await tcp.ConnectAsync(new IPEndPoint(route.Destination, 53), token).ConfigureAwait(false);
                using var stream = new NetworkStream(tcp, ownsSocket: false);
                var framed = new byte[query.Length + 2];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)query.Length);
                query.CopyTo(framed, 2);
                await stream.WriteAsync(framed, token).ConfigureAwait(false);
                var prefix = new byte[2];
                await stream.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
                response = new byte[System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(prefix)];
                await stream.ReadExactlyAsync(response, token).ConfigureAwait(false);
            }
            var addresses = PhysicalDnsMessage.ReadAddresses(response, query, host, type, out var canonicalName);
            if (addresses.Length == 0 && canonicalName != null)
                return await QueryDnsAsync(canonicalName, type, route, token, aliasDepth + 1).ConfigureAwait(false);
            return addresses;
        }

        // Windows SDK netioapi.h 的 x64 布局，仅暴露所需字段，保留完整原生结构空间。
        [StructLayout(LayoutKind.Explicit, Size = 1352)]
        private struct InterfaceRow
        {
            [FieldOffset(8)] public uint Index;
            [FieldOffset(1152)] public byte Flags;
        }

        [StructLayout(LayoutKind.Explicit, Size = 168)]
        private struct IpInterfaceRow
        {
            [FieldOffset(0)] public ushort Family;
            [FieldOffset(16)] public uint Index;
            [FieldOffset(148)] public uint Metric;
        }

        [StructLayout(LayoutKind.Explicit, Size = 104)]
        private struct RouteRow
        {
            [FieldOffset(8)] public uint InterfaceIndex;
            [FieldOffset(40)] public byte PrefixLength;
            [FieldOffset(84)] public uint Metric;
        }

        [StructLayout(LayoutKind.Explicit, Size = 28)]
        private struct NativeAddress
        {
            [FieldOffset(0)] public ushort Family;
            [FieldOffset(4)] public uint Ipv4;
            [FieldOffset(8)] public uint Ipv6A;
            [FieldOffset(12)] public uint Ipv6B;
            [FieldOffset(16)] public uint Ipv6C;
            [FieldOffset(20)] public uint Ipv6D;
            [FieldOffset(24)] public uint Scope;

            public static NativeAddress From(IPAddress address)
            {
                byte[] bytes = address.GetAddressBytes();
                if (address.AddressFamily == AddressFamily.InterNetwork)
                    return new() { Family = 2, Ipv4 = BitConverter.ToUInt32(bytes) };
                return new() { Family = 23, Ipv6A = BitConverter.ToUInt32(bytes, 0),
                    Ipv6B = BitConverter.ToUInt32(bytes, 4), Ipv6C = BitConverter.ToUInt32(bytes, 8),
                    Ipv6D = BitConverter.ToUInt32(bytes, 12), Scope = (uint)address.ScopeId };
            }

            public IPAddress ToAddress()
            {
                if (Family == 2) return new IPAddress(BitConverter.GetBytes(Ipv4));
                var bytes = new byte[16];
                BitConverter.GetBytes(Ipv6A).CopyTo(bytes, 0);
                BitConverter.GetBytes(Ipv6B).CopyTo(bytes, 4);
                BitConverter.GetBytes(Ipv6C).CopyTo(bytes, 8);
                BitConverter.GetBytes(Ipv6D).CopyTo(bytes, 12);
                return new IPAddress(bytes, Scope);
            }
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern uint GetIfEntry2(ref InterfaceRow row);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern uint GetIpInterfaceEntry(ref IpInterfaceRow row);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern uint GetBestRoute2(IntPtr luid, uint index, IntPtr source, in NativeAddress destination,
            uint options, out RouteRow route, out NativeAddress bestSource);
    }
}
