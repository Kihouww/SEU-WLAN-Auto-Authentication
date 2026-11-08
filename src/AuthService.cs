// Windows 服务

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
    class AuthService : ServiceBase
    {
        AuthMonitor monitor;
        Thread worker;

        public AuthService()
        {
            ServiceName = Paths.ServiceName;
            CanStop = true;
            CanShutdown = true;
        }

        protected override void OnStart(string[] args)
        {
            monitor = new AuthMonitor();
            worker = new Thread(monitor.Run) { IsBackground = true, Name = "AuthMonitor" };
            worker.Start();
        }

        protected override void OnStop()
        {
            if (monitor != null) monitor.Stop();
            if (worker != null) worker.Join(15000);
        }

        protected override void OnShutdown() { OnStop(); }
    }
}
