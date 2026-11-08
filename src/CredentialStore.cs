// 账号密码 / Credentials

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
    // 以 DPAPI（本机范围）加密，文件权限只开放给 SYSTEM 和 Administrators。
    // 不用凭据管理器，是因为服务以 SYSTEM 身份在 Windows 登录前运行，读不到用户的凭据。
    static class CredentialStore
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SEUAuth.credential.v1");

        public static bool Exists { get { return File.Exists(Paths.CredentialFile); } }

        public static void Save(string account, string password)
        {
            Directory.CreateDirectory(Paths.DataDir);
            byte[] plain = Encoding.UTF8.GetBytes(account + "\n" + password);
            byte[] blob = ProtectedData.Protect(plain, Entropy, DataProtectionScope.LocalMachine);
            Array.Clear(plain, 0, plain.Length);
            File.WriteAllBytes(Paths.CredentialFile, blob);

            var sec = new FileSecurity();
            sec.SetAccessRuleProtection(true, false);
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            File.SetAccessControl(Paths.CredentialFile, sec);
        }

        public static bool TryLoad(out string account, out string password, out string error)
        {
            account = password = error = null;
            try
            {
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(Paths.CredentialFile), Entropy, DataProtectionScope.LocalMachine);
                string text = Encoding.UTF8.GetString(plain);
                Array.Clear(plain, 0, plain.Length);
                int nl = text.IndexOf('\n');
                if (nl <= 0) { error = L.T("账号密码文件格式错误", "Credential file is malformed"); return false; }
                account = text.Substring(0, nl);
                password = text.Substring(nl + 1);
                return true;
            }
            catch (FileNotFoundException) { error = L.T("尚未保存账号密码（请先安装）", "No credentials saved yet (run setup first)"); }
            catch (UnauthorizedAccessException) { error = L.T("没有权限读取账号密码（需要管理员权限）", "No permission to read credentials (administrator required)"); }
            catch (Exception ex) { error = L.T("无法读取账号密码：", "Cannot read credentials: ") + ex.Message; }
            return false;
        }
    }
}
