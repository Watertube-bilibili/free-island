# v1.0.8 · 2026-09-24 验证

- WPF 使用 .NET Framework 4.6 参考程序集编译；核心 25 组、提醒音频 33 项、模型下载/校验/解析/取消/安全模式 80 项、双场景助手 UI 28 项通过。UI 夹具验证默认关闭、设置持久化、三档模型、三类音频、音量滑块、已有倒计时保护、明确点击才开始、正式提醒优先以及不创建关机计划。
- 真实模型验证 6 项通过：在隔离的 artifacts 中下载并校验官方 llama.cpp b11146 CPU x64 与 Qwen3 0.6B；实际启动，使用合成的 vlc 程序名输入，返回合法 volume 操作；无密钥推理请求拒绝，停止后不再运行模型。未读取真实应用内容或执行真实媒体/音量操作。证据为 artifacts/ai-runtime-probe/probe-result.txt。1.7B/4B 未下载实测。
- 下载来源、固定哈希、路径边界、解压白名单、代理关闭的本机推理、服务密钥和声明式动作限制经过独立只读审查。修复下载/启动期间关闭助手的竞态、过期建议、模型按钮标题与实际动作不一致、声音关闭/试听生命周期、音量拖动频繁保存。标准构建增加音频和 AI 离线测试，不会下载或启动真实模型。
- 两种场景界面统一检查，窄教室窗口导航改为简短标签后完成一次确认。截图位于 artifacts/assistant-ui-1.0.8-final。课堂按钮保留 54 DIP 高度，灵动岛音量滑块至少 44 DIP；未在真实教室触屏硬件测试。
- 原生版核心 20 组、更新 48 项、场景快捷 25 项通过；最终生产安全 UI 验证两种场景的音频设置及实际滑块拖动，未改变系统音量。原生音频模块严格编译通过，独立测试执行被 Windows 拒绝访问，不能声明其运行通过。
- 本机再次执行 WPF 旧 UpdateTests.exe 以及覆盖原生旧 CoreBehaviorTests.exe 时访问被拒绝。没有改安全软件、ACL 或重命名规避。最终打包使用 -SkipTests，保留本轮已经通过的独立验证结果；不把旧版更新测试当成本轮 WPF 通过项。安装包内嵌文件哈希校验通过。
- 与最终 WPF 字节一致的隔离副本通过 --smoke-test，覆盖两场景 12 个原有页面、全屏、任务交互、提醒、模拟关机与拖动；结果位于 artifacts/production-smoke-1.0.8/smoke-artifacts/result.txt。新助手页由单独 UI 夹具覆盖。
- 最终 WPF 应用 SHA-256：C570922E76765D9D4090798A8CC9554EDB8EBD3A81E8363F61ED8243B2B51A80。原生应用：DE1777974EBCA7EB41983338B19364A0E83659433AC2C166EC713DE5463C2240。
- 可选模型保守适配 Win10 2004+ x64 / AVX2，当前开发电脑最小模型实测不能代替所有硬件验证。基础 Win10 1507/17xx、Win7 和实际教室硬件兼容性仍需目标机器验证。未执行实际关机、安装、卸载、自启动修改或安全软件调整。模型及运行时只在忽略的测试目录中，不随源代码或安装包发布。

# v1.0.7 · 2026-09-17 验证

