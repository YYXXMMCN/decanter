# 醒酒器 1.0 安装器 · 构建与沙箱自测记录

更新时间：2026-10-29。真机路径（D:\、开始菜单、HKCU）**未实测**——本工作区禁止真实系统写入，全部用 `--sandbox` 等价验证（见「已知限制」）。

## 1. 产物

| 文件 | 说明 |
| --- | --- |
| `run\installer\Decanter-Setup-1.0.exe` | 单文件安装器（1.45 MB）。内嵌 `payload.zip`（Decanter.exe / config / sqlite3.dll / Uninstall.exe / README.txt），运行时解到目标目录 |
| `run\installer\Uninstall.exe` | 卸载器（54 KB，构建中间产物，也被打进 payload.zip） |
| `run\installer\install.png` | 安装界面截图（640×420） |
| `run\installer\install-success.png` | 安装成功屏 |
| `run\installer\install-error.png` | 安装错误屏（三段式） |
| `run\installer\uninstall.png` | 卸载三选屏（默认「保留数据」） |
| `run\installer\uninstall-confirm.png` | 删除数据二次确认屏（「返回」为默认按钮） |
| `run\installer\uninstall-done.png` | 卸载完成屏（逐项 ✓/✗） |
| `setup\Setup.cs` | 安装器：参数解析、安装核心、安装/成功/错误三屏 |
| `setup\Uninstall.cs` | 卸载器：三选/二次确认/逐项结果/自删除 |
| `setup\SetupUi.cs` | 共用：暖黑账本配色与字号、自绘无边框窗框、直角按钮/勾选/单选框、错误三段式、沙箱与真机落点、自删除、截图 |
| `setup\asInvoker.manifest` | `requestedExecutionLevel level="asInvoker"`，不弹 UAC |
| `setup\payload-README.txt` | 装到安装目录的 README.txt 源文件 |
| `setup\build-installer.ps1` | 可重复执行的构建脚本 |
| `setup\build\` | 构建中间目录（payload/、payload.zip、Uninstall.exe；含排障时留下的小 probe，**不会被打进 payload**） |

## 2. 构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File setup\build-installer.ps1
```

脚本用系统 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，两个 exe 都是
`/target:winexe /platform:x64 /optimize+ /win32manifest:setup\asInvoker.manifest /win32icon:work\App\app.ico`，
引用 `System.dll System.Core.dll System.Drawing.dll System.Windows.Forms.dll System.IO.Compression.dll System.IO.Compression.FileSystem.dll`。

