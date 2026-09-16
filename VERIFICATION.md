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
