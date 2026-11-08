# SEU-WLAN Auto Authentication

[简体中文](#简体中文) · [English](#english)

## 简体中文

东南大学校园网（SEU-WLAN）自动认证工具：以 Windows 服务在后台运行，认证过期掉线时自动重新登录，不用开浏览器。

> 非官方工具，与东南大学、城市热点 Dr.COM 无关。请遵守学校网络使用规定，风险自负。

- 每 30 秒检查一次，掉线后通常 20 秒内重新登录，开机自启。
- 开着 TUN 也能用。
- 只在连接 `SEU-WLAN*` 时动作，目前仅支持 Wi-Fi 的自动认证。
- 单个约 50 KB 的 exe，支持 Windows 10 1903+ / 11。
- 密码用 Windows DPAPI 加密保存在本机，只会用来提交给证书有效的 `w.seu.edu.cn`。

### 安装与使用

1. 从 [Releases](../../releases) 下载 zip 并解压，双击 `seu-auth.exe`。
   如果出现「Windows 已保护你的电脑」，点「更多信息」→「仍要运行」（程序未做代码签名）
2. 输入 `1`（安装或更新），同意 UAC 提示，输入账号（通常是一卡通号）和密码。

摆完挂机即可。

想查看状态或更新密码时，从开始菜单打开 **SEU-WLAN Auto Authentication**：
- `2` 查看状态
- `3` 立即登录
- `4` 更新账号密码
- `5` 卸载。
升级版本时，用新的 exe 再选一次 `1` 即可。

### 其他

- 设置：`C:\ProgramData\SEUAuth\config.ini`（检查间隔、重试策略、语言 `both`/`zh`/`en`），修改后执行 `sc stop SEUAuth && sc start SEUAuth`。
- 日志：`C:\ProgramData\SEUAuth\logs\`。
- 原理：所有连接都绑定在 Wi-Fi 网卡上，所以能绕过 TUN。接口细节见 [docs/portal-protocol.md](docs/portal-protocol.md)。
- 从源码编译：运行 `build.cmd`，用的是 Windows 自带的编译器。

## English

Auto-login for the Southeast University campus Wi-Fi (SEU-WLAN). Runs in the background as a Windows service and logs back in whenever the campus login expires — no browser needed.

> Unofficial; not affiliated with Southeast University or Dr.COM. Follow the university's network policy; use at your own risk.

- Checks every 30 s, usually logs back in within 20 s, and starts at boot.
- Works with TUN mode on.
- Acts only on `SEU-WLAN*`; currently only Wi-Fi is supported.
- A single ~50 KB exe for Windows 10 1903+ / 11.
- Your password is DPAPI-encrypted on this PC and only ever sent to `w.seu.edu.cn` with a valid certificate.

### Install and use

1. Download the zip from [Releases](../../releases), unzip, and double-click `seu-auth.exe`.
   If "Windows protected your PC" appears, click "More info" → "Run anyway" (the exe isn't code-signed).
2. Type `1` (Install or update), accept the UAC prompt, and enter your account (usually your campus card number) and password.

Then just leave it running.

To check status or change your password, open **SEU-WLAN Auto Authentication** from the Start menu:
- `2` Show status
- `3` Log in now
- `4` Update credentials
- `5` Uninstall

To upgrade, run the new exe and choose `1` again.

### More

- Settings: `C:\ProgramData\SEUAuth\config.ini` (interval, retry policy, language `both`/`zh`/`en`); then run `sc stop SEUAuth && sc start SEUAuth`.
- Logs: `C:\ProgramData\SEUAuth\logs\`.
- How it works: every connection is bound to the Wi-Fi adapter, which bypasses TUN. Protocol details (Chinese) are in [docs/portal-protocol.md](docs/portal-protocol.md).
- Build from source: run `build.cmd`, which uses the compiler that ships with Windows.

[MIT License](LICENSE)
