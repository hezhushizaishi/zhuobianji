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
            using (var sparse = new NoteStore(Path.Combine(root, "storage-sparse")))
            {
                // 早期和 0.8.x 的文件少字段（早期便签没有格式、外观、位置，旧设置没有快捷键、深浅色）：缺的字段取代码里的默认值。
                // 7e 试过编译时生成的 JSON 读法：init 属性缺了会读成空值和 0，早期便签提示「外观设置无效」，所以撤回了。
                var early = new NoteData { Version = 1, Text = "早期便签", CreatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00+00:00"), UpdatedAt = DateTimeOffset.Parse("2025-01-02T00:00:00+00:00") };
                File.WriteAllText(sparse.GetPath(early.Id),
                    "{\"Version\":1,\"Id\":\"" + early.Id + "\",\"Text\":\"早期便签\",\"CreatedAt\":\"2025-01-01T00:00:00+00:00\",\"UpdatedAt\":\"2025-01-02T00:00:00+00:00\"}");
                File.WriteAllText(Path.Combine(sparse.DirectoryPath, "settings.json"), "{\"DefaultFormat\":0}");
                var read = sparse.LoadAll();
                Check(read.Errors.Count == 0 && SameNoteData(read.Notes.SingleOrDefault(), early) &&
                    sparse.LoadPreferences() == new NotePreferences { DefaultFormat = 0 },
                    "早期文件缺的字段（格式、外观、位置；设置里的快捷键等）按默认值读入");
            }
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
            // 手改成带连字符的文件名：读进来的话保存会写到另一个文件，同一张变成两份。所以不读，原文件留着，提示怎么改名。
            using (var names = new NoteStore(Path.Combine(root, "storage-names")))
            {
                var renamed = new NoteData { Text = "手改了文件名" };
                names.Save(renamed);
                var hyphenated = Path.Combine(names.DirectoryPath, $"{renamed.Id:D}.json");
                File.Move(names.GetPath(renamed.Id), hyphenated);
                var read = names.LoadAll();
                Check(read.Notes.Count == 0 && read.Errors.Count == 1 && read.Errors[0].Contains($"{renamed.Id:N}.json") && File.Exists(hyphenated),
                    "带连字符的文件名不读入（免得存出第二份），提示改成程序用的文件名，原文件留着");
                File.Move(hyphenated, names.GetPath(renamed.Id));
                read = names.LoadAll();
                Check(read.Errors.Count == 0 && read.Notes.Count == 1 && read.Notes[0].Text == renamed.Text, "照提示改名后读得回来");
            }
            store.Save(second);
            File.WriteAllText(store.GetPath(second.Id) + ".tmp", "待写副本");
            store.Delete(second.Id);
            Check(!File.Exists(store.GetPath(second.Id)) && !File.Exists(store.GetPath(second.Id) + ".bak") &&
                !File.Exists(store.GetPath(second.Id) + ".tmp") && store.LoadAll().Notes.Count == 1,
                "永久删除同时清理活动记录、保存副本与临时文件");
        }
        using var reopened = new NoteStore(directory);
        Check(reopened.LoadAll().Notes.Count == 1, "释放实例后可重新打开数据目录");
        // 0.8.x 每张便签的外观里存着 Dark：照常读进来（只用来定下程序的深浅色模式），清掉以后不再写出。
        var legacyAppearance = JsonSerializer.Deserialize<NoteAppearance>(
            "{\"Palette\":\"green\",\"Dark\":true,\"FontFamily\":\"Microsoft YaHei UI\",\"FontSize\":17,\"Opacity\":1}")!;
        Check(legacyAppearance.LegacyDark == true && legacyAppearance.Palette == "green" &&
            !JsonSerializer.Serialize(legacyAppearance with { LegacyDark = null }).Contains("Dark"), "0.8.x 外观里的深浅色照常读入，清掉后不再写出");
        var withPreview = JsonSerializer.Deserialize<NoteData>("{\"Id\":\"" + Guid.NewGuid() + "\",\"Text\":\"第一行\\n第二行\",\"Preview\":\"旧的预览\"}")!;
        Check(withPreview.Preview == "第一行" && !JsonSerializer.Serialize(withPreview).Contains("Preview\""),
            "第一行预览由正文算出、不写进文件，旧文件里的 Preview 字段照常忽略");
    }
}
