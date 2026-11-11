using System;
using System.IO;
using System.Text;

namespace MirrorpondHUD.Core;

/// <summary>设置文件读写。优先放在 exe 同目录（便携），不可写时退回 %LOCALAPPDATA%。</summary>
public static class ConfigStore
{
    static string _path;
    static readonly object SaveSync = new();

    /// <summary>
    /// 宿主注入的卡片归一化钩子（实际是 <c>CardModel.Normalize</c>）。
    /// ConfigStore 在 Core 层，不应该为了卡片结构反过来引用 Cards；
    /// App 在启动早期（插件注册之后、第一次 Load 之前）注入这个委托。
    /// </summary>
    public static Action<Settings> SettingsNormalizer;

    static void NormalizeCards(Settings settings)
    {
        var normalizer = SettingsNormalizer;
        if (normalizer is not null) normalizer(settings);
    }

    /// <summary>最近一次加载设置的警告（损坏 / 从备份恢复 / 未来版本只读）。正常加载为空。</summary>
    public static string LastLoadWarning { get; private set; } = "";

    /// <summary>配置处于只读加载状态：更高 SchemaVersion，或本次读取 I/O 失败；Save 拒绝覆盖。</summary>
    public static bool LastLoadReadOnly { get; private set; }

    /// <summary>
    /// 最近一次 <see cref="Save"/> 失败的原因；成功会被清空。
    /// 设置窗 / 控制器用它在界面上如实提示「保存失败」，而不是谎报成功（P0-2）。
    /// </summary>
    public static string LastSaveError { get; private set; } = "";

