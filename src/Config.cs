// 设置 / Config

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
    class Config
    {
        public string Language = "both";
        public string SsidPrefix = "SEU-WLAN";
        public string PortalHost = "w.seu.edu.cn";
        public string PortalIpFallback = "10.80.128.2";
        public int StatusPort = 443;
        public int EportalPort = 802;
        public string AccountPrefix = ",0,";
        public string AccountSuffix = "";
        public int CheckIntervalSeconds = 30;
        public int FastRetryCount = 3;
        public int FastRetryDelaySeconds = 10;
        public int SlowRetryDelaySeconds = 120;
        public int RejectedRetryDelaySeconds = 60;
        public int LogRetentionDays = 14;

        const string DefaultFile =
@"; SEU-WLAN 自动认证设置。修改后重启服务生效：sc stop SEUAuth && sc start SEUAuth
; SEU-WLAN Auto Authentication settings. Restart the service after editing: sc stop SEUAuth && sc start SEUAuth

; 界面与日志语言 / UI and log language: both | zh | en
Language=both
; 只有 SSID 以此开头的 Wi-Fi 才会动作 / Only act on Wi-Fi whose SSID starts with this
SsidPrefix=SEU-WLAN
PortalHost=w.seu.edu.cn
; DNS 解析失败时使用的认证服务器 IP / Portal IP used when DNS fails
PortalIpFallback=10.80.128.2
StatusPort=443
EportalPort=802
; 提交的账号 = AccountPrefix + 账号 + AccountSuffix（电脑端默认前缀 ,0,）
; Submitted account = AccountPrefix + account + AccountSuffix (PC prefix is ,0,)
AccountPrefix=,0,
AccountSuffix=
; 在线时的检查间隔（秒）/ Check interval while online (seconds)
CheckIntervalSeconds=30
; 登录失败：前 FastRetryCount 次每 FastRetryDelaySeconds 秒重试，之后每 SlowRetryDelaySeconds 秒
; On failure: retry every FastRetryDelaySeconds for FastRetryCount times, then every SlowRetryDelaySeconds
FastRetryCount=3
FastRetryDelaySeconds=10
SlowRetryDelaySeconds=120
; 服务器拒绝账号或密码（ret_code=1）后，隔这么久再试。这类拒绝也可能是服务器暂时出错（如 ErrCode=05）
; Wait this long after the server rejects the credentials (ret_code=1); such rejections can also be transient (e.g. ErrCode=05)
RejectedRetryDelaySeconds=60
LogRetentionDays=14
";

        // 旧版写入的默认值 → 新默认值。只迁移用户没改过的值。
        static readonly string[][] Migrations =
        {
            new[] { "RejectedRetryDelaySeconds=1800", "RejectedRetryDelaySeconds=60" }   // 发布前的测试版默认等 30 分钟
        };

        public static void EnsureFile()
        {
            Directory.CreateDirectory(Paths.DataDir);
            if (!File.Exists(Paths.ConfigFile))
            {
                File.WriteAllText(Paths.ConfigFile, DefaultFile, new UTF8Encoding(true));
                return;
            }

            string[] lines = File.ReadAllLines(Paths.ConfigFile);
            bool changed = false;
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (string[] m in Migrations)
                {
                    if (lines[i].Trim() != m[0]) continue;
                    lines[i] = m[1];
                    changed = true;
                    Console.WriteLine(L.T("设置已更新：", "Setting updated: ") + m[0] + " → " + m[1]);
                }
            }
            if (changed) File.WriteAllLines(Paths.ConfigFile, lines, new UTF8Encoding(true));
        }

        public static Config Load()
        {
            var cfg = new Config();
            if (File.Exists(Paths.ConfigFile))
            {
                foreach (string rawLine in File.ReadAllLines(Paths.ConfigFile))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    var field = typeof(Config).GetField(key);
                    if (field == null) continue;
                    try { field.SetValue(cfg, Convert.ChangeType(value, field.FieldType)); }
                    catch { Log.Warn(L.T("设置值无效，已忽略：", "Invalid setting ignored: ") + key + "=" + value); }
                }
            }
            string lang = cfg.Language.ToLowerInvariant();
            L.Mode = lang == "zh" || lang == "en" ? lang : "both";
            return cfg;
        }
    }
}
