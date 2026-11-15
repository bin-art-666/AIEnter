# AIEnter v1.5.0 使用说明

## 运行程序

1. 下载 AIEnter-v1.5.0-win-x64.zip，完整解压到固定目录。
2. 在 Windows 11 x64 上运行 AIEnter.exe；需安装 Microsoft WebView2 Runtime。
3. 初次打开 AI 网站时自行登录。程序包已包含 .NET 与 Windows App SDK 运行依赖，无需安装开发 SDK。

程序数据保存在当前用户的 %LOCALAPPDATA%\AIEnter，不在压缩包内。更新时先退出旧版，再替换程序文件，保留该用户数据目录。

## 安装时间右侧任务栏入口

此入口依赖 Windhawk；本地验证使用 1.7.3。发行包不附带 Windhawk。

1. 从 [Windhawk 官网](https://windhawk.net/) 安装 Windhawk。
2. 参考 [官方自定义模组说明](https://github.com/ramensoftware/windhawk/wiki/Creating-a-new-mod)，创建一个新模组，将编辑器内容替换为包内 Taskbar/aienter-taskbar-ai.wh.cpp 的完整内容，保存并编译。
3. 在模组设置中，将 AIEnter executable path（executable）填写为此目录 AIEnter.exe 的完整路径，保存并启用。
4. 图标位于时间日期右侧：左键打开／收起对话，右键打开设置，拖入内容交给当前 AI，不自动发送消息。

不要将开发机器上的 .tools/windhawk 配置复制到其他电脑。内含的机器路径、缓存及自启任务不适用于其他安装位置。

## 边界

- 本版不提供自定义任务栏图标替换入口。
- 压缩包不注册开机自启；如需登录时加载任务栏入口，可使用 Windhawk 自身的启动设置。
- 任务栏模组依赖 Explorer 内部布局，Windows 更新后可能需要适配；不支持的布局不插入按钮。
- 拖入附件依赖各 AI 网站的上传入口；不可用时保留待处理内容并提供重试，不自动点击发送。
- 校验压缩包可使用同一 Release 下的 SHA256SUMS.txt。
