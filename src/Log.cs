// 日志 / Log

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
    static class Log
    {
        static readonly object Gate = new object();
        public static bool ToConsole;
        public static bool ToFile = true;

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }
        public static void Error(string msg) { Write("ERROR", msg); }

        static void Write(string level, string msg)
        {
            DateTime now = DateTime.Now;
            string line = now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + msg;
            lock (Gate)
            {
                if (ToConsole) Console.WriteLine(line);
                if (!ToFile) return;
                try
                {
                    Directory.CreateDirectory(Paths.LogDir);
                    File.AppendAllText(Path.Combine(Paths.LogDir, now.ToString("yyyy-MM-dd") + ".log"), line + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }

        public static void Purge(int retentionDays)
        {
            try
            {
                foreach (string f in Directory.GetFiles(Paths.LogDir, "*.log"))
                    if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-retentionDays)) File.Delete(f);
            }
            catch { }
        }

        public static string[] Tail(int count)
        {
            if (!Directory.Exists(Paths.LogDir)) return new string[0];
            try
            {
                string latest = Directory.GetFiles(Paths.LogDir, "*.log").OrderBy(f => f).LastOrDefault();
                if (latest == null) return new string[0];
                string[] lines = File.ReadAllLines(latest, Encoding.UTF8);
                return lines.Skip(Math.Max(0, lines.Length - count)).ToArray();
            }
            catch { return null; }
        }
    }
}
