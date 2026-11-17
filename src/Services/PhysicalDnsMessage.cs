using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace cpu_net.Services
{
    // 物理接口 DNS 查询使用的 A/AAAA、CNAME 和压缩名称编解码。
    internal static class PhysicalDnsMessage
    {
        public static byte[] Create(string host, ushort type)
        {
            string name = new IdnMapping().GetAscii(host.TrimEnd('.'));
            if (name.Length == 0 || name.Length > 253) throw InvalidMessage();
            var message = new List<byte>(new byte[12]);
            foreach (string label in name.Split('.'))
            {
                if (label.Length == 0 || label.Length > 63) throw InvalidMessage();
                message.Add((byte)label.Length);
                message.AddRange(Encoding.ASCII.GetBytes(label));
            }
            message.AddRange(new byte[] { 0, 0, (byte)type, 0, 1 });
            byte[] query = message.ToArray();
            BinaryPrimitives.WriteUInt16BigEndian(query, (ushort)RandomNumberGenerator.GetInt32(65536));
            query[2] = 1; // 标准递归查询。
            query[5] = 1;
            return query;
        }

        public static bool IsTruncated(byte[] response, byte[] query)
        {
            ValidateHeader(response, query);
            return (response[2] & 2) != 0;
        }

        public static IPAddress[] ReadAddresses(byte[] response, byte[] query, string host, ushort type, out string? canonicalName)
        {
            canonicalName = null;
            ValidateHeader(response, query);
            int status = response[3] & 15;
            if (status == 3) return Array.Empty<IPAddress>();
            if (status != 0) throw new PhysicalNetworkException($"物理网卡 DNS 返回错误，响应码={status}。");
            if ((response[2] & 2) != 0) throw InvalidMessage();
            int offset = 4;
            int questions = ReadUInt16(response, ref offset);
            int answers = ReadUInt16(response, ref offset);
            offset = 12;
            string expectedName = new IdnMapping().GetAscii(host.TrimEnd('.'));
            if (questions != 1 || !string.Equals(ReadName(response, ref offset), expectedName, StringComparison.OrdinalIgnoreCase) ||
                ReadUInt16(response, ref offset) != type || ReadUInt16(response, ref offset) != 1) throw InvalidMessage();
            var addresses = new List<(string Name, IPAddress Address)>();
            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < answers; index++)
            {
                string owner = ReadName(response, ref offset);
                int recordType = ReadUInt16(response, ref offset);
                int recordClass = ReadUInt16(response, ref offset);
                EnsureAvailable(response, offset, 4);
                offset += 4; // TTL 仅由本次解析使用，不建立 DNS 缓存。
                int size = ReadUInt16(response, ref offset);
                EnsureAvailable(response, offset, size);
                if (recordClass == 1 && recordType == type && size == (type == 1 ? 4 : 16))
                    addresses.Add((owner, new IPAddress(response.AsSpan(offset, size))));
                if (recordClass == 1 && recordType == 5)
                {
                    int nameOffset = offset;
                    aliases[owner] = ReadName(response, ref nameOffset);
                    if (nameOffset > offset + size) throw InvalidMessage();
                }
                offset += size;
            }
            var acceptedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { expectedName };
            string currentName = expectedName;
            while (aliases.TryGetValue(currentName, out var alias) && acceptedNames.Add(alias)) currentName = alias;
            if (acceptedNames.Count > 1) canonicalName = currentName;
            return addresses.Where(item => acceptedNames.Contains(item.Name)).Select(item => item.Address).Distinct().ToArray();
        }

        private static void ValidateHeader(byte[] response, byte[] query)
        {
            EnsureAvailable(response, 0, 12);
            if (response[0] != query[0] || response[1] != query[1] || (response[2] & 0xF8) != 0x80)
                throw InvalidMessage();
        }

        private static ushort ReadUInt16(byte[] data, ref int offset)
        {
            EnsureAvailable(data, offset, 2);
            ushort value = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
            offset += 2;
            return value;
        }

        private static string ReadName(byte[] data, ref int offset)
        {
            var labels = new List<string>();
            int cursor = offset;
            bool jumped = false;
            int nameLength = 0;
            for (int steps = 0; steps < 128; steps++)
            {
                EnsureAvailable(data, cursor, 1);
                int length = data[cursor++];
                if (length == 0)
                {
                    if (!jumped) offset = cursor;
                    return string.Join(".", labels);
                }
                if ((length & 0xC0) == 0xC0)
                {
                    EnsureAvailable(data, cursor, 1);
                    int destination = ((length & 0x3F) << 8) | data[cursor++];
                    if (!jumped) offset = cursor;
                    jumped = true;
                    cursor = destination;
                    continue;
                }
                if (length > 63) throw InvalidMessage();
                EnsureAvailable(data, cursor, length);
                nameLength += length + 1;
                if (nameLength > 254) throw InvalidMessage();
                labels.Add(Encoding.ASCII.GetString(data, cursor, length));
                cursor += length;
            }
            throw InvalidMessage();
        }

        private static void EnsureAvailable(byte[] data, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset > data.Length - count) throw InvalidMessage();
        }

        private static PhysicalNetworkException InvalidMessage()
            => new("物理网卡 DNS 查询或响应格式无效。");
    }
}
