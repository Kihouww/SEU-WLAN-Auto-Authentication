// 菜单与各命令的实现 / Menu and command implementations

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
    static class Commands
    {
        public static void Help()
        {
            Console.WriteLine(L.Block(
@"SEU-WLAN 自动认证 v" + AppInfo.Version + @"（非官方工具）

直接双击即可打开菜单。命令行用法：seu-auth <命令>

  setup            输入账号密码，安装并启动后台服务
  set-credential   只更新账号密码
  status           查看服务、Wi-Fi、认证状态和最近的日志
  login [--force]  立即登录一次；已在线时加 --force 仍强制提交
  logout           从校园网注销（测试用，会断网）
  run              在前台运行监控循环，方便调试，Ctrl+C 结束
  uninstall        停止并移除服务

需要管理员权限的命令会自动弹出 UAC 提示。
设置文件：" + Paths.ConfigFile + @"
日志目录：" + Paths.LogDir,
@"SEU-WLAN Auto Authentication v" + AppInfo.Version + @" (unofficial)

Double-click to open the menu. Command line: seu-auth <command>

  setup            Enter credentials, install and start the background service
  set-credential   Update credentials only
  status           Show service, Wi-Fi and login status plus recent log lines
  login [--force]  Log in now; --force submits even when already online
  logout           Log out of the campus network (for testing; drops the connection)
  run              Run the monitor loop in the foreground for debugging; Ctrl+C to stop
  uninstall        Stop and remove the service

Commands that need administrator rights show a UAC prompt automatically.
Settings: " + Paths.ConfigFile + @"
Logs:     " + Paths.LogDir));
        }

        public static int Menu()
        {
            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("==================================================");
                Console.WriteLine(" " + L.T("SEU-WLAN 自动认证", "SEU-WLAN Auto Authentication") + "  v" + AppInfo.Version);
                Console.WriteLine(" " + L.T("非官方工具，与东南大学及 Dr.COM 无关", "Unofficial; not affiliated with SEU or Dr.COM"));
                Console.WriteLine("==================================================");
                Console.WriteLine(" " + L.T("服务", "Service") + ": " + ServiceStatusText());
                Console.WriteLine();
                Console.WriteLine("  1  " + L.T("安装或更新", "Install or update"));
                Console.WriteLine("  2  " + L.T("查看状态", "Show status"));
                Console.WriteLine("  3  " + L.T("立即登录", "Log in now"));
                Console.WriteLine("  4  " + L.T("更新账号密码", "Update credentials"));
                Console.WriteLine("  5  " + L.T("卸载", "Uninstall"));
                Console.WriteLine("  0  " + L.T("退出", "Exit"));
                Console.Write(L.T("请输入数字", "Choose") + ": ");
                string choice = (Console.ReadLine() ?? "0").Trim();
                Console.WriteLine();

                string cmd;
                switch (choice)
                {
                    case "1": cmd = "setup"; break;
                    case "2": cmd = "status"; break;
                    case "3": cmd = "login"; break;
                    case "4": cmd = "set-credential"; break;
                    case "5": cmd = "uninstall"; break;
                    case "0":
                    case "": return 0;
                    default: continue;
                }

                try
                {
                    if (cmd != "status" && !IsAdmin()) RunElevated(new[] { cmd });
                    else Program.Dispatch(cmd, new[] { cmd });
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(L.T("错误：", "Error: ") + ex.Message);
                }
            }
        }

        public static int Setup()
        {
            Config.EnsureFile();
            Config.Load();
            Console.WriteLine("== " + L.T("安装", "Install") + " ==");
            Console.WriteLine(L.T(
                "账号密码会用 Windows DPAPI 加密后保存在本机，只有系统和管理员能读取，不会上传到任何地方。",
                "Your credentials are encrypted with Windows DPAPI and stored only on this PC, readable only by SYSTEM and administrators. Nothing is uploaded."));
            Console.WriteLine();
            if (!CredentialStore.Exists || AskYesNo(L.T("已经保存过账号密码，要重新输入吗？", "Credentials already saved. Enter them again?"), false))
                PromptAndSaveCredential();

            InstallService();
            Console.WriteLine();
            Console.WriteLine(L.T(
                "安装完成。服务会在开机时自动启动，校园网掉线时自动重新登录。",
                "Done. The service starts at boot and logs back in whenever the campus login expires."));
            Console.WriteLine(L.T(
                "以后可在开始菜单打开「SEU-WLAN Auto Authentication」查看状态。",
                "Open \"SEU-WLAN Auto Authentication\" from the Start menu to check its status later."));

            WlanInfo w = Wlan.GetConnected();
            if (w != null)
            {
                string detail;
                PortalState state = new PortalClient(Config.Load()).CheckStatus(w, out detail);
                Console.WriteLine(L.T("当前校园网认证", "Current campus login") + ": " + StateText(state, detail));
            }
            return 0;
        }

        public static int SetCredential()
        {
            Config.EnsureFile();
            PromptAndSaveCredential();
            Console.WriteLine(L.T("服务下次登录时就会使用新的账号密码，不需要重启。", "The service will use the new credentials on its next login; no restart needed."));
            return 0;
        }

        public static int Status()
        {
            Log.ToFile = false;
            Console.WriteLine(L.T("服务", "Service") + ": " + ServiceStatusText());
            Console.WriteLine(L.T("账号密码", "Credentials") + ": " + (CredentialStore.Exists ? L.T("已保存", "saved") : L.T("未保存", "not saved")));

            WlanInfo w = Wlan.GetConnected();
            if (w == null)
            {
                Console.WriteLine("Wi-Fi: " + L.T("未连接", "not connected"));
            }
            else
            {
                string ssid = w.Ssid == null ? L.T("（读不到）", "(unavailable)") : w.Ssid + " (" + w.SsidSource + ")";
                Console.WriteLine("Wi-Fi: " + w.InterfaceName + ", IP " + w.Ipv4 + ", SSID " + ssid);
                string detail;
                PortalState state = new PortalClient(Config.Load()).CheckStatus(w, out detail);
                Console.WriteLine(L.T("校园网认证", "Campus login") + ": " + StateText(state, detail));
            }

            string[] tail = Log.Tail(15);
            Console.WriteLine();
            if (tail == null) Console.WriteLine(L.T("（没有权限读取日志）", "(no permission to read logs)"));
            else if (tail.Length == 0) Console.WriteLine(L.T("（暂无日志）", "(no logs yet)"));
            else
            {
                Console.WriteLine(L.T("最近的日志", "Recent log lines") + ":");
                foreach (string line in tail) Console.WriteLine("  " + line);
            }
            return 0;
        }

        public static int Login(bool force)
        {
            Config cfg = Config.Load();
            var portal = new PortalClient(cfg);
            WlanInfo w = RequireCampusWlan(cfg);

            string detail;
            if (!force && portal.CheckStatus(w, out detail) == PortalState.Online)
            {
                Console.WriteLine(L.T("当前已经登录，不需要再登录。（要强制提交请加 --force）", "Already logged in; nothing to do. (Use --force to submit anyway.)"));
                return 0;
            }

            string account, password, error;
            if (!CredentialStore.TryLoad(out account, out password, out error)) throw new InvalidOperationException(error);
            Log.Info(L.T("手动登录", "Manual login"));
            LoginOutcome outcome = portal.Login(w, account, password, out detail);
            Log.Info(L.T("登录结果", "Login result") + ": " + outcome + " (" + detail + ")");
            Thread.Sleep(2000);
            string statusDetail;
            PortalState state = portal.CheckStatus(w, out statusDetail);
            Log.Info(L.T("登录后状态", "Status after login") + ": " + StateText(state, statusDetail));
            return state == PortalState.Online ? 0 : 1;
        }

        public static int Logout()
        {
            Config cfg = Config.Load();
            WlanInfo w = RequireCampusWlan(cfg);
            Console.WriteLine(L.T(
                "这会让本机立即从校园网注销。如果你正在远程连接，连接会中断，直到服务（或你）重新登录为止。",
                "This logs this PC out of the campus network immediately. A remote session will drop until the service (or you) logs back in."));
            Console.Write(L.T("确定要注销请输入 YES", "Type YES to continue") + ": ");
            if (Console.ReadLine() != "YES")
            {
                Console.WriteLine(L.T("已取消。", "Cancelled."));
                return 1;
            }
            string detail;
            bool ok = new PortalClient(cfg).Logout(w, out detail);
            Log.Info(L.T("手动注销", "Manual logout") + ": " + (ok ? L.T("成功", "succeeded") : L.T("失败", "failed")) + " (" + detail + ")");
            return ok ? 0 : 1;
        }

        public static int RunForeground()
        {
            var monitor = new AuthMonitor();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; monitor.Stop(); };
            Console.WriteLine(L.T("前台监控中，按 Ctrl+C 结束。", "Monitoring in the foreground. Press Ctrl+C to stop."));
            monitor.Run();
            return 0;
        }

        public static int Uninstall()
        {
            if (ServiceExists())
            {
                StopService();
                Sc("delete " + Paths.ServiceName);
                Console.WriteLine(L.T("已移除服务。", "Service removed."));
            }
            else
            {
                Console.WriteLine(L.T("服务未安装。", "Service is not installed."));
            }

            try { if (File.Exists(Paths.StartMenuShortcut)) File.Delete(Paths.StartMenuShortcut); } catch { }

            if (Directory.Exists(Paths.InstallDir))
            {
                string self = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
                if (self.StartsWith(Path.GetFullPath(Paths.InstallDir), StringComparison.OrdinalIgnoreCase))
                {
                    // 正在运行的就是安装目录里的程序：退出几秒后再删除
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"" + Paths.InstallDir + "\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                else
                {
                    Directory.Delete(Paths.InstallDir, true);
                }
            }

            if (Directory.Exists(Paths.DataDir) &&
                AskYesNo(L.T("一并删除账号密码、设置和日志（" + Paths.DataDir + "）？", "Also delete credentials, settings and logs (" + Paths.DataDir + ")?"), true))
            {
                Directory.Delete(Paths.DataDir, true);
                Console.WriteLine(L.T("已删除。", "Deleted."));
            }
            Console.WriteLine(L.T("卸载完成。", "Uninstalled."));
            return 0;
        }

        // ── 辅助 / Helpers ──

        public static bool IsAdmin()
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        }

        // 以管理员身份重新启动自己并等待结束（新窗口会在结束前停住）
        public static int RunElevated(string[] args)
        {
            var psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                string.Join(" ", args.Select(a => "\"" + a.Replace("\"", "\\\"") + "\"")))
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            try
            {
                Console.WriteLine(L.T("需要管理员权限，正在新窗口中执行……", "Administrator rights required; continuing in a new window..."));
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (Win32Exception)
            {
                Console.WriteLine(L.T("已取消（没有获得管理员权限）。", "Cancelled (administrator rights not granted)."));
                return 1;
            }
        }

        static string StateText(PortalState state, string detail)
        {
            if (state == PortalState.Online) return L.T("已登录", "logged in");
            if (state == PortalState.Offline) return L.T("未登录", "logged out");
            return L.T("未知", "unknown") + " (" + detail + ")";
        }

        static WlanInfo RequireCampusWlan(Config cfg)
        {
            WlanInfo w = Wlan.GetConnected();
            if (w == null) throw new InvalidOperationException(L.T("Wi-Fi 未连接", "Wi-Fi is not connected"));
            if (w.Ssid != null && !w.Ssid.StartsWith(cfg.SsidPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L.T("当前连接的是「" + w.Ssid + "」，不是校园网", "Connected to \"" + w.Ssid + "\", not the campus network"));
            return w;
        }

        static void PromptAndSaveCredential()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Console.Write(L.T("账号（通常是一卡通号，不含任何前后缀）", "Account (usually your campus card number, no prefix or suffix)") + ": ");
                string account = (Console.ReadLine() ?? "").Trim();
                if (account.Length == 0) { Console.WriteLine(L.T("账号不能为空。", "Account cannot be empty.")); continue; }
                string p1 = ReadPassword(L.T("密码（输入时不显示）", "Password (hidden)") + ": ");
                string p2 = ReadPassword(L.T("再输入一次密码", "Repeat password") + ": ");
                if (p1.Length == 0) { Console.WriteLine(L.T("密码不能为空。", "Password cannot be empty.")); continue; }
                if (p1 != p2) { Console.WriteLine(L.T("两次输入的密码不一致，请重新输入。", "Passwords do not match; try again.")); continue; }
                CredentialStore.Save(account, p1);
                Console.WriteLine(L.T("账号密码已加密保存。", "Credentials encrypted and saved."));
                return;
            }
            throw new InvalidOperationException(L.T("账号密码输入失败", "Failed to enter credentials"));
        }

        static string ReadPassword(string prompt)
        {
            Console.Write(prompt);
            var sb = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0) { sb.Length--; Console.Write("\b \b"); }
                    continue;
                }
                if (!char.IsControl(key.KeyChar)) { sb.Append(key.KeyChar); Console.Write('*'); }
            }
            Console.WriteLine();
            return sb.ToString();
        }

        static bool AskYesNo(string question, bool defaultYes)
        {
            Console.Write(question + (defaultYes ? " [Y/n] " : " [y/N] "));
            string answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
            if (answer.Length == 0) return defaultYes;
            return answer == "y" || answer == "yes";
        }

        static bool ServiceExists()
        {
            return ServiceController.GetServices().Any(s => s.ServiceName.Equals(Paths.ServiceName, StringComparison.OrdinalIgnoreCase));
        }

        static string ServiceStatusText()
        {
            if (!ServiceExists()) return L.T("未安装", "not installed");
            using (var sc = new ServiceController(Paths.ServiceName))
            {
                if (sc.Status == ServiceControllerStatus.Running) return L.T("运行中", "running");
                if (sc.Status == ServiceControllerStatus.Stopped) return L.T("已停止", "stopped");
                return sc.Status.ToString();
            }
        }

        static void StopService()
        {
            using (var sc = new ServiceController(Paths.ServiceName))
            {
                if (sc.Status == ServiceControllerStatus.Stopped) return;
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }

        static void InstallService()
        {
            bool exists = ServiceExists();
            if (exists) StopService();

            string self = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
            Directory.CreateDirectory(Paths.InstallDir);
            if (!self.Equals(Path.GetFullPath(Paths.InstalledExe), StringComparison.OrdinalIgnoreCase))
            {
                // 服务回报「已停止」后，进程可能还要片刻才真正退出并释放 exe
                for (int attempt = 1; ; attempt++)
                {
                    try { File.Copy(self, Paths.InstalledExe, true); break; }
                    catch (IOException) { if (attempt >= 20) throw; Thread.Sleep(500); }
                    catch (UnauthorizedAccessException) { if (attempt >= 20) throw; Thread.Sleep(500); }
                }
            }

            if (!exists)
                Sc("create " + Paths.ServiceName + " binPath= \"\\\"" + Paths.InstalledExe + "\\\"\" start= auto DisplayName= \"" + Paths.ServiceDisplayName + "\"");
            Sc("description " + Paths.ServiceName + " \"Unofficial tool: logs back in to the SEU-WLAN Dr.COM portal when the campus login expires.\"");
            // 服务异常结束时自动重启
            Sc("failure " + Paths.ServiceName + " reset= 86400 actions= restart/5000/restart/10000/restart/60000");

            using (var sc = new ServiceController(Paths.ServiceName))
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            }
            CreateStartMenuShortcut();
            Console.WriteLine((exists ? L.T("服务已更新并启动", "Service updated and started") : L.T("服务已安装并启动", "Service installed and started")) + ": " + Paths.InstalledExe);
        }

        static void CreateStartMenuShortcut()
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { Paths.StartMenuShortcut });
                Type linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Paths.InstalledExe });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Paths.InstallDir });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "SEU-WLAN Auto Authentication" });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine(L.T("无法建立开始菜单快捷方式：", "Could not create the Start menu shortcut: ") + ex.Message);
            }
        }

        static void Sc(string arguments)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) throw new InvalidOperationException("sc " + arguments + " failed: " + output.Trim());
            }
        }
    }
}
