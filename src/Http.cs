// 网络底层

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SeuAuth
{
    static class MiniDns
    {
        // 直接向 Wi-Fi 网卡的 DNS 服务器查 A 记录（绑定 WLAN 地址，避开代理软件的 DNS 劫持）
        public static IPAddress QueryA(string host, IPAddress server, IPAddress localIp, int timeoutMs)
        {
            try
            {
                using (var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    sock.Bind(new IPEndPoint(localIp, 0));
                    sock.ReceiveTimeout = timeoutMs;
                    ushort id = (ushort)new Random().Next(1, 65535);
                    var q = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
                    foreach (string label in host.Split('.'))
                    {
                        byte[] l = Encoding.ASCII.GetBytes(label);
                        q.Add((byte)l.Length);
                        q.AddRange(l);
                    }
                    q.AddRange(new byte[] { 0, 0, 1, 0, 1 });
                    sock.SendTo(q.ToArray(), new IPEndPoint(server, 53));

                    byte[] buf = new byte[1500];
                    int n = sock.Receive(buf);
                    if (n < 12 || buf[0] != (byte)(id >> 8) || buf[1] != (byte)id) return null;
                    int qdCount = (buf[4] << 8) | buf[5];
                    int anCount = (buf[6] << 8) | buf[7];
                    int pos = 12;
                    for (int i = 0; i < qdCount; i++) pos = SkipName(buf, pos) + 4;
                    for (int i = 0; i < anCount; i++)
                    {
                        pos = SkipName(buf, pos);
                        if (pos + 10 > n) break;
                        int type = (buf[pos] << 8) | buf[pos + 1];
                        int rdLength = (buf[pos + 8] << 8) | buf[pos + 9];
                        pos += 10;
                        if (type == 1 && rdLength == 4 && pos + 4 <= n)
                            return new IPAddress(new[] { buf[pos], buf[pos + 1], buf[pos + 2], buf[pos + 3] });
                        pos += rdLength;
                    }
                }
            }
            catch { }
            return null;
        }

        static int SkipName(byte[] b, int pos)
        {
            while (pos < b.Length)
            {
                int len = b[pos];
                if (len == 0) return pos + 1;
                if ((len & 0xC0) == 0xC0) return pos + 2;
                pos += len + 1;
            }
            return pos;
        }
    }

    class HttpResult
    {
        public int Status;
        public string Body;
    }

    static class Http
    {
        static readonly byte[] HeaderEnd = { 13, 10, 13, 10 };
        static readonly byte[] CrLf = { 13, 10 };

        // 极简 HTTPS GET。源地址绑定在 Wi-Fi 网卡：Windows 采用 strong host 模型，
        // 数据包会直接从物理网卡发出，不会进入代理软件的 TUN 虚拟网卡。
        public static HttpResult Get(IPAddress localIp, IPAddress remoteIp, int port, string host, string pathAndQuery, int timeoutMs)
        {
            using (var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                sock.Bind(new IPEndPoint(localIp, 0));
                IAsyncResult ar = sock.BeginConnect(new IPEndPoint(remoteIp, port), null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                    throw new System.TimeoutException(L.T("连接超时 ", "Connection timed out ") + remoteIp + ":" + port);
                sock.EndConnect(ar);

                using (var net = new NetworkStream(sock, false))
                using (var ssl = new SslStream(net, false, (s, cert, chain, errors) => errors == SslPolicyErrors.None))
                {
                    net.ReadTimeout = net.WriteTimeout = timeoutMs;
                    // 不检查证书吊销：未登录时连不到 CRL/OCSP 服务器
                    ssl.AuthenticateAsClient(host, null, SslProtocols.None, false);

                    string hostHeader = port == 443 ? host : host + ":" + port;
                    string request =
                        "GET " + pathAndQuery + " HTTP/1.1\r\n" +
                        "Host: " + hostHeader + "\r\n" +
                        "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) SEUAuth/" + AppInfo.Version + "\r\n" +
                        "Accept: */*\r\n" +
                        "Referer: https://" + host + "/\r\n" +
                        "Accept-Encoding: identity\r\n" +
                        "Connection: close\r\n\r\n";
                    byte[] reqBytes = Encoding.ASCII.GetBytes(request);
                    ssl.Write(reqBytes);
                    ssl.Flush();
                    return Parse(ReadAll(ssl));
                }
            }
        }

        static byte[] ReadAll(Stream s)
        {
            var ms = new MemoryStream();
            byte[] buf = new byte[8192];
            while (true)
            {
                int n;
                try { n = s.Read(buf, 0, buf.Length); }
                catch (IOException) { if (ms.Length > 0) break; throw; }
                if (n <= 0) break;
                ms.Write(buf, 0, n);
            }
            return ms.ToArray();
        }

        static HttpResult Parse(byte[] raw)
        {
            int headerEnd = IndexOf(raw, HeaderEnd, 0);
            if (headerEnd < 0) throw new IOException(L.T("HTTP 响应不完整", "Incomplete HTTP response"));
            string[] headerLines = Encoding.ASCII.GetString(raw, 0, headerEnd).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] statusParts = headerLines[0].Split(' ');
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string h in headerLines.Skip(1))
            {
                int c = h.IndexOf(':');
                if (c > 0) headers[h.Substring(0, c).Trim()] = h.Substring(c + 1).Trim();
            }

            byte[] body = new byte[raw.Length - headerEnd - 4];
            Array.Copy(raw, headerEnd + 4, body, 0, body.Length);
            string te;
            if (headers.TryGetValue("Transfer-Encoding", out te) && te.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
                body = Dechunk(body);

            string contentType;
            headers.TryGetValue("Content-Type", out contentType);
            return new HttpResult { Status = statusParts.Length > 1 ? int.Parse(statusParts[1]) : 0, Body = DecodeBody(body, contentType) };
        }

        static byte[] Dechunk(byte[] b)
        {
            var output = new MemoryStream();
            int pos = 0;
            while (pos < b.Length)
            {
                int lineEnd = IndexOf(b, CrLf, pos);
                if (lineEnd < 0) break;
                int size = Convert.ToInt32(Encoding.ASCII.GetString(b, pos, lineEnd - pos).Split(';')[0].Trim(), 16);
                pos = lineEnd + 2;
                if (size == 0) break;
                output.Write(b, pos, Math.Min(size, b.Length - pos));
                pos += size + 2;
            }
            return output.ToArray();
        }

        static string DecodeBody(byte[] body, string contentType)
        {
            Match m = Regex.Match(contentType ?? "", @"charset=([\w-]+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                try { return Encoding.GetEncoding(m.Groups[1].Value).GetString(body); } catch { }
            }
            try { return new UTF8Encoding(false, true).GetString(body); }
            catch (DecoderFallbackException) { return Encoding.GetEncoding(936).GetString(body); }
        }

        static int IndexOf(byte[] haystack, byte[] needle, int start)
        {
            for (int i = start; i <= haystack.Length - needle.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }
}
