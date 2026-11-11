# 雄兴屏拓 · V1.0-beta

由 **[KacaleSSS](https://github.com/KacaleSSS)** 发布 · ZZXX CloudTech

让 Windows、Mac 与 iPhone / iPad 共享键鼠操作。纯软件方案，移动端通过 Windows 蓝牙 HID 接力，桌面端通过局域网连接。

## 下载
从 [V1.0-beta Releases](https://github.com/KacaleSSS/xiongxing-pingtuo/releases/tag/v1.0-beta) 下载：
- 雄兴屏拓.exe：Windows x64 单文件便携版，内置桌面发现和输入引擎。
- 雄兴屏拓.dmg：macOS Intel / Apple Silicon 通用应用包，未经过 Apple 公证。
- SHA256SUMS.txt：下载校验。
- xiongxing-pingtuo-v1.0-beta-source.zip：发布源码、构建脚本与引擎对应源码。

## 功能范围
- Windows ↔ Mac、Windows ↔ Windows 局域网设备发现、确认配对和键鼠切换。
- Windows 蓝牙鼠标／键盘转发到 iPhone、iPad；F8 返回 Windows。
- 屏幕布局拖动、吸附、接缝插入与贴合边缘切换；Windows 本机屏和外接屏保留相邻通道。
- 分别绑定手机和平板；根据布局在移动设备间切换。
- Mac 键鼠经 Windows 接力到移动设备；Windows 必须持续运行。
- Windows 文件共享入口，供 iPhone「文件」App 通过 SMB 查看、新增、复制和删除文件。

详细步骤见 [使用说明](使用说明.md)。

## Beta 边界与验证情况
- 用户已确认 Windows → iPhone 基础跨屏操作及 SMB 文件夹查看、新增、删除可用。
- 本次发布对 Windows 包执行了 33 项自检，全部通过；不是所有硬件、Mac / iPad 组合的验收。
- 手机边缘返回依赖相对位移估算，灵敏度、DPI 和横竖屏改变后可能需要重新校准。
- Windows 蓝牙适配器和驱动需要支持本应用使用的 BLE 外设功能。
- Mac 包未签名／未公证，系统可能阻止打开；本次未在 Mac 重新编译或验收。输入监控、辅助功能和本地网络权限需按系统提示设置。
- **不包含跨设备文件直接拖拽**。SMB 文件共享不等于把拖拽会话跨屏接续。
- iPhone 原生媒体应用仍处于未编译验证的源码阶段，不包含在本次发布中。
- 这是首个对外版本，发布标签为 v1.0-beta；为保留已使用的二进制，Windows 内部版本仍为 6.1.1.0，Mac 内部版本仍为 6.0。

## 文件共享注意事项
共享使用当前 Windows 账户和密码，而不是 Windows Hello PIN。仅对所选网卡及当前 IPv4 子网开放。
退出应用不会关闭已创建的共享，请在应用中选择「关闭共享」。
曾在非 ASCII 计算机名环境遇到 iPhone SMB 认证失败；改用 ASCII 计算机名后该用户实测恢复，此为已观察案例，不代表所有中文名称都会失败。
若代理 TUN 干扰局域网，可按自己的网段设置绕过；本应用不自动修改第三方代理。

## 源码与构建
Windows 使用 .NET Framework、Windows SDK，在 Windows PowerShell 执行 build.ps1。
运行「雄兴屏拓.exe --check」执行自检。自检可能临时创建蓝牙广播和注册 F8，避免在正在使用跨屏会话时执行。
桌面键鼠互通/发现 是 Go 配对服务和原生 Mac UI 源码；Mac UI 构建需要 macOS 开发工具。
桌面键鼠互通/engine/lan-mouse-engine-source.zip 提供随包引擎对应源码，内含 Cargo.lock 与上游构建说明。
构建脚本使用仓库内随附的 helper / engine 二进制，以便重建 Windows 外壳；不声称当前仓库具备一键重建所有平台 DMG 的流程。

## 开源与致谢
桌面输入引擎基于 [Lan Mouse](https://github.com/feschber/lan-mouse)，保留原作者与许可证。
本项目以 GPL-3.0 发布；第三方组件遵守各自许可证，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
ZZXX CloudTech 名称及标志不因代码许可证而授予商标授权。
