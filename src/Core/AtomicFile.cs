using System;
using System.IO;
using System.Text;

namespace MirrorpondHUD.Core;

/// <summary>
/// ConfigStore / CardStateStore 共用的原子写工具：临时文件 → File.Replace（生成 .bak）
/// → 失败回退 Copy + Move。抽出来的目的只有一个：两边的安全语义（备份失败中止、
/// 回退失败清理、权限异常向上传递）不再各写一份、各自漂移（审阅报告 §4.3 / 第三批第 14 项）。
///
/// 日志与错误文案仍由调用方拼装：ConfigStore 要把失败翻成 LastSaveError 给设置窗，
/// CardStateStore 只记 WARN 并返回 false，两边措辞不同，不能在这里硬编码。
/// </summary>
internal static class AtomicFile
{
    /// <summary>
    /// 一次原子写的结果。失败原因按阶段分字段带回，调用方据此决定日志级别与文案：
    /// ReplaceError 非空而另外两个为空 = Replace 失败但回退成功（只记 WARN）；
    /// BackupError 非空 = 旧文件备份失败，已中止，原文件未被覆盖；
    /// Error 非空 = 写临时文件 / Move 等其它阶段的失败。
    /// </summary>
    internal sealed class WriteResult
    {
        public bool Success;
        public Exception ReplaceError;
        public Exception BackupError;
        public Exception Error;
    }

    /// <summary>
    /// 原子写文本。安全语义与两份原实现逐行一致：
    /// 1) 临时文件 = 目标文件 + ".tmp"，与目标一一对应；同一目标的并发由调用方锁串行化
    ///    （ConfigStore.SaveSync / CardStateStore.WriteSync），所以不需要 GUID 命名，
    ///    SelfTest 也按「写入后不留 state.json.tmp」断言清理结果。
    /// 2) 目标已存在且 createBackup 时走 File.Replace，.bak 是上一版；Replace 失败则回退
    ///    「Copy 旧文件到 .bak → Move 临时文件覆盖」。
    /// 3) Copy 备份失败立即中止：宁可这次保存失败，也不能在没有备份的情况下覆盖旧文件。
    /// 4) 只读 / 权限 / 磁盘满等异常不在这里分类或吞掉，原样带回给调用方。
    /// </summary>
    internal static WriteResult Write(string path, string text, bool createBackup)
    {
        WriteResult result = new();
        string tmp = $"{path}.tmp";

        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            WriteDurable(tmp, text ?? "");
            MoveIntoPlace(tmp, path, createBackup, result);
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex;
            return result;
        }
        finally
        {
            CleanupTemp(tmp);
        }
    }

    /// <summary>
    /// 原子写二进制（截图 PNG 等）。语义与 <see cref="Write"/> 完全一致，内容由回调写进临时流。
    ///
    /// 为什么不让调用方自己 Create → Save → Move：截图路径原来的 <c>File.Create</c> 直写目标，
    /// 编码中途失败（磁盘满 / 编码器异常）会留下一张半截图，且已经把上一张验收图覆盖掉；
    /// 临时文件仍是「目标 + .tmp」，目标路径的 .png 语义不变——编码器只认流，不认扩展名。
    /// </summary>
    internal static WriteResult WriteBinary(string path, Action<Stream> write, bool createBackup)
    {
        WriteResult result = new();
        string tmp = $"{path}.tmp";

        try
        {
            if (write is null) throw new ArgumentNullException("write");

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // WriteThrough + Flush(true)：与文本路径一样尽量落盘，避免"文件在、内容是空的"。
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                                               4096, FileOptions.WriteThrough))
            {
                write(stream);
                stream.Flush(true);
            }

            MoveIntoPlace(tmp, path, createBackup, result);
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex;
            return result;
        }
        finally
        {
            CleanupTemp(tmp);
        }
    }

    /// <summary>
    /// 临时文件 → 目标的收尾，两条写入路径共用：
    /// 有备份时 Replace（.bak = 上一版），Replace 被拒则「Copy 备份 → Move 覆盖」；
    /// 备份失败只写 BackupError 并返回（绝不无备份覆盖主文件）；其余异常留给调用方的 catch。
    /// </summary>
    static void MoveIntoPlace(string tmp, string path, bool createBackup, WriteResult result)
    {
        if (File.Exists(path))
        {
            if (createBackup)
            {
                string backup = $"{path}.bak";
                try
                {
                    File.Replace(tmp, path, backup, true);
                }
                catch (Exception ex)
                {
                    // 某些文件系统 / 杀软会拒绝 Replace：退回「先备份再覆盖」。
                    result.ReplaceError = ex;
                    try
                    {
                        File.Copy(path, backup, true);
                    }
                    catch (Exception copyEx)
                    {
                        // 备份失败必须中止，绝不能无备份覆盖主文件。
                        result.BackupError = copyEx;
                        return;
                    }

                    File.Move(tmp, path, true);
                }
            }
            else
            {
                // 损坏恢复路径（backup=false）不能把旧文件复制回 .bak，否则会顶掉唯一的好备份。
                File.Move(tmp, path, true);
            }
        }
        else
        {
            File.Move(tmp, path, true);
        }

        result.Success = true;
    }

    /// <summary>写到临时文件并尽量落盘（WriteThrough + Flush(true)），两处原实现逐字相同。</summary>
    static void WriteDurable(string path, string text)
    {
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                                           4096, FileOptions.WriteThrough))
        {
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
            {
                writer.Write(text ?? "");
                writer.Flush();
            }
            stream.Flush(true);
        }
    }

    /// <summary>
    /// 清理临时文件。它只是一个没有被任何地方引用的中间文件，删不掉不能反过来把
    /// 已经成功的保存/写入报成失败，所以只留 DEBUG 级痕迹（默认不输出）。
    /// </summary>
    static void CleanupTemp(string tmp)
    {
        try
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        catch (Exception ex)
        {
            Log.Debug($"清理原子写临时文件失败({tmp}): {ex.Message}");
        }
    }
}
