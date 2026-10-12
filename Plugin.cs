using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Contracts.PluginUi;
using GalgameManager.WinApp.Base.Models;
using Microsoft.UI.Xaml;

namespace PotatoVN.ExternalPlayTime;

public sealed class Plugin : IPlugin, IPluginSetting
{
    public PluginInfo Info { get; } = new()
    {
        // 与 csproj 的 AssemblyName 保持一致（A + 插件 Id）
        Id = new Guid("9dba97f8-9d2d-46da-9d24-cf3726202886"),
        Name = "外部启动计时",
        Description = "即使不从 PotatoVN 内部启动游戏，也能记录游玩时间。\n"
                      + "每分钟写一次游戏库；官方正在计时的游戏会自动跳过。",
    };

    private IPotatoVnApi _hostApi = null!;
    private PluginSettings _settings = new();
    private PlayTimeMonitor? _monitor;

    public async Task InitializeAsync(IPotatoVnApi hostApi)
    {
        _hostApi = hostApi;

        string? dataJson = await hostApi.GetDataAsync();
        if (!string.IsNullOrWhiteSpace(dataJson))
        {
            try
            {
                _settings = JsonSerializer.Deserialize<PluginSettings>(dataJson) ?? new PluginSettings();
            }
            catch
            {
                _settings = new PluginSettings();
            }
        }

        _monitor = new PlayTimeMonitor(hostApi, _settings);
        _monitor.Start();
    }

    public async Task OnUninstallAsync(bool deleteData, Action<TimeSpan> extendWaitHandler, CancellationToken cts)
    {
        if (cts.IsCancellationRequested) return;
        // 先把进行中的会话结算掉，再让宿主回收插件
        if (_monitor is not null) await _monitor.StopAsync();
    }

    public FrameworkElement CreateSettingUi() => new SettingUi(_settings, SaveData);

    private void SaveData() => _ = SaveDataAsync();

    private async Task SaveDataAsync()
    {
        try
        {
            await _hostApi.SaveDataAsync(JsonSerializer.Serialize(_settings));
        }
        catch
        {
            // 保存失败不影响计时
        }
    }
}
