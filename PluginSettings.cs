namespace PotatoVN.ExternalPlayTime;

public sealed class PluginSettings
{
    public const int CurrentVersion = 1;

    /// <summary>数据结构版本，用于以后做迁移</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>是否启用外部启动计时</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>扫描运行中进程的间隔（秒）。过短没有意义，默认 10 秒。</summary>
    public int ScanIntervalSeconds { get; set; } = 10;

    /// <summary>是否仅在游戏窗口处于前台时计时（与宿主的“仅在前台时记录”同理）。</summary>
    public bool RecordOnlyWhenForeground { get; set; }

    /// <summary>单次游玩达到多少分钟后，才把这次游玩计入“游玩次数”。</summary>
    public int MinPlayTimeRecordThresholdMinutes { get; set; } = 5;
}
