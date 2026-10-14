# PotatoVN 外部启动计时插件

让 PotatoVN 在你**没有从软件内部启动**游戏的时候，也能记录游玩时间。

## 它解决什么问题

PotatoVN 只在从软件内部启动游戏时计时。如果你习惯直接双击 exe（或者从开始菜单、Steam、快捷方式启动），
游戏库里就不会记录游玩时间。

这个插件每隔几秒扫描一次电脑上正在运行的进程，把可执行文件路径落在游戏库安装目录里的进程判定为“正在游玩”，
然后按分钟把时间写回游戏库，逻辑与官方计时保持一致：

- 每游玩 1 分钟，总游玩时间 +1，当日游玩时间 +1
- 单次游玩达到设定时长（默认 5 分钟）后，游玩次数 +1
- 可选“仅在前台时计时”：窗口最小化或不在最前时不计入
- 官方正在计时的游戏会自动跳过，不会重复计算

## 已知限制（v1）

- **PotatoVN 必须保持运行**：插件寄生在 PotatoVN 进程里，PotatoVN 关了就没办法记录。
  想做到“关着也记”，需要做成开机自启的独立后台程序直接读写数据库，是另一条技术路线（留待 v2）。
- **靠路径匹配**：用 Locale Emulator 之类的壳启动、或通过快捷方式重定向到别的目录时，可能认不出。
- **UWP 沙箱限制**：部分系统进程的路径读不到。
- **PVN 服务器同步不是立即的**：插件把改过的游戏标记为“需要同步”，实际上传在你下次让
  PotatoVN 执行同步时发生。

## 编译

本仓库用 submodule 引入了 PotatoVN 源码，克隆时记得带 submodule：

```bash
git clone --recurse-submodules https://github.com/laihuqi606/PotatoVN.ExternalPlayTime.git

# 或者已经克隆过的话：
git submodule update --init --recursive
```

环境：Visual Studio 2022（含 .NET 桌面开发 + Windows 应用程序 SDK 工作负载），
或 .NET 8 SDK + Windows App SDK 的命令行。

用 Visual Studio 打开 `PotatoVN.ExternalPlayTime.sln`，或直接打开
`PotatoVN.ExternalPlayTime.csproj`，编译。Release 配置会自动在
`artifacts/plugin.pvnplugin.zip` 生成可分发的插件包。

## 安装到本地 PotatoVN

1. 在 PotatoVN 的 **设置 → 其他设置** 里打开**开发者模式**（否则无法从编译输出加载插件）。
2. 进入**插件**界面，点击右上角开发者专用的**添加插件**按钮。
3. 选择插件编译输出根目录（也就是那一堆 dll 里有一个叫
   `A9dba97f8-9d2d-46da-9d24-cf3726202886.dll` 的那个目录），点添加。

> 注：添加插件后列表不会立刻刷新，切到别的界面再切回来就能看到了。

## 分发 / 发布到插件市场

自行分发的话，把 Release 编译出来的 `artifacts/plugin.pvnplugin.zip` 发给别人就行。
想进 PotatoVN 插件市场，参考官方的发布文档（potatovn.net/development/client-plugin/deploy-manage.html）：
在插件平台创建插件项目，审核通过后上传 `plugin.pvnplugin.zip` 和版本号。

## 许可证

本项目采用 [Apache-2.0](LICENSE) 许可证。

插件引用了 PotatoVN 宿主源码（通过 submodule 引入，同样为 Apache-2.0），
在此致谢 [GoldenPotato137/PotatoVN](https://github.com/GoldenPotato137/PotatoVN)。
