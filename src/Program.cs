// 程序入口：服务模式或命令行 / Entry point: service mode or CLI

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
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

        static readonly string[] AdminCommands = { "setup", "set-credential", "login", "run", "uninstall" };

        static int Main(string[] args)
        {
            if (args.Length == 0 && !Environment.UserInteractive)
            {
                ServiceBase.Run(new AuthService());
                return 0;
            }

            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            Log.ToConsole = true;
            Config.Load();

            int code;
            try
            {
                if (args.Length == 0)
                {
                    code = Commands.Menu();
                }
                else
                {
                    string cmd = args[0].ToLowerInvariant();
                    if (AdminCommands.Contains(cmd) && !Commands.IsAdmin())
                        code = Commands.RunElevated(args);
                    else
                        code = Dispatch(cmd, args);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(L.T("错误：", "Error: ") + ex.Message);
                code = 1;
            }

            // UAC 提权后开的新窗口（或双击直接执行命令）：结束前停住，否则窗口会直接消失。
            // 菜单本身是用户主动选择「退出」，不需要再停。
            if (args.Length > 0 && OwnsConsole())
            {
                Console.WriteLine();
                Console.Write(L.T("按回车键关闭窗口", "Press Enter to close"));
                Console.ReadLine();
            }
            return code;
        }

        public static int Dispatch(string cmd, string[] args)
        {
            switch (cmd)
            {
                case "setup": return Commands.Setup();
                case "set-credential": return Commands.SetCredential();
                case "status": return Commands.Status();
                case "login": return Commands.Login(args.Contains("--force"));
                case "logout": return Commands.Logout();
                case "run": return Commands.RunForeground();
                case "uninstall": return Commands.Uninstall();
                case "help":
                case "--help":
                case "-h":
                case "/?":
                    Commands.Help();
                    return 0;
                default:
                    Console.WriteLine(L.T("未知命令：", "Unknown command: ") + cmd);
                    Commands.Help();
                    return 1;
            }
        }

        static bool OwnsConsole()
        {
            try { return GetConsoleProcessList(new uint[2], 2) <= 1; }
            catch { return false; }
        }
    }
}
