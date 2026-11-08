// SEU-WLAN 自动认证 / SEU-WLAN Auto Authentication
// 定期查询 Dr.COM ePortal 的登录状态，掉线时直接以 HTTP 重新登录。
// 所有对认证服务器的连接都绑定在 Wi-Fi 网卡的 IP 上，因此不受 Clash 等代理软件的 TUN 模式影响。
// 以 Windows 内置的 .NET Framework 4.8 编译（见 build.cmd），C# 5 语法。

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

// 让运行时以 4.8 行为运作（TLS 版本交给操作系统决定）
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
[assembly: AssemblyTitle("SEU-WLAN Auto Authentication")]
[assembly: AssemblyDescription("Unofficial auto re-login tool for the SEU-WLAN Dr.COM portal")]
[assembly: AssemblyProduct("SEU-WLAN Auto Authentication")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion(SeuAuth.AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(SeuAuth.AppInfo.Version + ".0")]

namespace SeuAuth
{
    static class AppInfo
    {
        public const string Version = "1.0.0";
    }

    static class Paths
    {
        public const string ServiceName = "SEUAuth";
        public const string ServiceDisplayName = "SEU-WLAN Auto Authentication";
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SEUAuth");
        public static readonly string ConfigFile = Path.Combine(DataDir, "config.ini");
        public static readonly string CredentialFile = Path.Combine(DataDir, "credential.bin");
        public static readonly string LogDir = Path.Combine(DataDir, "logs");
        public static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SEUAuth");
        public static readonly string InstalledExe = Path.Combine(InstallDir, "seu-auth.exe");
        public static readonly string StartMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "SEU-WLAN Auto Authentication.lnk");
    }
}
