// Dr.COM ePortal

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
    enum PortalState { Online, Offline, Unknown }

    enum LoginOutcome { Success, AlreadyOnline, Rejected, Failed, NetworkError }

    class PortalClient
    {
        // 与认证页 a41.js 中的 jsVersion 一致
        const string JsVersion = "3.3.3";
        static readonly Random Rng = new Random();

        readonly Config cfg;
        IPAddress cachedIp;

        public PortalClient(Config cfg) { this.cfg = cfg; }

        public void ResetCache() { cachedIp = null; }

        // GET https://w.seu.edu.cn/drcom/chkstatus → result 1 在线、0 未登录
        public PortalState CheckStatus(WlanInfo w, out string detail)
        {
            string body = Get(w, cfg.StatusPort, "/drcom/chkstatus?callback=dr1002&v=" + Rng.Next(500, 10500), out detail);
            if (body == null) return PortalState.Unknown;
            string result = JsonField(body, "result");
            if (result == "1") return PortalState.Online;
            if (result == "0") return PortalState.Offline;
            detail = L.T("无法解析状态响应：", "Unrecognized status response: ") + Truncate(body, 200);
            return PortalState.Unknown;
        }

        // 对应 a42.js 的 login_portal（login_method=1）
        public LoginOutcome Login(WlanInfo w, string account, string password, out string detail)
        {
            var q = new List<KeyValuePair<string, string>>
            {
                Kv("callback", "dr1003"),
                Kv("login_method", "1"),
                Kv("user_account", cfg.AccountPrefix + account + cfg.AccountSuffix),
                Kv("user_password", password),
                Kv("wlan_user_ip", w.Ipv4.ToString()),
                Kv("wlan_user_ipv6", ""),
                Kv("wlan_user_mac", "000000000000"),
                Kv("wlan_ac_ip", ""),
                Kv("wlan_ac_name", ""),
                Kv("jsVersion", JsVersion),
                Kv("v", Rng.Next(500, 10500).ToString())
            };
            // 注意：网址含密码，任何情况下都不可写入日志
            string body = Get(w, cfg.EportalPort, "/eportal/?c=Portal&a=login&" + Query(q), out detail);
            if (body == null) return LoginOutcome.NetworkError;

            string result = JsonField(body, "result");
            string retCode = JsonField(body, "ret_code");
            string msg = DecodeMsg(JsonField(body, "msg"));
            detail = "result=" + result + " ret_code=" + retCode + " msg=" + msg;
            if (result == "1" || result == "ok") return LoginOutcome.Success;
            // ret_code：1 账号密码不对、2 IP 已在线、3 系统忙……（见 a42.js）
            if (retCode == "2") return LoginOutcome.AlreadyOnline;
            if (retCode == "1") return LoginOutcome.Rejected;
            return LoginOutcome.Failed;
        }

        // 对应 a42.js 的 logout_portal，仅供测试
        public bool Logout(WlanInfo w, out string detail)
        {
            var q = new List<KeyValuePair<string, string>>
            {
                Kv("callback", "dr1004"),
                Kv("login_method", "1"),
                Kv("user_account", "drcom"),
                Kv("user_password", "123"),
                Kv("ac_logout", "1"),
                Kv("register_mode", "1"),
                Kv("wlan_user_ip", w.Ipv4.ToString()),
                Kv("wlan_user_ipv6", ""),
                Kv("wlan_vlan_id", "1"),
                Kv("wlan_user_mac", "000000000000"),
                Kv("wlan_ac_ip", ""),
                Kv("wlan_ac_name", ""),
                Kv("jsVersion", JsVersion),
                Kv("v", Rng.Next(500, 10500).ToString())
            };
            string body = Get(w, cfg.EportalPort, "/eportal/?c=Portal&a=logout&" + Query(q), out detail);
            if (body == null) return false;
            string result = JsonField(body, "result");
            detail = "result=" + result + " msg=" + DecodeMsg(JsonField(body, "msg"));
            return result == "1" || result == "ok";
        }

        string Get(WlanInfo w, int port, string pathAndQuery, out string error)
        {
            error = null;
            IPAddress ip = ResolvePortal(w);
            try
            {
                HttpResult r = Http.Get(w.Ipv4, ip, port, cfg.PortalHost, pathAndQuery, 8000);
                if (r.Status != 200) { error = "HTTP " + r.Status; return null; }
                return r.Body;
            }
            catch (Exception ex)
            {
                cachedIp = null;
                error = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        IPAddress ResolvePortal(WlanInfo w)
        {
            if (cachedIp != null) return cachedIp;
            IPAddress ip = null;
            try
            {
                ip = Dns.GetHostAddresses(cfg.PortalHost)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IsFakeIp(a));
            }
            catch { }
            if (ip == null)
            {
                foreach (IPAddress dns in w.DnsServers)
                {
                    ip = MiniDns.QueryA(cfg.PortalHost, dns, w.Ipv4, 3000);
                    if (ip != null) break;
                }
            }
            if (ip == null) ip = IPAddress.Parse(cfg.PortalIpFallback);
            cachedIp = ip;
            return ip;
        }

        // Clash fake-ip 默认网段 198.18.0.0/15
        static bool IsFakeIp(IPAddress a)
        {
            byte[] b = a.GetAddressBytes();
            return b[0] == 198 && (b[1] == 18 || b[1] == 19);
        }

        static KeyValuePair<string, string> Kv(string k, string v) { return new KeyValuePair<string, string>(k, v); }

        static string Query(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            return string.Join("&", pairs.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        }

        // 从 JSONP 响应中取出单个字段（响应结构很简单，不需要完整的 JSON 解析器）
        public static string JsonField(string body, string name)
        {
            Match m = Regex.Match(body, "\"" + Regex.Escape(name) + "\"\\s*:\\s*(?:\"((?:[^\"\\\\]|\\\\.)*)\"|([^,}\\s]+))");
            if (!m.Success) return null;
            if (!m.Groups[1].Success) return m.Groups[2].Value;
            try { return Regex.Unescape(m.Groups[1].Value); } catch { return m.Groups[1].Value; }
        }

        // Dr.COM 的 msg 有时是 Base64（例如 "dXNlcmlkIGVycm9yMQ==" = "userid error1"）
        static string DecodeMsg(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return msg;
            if (msg.Length >= 8 && msg.Length % 4 == 0 && Regex.IsMatch(msg, "^[A-Za-z0-9+/]+={0,2}$"))
            {
                try
                {
                    string decoded = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(msg));
                    if (decoded.All(c => !char.IsControl(c))) return decoded + " (Base64: " + msg + ")";
                }
                catch { }
            }
            return msg;
        }

        static string Truncate(string s, int max) { return s.Length <= max ? s : s.Substring(0, max) + "…"; }
    }
}
