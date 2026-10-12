using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PotatoVN.ExternalPlayTime;

/// <summary>
/// 进程与路径工具。
/// 宿主的 GalgameManager.Helpers 在插件打包时会被移除，且不能被插件直接引用，
/// 所以这里复刻计时所需的最小集合，逻辑与宿主 GameProcessDetector / ProcessExtensions 保持一致。
/// </summary>
internal static class ProcessHelper
{
    private const int ProcessQueryLimitedInformation = 0x1000;

    /// <summary>WINDOWPLACEMENT 的托管镜像。字段顺序和大小必须与 Win32 一致（44 字节）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
        public int NormalLeft;
        public int NormalTop;
        public int NormalRight;
        public int NormalBottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacement lpwndpl);

    [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName,
        ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// 尝试获取进程的可执行文件完整路径。使用 QueryFullProcessImageName，
    /// 可以跨 32/64 位进程工作且不需要额外权限；失败（进程已退出、拒绝访问等）时返回 null。
    /// </summary>
    public static string? TryGetExecutablePath(this Process process)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero) return null;
            int capacity = 1024;
            StringBuilder buffer = new(capacity);
            return QueryFullProcessImageName(handle, 0, buffer, ref capacity) ? buffer.ToString() : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

    /// <summary>主窗口是否最小化</summary>
    public static bool IsMainWindowMinimized(this Process process)
    {
        try
        {
            if (process.MainWindowHandle == IntPtr.Zero) return false;
            WindowPlacement placement = new() { Length = Marshal.SizeOf<WindowPlacement>() };
            return GetWindowPlacement(process.MainWindowHandle, ref placement) && placement.ShowCmd == 2;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>主窗口是否处于前台</summary>
    public static bool IsMainWindowActive(this Process process)
    {
        try
        {
            if (process.MainWindowHandle == IntPtr.Zero) return false;
            GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundProcessId);
            return process.Id == foregroundProcessId;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAlive(this Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public static bool HasWindow(this Process process)
    {
        try
        {
            return process.MainWindowHandle != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 把目录规范化为带结尾分隔符的完整路径前缀，避免把兄弟目录（如 game2）误判为子路径。
    /// </summary>
    public static string GetDirectoryPrefix(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
               Path.DirectorySeparatorChar;
    }

    /// <summary>同一安装目录下挑选更“像本体”的进程：优先有主窗口的，其次启动时间更晚的。</summary>
    public static bool IsBetterCandidate(Process candidate, Process current)
    {
        bool candidateHasWindow = candidate.HasWindow();
        bool currentHasWindow = current.HasWindow();
        if (candidateHasWindow != currentHasWindow) return candidateHasWindow;
        try
        {
            return candidate.StartTime > current.StartTime;
        }
        catch
        {
            return false;
        }
    }
}