- WPF 自动更新 85 项注入测试通过：正式版本筛选、对应安装包、严格 URL 和版本检查、大小与 SHA-256、损坏/取消下载、同版缓存复用、撤回版本清理、便携/安全模式、运行或暂停任务及关机计划阻止安装、可见浮窗阻止安装、管理员进程排除、失败版本停止自动重试。测试不连接真实网络、不执行安装器，见 `artifacts/update-tests-1.0.7-recurring/result.txt`。
- WPF 安装器新增 55 项自动升级检查、42 组路径检查、25 项升级回归通过。覆盖安装归属、版本、链接文件拒绝、原偏好保留、替换回滚、结果文件与经过哈希验证的旧版恢复。只使用隔离文件、注册/启动适配器及测试子进程；未执行真实安装、提权或修改正式注册表。
- WPF 核心 25 组、触屏时间控件 44 项通过。生产打包时通用路径 `artifacts/CoreBehaviorTests.exe` 被本机拒绝执行，随后该文件不存在，原因尚未确认；独立编译的同组核心测试已通过。最终使用 `build.ps1 -SkipTests` 复用已完成的测试结果打包，不更改系统保护设置。新增更新测试步骤已纳入普通构建脚本。
- 最终 WPF 生产程序的 SHA-256 为 `BE3D1D61CA721CEF5778974CDC501225AD3EAB0071793DFD505B22034265C980`。字节一致的隔离副本完成 `--smoke-test`，退出码 0；覆盖两种场景共 12 个控制页面、全屏、计时暂停继续、提醒、模拟关机、灵动岛方向和拖动。安装包内嵌文件 SHA-256 校验通过。证据位于 `artifacts/production-smoke-1.0.7-recurring`。
- 46 个旧发行/源码文件（14,591,251 字节）已提交至 GitHub `main/backup`，在不可变提交 `2a37e3644620e78c4bd8ff869ab19edbe69612cd` 中逐项核对原始 Git blob 哈希和大小，全部一致；`backup/manifest.json` 另保存各文件 SHA-256。只在远端验证完成后清理对应本地旧包。
- WPF 多任务界面 810 项检查通过，覆盖 40/64/88/160 px、两场景与三档材质、控件和拖动、隐藏停止采样及结束后恢复小点。统一检查任务球与更新设置截图，未发现新增控件裁切。首轮夹具提前结束且原因未定，未计为通过；无源码改动的隔离重跑退出码 0，证据为 `artifacts/multitask-ui-1.0.7-final/result.txt`。
- Win7 更新器 48 项、核心 20 组、安装路径/归属 7 组及事务/进程退出 5 组通过。更新器使用固定网络数据及禁止启动的测试构建；安装器覆盖原属账户、注册目录、回滚及恢复前原文件哈希检查。原生应用与安装器 PE32、子系统 6.1、无 CLR/外部 VC 运行库及安装载荷 SHA-256 检查通过；新增依赖为 Windows 自带 WinHTTP 和版本信息接口。

- 最终原生生产程序的字节一致隔离副本完成 `--recurring-shutdown-test` 和 `--multitask-test`，均退出码 0；确认 40/160 px 端点、64/88 默认值、暂停任务、卡片控制、最后 10 秒关机预警，以及关机远期等待 2,200 ms 内 0 次玻璃重画。两场景更新界面及任务缩略球统一检查通过；证据位于 `artifacts/production-native-1.0.7-recurring`。
- 循环关机新增星期和时间持久化、严格未来日期、跨周、夏令时空缺/重复时间及最终预警边界测试。WPF 68 项安全界面检查、14 项循环计划与更新退出/重启联动检查通过；原生安全 UI 检查覆盖两场景、每天/工作日/自选星期、未确认不启用、时间触摸选择、恢复与停用，以及距关机 5 分钟的更新边界。证据为 `artifacts/recurring-shutdown-ui-1.0.7-final`、`artifacts/recurring-update-integration`、`artifacts/native-recurring`。
- 本轮不执行真实自动安装、卸载、关机或自启动修改；更新网络与进程测试使用注入数据，真实 GitHub 发布与资产另行校验。未在实体 Windows 7、Windows 10 1507 或学校触摸大屏上验证。

# v1.0.6 · 2026-09-17 验证

- WPF 应用、安装器、卸载器使用微软 .NET Framework 4.6 参考程序集严格编译；核心行为测试 22 组通过。新增覆盖暂停零秒正计时、任务稳定顺序、倒计时原始时长持久化/旧数据迁移、任务球大小设置，以及关机在 10.001 秒不显示、10 秒进入预警的边界。
- WPF 多任务界面测试通过 810 项检查，覆盖电脑/教室 × 关闭/轻量/水滴六组组合：同时展开两个任务、独立暂停/继续/结束、提醒与任务共存、关机取消通知不丢失、30/36/48/50 物理像素尺寸、顶部/两侧停靠、教室至少 44 DIP 触控区域、全部结束后缩回小点，以及隐藏后停止玻璃更新。证据位于 `artifacts/multitask-ui-1.0.6`；使用自有固定背景与注入时钟，当前系统缩放 100%，未修改系统缩放。
- WPF 触屏输入测试通过 44 项检查：滑块与加减按钮、预设只选择而不启动、零时长和 7 天上限、日期/时间选择、日程快捷内容、预约后取消、任务球大小，以及电脑模式文本框保留。证据位于 `artifacts/touch-time-controls-1.0.6`。电脑和教室界面截图已集中检查，未发现需要追加修改的裁切或重叠问题；较小窗口通过滚动访问更多选时控件，开始/预约按钮保留在底部。
- WPF 安装包内嵌文件 SHA-256 校验通过。最终生产程序 1.0.6.0 的 SHA-256 为 `995327642366259c46424408c3843ee7a63fef919c6ba467d0bd2bc726f31ca8`；字节一致的隔离副本执行 `--smoke-test` 退出码 0，覆盖两场景六个页面、实际计时按钮、日程、模拟关机预约/取消、全屏展示及拖动停靠。证据位于 `artifacts/production-smoke-1.0.6`。
- 原生版核心行为测试 18 组通过，新增覆盖任务球场景尺寸、设置迁移、倒计时环的暂停和持久化。原生多任务安全检查覆盖独立任务卡片、暂停计数、缩略图点击、到期提醒、30/50px 端点与 36/48px 默认尺寸；70 秒后的关机预约在 2,200ms 内没有玻璃重绘，最后 10 秒展开并能取消。证据位于 `artifacts/native-multitask`。
- 原生版完整安全界面回归通过，覆盖原有 12 个页面、材质调节与静止重绘检查；新版滑块在一次集中修正后使用 32px 可见滑块和整条轨道的拖动/点按区域，最终电脑/教室截图已检查。发行程序与安装器均通过 x86 PE32、子系统 6.1、无 CLR 和系统 DLL 导入检查，安装器内嵌应用 SHA-256 校验通过；发行程序的隔离副本 `--multitask-test` 退出码 0，证据位于 `artifacts/production-native-1.0.6-final`。
- 所有检查在当前开发电脑和隔离测试数据上完成，未执行真实关机、安装/卸载、自启动修改或安全软件操作；没有实体 Win7、Windows 10 1507/17xx 或学校触摸大屏可供实测。背景采样算法沿用 v1.0.5，本轮没有重复运行未改动的折射数学或旧系统采样专项测试；历史结果保留如下。

