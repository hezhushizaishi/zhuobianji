using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

internal static partial class Program
{
    private static void StorageChecks()
    {
        var directory = Path.Combine(root, "storage");
        using (var store = new NoteStore(directory))
        {
            var first = new NoteData { Text = "中文 📝\n第二行\t制表符", Left = -300, IsPinned = true };
            var second = new NoteData { Text = "独立便签", IsHidden = true };
            store.Save(first);
            store.Save(second);
            var loaded = store.LoadAll();
            Check(loaded.Errors.Count == 0 && loaded.Notes.Count == 2 && loaded.Notes.Any(note => SameNoteData(note, first)) && loaded.Notes.Any(note => SameNoteData(note, second)),
                "Unicode、多行、窗口设置完整往返，两便签互不覆盖");
            var updated = first with { Text = "最新内容" };
            store.Save(updated);
            var backup = JsonSerializer.Deserialize<NoteData>(File.ReadAllText(store.GetPath(first.Id) + ".bak"));
            Check(SameNoteData(backup, first), "覆盖保存保留上次成功版本");
            var originalBytes = File.ReadAllBytes(store.GetPath(first.Id));
            using (var locked = new FileStream(store.GetPath(first.Id), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failed = false;
                try { store.Save(updated with { Text = "不应写入" }); }
                catch (IOException) { failed = true; }
                Check(failed && originalBytes.SequenceEqual(File.ReadAllBytes(store.GetPath(first.Id))), "替换失败时现有内容保持完整");
            }
            var blocked = false;
            try { using var duplicate = new NoteStore(directory); }
            catch (IOException) { blocked = true; }
            Check(blocked, "同一数据目录阻止第二个写入实例");
            var brokenPath = Path.Combine(directory, "broken.json");
            File.WriteAllText(brokenPath, "不是 JSON");
            loaded = store.LoadAll();
            Check(loaded.Notes.Count == 2 && loaded.Errors.Count == 1 && File.ReadAllText(brokenPath) == "不是 JSON", "损坏记录报告错误且原文不被改写");
            store.Save(second);
            File.WriteAllText(store.GetPath(second.Id) + ".tmp", "待写副本");
            store.Delete(second.Id);
            Check(!File.Exists(store.GetPath(second.Id)) && !File.Exists(store.GetPath(second.Id) + ".bak") &&
                !File.Exists(store.GetPath(second.Id) + ".tmp") && store.LoadAll().Notes.Count == 1,
                "永久删除同时清理活动记录、保存副本与临时文件");
        }
        using var reopened = new NoteStore(directory);
        Check(reopened.LoadAll().Notes.Count == 1, "释放实例后可重新打开数据目录");
        Console.WriteLine($"PASS storage：{passed} 项检查");
    }
}
