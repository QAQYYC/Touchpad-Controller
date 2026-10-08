# 小米笔记本 Pro 14 触摸板开关

[中文](#中文) · [English](#english)

## 中文

在 Windows 11 上开关这块笔记本的触摸板。窗口是平面、圆角的，可以拉伸和全屏。点大开关，或按你设的快捷键，都是按一下开启、再按一下关闭。每次真正切换后，Windows 通知中心会在右下角提醒。

适用机型是小米笔记本 Pro 14 顶配。触摸板按 Goodix 硬件 ID 识别（`BLTP7853` / `347D:7853`，或 `GXTP7863` / `27C6:01E0`）。指纹（`27C6:6890`）和触摸屏会被跳过。

程序是一个独立的 64 位 exe，已经带好运行所需的组件。拷到别的 Windows 10 或 Windows 11 64 位电脑上可以直接打开。

### 可迁移的程序

`dist/触摸板开关.exe` 可以单独拷走。放到桌面、U 盘，或另一台电脑上，双击就能打开。

开机启动会记住这个 exe 当时所在的位置。如果以后把文件挪到别的文件夹，再打开一次，登录时就会改用新位置。

这个 exe 没有数字签名。Windows SmartScreen 第一次打开时可能提示风险。点「更多信息」，再点「仍要运行」。

### 安装到桌面

1. 把这个文件夹拷到笔记本。
2. 双击 `安装到桌面.bat`，允许一次管理员权限。
3. 桌面会出现 `触摸板开关.exe`，并打开窗口。
4. 之后登录会在后台启动，不抢焦点。关掉窗口后它留在托盘里，快捷键仍然有效。
5. 如果已经从托盘退出，再次打开会再要一次管理员权限。登录自启不会反复询问。

也可以不运行安装脚本，直接双击 `dist/触摸板开关.exe`。这样程序就留在你拷贝到的那个位置。

### 使用

- 点中间的滑钮，或按快捷键，在开启和关闭之间切换。默认快捷键是 `Ctrl+Alt+T`。
- 「更改快捷键」后按下新的组合键。至少要带 Ctrl、Alt、Shift、Win 或 Fn 中的一个。`Esc` 取消录制。如果这个组合键已被占用，会提示并继续用原来的。Fn 由键盘扫描码识别，录制时按住 Fn 再按主键。
- 外观可以选浅色、深色，或适应系统。适应系统时，只在 Windows 改变主题时跟着变。
- 拖窗口边缘可以拉伸。右上角可以全屏，再按一次或按 `Esc` 退出全屏。全屏铺满当前屏幕，包括任务栏。
- 「开机启动」可以单独关掉，不必卸载。
- 最小化和关闭都收到托盘。左键点击托盘图标打开主窗口。右键打开菜单，里面有打开主窗口、切换触摸板、开机启动，以及退出。只有「退出」会结束程序。
- 每次真正切换后，右下角出现系统通知：「触摸板已开启」「触摸板已关闭」，失败时是「未能切换触摸板」。打开窗口、改主题、改快捷键或改开机启动都不会发通知。

### 打开时对齐系统

每次打开主窗口，都会先读现在的系统状态再更新界面：触摸板是开还是关、适应系统时的浅色或深色，以及开机启动是否还在。和界面一致时不重播动画。后台待命时不反复查询。用 Fn 或系统设置改过触摸板之后，下次打开窗口时会对齐。

### 能耗

后台不做周期检查，也不轮询键盘。小圆点动画只在窗口看得见、而且触摸板开着时播放。快捷键由系统在按下时回调。

### 识别失败

如果自动识别不确定，窗口会列出可选设备。点一项就会记住，以后只用这一项。指纹和触摸屏不在列表里。点「重新检测」会忘掉上次的选择再找一次。

也可以打开设备管理器，在「鼠标和其他指针设备」或「人体学输入设备」里看触摸板的名称，再回到程序里点选同名的一项。

### 卸载

双击 `卸载.bat` 并允许管理员权限。它会结束程序，并删掉登录任务、开始菜单快捷方式、本机设置和桌面上的 exe。`dist` 里的那份程序还在，可以再装一次。

### 重新编译

需要 .NET 8 SDK。在 Windows 或 Linux 上：

```bash
dotnet publish src/TouchpadToggle/TouchpadToggle.csproj -c Release -r win-x64 --self-contained true -o dist -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

生成的文件是 `dist/触摸板开关.exe`。

---

## English

Toggle the touchpad on a Xiaomi Book Pro 14 from Windows 11. The window is flat, with rounded corners, and it can be resized or made fullscreen. The large switch and the hotkey do the same thing: press once to turn the touchpad on, press again to turn it off. Each real change posts a toast in Windows notification center.

The app targets the top-spec Xiaomi Book Pro 14. It finds the Goodix touchpad by hardware ID (`BLTP7853` / `347D:7853`, or `GXTP7863` / `27C6:01E0`). The fingerprint reader (`27C6:6890`) and the touchscreen are skipped.

The download is one self-contained 64-bit executable. Copy it to another 64-bit Windows 10 or Windows 11 PC and open it there.

### Portable executable

`dist/触摸板开关.exe` can travel on its own. Put it on the desktop, a USB drive, or another computer and double-click it.

Startup with Windows remembers the path of the executable at the time you turn that option on. Move the file later, open it once, and the next sign-in uses the new location.

The executable is unsigned. On the first launch, Windows SmartScreen may warn you. Choose **More info**, then **Run anyway**.

### Install to the desktop

1. Copy this folder onto the laptop.
2. Double-click `安装到桌面.bat` and approve the administrator prompt once.
3. `触摸板开关.exe` appears on the desktop and the window opens.
4. Later sign-ins start it in the background, without taking focus. Closing the window leaves it in the tray, and the hotkey keeps working.
5. After you quit from the tray, the next manual launch asks for administrator rights again. The sign-in task does not ask every time.

You can also skip the installer and double-click `dist/触摸板开关.exe`. The app then stays in the folder you copied.

### Use

- Click the switch, or press the hotkey, to turn the touchpad on or off. The default hotkey is `Ctrl+Alt+T`.
- Choose **更改快捷键** and press a new combination. It must include Ctrl, Alt, Shift, Win, or Fn. `Esc` cancels recording. If the combination is already taken, the app says so and keeps the previous hotkey. Fn is recognized from the keyboard scan code: hold Fn, then press the main key.
- Appearance can be light, dark, or follow the system. Follow-system updates when Windows changes its theme.
- Drag the window edges to resize. The button at the top right fills the current screen, including the taskbar. Press it again, or press `Esc`, to leave fullscreen.
- **开机启动** can be turned off on its own, without uninstalling.
- Minimize and close both hide the window in the tray. Left-click the tray icon to open the main window. Right-click for the menu: open the window, toggle the touchpad, startup with Windows, and quit. **退出** is the only item that ends the process.
- After each real toggle, a system toast appears: “触摸板已开启”, “触摸板已关闭”, or “未能切换触摸板” when the change fails. Opening the window, changing the theme, changing the hotkey, or changing startup does not send a toast.

### Sync when the window opens

Each time the main window opens, the app reads the current system state and updates the screen: whether the touchpad is on, the light or dark theme when follow-system is selected, and whether the sign-in task still exists. When that already matches the screen, the switch does not animate again. While the app waits in the tray it does not poll. If you change the touchpad with Fn or Windows Settings, the next time you open the window it lines up with that state.

### Power

The background process does not poll on a timer, and it does not poll the keyboard. The moving dot plays only while the window is visible and the touchpad is on. Windows calls the hotkey handler when you press the combination.

### If detection is unsure

When more than one device could be the touchpad, the window lists the candidates. Pick one and the app remembers it. The fingerprint reader and the touchscreen are left off that list. **重新检测** forgets the saved choice and searches again.

You can also open Device Manager, look under Mice and other pointing devices or Human Interface Devices for the touchpad name, then pick the same name in the app.

### Uninstall

Double-click `卸载.bat` and approve the administrator prompt. It stops the app and removes the sign-in task, the Start menu shortcut, the local settings, and the desktop executable. The copy in `dist` remains, so you can install again.

### Rebuild

Requires the .NET 8 SDK. On Windows or Linux:

```bash
dotnet publish src/TouchpadToggle/TouchpadToggle.csproj -c Release -r win-x64 --self-contained true -o dist -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

The published file is `dist/触摸板开关.exe`.