    public static string Path
    {
        get
        {
            if (_path is not null) return _path;

            string portable = System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (CanWrite(AppContext.BaseDirectory))
            {
                _path = portable;
            }
            else
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MirrorpondHUD");
                Directory.CreateDirectory(dir);
                _path = System.IO.Path.Combine(dir, "settings.json");
            }
            return _path;
        }
    }

    static bool CanWrite(string dir)
    {
        try
        {
            string probe = System.IO.Path.Combine(dir, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    public static Settings Load()
    {
        LastLoadWarning = "";
        LastLoadReadOnly = false;

        Settings settings = new();
        bool migrated = false;
        bool corrupted = false;
        bool recovered = false;
        bool future = false;
        bool empty = false;
        bool brokenBackupFailed = false;
        bool readIoFailed = false;

        string file = Path;
        bool exists = File.Exists(file);
        if (exists)
        {
            if (!TryReadSettings(file, out settings, out migrated, out corrupted, out empty, out future,
                                out readIoFailed))
            {
                // I/O 失败（占用 / 权限 / 网络盘抖动）不是“内容损坏”：不能备份后用默认值覆盖原文件。
                if (readIoFailed)
                {
                    LastLoadReadOnly = true;
                    LastLoadWarning = $"设置文件暂时读不到（可能被占用 / 权限不足），本次只读加载默认设置；不会覆盖原文件。处理后可重启或重新保存。";
                    Log.Warn($"设置文件读取失败，按只读默认设置启动（不会覆盖原文件）: {file}");
                }
                else
                {
                    corrupted = true;
                }
            }

            if (!readIoFailed && (corrupted || empty))
            {
                if (corrupted)
                {
                    brokenBackupFailed = !TryBackupBroken();
                    if (brokenBackupFailed)
                    {
                        Log.Error($"设置文件损坏，且备份失败（磁盘满 / 只读 / 文件被占用）。后续不会把默认设置覆盖到原文件上，请先把 settings.json 手动拷出再处理");
                        LastLoadWarning = $"设置文件损坏，且备份失败：请先把 {file} 手动拷出（详情见日志）";
                    }
                    else
                    {
                        Log.Warn("设置文件损坏，已备份为 .broken-*");
                        LastLoadWarning = "设置文件损坏，原文件已备份为 .broken-*。";
                    }
                }
                else
                {
                    LastLoadWarning = "设置文件为空。";
                }

                if (!brokenBackupFailed)
                {
                    if (TryLoadBackup(out Settings backupSettings, out bool backupMigrated, out bool backupFuture))
                    {
                        settings = backupSettings;
                        migrated = backupMigrated;
                        future = backupFuture;
                        recovered = true;
                        LastLoadWarning += " 已从 settings.json.bak 恢复。";
                        Log.Warn("已从 settings.json.bak 恢复设置");
                    }
                    else
                    {
                        settings = new Settings();
                        LastLoadWarning += " 没有可用的备份，已使用默认设置。";
                    }
                }
            }
        }
        else
        {
            // 主文件缺失（杀软隔离 / OneDrive 占位 / 误删）：不要直接覆盖成默认值，先看 .bak。
            if (TryLoadBackup(out Settings backupSettings, out bool backupMigrated, out bool backupFuture))
            {
                settings = backupSettings;
                migrated = backupMigrated;
                future = backupFuture;
                recovered = true;
                LastLoadWarning = "settings.json 不存在，已从 settings.json.bak 恢复。";
                Log.Warn("settings.json 不存在，已从 .bak 恢复");
            }
        }

        if (future)
        {
            LastLoadReadOnly = true;
            LastLoadWarning = "设置来自更高版本，已只读加载；不会覆盖原文件。";
            Log.Warn("设置来自更高 SchemaVersion，只读加载");
        }

        try
        {
            settings.Normalize();
            NormalizeCards(settings);
        }
        catch (Exception ex)
        {
            Log.Error("设置归一化失败，使用默认设置", ex);

            bool localizedBackupFailed = !TryBackupBroken();
            settings = new Settings();
            try
            {
                settings.Normalize();
                NormalizeCards(settings);
            }
            catch (Exception retry)
            {
                Log.Error("默认设置归一化也失败", retry);
            }

            corrupted = true;
            if (localizedBackupFailed)
            {
                LastLoadWarning = $"设置归一化失败，且损坏现场备份失败：请先把 {file} 手动拷出（详情见日志）；本次不会写入默认设置。";
            }
            else
            {
                LastLoadWarning = "设置归一化失败，已使用默认设置；原文件已备份为 .broken-*。";
            }

            // 归一化失败且备不下来的现场绝不能被默认值覆盖。
            if (localizedBackupFailed) brokenBackupFailed = true;
        }

        // 损坏恢复后立刻落盘，避免每次启动重复处理。
        // 从 .bak 恢复时不要再把损坏的主文件复制回 .bak。
        // 但「损坏现场备份失败」时必须中止：没有备份就不允许用默认值覆盖原文件（P1-2）。
        if (!LastLoadReadOnly && !brokenBackupFailed)
        {
            bool keepBackup = !recovered;
            if (migrated || recovered || corrupted || empty)
                Save(settings, keepBackup);
        }

        return settings;
    }

    static bool TryReadSettings(string file, out Settings settings, out bool migrated,
                                out bool corrupted, out bool empty, out bool future,
                                out bool ioFailed)
    {
        settings = new Settings();
        migrated = false;
        corrupted = false;
        empty = false;
        future = false;
        ioFailed = false;

        string text;
        try
        {
            text = File.ReadAllText(file, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            // 读文件本身失败（共享冲突 / 权限 / 网络盘）和“内容解析失败”必须分开：
            // 前者不能走损坏恢复，否则可能用默认值覆盖仍然有效的配置。
            ioFailed = true;
            Log.Warn($"读取设置文件失败（I/O）: {ex.Message}");
            return false;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                empty = true;
                return true;
            }

            settings = SettingsLoader.Load(text, out bool needsSave, out bool bad, out bool isFuture);
            migrated = needsSave;
            corrupted = bad;
            future = isFuture;
            return !bad;
        }
        catch (Exception ex)
        {
            // 解析器异常属于内容损坏，按 corrupted 处理（后续可备份现场）。
            corrupted = true;
            Log.Warn($"设置内容解析失败: {ex.Message}");
            return false;
        }
    }

    static bool TryLoadBackup(out Settings settings, out bool migrated, out bool future)
    {
        settings = null;
        migrated = false;
        future = false;

        string backup = $"{Path}.bak";
        if (!File.Exists(backup)) return false;

        try
        {
            string text = File.ReadAllText(backup, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text)) return false;

            Settings loaded = SettingsLoader.Load(text, out bool needsSave, out bool bad, out bool isFuture);
            if (bad) return false;

            settings = loaded;
            migrated = needsSave;
            future = isFuture;
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"读取设置备份失败: {ex.Message}");
            return false;
        }
    }

    public static bool Save(Settings settings) => Save(settings, true);

    static bool Save(Settings settings, bool backup)
    {
        // 并发 Save 共用 settings.json.tmp：串行化，避免互相删掉正在写的临时文件。
        lock (SaveSync)
        {
            return SaveCore(settings, backup);
        }
    }

    static bool SaveCore(Settings settings, bool backup)
    {
        if (settings is null)
        {
            LastSaveError = "设置对象为空";
            return false;
        }

        if (LastLoadReadOnly)
        {
            LastSaveError = "配置处于只读加载状态（更高版本或本次读取失败），拒绝覆盖原文件";
            Log.Warn("配置只读，拒绝保存");
            return false;
        }

        try
        {
            settings.Normalize();
            NormalizeCards(settings);

            string file = Path;

            // 临时文件 → Replace（生成 .bak）→ 失败回退 Copy+Move 的细节统一在 AtomicFile；
            // 这里只负责把结果翻成设置窗能显示的 LastSaveError 与日志（P0-2 / P1-2）。
            AtomicFile.WriteResult result = AtomicFile.Write(file, Json.Serialize(settings), backup);

            if (result.ReplaceError is not null)
                Log.Warn($"设置原子替换失败，回退到备份+覆盖: {result.ReplaceError.Message}");

            if (result.BackupError is not null)
            {
                // 备份失败必须中止：没有备份就不允许覆盖旧配置。
                Log.Error("设置回退备份失败，已中止保存", result.BackupError);
                LastSaveError = "旧配置备份失败（磁盘满 / 只读 / 文件被占用），已中止保存";
                return false;
            }

            if (!result.Success)
            {
                Exception error = result.Error;
                Log.Error("保存设置失败", error);
                LastSaveError = error is not null
                    ? DescribeIoError(error, "写入失败")
                    : "写入失败：未知错误";
                return false;
            }

            Log.Info($"设置已保存: {file}");
            LastSaveError = "";
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("保存设置失败", ex);
            LastSaveError = DescribeIoError(ex, "写入失败");
            return false;
        }
    }

    /// <summary>把写盘异常整理成一句能指导用户行动的原因（进设置窗状态行与日志）。</summary>
    static string DescribeIoError(Exception ex, string prefix)
    {
        string message = string.IsNullOrEmpty(ex.Message) ? "未知错误" : ex.Message;
        while (message.EndsWith("\r\n") || message.EndsWith("\n") || message.EndsWith("\r"))
            message = message.Substring(0, message.Length - Environment.NewLine.Length);

        if (ex is UnauthorizedAccessException)
            return $"{prefix}：访问被拒绝（配置目录或文件只读 / 被更高权限进程占用）";
        if (ex is IOException io && IsDiskFull(io))
            return $"{prefix}：磁盘空间不足";
        if (ex is DirectoryNotFoundException || ex is FileNotFoundException)
            return $"{prefix}：配置目录或文件不见了（被移动 / 删除？）";

        return $"{prefix}：{message}";
    }

    static bool IsDiskFull(IOException ex)
    {
        // Win32 ERROR_DISK_FULL = 0x27, ERROR_DISK_QUOTA_EXCEEDED = 0x4B1;HResult 低 16 位即可。
        int hresult = ex.HResult & 0xFFFF;
        return hresult == 0x0027 || hresult == 0x04B1 || hresult == 0x0070;
    }

    /// <summary>校验损坏配置的备份；失败返回 false（调用方必须当作「没有备份」处理）。</summary>
    static bool TryBackupBroken()
    {
        try
        {
            string file = Path;
            if (File.Exists(file))
            {
                string bad = $"{file}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(file, bad, true);
                Log.Warn($"已备份损坏的配置文件到 {bad}");
                return true;
            }

            return true;    // 没有主文件就不存在「损坏现场」，不算失败
        }
        catch (Exception ex)
        {
            // 磁盘满 / 只读 / 杀软锁文件：再保存就会把损坏现场永久覆盖掉。
            Log.Error($"备份损坏的配置文件失败（现场已被保留在原文件里）: {ex.Message}", ex);
            return false;
        }
    }
}
