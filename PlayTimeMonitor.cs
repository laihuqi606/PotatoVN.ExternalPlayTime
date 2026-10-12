using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using GalgameManager.Enums;
using GalgameManager.Models;
using GalgameManager.Models.BgTasks;
using GalgameManager.WinApp.Base.Contracts;
using GalgameManager.WinApp.Base.Models.Plugin;
using Microsoft.UI.Xaml.Controls;

namespace PotatoVN.ExternalPlayTime;

/// <summary>
/// 后台轮询监视器：每 N 秒扫描一次运行中的进程，把可执行文件路径落在游戏库安装目录内的进程
/// 认定为“正在游玩”，按分钟累计游玩时间并写回游戏库。
/// </summary>
internal sealed class PlayTimeMonitor
{
    /// <summary>宿主官方计时任务的类名，用来在后台任务列表里识别并跳过，避免重复计时。</summary>
    private const string OfficialRecordTaskName = "RecordPlayTimeTask";

    private const double SecondsPerMinute = 60;

    private readonly IPotatoVnApi _api;
    private readonly PluginSettings _settings;
    private readonly Dictionary<Guid, GameSession> _sessions = new();

    private CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public PlayTimeMonitor(IPotatoVnApi api, PluginSettings settings)
    {
        _api = api;
        _settings = settings;
    }

    public void Start()
    {
        if (_loopTask is not null && !_loopTask.IsCompleted) return;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch (OperationCanceledException)
            {
                // 正常停止
            }
            catch (Exception ex)
            {
                _api.DeveloperEvent(msg: "外部启动计时监视器停止时出错", e: ex);
            }
        }