# v1.0.5 · 2026-09-16 验证

- 应用、安装器和卸载器使用微软 .NET Framework 4.6 参考程序集，开启 `/noconfig /nostdlib+` 严格编译；App.config、项目目标与安装器最低运行检查一致。参考包为 `Microsoft.NETFramework.ReferenceAssemblies.net46` 1.0.3，下载 SHA-256 为 `DCBB79BB3868DBFBB64C643116FAA63D888EE9ECCBA0C4E965E9992ED7C4E35D`。这验证了托管 API 依赖范围，不能代替实体旧系统测试。
- 早期 Win10 路径在当前 Windows 强制启用，使用独立进程的自有绘图窗口验证，最终通过 80 项断言：实际背景像素、坐标与遮挡顺序、排除本程序窗口、同窗 24 次交错区域请求、两浮窗公平取帧、缓存复用及上限、黑帧和未绘制内容回退、750 ms 模拟源窗口卡顿、隐藏/重显/停用后的清理。结果位于 `artifacts/legacy-backdrop-1.0.5`。
- 兼容采样不会反复隐藏浮窗、修改透明度或设置捕获排除。一个后台线程处理有上限的合并请求；同窗按钮共用完整浮窗背景帧（上限 4MP）。下层源窗口缓存最多 2 个，总像素不超过 4096×2160，150 ms 内复用；成功帧最多保留 400 ms。无法安全强行终止外部窗口的同步绘制：超时后 UI 回退清透，工作线程和缓存数量保持有界，源窗口返回后清理。
- 设置 UI 在 .NET 4.6 参考编译下通过 70 项检查，覆盖电脑/教室场景、44/54 DIP 滑杆目标、实时材质更新、500 ms 合并保存、重启持久化、模式禁用、恢复推荐及关闭前保存。4 张设置截图统一检查，参数行无裁切。核心行为测试 18 组通过，含旧配置迁移及 0–100 越界校正；证据为 `artifacts/glass-settings-ui` 与 `artifacts/glass-settings-core-result.txt`。
- 最终生产 WPF 1.0.5.0 的 SHA-256 为 `2412800B11A089F70E2756E057D78F64588CE411E732C01F3002F4F898DE876A`。字节一致的隔离副本完成 `--smoke-test`，退出码 0，生成 39 张界面图，输出在 `artifacts/production-smoke-1.0.5-final`。最终安装器内嵌文件与生成程序逐个 SHA-256 一致。
- 光学计算 13 组通过，新增折射位移、透明度端点、高光独立性、缓存失效与非有限参数拒绝。小水滴夹具通过 1,561 项断言，主体材质通过 144 项断言，覆盖两种场景、深浅背景、三档模式、3/6/20 px 小点和隐藏停止更新。使用固定自有图案，非用户桌面；这些结果不代表学校大屏的实际帧率。
- 原生版 17 组核心测试及最终独立安全 UI 检查通过：三项外观参数改变实际材质像素，文字位置及衬底保持稳定；两种场景的新页面已检查。2,200 ms 静止主窗/按钮均 0 次重绘，轻量/水滴材质空闲重绘为 0。原生应用和安装包 PE32、子系统 6.1、无 CLR/外部 VC 运行库检查及内嵌应用 SHA-256 校验通过。证据位于 `artifacts/win7-native-glass`。
- 全部验证均在当前开发机器完成。未在实体 Windows 10 1507、17xx、Win7 或学校触摸大屏上测试；未执行实际安装、卸载、开机启动修改或关机，未关闭安全软件。部分加速、受保护或透明窗口不能提供兼容采样像素，会显示清透材质。

