using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PotatoVN.ExternalPlayTime;

/// <summary>插件的设置界面。纯代码描述，不依赖 XAML 资源字典。</summary>
public sealed class SettingUi : UserControl
{
    private readonly PluginSettings _settings;
    private readonly Action _onChanged;

    public SettingUi(PluginSettings settings, Action onChanged)
    {
        _settings = settings;
        _onChanged = onChanged;

        ToggleSwitch enabledSwitch = new()
        {
            Header = "启用外部启动计时",
            IsOn = _settings.Enabled,
        };
        enabledSwitch.Toggled += (_, _) =>
        {
            _settings.Enabled = enabledSwitch.IsOn;
            Save();
        };

        NumberBox intervalBox = new()
        {
            Header = "扫描间隔（秒）",
            Value = _settings.ScanIntervalSeconds,
            Minimum = 2,
            Maximum = 3600,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        intervalBox.ValueChanged += (_, _) =>
        {
            _settings.ScanIntervalSeconds = Math.Clamp((int)intervalBox.Value, 2, 3600);
            Save();
        };

        ToggleSwitch foregroundSwitch = new()
        {
            Header = "仅在前台时计时（窗口最小化或不在最前时不计入）",
            IsOn = _settings.RecordOnlyWhenForeground,
        };
        foregroundSwitch.Toggled += (_, _) =>
        {
            _settings.RecordOnlyWhenForeground = foregroundSwitch.IsOn;
            Save();
        };

        NumberBox thresholdBox = new()
        {
            Header = "最短游玩时长（分钟，达到才计入游玩次数）",
            Value = _settings.MinPlayTimeRecordThresholdMinutes,
            Minimum = 0,
            Maximum = 1440,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        thresholdBox.ValueChanged += (_, _) =>
        {
            _settings.MinPlayTimeRecordThresholdMinutes = Math.Clamp((int)thresholdBox.Value, 0, 1440);
            Save();
        };

        Content = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "外部启动计时",
                    Style = Application.Current.Resources["TitleTextBlockStyle"] as Style,
                },
                new TextBlock
                {
                    Text = "开启后，即使直接双击 exe 启动游戏（而不是从 PotatoVN 里启动），"
                           + "PotatoVN 也会每分钟记录一次游玩时间。\n"
                           + "注意：PotatoVN 本身需要保持运行；PVN 服务器的同步会在下一次 "
                           + "PotatoVN 执行同步时完成。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                },
                enabledSwitch,
                intervalBox,
                foregroundSwitch,
                thresholdBox,
            },
        };
    }

    private void Save() => _onChanged();
}
