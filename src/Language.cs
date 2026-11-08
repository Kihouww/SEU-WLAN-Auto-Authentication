// 语言 / Language

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
    static class L
    {
        // both：中英并列（默认）；zh：只显示中文；en：只显示英文
        public static string Mode = "both";

        public static string T(string zh, string en)
        {
            if (Mode == "zh") return zh;
            if (Mode == "en") return en;
            return zh + " / " + en;
        }

        // 多行文字：both 模式下中文段落在前，英文段落在后
        public static string Block(string zh, string en)
        {
            if (Mode == "zh") return zh;
            if (Mode == "en") return en;
            return zh + Environment.NewLine + Environment.NewLine + en;
        }
    }
}