# v1.0.4 · 2026-09-16 验证

- 收起小点玻璃夹具通过 1,561 项断言、12 组组合：电脑/教室场景，3/6/20 物理像素，深浅固定自有背景。覆盖三档连续切换、顶部/左侧/右侧位置、24/44 DIP 透明触控区域、最小材质非空、隐藏和关闭停止更新、重显恢复折射。此检查于主体图标的最后可读性修正之前完成；该修正未改动小点实现，未重复运行小点夹具。
- 最终主体材质夹具通过 144 项断言，生成电脑/教室 × 深浅背景 × 水滴/轻量共 8 组对照。最终确认包含标题与详情局部衬底、左侧图标局部衬底和图标载入后的颜色刷新；输出在 `artifacts/water-visuals-1.0.4-final`。这些固定图案渲染不采样用户桌面，也不证明真实设备帧率。
- 最终生产 WPF 1.0.4.0 程序的 SHA-256 为 `66f42c474af2c94939667884d7f70610287e2faa240820ef14d8842ca843a6f9`。使用字节一致的隔离副本执行 `--smoke-test`，退出码 0，输出 39 张界面图；结果在 `artifacts/production-smoke-1.0.4-final`。生产功能/布局检查与上述材质断言独立。
- 本轮 WaterLens 数学测试 12/12 组通过，覆盖固定棋盘像素几何位移、中心内容、抗锯齿、预乘 BGRA、参数边界与弹簧收敛；日志为 `artifacts/water-lens-tests-1.0.4.log`。660×153 的形变加折射本机测得约 5.53 ms/帧，仅指纯数学计算，不包含桌面采样或 WPF 上传。
- Win7 原生安全 smoke 通过：12 个控制页面、两场景三档材质、3/6/20 px 小点在三方向的位置和触控范围、深浅背景可见性、立即切换与静止零重画。稳定性检查记录 2,200 ms 静止时主窗/按钮均 0 次重画；交互中只改变材质，文字原位，250 ms 回弹结束，拖动形变不超过 4%，轻量无形变。证据位于 `artifacts/win7-native/smoke-artifacts`。
- 以上均为当前 Windows 开发环境中的安全验证，未执行真实安装、卸载、UAC、开机启动修改或关机。没有实体 Windows 7 设备或教室触摸大屏验证，不据此保证所有显卡、触摸驱动及录屏软件兼容。

# v1.0.3 水滴验证补充

- WaterLens 12/12 组通过：真实棋盘像素位置变化、中心颜色、抗锯齿、预乘 BGRA、溢出及非有限输入、弹簧收敛。
- DesktopBackdrop 17 项自有窗口检查通过：排除自身后与原背景逐字节一致，禁用/隐藏/重显正确恢复捕获状态。
- WPF 144 项材质/隐藏生命周期检查通过，8组桌面/教室 × 深浅背景 × 水滴/轻量自有图案输出完成。静态图不证明实际帧率。
- 生产 v1.0.3 WPF 程序完成 `--smoke-test`，退出码 0，输出 39 张界面图。此流程验证实际发布程序的两种场景、功能与布局；它与上面的 144 项光学/生命周期断言独立，不能互相替代。
- Win7 安全 smoke 通过：静止重绘0，按压文字原位，250ms形变回弹，最大4%拖动形变，轻量无动态重绘。
- Windows开发环境验证，不等于实体Win7教学大屏或所有录屏软件兼容验证。未执行真实安装、卸载、UAC操作或关机。
# Verification · 2026-09-11

## v1.0.2 installer update

