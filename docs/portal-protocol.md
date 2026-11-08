# SEU-WLAN 认证协议笔记

本文记录 `w.seu.edu.cn`（城市热点 Dr.COM ePortal）的认证接口，供维护或移植到其他平台时参考。
内容来自 2026-10 对认证页脚本的分析，并已在 Windows 上实测登录成功。学校升级认证系统后可能失效。

## 来源

| 文件 | 网址 | 内容 |
|---|---|---|
| 认证页 | `https://w.seu.edu.cn/` | GBK 编码。已登录时显示注销页（`Dr.COMWebLoginID_1.htm`） |
| `a41.js` | `https://w.seu.edu.cn/a41.js` | 页面框架、`page.eportal` 地址、`jsVersion='3.3.3'`、账号前缀规则 |
| `a42.js` | `https://w.seu.edu.cn/a42.js` | 登录、注销、错误码处理（`login_portal`、`logout_portal`） |
| `config.js` | `https://w.seu.edu.cn:802/eportal/extern/SEU1/config.js` | 页面匹配规则，决定 `loginMethod` |

`page.eportal` = `协议://w.seu.edu.cn:端口/eportal/`，HTTP 用 801，HTTPS 用 802（`enHttps=1`，强制 HTTPS）。

`config.js` 中 `pageSetting=2`（按 IP 匹配），`ipPageAry[2]` 覆盖 `0.0.0.0–255.255.255.255`，其 `loginMethod=1`，所以走 `login_portal`（Portal 协议），而不是 `default_login`（`/drcom/login`）。

## 接口

所有接口都是 JSONP：GET 请求，参数里带 `callback=drNNNN`，响应形如 `drNNNN({...})`。
`v` 是防缓存用的随机数（页面用 500–10499）。

### 查询状态

```
GET https://w.seu.edu.cn/drcom/chkstatus?callback=dr1002&v=<随机数>
```

- `"result":1`：在线，响应还包含 `uid`、`v4ip`、`stime`、`etime`、流量等字段。
- `"result":0`：未登录。

### 登录

```
GET https://w.seu.edu.cn:802/eportal/?c=Portal&a=login&callback=dr1003
    &login_method=1
    &user_account=,0,<账号>
    &user_password=<密码，明文>
    &wlan_user_ip=<本机 IPv4>
    &wlan_user_ipv6=
    &wlan_user_mac=000000000000
    &wlan_ac_ip=
    &wlan_ac_name=
    &jsVersion=3.3.3
    &v=<随机数>
```

- 账号前缀：`config.js` 中 `accountPrefix=1`，电脑端加 `,0,`，手机端加 `,1,`（`a42.js` 依 `term.type` 判断）。学校可能分别统计电脑和手机的在线数，移植到手机时应改用 `,1,`。
- 账号后缀：`accountSuffix=""`。认证页头部虽然定义了运营商选项（`@xyw`、`@dx`、`@lt`），但当前页面不显示，实测不需要。
- `enMd5=0`：密码以明文提交（靠 HTTPS 保护）。
- 成功响应：`"result":1`（或 `"ok"`），`msg` 为 `认证成功`。
- 失败响应：`"result":0`，以 `ret_code` 区分原因（见 `a42.js`）：

| ret_code | 含义 |
|---|---|
| 1 | 账号或密码不对 |
| 2 | 该 IP 已经在线 |
| 3 | 系统繁忙 |
| 4 | 未知错误 |
| 5 / 6 | REQ_CHALLENGE 失败 / 超时 |
| 7 / 8 | 认证失败 / 超时 |
| 9 / 10 | 下线失败 / 超时 |
| 11 | 其他错误 |

`msg` 有时是 Base64，例如 `dXNlcmlkIGVycm9yMQ==` 解码后是 `userid error1`。

### 注销（测试用）

```
GET https://w.seu.edu.cn:802/eportal/?c=Portal&a=logout&callback=dr1004
    &login_method=1
    &user_account=drcom
    &user_password=123
    &ac_logout=1
    &register_mode=1
    &wlan_user_ip=<本机 IPv4>
    &wlan_user_ipv6=
    &wlan_vlan_id=1
    &wlan_user_mac=000000000000
    &wlan_ac_ip=
    &wlan_ac_name=
    &jsVersion=3.3.3
    &v=<随机数>
```

`user_account` 和 `user_password` 是页面写死的占位值，服务器按 `wlan_user_ip` 注销。

## 网络环境

- `w.seu.edu.cn` 解析为 `10.80.128.2`，校园 DNS 为 `172.19.2.1`、`172.19.2.2`（以 DHCP 下发的为准）。
- 证书是公开 CA 签发的，可以正常验证。未登录时连不到 CRL/OCSP 服务器，所以不能要求检查证书吊销。
- 代理软件开启 TUN 模式时，即使规则写了 `DIRECT`，经过 TUN 访问认证页也会 TLS 握手失败（原因未查明）。解决办法是把连接绑定到 Wi-Fi 网卡：

| 平台      | 做法                                                    |
| ------- | ----------------------------------------------------- |
| Windows | 绑定 Wi-Fi 的源 IP 即可（strong host 模型），本项目采用此法             |
| Linux   | 需要 `SO_BINDTODEVICE`（weak host 模型下只绑 IP 无效），通常需要 root |
| macOS   | `IP_BOUND_IF`，不需要 root                                |

用 curl 可以快速验证（Linux/macOS 填网卡名，Windows 填 IP）：

```
curl --interface <网卡或IP> "https://w.seu.edu.cn/drcom/chkstatus?callback=dr1002"
```