构建顺序（脚本内固定）：
1. 编译 `Uninstall.exe`（SetupUi.cs + Uninstall.cs）；
2. 建 `setup\build\payload\`，复制当前 `run\Decanter.exe`、`run\Decanter.exe.config`、`run\sqlite3.dll`、刚编译的 Uninstall.exe、README.txt；
3. `Compress-Archive` 成 `payload.zip`（固定这 5 个文件，重复构建不叠旧内容）；
4. 编译 Setup.exe 并 `/resource:payload.zip,payload.zip`，输出 `run\installer\Decanter-Setup-1.0.exe`。

重复执行直接覆盖产物；payload 用当前 `run\Decanter.exe`（应用重新构建后重跑本脚本即可）。

## 3. 安装器行为

- 单屏 640×420，暖黑账本（底 `#15110D`、卡 `#1D1813`、墨 `#EFE6D4`、黄铜 `#D89A4E`、危险 `#E4795F`），全直角、无阴影/渐变/纹理，自绘无边框、可拖动、×关闭、居中、无控制台。
- 文案：标题「醒酒器 1.0」；「一步装好。不用选路径，不用管理员权限。」；显示实际安装位置与实际数据位置（`data\`）；「开机自启（默认关）」勾选框；主按钮「安装」、次按钮「取消」；底部进度文字。
- 目标目录：真机默认 `D:\醒酒器`；D 盘不存在/不可写回退 `%LOCALAPPDATA%\Programs\Decanter` 并在界面显示实际路径（没有路径选择器）。
- 安装：解压 payload；开始菜单 `%APPDATA%\Microsoft\Windows\Start Menu\Programs\醒酒器\` 下两个 .lnk；HKCU `...\Uninstall\Decanter`（DisplayName/DisplayVersion/Publisher/InstallLocation/DisplayIcon/UninstallString/EstimatedSize/NoModify/NoRepair）；勾选时写 HKCU `...\Run\Decanter`，不勾则删除旧值；不建桌面快捷方式。
- 错误（无法建目录/磁盘满/程序在运行等）：错误屏三段式「发生了什么 / 可能原因 / 怎么办」+「重试」+「取消」，不显示错误码；程序在运行时不杀进程。
- 成功屏：「打开醒酒器」/「完成」，不自动启动。退出码 0 成功、1 失败/取消。

## 4. 卸载器行为

- 560×460 单文件，读自身目录为安装目录（目录里有 `sandbox.txt` 时按沙箱走）。
- 三选（自绘直角 radio，默认第 1 项）：保留数据（默认）/ 导出数据（默认 `桌面\醒酒器数据导出-yyyyMMdd-HHmm`，旁边「选择…」打开 FolderBrowserDialog）/ 删除数据（危险色，选中后「卸载」先弹二次确认，「仍要删除」+「返回」，Esc=返回）。
- 逐项执行并在完成屏逐项 ✓/✗：检查运行中的醒酒器（在运行则三段式错误 + 「重试」，不杀进程）→ 开始菜单 → 卸载登记 → 开机自启 → 数据（保留/导出/删除）→ 程序文件 → 卸载程序自身 → 安装目录。
- 自删除：先把正在运行的 Uninstall.exe 改名移出原路径（Windows 允许重命名运行中的 exe，不允许删除），原路径立刻空出来，避免「刚卸完马上重装」撞文件锁；再在 `%TEMP%` 写小 `.cmd`（`timeout /t 2` + `ping` 等待，del 失败重试，按需 `rmdir /s /q 安装目录`，最后删脚本自身），`cmd.exe /c` + `CreateNoWindow=true` + `UseShellExecute=false` 启动，退出。不弹终端窗口，程序目录不留 cmd。

## 5. 测试模式（本工作区合规测试用）

```text
Decanter-Setup-1.0.exe --sandbox <root> [--silent] [--autostart 0|1]
Uninstall.exe --sandbox <root> --silent --choice keep|export|delete [--export-dir <dir>]
Decanter-Setup-1.0.exe --shot <png> --state install|success|error
Uninstall.exe --shot <png> --state uninstall|uninstall-confirm|uninstall-done|error
```

- `<root>` 必须在当前工作目录内（防误写真实系统）；目标目录 `<root>\program`，开始菜单 `<root>\StartMenu`，注册表等价文本 `<root>\registry.txt`，自启 `<root>\autostart.txt`，`<root>\install-result.txt` / `<root>\uninstall-result.txt` 记录结果；`program\sandbox.txt` 记录 root，Uninstall.exe 据此继续走沙箱。
- 静默模式不弹任何 UI；有错误写结果文件并以非零退出。
- 真机与沙箱共用同一套核心逻辑，只把「系统落点」抽象成两套实现。

## 6. 沙箱自测记录（全部在本工作区，未写真实 D:\ / HKCU / 开始菜单）

### 6.1 安装（`run\installer-test-a4`，--autostart 1）

```powershell
Decanter-Setup-1.0.exe --sandbox run\installer-test-a4 --silent --autostart 1   # exit 0
```
- `program\` 下：Decanter.exe、Decanter.exe.config、sqlite3.dll、Uninstall.exe、README.txt、sandbox.txt ✅
- `StartMenu\醒酒器.lnk`、`StartMenu\卸载醒酒器.lnk` ✅（用 WScript.Shell 读回：目标分别为 program\Decanter.exe、program\Uninstall.exe，工作目录 program）
- `registry.txt` ✅（DisplayName=醒酒器 Decanter、DisplayVersion=1.0.0、Publisher=CiviWineHouse Studio、InstallLocation、DisplayIcon、UninstallString=`"…\Uninstall.exe"`、EstimatedSize、NoModify/NoRepair）
- `autostart.txt` = `Decanter="…\program\Decanter.exe"` ✅；`install-result.txt` ok=true。
- 安装器 exe 二进制里含 `asInvoker` / `requestedExecutionLevel`（未弹 UAC）。

### 6.2 应用可用性（数据默认在程序目录\data）

```text
program\Decanter.exe --cli init / --cli add --title "测试一条" --importance 2 / --cli list / --cli db-check
```
（`DECANTER_HOME=run\installer-test-a4\program`）四个命令 exit 0，`program\data\todos.db` 24 KB ✅。

### 6.3 卸载（保留数据）→ 再装同目录

```powershell
program\Uninstall.exe --sandbox run\installer-test-a4 --silent --choice keep   # exit 0
```
- `uninstall-result.txt`：ok=true、cleanup=ok、逐项 8 项全 ✓。
- 6 秒后 `program\` 只剩 `data\todos.db`（无 Uninstall.exe、无 .tmp/.cmd）✅；开始菜单、registry.txt、autostart.txt 均已清 ✅。
- 立刻再跑 `Decanter-Setup-1.0.exe --sandbox run\installer-test-a4 --silent --autostart 0`：**exit 0**，覆盖安装成功、`autostart.txt` 被清（不勾自启）✅（验证「卸载后目录残留导致访问被拒绝」这个旧坑没再出现）。

### 6.4 卸载（导出数据）

```powershell
program\Uninstall.exe --sandbox run\installer-test-d3 --silent --choice export --export-dir run\installer-test-d3\exported
```
- 最终构建复测（先 `--cli add` 造出数据再卸载）：exit 0；`uninstall-result.txt` ok=true、cleanup=ok、8 项全 ✓；`exported\todos.db` 存在 ✅；6 秒后 `program\` 目录已删 ✅。
- 早期同场景 `run\installer-test-d` 也通过（数据副本 `exported\todos.db` 24 KB）。

### 6.5 卸载（删除数据）

```powershell
program\Uninstall.exe --sandbox run\installer-test-e --silent --choice delete
```
- exit 0；8 项全 ✓；6 秒后 `program\` 目录已删 ✅（静默模式不弹二次确认，UI 才弹）。

### 6.6 程序运行中：不杀进程、可重试

- 启动安装目录里的 Decanter.exe 后再卸载：exit 1，`uninstall-result.txt` ok=false，给「醒酒器还在运行…」三段话，文件一个没少 ✅。
- 同时再跑安装：exit 1，同样三段话，不静默杀进程 ✅。
- 关掉应用后重试卸载：exit 0，清理完成 ✅。

### 6.7 UI 流程（沙箱，用 Win32 投递鼠标消息驱动；本机 150% 缩放，投递坐标按 /1.5 换算）

- 安装界面点「安装」→ 状态标题变「醒酒器 1.0 · 已安装」，program 文件齐全，关闭返回 0 ✅。
- 应用在运行时点「安装」→ 错误屏「醒酒器 1.0 · 需要重试」；关掉应用点「重试」→「已安装」✅。
- 卸载界面点「卸载」（保留默认）→ 完成屏 8 项 ✓ → 点「完成」→ 退出 0，6 秒后 program 只剩 data ✅。
- 卸载界面选「删除数据」→ 点「卸载」→ 确认屏（标题「卸载醒酒器 · 确认删除数据」）→ 点「仍要删除」→ 完成屏 → 「完成」→ 退出 0，program 目录已删 ✅。

### 6.8 截图（已用 read_image 目视检查）

- `run\installer\install.png` / `install-success.png` / `install-error.png`：640×420，无浅色标题栏、无越界、按钮文案完整（「取消 / 安装」「完成 / 打开醒酒器」「取消 / 重试」）。
- `run\installer\uninstall.png` / `uninstall-confirm.png` / `uninstall-done.png`：560×460，三个选项完整（保留数据默认选中、导出、删除数据危险色），确认屏「返回」为主按钮、「仍要删除」为危险描边文字按钮，完成屏 8 项 ✓ 逐项来自实际执行结果。

## 7. 已知限制 / 风险

1. **没有代码签名**：首次运行会有 SmartScreen「未知发布者」，按 README 说明「更多信息 → 仍要运行」；不承诺消除。
2. **真机落点未实测**：本工作区规则禁止写真实 D:\、开始菜单、HKCU，因此真机分支只做了代码同构与沙箱等价验证；风险点：杀软拦截注册表/快捷方式、D 盘权限不足的回退、个别系统 WScript.Shell 组件异常。安装/卸载核心逻辑与沙箱完全相同，差异只在 `SysOps` 的两套落点实现。
3. **%TEMP% 写入可能被策略拒绝**：本会话环境就不允许往 %TEMP% 写文件，自删除脚本会按 `%TEMP% → %LOCALAPPDATA%\Temp → 安装目录 → 当前目录` 依次回退，最后还有不落脚本的 `cmd /c ping… & del …` 兜底。落在安装目录时脚本会自删，实测无残留。真机优先仍用 `%TEMP%`。
4. **清理是退出后异步执行**：进程退出后约 1–3 秒完成（`timeout` + `ping` 等待 + 重试）。`uninstall-result.txt` 的 `cleanup=ok/cleanup_cmd=<脚本路径>` 表示「清理命令已成功启动」，脚本随后自删，所以该路径事后不存在；集成测试请在 Uninstall 退出后等 3–5 秒再断言目录。
5. **「安装目录」一项的口径**：删除/导出分支的最后一步实际是「目录里只剩正在运行的卸载程序，退出后删除」，所以完成屏显示 ✓ + 明确说明，而不是当场目录已不存在；cmd 退出后目录会被删（6.4/6.5 已验证）。
6. **保留数据时目录保留是预期**：`program\data\` 是用户数据，不是残留；若保留但没有 data 目录，空 program 目录会在退出后清掉。
7. **高 DPI**：manifest 使用系统 DPI 缩放（`dpiAware=false`），Windows 在 150%/200% 下把整个安装/卸载窗口按比例放大；截图按逻辑尺寸 640×420 / 560×460。
8. **历史测试目录**：`run\installer-test-a`、`a2`、`a3`（以及集成测试的 `run\my-install-a`）里各有一个修复前的 `uninstall-leaving-*.tmp`，那是自删除第一版在「%TEMP% 不可写」下的残留证据；当前构建的实测目录（`installer-test-a4/c/d/e/f`、`installer-uitest10/11/12`、`installer-smoke`）已无 .tmp/.cmd/卸载器本体残留。本会话沙箱策略不允许我删除这些旧目录，保留作为对比证据，不影响交付产物。
9. `setup\build\` 里的 `probe-*` 是排查自删除时写的小实验程序，不参与构建、不会进 payload；`build\` 整个目录删掉后重跑脚本即可重生。

## 8. 没做完 / 替代

- 真机「装 → 用 → 卸」全流程未跑（规则禁止），由沙箱等价测试覆盖；如需真机验收，请在一台可写 D:\ 的普通账户机器上双击 `run\installer\Decanter-Setup-1.0.exe`，再按开始菜单卸载。
- 没做代码签名、没做安装包图标以外的视觉资源（安装器无品牌插画，保持与主程序同一套暖黑账本语言）。
- 真人鼠标/键盘操作未逐项手点（本会话无法可靠注入真实输入），UI 交互用 Win32 投递鼠标消息驱动走通了安装、错误重试、卸载保留、删除确认四条主路径；键盘 Enter/Esc 已在代码里接好但未逐键实测。