- WPF custom-directory and ownership checks: 42 isolated checks passed, including Chinese/space paths, valid upgrades, nonempty unrelated folders, reserved names, system roots, real NTFS junctions, and elevation arguments round-tripped through `CommandLineToArgvW`.
- WPF uninstall argument and account checks: 24 passed. This harness never called the removal routine or elevation UI. Default uninstall preserves data; the prompt displays the selected directory.
- WPF file replacement/deletion regression: 25 checks passed with the final Common source. A fixture with an ordinary file denying only `WRITE_ATTRIBUTES` reproduces the old failure; replacement and deletion now succeed without requesting that right. Checks also cover read-only restoration on failure, partial rollback, transient locks and actual isolated-process teardown.
- Native custom-directory tests: six scenario groups passed, including existing-install markers, unrelated content, links/hard links, shortcut ownership, and uninstall handoff origin. The original native upgrade harness compiled but this run was denied execution; its previous v1.0.1 results below are historical, not a fresh v1.0.2 pass.
- The WPF setup compiled and passed embedded payload SHA-256 verification. Default/custom-directory UI previews were inspected. No real installation, uninstall, administrator prompt, startup mutation, real data removal or shutdown was performed.
- Final native v1.0.2 application/setup builds passed PE32 subsystem 6.1 and no-CLR/external-VC-runtime checks; embedded application SHA-256 matched. All 16 native core groups passed again. Native compiler reports existing macro-redefinition and compressed-statement indentation warnings.
- On the reported PC, the existing v1.0.0 EXE has an ordinary file attribute and current-user FullControl, yet read-only handle diagnostics returned Windows error 5 for write-attributes and delete access. Restart Manager reported no holders. There is no confirmed security-product attribution; the code fix does not prove that this separate deletion restriction has been removed. The installer now offers a user-selected administrator retry and a different directory.

## Earlier v1.0.1 verification

Release v1.0.1 adds switchable glass material, a 0–100% dot slider (default 20%, 6 physical pixels), and transactional installer upgrades. It retains the six independent radial buttons, expanded-island scaling and the native painting correction.

- Both core settings implementations compile with the new percentage and material fields. All 16 native core test groups passed, including migration, normalization and mode persistence. The application and installer remain x86 subsystem 6.1, with no CLR or external VC/UCRT DLL imports.
- Both editions: the release EXE completed --smoke-test with exit 0. The real settings controls apply valid dimensions, reject invalid dimensions, and restore defaults. Stopwatch/countdown/reminder/safe shutdown, docking and ball edge tests also completed. No actual shutdown or startup changes were made.
- Win7: over a 2,200 ms idle interval, the real window received zero paints and native buttons received zero owner-draw events. Over a 2,200 ms running-countdown interval, the window painted twice and child buttons zero times. This checks continuous redraw regression, not all possible driver/compositor defects.
- Captures cover both desktop and classroom menus/settings/default dots and the maximum custom dot/island. Physical default dot diameter and upper-bound size were inspected from rendered pixels.
- Installer regression checks passed: 13 WPF assertions and five native scenario groups, including an isolated running EXE, process teardown after early mutex release, transient/persistent file locks, read-only files, staging failure and rollback/data preservation. Native checks also reject hard-linked destinations. Production installation and registry writes were not exercised.
- The standalone WPF core test executable could not be launched: Windows reported the generated artifacts/CoreBehaviorTests.exe missing. This also occurred after excluding the real shutdown adapter from that test build. No claim is made that the updated standalone WPF core suite passed; all production WPF UI checks passed separately, including all three glass selectors, percentage application, invalid scaling rejection and reset. No security settings were changed.
- Windows 7 hardware, the reported teaching display and real touch drivers are unavailable here. Local verification cannot guarantee absence of flicker on those devices. No flashing demonstration was generated.

Native child clipping follows [Microsoft's child update-region documentation](https://learn.microsoft.com/en-us/windows/win32/gdi/child-window-update-region). The larger, nearly transparent input area follows [layered-window hit testing](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features).

WPF v1.0.1: production app, uninstaller and setup compiled successfully; embedded payload SHA-256 verification passed. The release EXE completed --smoke-test with exit 0. A bounded visual review covered both settings scenes, Lite/Standard radial menus and the Standard island. Text remains readable, touch targets remain distinct, and no radial backing disk was reintroduced. Lite/Off use no DropShadowEffect; Standard light only updates on pointer/touch interaction, with no new idle animation timer.

Glass is a vector optical approximation, without desktop capture or real background refraction. Native Standard may add the documented [Windows 7 Aero blur API](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmenableblurbehindwindow) when composition is available. The API is not used to claim desktop blur on Windows 8+, where Microsoft documents that effect as unavailable. Real Win7 Aero behavior remains unverified on hardware.

Native v1.0.1: complete build and embedded-payload SHA-256 verification passed. Release --smoke-test exited 0, including slider/edit draft synchronization, mode changes without hiding/showing windows, both scenes and all three material modes. Lite and Standard idle overlay paint counts were both zero during the additional stability check. Build evidence is generated at artifacts/win7-native/build-glass-1.0.1.log and smoke-artifacts/paint-stability.txt (not included in source archives).
