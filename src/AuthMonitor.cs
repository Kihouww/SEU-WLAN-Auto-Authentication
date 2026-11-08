// 监控主循环 / Monitor loop

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
    class AuthMonitor
    {
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        readonly AutoResetEvent networkChanged = new AutoResetEvent(false);
        Config cfg;
        PortalClient portal;
        string lastCondition;      // 只在状态改变时写日志，避免日志被重复信息塞满
        bool offline;
        bool ssidWarned;
        int consecutiveFailures;
        DateTime nextLoginAllowed = DateTime.MinValue;
        DateTime lastPurge = DateTime.MinValue;

        public void Stop() { stop.Set(); }

        public void Run()
        {
            cfg = Config.Load();
            portal = new PortalClient(cfg);
            Log.Info(L.T("监控启动", "Monitor started") + " (v" + AppInfo.Version + ", " + cfg.CheckIntervalSeconds + "s, SSID " + cfg.SsidPrefix + "*)");
            NetworkAddressChangedEventHandler onAddress = (s, e) => networkChanged.Set();
            NetworkAvailabilityChangedEventHandler onAvailability = (s, e) => networkChanged.Set();
            NetworkChange.NetworkAddressChanged += onAddress;
            NetworkChange.NetworkAvailabilityChanged += onAvailability;
            try
            {
                while (!stop.WaitOne(0))
                {
                    try { Tick(); }
                    catch (Exception ex) { Log.Error(L.T("意外错误：", "Unexpected error: ") + ex); }

                    int signaled = WaitHandle.WaitAny(new WaitHandle[] { stop, networkChanged }, NextDelaySeconds() * 1000);
                    if (signaled == 1)
                    {
                        // 网络刚变化，等 DHCP／路由稳定后再检查
                        if (stop.WaitOne(3000)) break;
                        portal.ResetCache();
                    }
                }
            }
            finally
            {
                NetworkChange.NetworkAddressChanged -= onAddress;
                NetworkChange.NetworkAvailabilityChanged -= onAvailability;
                Log.Info(L.T("监控停止", "Monitor stopped"));
            }
        }

        int NextDelaySeconds()
        {
            if (offline && nextLoginAllowed > DateTime.Now)
            {
                int untilRetry = (int)Math.Ceiling((nextLoginAllowed - DateTime.Now).TotalSeconds);
                return Math.Max(1, Math.Min(cfg.CheckIntervalSeconds, untilRetry));
            }
            return Math.Max(5, cfg.CheckIntervalSeconds);
        }

        void Report(string condition, string message)
        {
            if (condition == lastCondition) return;
            lastCondition = condition;
            Log.Info(message);
        }

        void Tick()
        {
            if ((DateTime.Now - lastPurge).TotalHours >= 24)
            {
                Log.Purge(cfg.LogRetentionDays);
                lastPurge = DateTime.Now;
            }

            offline = false;
            WlanInfo w = Wlan.GetConnected();
            if (w == null)
            {
                Report("no-wlan", L.T("Wi-Fi 未连接，等待中", "Wi-Fi not connected, waiting"));
                return;
            }
            if (w.Ssid == null)
            {
                // 读不到 SSID 时仍继续：HTTPS 证书验证保证密码只会发到真正的 w.seu.edu.cn
                if (!ssidWarned) Log.Warn(L.T("读不到 Wi-Fi 名称，改以认证服务器是否可达来判断", "Cannot read the Wi-Fi name; relying on whether the portal is reachable"));
                ssidWarned = true;
            }
            else if (!w.Ssid.StartsWith(cfg.SsidPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Report("ssid:" + w.Ssid, L.T("当前连接的是「" + w.Ssid + "」，不是校园网，不做任何事", "Connected to \"" + w.Ssid + "\", not the campus network; doing nothing"));
                return;
            }

            string detail;
            PortalState state = portal.CheckStatus(w, out detail);
            string where = (w.Ssid ?? w.InterfaceName) + ", " + w.Ipv4;
            if (state == PortalState.Online)
            {
                consecutiveFailures = 0;
                nextLoginAllowed = DateTime.MinValue;
                Report("online", L.T("校园网已登录", "Campus network is logged in") + " (" + where + ")");
                return;
            }
            if (state == PortalState.Unknown)
            {
                Report("unknown", L.T("无法获取认证状态：", "Cannot get login status: ") + detail);
                return;
            }

            offline = true;
            Report("offline", L.T("检测到校园网未登录", "Campus network is logged out") + " (" + where + ")");
            if (DateTime.Now >= nextLoginAllowed) TryLogin(w);
        }

        void TryLogin(WlanInfo w)
        {
            string account, password, error;
            if (!CredentialStore.TryLoad(out account, out password, out error))
            {
                Log.Error(error);
                nextLoginAllowed = DateTime.Now.AddSeconds(cfg.SlowRetryDelaySeconds);
                return;
            }

            Log.Info(L.T("尝试登录（连续第 " + (consecutiveFailures + 1) + " 次）", "Logging in (attempt " + (consecutiveFailures + 1) + ")"));
            string detail;
            LoginOutcome outcome = portal.Login(w, account, password, out detail);
            password = null;

            if (outcome == LoginOutcome.Success || outcome == LoginOutcome.AlreadyOnline)
            {
                // 服务器状态可能有一点延迟
                stop.WaitOne(2000);
                string verifyDetail;
                if (portal.CheckStatus(w, out verifyDetail) == PortalState.Online)
                {
                    Log.Info(L.T("登录成功", "Login succeeded") + " (" + detail + ")");
                    consecutiveFailures = 0;
                    nextLoginAllowed = DateTime.MinValue;
                    offline = false;
                    lastCondition = "online";
                    return;
                }
                detail = L.T("服务器回报成功，但状态查询仍显示未登录", "Server reported success but status is still logged out") + " (" + detail + ")";
                outcome = LoginOutcome.Failed;
            }

            consecutiveFailures++;
            int delay;
            if (outcome == LoginOutcome.Rejected)
            {
                delay = cfg.RejectedRetryDelaySeconds;
                Log.Error(L.T(
                    "服务器拒绝了账号或密码（" + detail + "）。" + delay + " 秒后再试；如果改过密码，请在菜单中选择「更新账号密码」",
                    "Server rejected the credentials (" + detail + "). Retrying in " + delay + "s; if you changed your password, choose \"Update credentials\" in the menu"));
            }
            else
            {
                delay = consecutiveFailures < cfg.FastRetryCount ? cfg.FastRetryDelaySeconds : cfg.SlowRetryDelaySeconds;
                Log.Warn(L.T("登录失败（" + detail + "），" + delay + " 秒后重试", "Login failed (" + detail + "), retrying in " + delay + "s"));
            }
            nextLoginAllowed = DateTime.Now.AddSeconds(delay);
        }
    }
}
