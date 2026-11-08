// Wi-Fi 状态

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
    class WlanInfo
    {
        public string InterfaceName;
        public IPAddress Ipv4;
        public IPAddress[] DnsServers;
        public string Ssid;        // null 表示读不到
        public string SsidSource;
    }

    static class Wlan
    {
        [DllImport("wlanapi.dll")]
        static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);
        [DllImport("wlanapi.dll")]
        static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);
        [DllImport("wlanapi.dll")]
        static extern int WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opCode, IntPtr reserved, out int dataSize, out IntPtr data, IntPtr opcodeValueType);
        [DllImport("wlanapi.dll")]
        static extern void WlanFreeMemory(IntPtr memory);

        const int OpcodeCurrentConnection = 7;
        // WLAN_CONNECTION_ATTRIBUTES：isState(4) + wlanConnectionMode(4) + strProfileName(WCHAR[256]) 之后就是 DOT11_SSID
        const int SsidOffset = 8 + 512;

        public static WlanInfo GetConnected()
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 || nic.OperationalStatus != OperationalStatus.Up) continue;
                IPInterfaceProperties props = nic.GetIPProperties();
                IPAddress ip = props.UnicastAddresses
                    .Select(a => a.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254."));
                if (ip == null) continue;

                var info = new WlanInfo
                {
                    InterfaceName = nic.Name,
                    Ipv4 = ip,
                    DnsServers = props.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork).ToArray()
                };
                info.Ssid = QuerySsid(nic.Id);
                if (info.Ssid != null)
                {
                    info.SsidSource = "wlanapi";
                }
                else
                {
                    // Windows 11 的定位隐私设置可能挡住 SSID 查询，改读网络配置文件名称（默认由 SSID 而来）
                    info.Ssid = QueryNetworkProfileName(nic);
                    if (info.Ssid != null) info.SsidSource = L.T("网络配置文件名称", "network profile name");
                }
                return info;
            }
            return null;
        }

        static string QuerySsid(string nicId)
        {
            Guid guid;
            try { guid = new Guid(nicId); } catch { return null; }
            IntPtr handle = IntPtr.Zero, data = IntPtr.Zero;
            try
            {
                uint version;
                if (WlanOpenHandle(2, IntPtr.Zero, out version, out handle) != 0) return null;
                int size;
                if (WlanQueryInterface(handle, ref guid, OpcodeCurrentConnection, IntPtr.Zero, out size, out data, IntPtr.Zero) != 0) return null;
                if (size < SsidOffset + 4 + 32) return null;
                int len = Marshal.ReadInt32(data, SsidOffset);
                if (len <= 0 || len > 32) return null;
                byte[] raw = new byte[len];
                Marshal.Copy(IntPtr.Add(data, SsidOffset + 4), raw, 0, len);
                return Encoding.UTF8.GetString(raw);
            }
            catch { return null; }
            finally
            {
                if (data != IntPtr.Zero) WlanFreeMemory(data);
                if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero);
            }
        }

        static string QueryNetworkProfileName(NetworkInterface nic)
        {
            try
            {
                int index = nic.GetIPProperties().GetIPv4Properties().Index;
                using (var searcher = new ManagementObjectSearcher(@"root\StandardCimv2", "SELECT Name FROM MSFT_NetConnectionProfile WHERE InterfaceIndex = " + index))
                {
                    foreach (ManagementBaseObject mo in searcher.Get())
                    {
                        object name = mo["Name"];
                        if (name != null) return name.ToString();
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
