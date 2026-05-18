namespace WindowBouncer.Win32;

public sealed record WindowInfo(
    nint Handle,
    string Title,
    string ProcessName,
    uint ProcessId,
    bool IsMinimized
);
