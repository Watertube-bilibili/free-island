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