        await EndAllSessionsAsync();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ScanAsync();
            }
            catch (Exception ex)
            {
                _api.DeveloperEvent(msg: "外部启动计时扫描出错", e: ex);
            }

            int intervalMs = Math.Max(2, _settings.ScanIntervalSeconds) * 1000;
            try
            {
                await Task.Delay(intervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ScanAsync()
    {
        if (!_settings.Enabled)
        {
            await EndAllSessionsAsync();
            return;
        }

        List<Galgame> games = _api.GetAllGames();
        HashSet<Guid> officiallyTracked = GetOfficiallyTrackedGames();

        // 安装目录前缀 -> 所属游戏。每轮重新取，为了能发现新加入库的游戏与新安装实例。
        List<(string Prefix, Galgame Game)> prefixes = new();
        foreach (Galgame game in games)
        {
            if (!game.IsLocalGame) continue;
            if (officiallyTracked.Contains(game.Uuid)) continue;

            IReadOnlyList<GameInstallationInfo> installations;
            try
            {
                installations = _api.GetGameInstallations(game);
            }
            catch
            {
                continue;
            }

            foreach (GameInstallationInfo installation in installations)
            {
                if (!installation.IsAvailable || string.IsNullOrEmpty(installation.Path)) continue;
                try
                {
                    prefixes.Add((ProcessHelper.GetDirectoryPrefix(installation.Path), game));
                }
                catch
                {
                    // 路径无效（例如已离线的网络盘），跳过这个安装实例
                }
            }
        }

        // 找出每个游戏当前的最佳进程（同一目录里可能有启动器和本体两个进程）
        Dictionary<Guid, (Galgame Game, Process Process)> running = new();
        List<Process> leftovers = new();
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.HasExited)
                {
                    process.Dispose();
                    continue;
                }

                string? path = process.TryGetExecutablePath();
                if (path is null)
                {
                    process.Dispose();
                    continue;
                }

                Galgame? matched = null;
                foreach ((string prefix, Galgame game) in prefixes)
                {
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = game;
                        break;
                    }
                }

                if (matched is null)
                {
                    process.Dispose();
                    continue;
                }

                if (running.TryGetValue(matched.Uuid, out (Galgame Game, Process Process) existing))
                {
                    if (ProcessHelper.IsBetterCandidate(process, existing.Process))
                    {
                        running[matched.Uuid] = (matched, process);
                        leftovers.Add(existing.Process);
                    }
                    else
                    {
                        leftovers.Add(process);
                    }
                }
                else
                {
                    running[matched.Uuid] = (matched, process);
                }
            }
            catch
            {
                process.Dispose();
            }
        }

        foreach (Process leftover in leftovers)
        {
            try { leftover.Dispose(); } catch { }
        }

        DateTime nowUtc = DateTime.UtcNow;
        // 休眠 / 挂起恢复后不做时间补偿，避免一觉醒来玩了几百分钟
        double maxDeltaSeconds = Math.Max(120, _settings.ScanIntervalSeconds + 30);

        foreach ((Guid uuid, (Galgame Game, Process Process) pair) in running)
        {
            if (!_sessions.TryGetValue(uuid, out GameSession? session))
            {
                session = new GameSession { Game = pair.Game, Process = pair.Process, LastTickUtc = nowUtc };
                _sessions.Add(uuid, session);
            }
            else
            {
                try { session.Process?.Dispose(); } catch { }
                session.Process = pair.Process;
            }

            Process process = pair.Process;
            bool alive = process.IsAlive();
            bool eligible = alive && IsEligible(process);

            double delta = (nowUtc - session.LastTickUtc).TotalSeconds;
            if (delta > maxDeltaSeconds) delta = maxDeltaSeconds;
            if (eligible) session.PendingSeconds += delta;
            session.LastTickUtc = nowUtc;

            while (session.PendingSeconds >= SecondsPerMinute)
            {
                if (!process.IsAlive()) break;
                session.PendingSeconds -= SecondsPerMinute;
                session.AwardedMinutes++;
                AwardMinute(pair.Game);
            }
        }

        // 本轮没再出现的游戏视为已退出，结算会话
        List<Guid> ended = new();
        foreach ((Guid uuid, GameSession session) in _sessions)
        {
            if (running.ContainsKey(uuid)) continue;
            await EndSessionAsync(session);
            ended.Add(uuid);
        }
        foreach (Guid uuid in ended) _sessions.Remove(uuid);
    }

    /// <summary>
    /// 宿主自己正在计时的游戏。用反射读 RecordPlayTimeTask 的 Galgame 字段，
    /// 因为插件不能直接引用宿主的任务类型；只在任务仍在运行时才算。
    /// </summary>
    private HashSet<Guid> GetOfficiallyTrackedGames()
    {
        HashSet<Guid> result = new();
        try
        {
            foreach (BgTaskBase task in _api.GetBgTasks())
            {
                if (task.GetType().Name != OfficialRecordTaskName) continue;
                if (!task.IsRunning) continue;
                if (task.GetType().GetField("Galgame")?.GetValue(task) is Galgame game && game.Uuid != Guid.Empty)
                {
                    result.Add(game.Uuid);
                }
            }
        }
        catch
        {
            // 读不到宿主任务列表时不影响扫描，大不了这一轮去重失败
        }

        return result;
    }

    private bool IsEligible(Process process)
    {
        if (!_settings.RecordOnlyWhenForeground) return true;
        return !process.IsMainWindowMinimized() && process.IsMainWindowActive();
    }

    /// <summary>
    /// 记录一分钟。属性的修改和落盘都丢到主线程上做（和官方 RecordPlayTimeTask 一致），
    /// 这里用不等待的重载，避免扫描循环被 UI 线程卡住。
    /// </summary>
    private void AwardMinute(Galgame game)
    {
        _api.InvokeOnMainThread(() =>
        {
            game.TotalPlayTime++;
            string today = DateTime.Now.ToString("yyyy/M/d");
            if (!game.PlayedTime.TryAdd(today, 1)) game.PlayedTime[today]++;
            game.LastPlayTime = DateTime.Now;
            MarkForSync(game);
            _api.SaveGameAsync(game);
        });
    }

    /// <summary>结算一次会话：达到阈值就计入游玩次数。</summary>
    private async Task EndSessionAsync(GameSession session)
    {
        if (session.AwardedMinutes >= _settings.MinPlayTimeRecordThresholdMinutes)
        {
            try
            {
                await _api.InvokeOnMainThreadAsync(() =>
                {
                    session.Game.PlayCount++;
                    MarkForSync(session.Game);
                    return _api.SaveGameAsync(session.Game);
                });
            }
            catch (Exception ex)
            {
                _api.DeveloperEvent(msg: "外部启动计时的会话结算出错", e: ex);
            }
        }

        try { session.Process?.Dispose(); } catch { }
        session.Process = null;
    }

    private async Task EndAllSessionsAsync()
    {
        foreach (GameSession session in _sessions.Values)
        {
            await EndSessionAsync(session);
        }

        _sessions.Clear();
    }

    /// <summary>
    /// 标记这个游戏需要同步到 PVN 服务器。插件只负责标记，真正的上传由宿主在下一次同步时完成。
    /// </summary>
    private static void MarkForSync(Galgame game)
    {
        game.PvnUpdate = true;
        game.PvnUploadProperties |= PvnUploadProperties.PlayTime;
    }

    private sealed class GameSession
    {
        public Galgame Game = null!;
        public Process? Process;
        public DateTime LastTickUtc;
        public double PendingSeconds; // 已经累计但还没换算成分钟的秒数
        public int AwardedMinutes; // 本次会话已经记下的分钟数
    }
}
