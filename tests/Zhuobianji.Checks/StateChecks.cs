using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zhuobianji.Core;

namespace Zhuobianji.Checks;

// 状态层：直接测 NoteBook（只用 Core），不起界面程序、不开窗口。
// 窗口组只测界面那一半：窗口跟着变、提示显示、按钮链路走通。
internal static partial class Program
{
    // 假窗口：保存前交出「编辑器里」还没交出来的正文和位置，记下每次存完的结果。
    private sealed class FakeView : INoteView
    {
        public string? Text;
        public double? Left;
        public List<string?> Results { get; } = [];
        public NoteData Capture(NoteData data) => data with { Text = Text ?? data.Text, Left = Left ?? data.Left };
        public void Saved(string? error) => Results.Add(error);
    }

    private static FileStream Lock(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    private static void StateChecks()
    {
        StateSaving();
        StateCollection();
        StateOrder();
        StatePreferences();
    }

    private static void StateSaving()
    {
        var directory = Path.Combine(root, "state-saving");
        var first = new NoteData { Text = "第一张\n第二行", Left = 120, IsPinned = true };
        var second = new NoteData { Text = "第二张", IsHidden = true };
        using (var seed = new NoteStore(directory)) { seed.Save(first); seed.Save(second); }
        NoteData[] expected;
        using (var store = new NoteStore(directory))
        {
            var book = new NoteBook(store);
            var changes = new List<(NoteEntry Note, NoteData Before)>();
            book.Changed += (note, before) => changes.Add((note, before));
            Check(book.Load().Count == 0 && book.Notes.Count == 2 && SameNoteData(book.Find(first.Id)?.Data, first) &&
                SameNoteData(book.Find(second.Id)?.Data, second) && book.Notes.All(note => !note.IsDirty),
                "读入全部便签，数据和文件一致，刚读入的不算待保存");
            var note = book.Find(first.Id)!;
            book.Update(note, data => data with { IsPinned = true });
            Check(!note.IsDirty && changes.Count == 0, "改成一样的值不算改动：不排队保存、不发通知");
            book.Update(note, data => data with { IsPinned = false });
            Check(note.IsDirty && changes.Count == 1 && changes[0].Note == note && changes[0].Before.IsPinned && !note.Data.IsPinned,
                "改状态排队保存，通知带着改之前的数据");

            var view = new FakeView { Text = "编辑器里还没交出来的正文", Left = 300 };
            note.View = view;
            Check(book.Save(note) && !note.IsDirty && note.SaveError is null && view.Results.SequenceEqual([null]) &&
                note.Data.Text == view.Text && note.Data.Left == 300 && changes.Count == 2 &&
                store.LoadAll().Notes.Single(data => data.Id == note.Id).Text == view.Text,
                "保存时向窗口取正文和位置，存完告诉窗口结果并发通知");
            Check(book.Save(note) && view.Results.Count == 1, "没有待保存的改动时不写文件");

            var savedBytes = File.ReadAllBytes(store.GetPath(note.Id));
            view.Text = "写不进去的正文";
            book.MarkDirty(note);
            using (Lock(store.GetPath(note.Id)))
            {
                Check(!book.Save(note) && note.IsDirty && note.SaveError is not null && view.Results.Last() == note.SaveError &&
                    note.Data.Text != view.Text && savedBytes.SequenceEqual(File.ReadAllBytes(store.GetPath(note.Id))),
                    "写入失败留着待保存和错误，内存和旧文件都不动，窗口收到错误");
                var beforeCommit = note.Data;
                Check(!book.TryCommit(note, data => data with { IsHidden = true }) && note.Data == beforeCommit && note.IsDirty,
                    "必须落盘的改动（关闭隐藏）存不上就恢复原状态");
                Check(!book.SaveAll(), "退出前有一张没存上就报告失败");
            }
            Check(book.Save(note) && !note.IsDirty && note.SaveError is null && view.Results.Last() is null &&
                store.LoadAll().Notes.Single(data => data.Id == note.Id).Text == "写不进去的正文",
                "解除占用后重试保存成功，错误清掉");

            var beforeClose = store.LoadAll().Notes.Single(data => data.Id == note.Id);
            view.Text = "关闭前刚写的";
            book.MarkDirty(note);
            Check(book.TryCommit(note, data => data with { IsHidden = true }) && note.Data.IsHidden && note.Data.Text == "关闭前刚写的" &&
                SameNoteData(JsonSerializer.Deserialize<NoteData>(File.ReadAllText(store.GetPath(note.Id) + ".bak")), beforeClose),
                "待写正文和状态一次提交，上一版副本是提交前成功存的那一版");

            var added = book.Add(new NoteData { Text = "新便签" });
            Check(added.IsDirty && book.Notes.Count == 3 && !File.Exists(store.GetPath(added.Id)) && book.SaveAll() &&
                File.Exists(store.GetPath(added.Id)) && !added.IsDirty, "新便签先等着保存，退出前全部存上");

            var other = book.Find(second.Id)!;
            book.Update(other, data => data with { Text = "删除前改过一次" });
            book.Save(other);
            other.View = new FakeView();
            using (Lock(store.GetPath(other.Id)))
            {
                var failed = false;
                try { book.Delete(other); }
                catch (IOException) { failed = true; }
                Check(failed && book.Find(other.Id) == other && other.View is not null && File.Exists(store.GetPath(other.Id)),
                    "文件被占用删不掉时便签留着");
            }
            book.Delete(other);
            Check(book.Find(other.Id) is null && other.View is null && book.Notes.Count == 2 &&
                !Directory.EnumerateFiles(store.DirectoryPath, other.Id.ToString("N") + "*").Any() &&
                !Directory.Exists(Path.Combine(store.DirectoryPath, "Deleted")), "永久删除不留副本，不再连着窗口");
            expected = book.Notes.Select(entry => entry.Data).ToArray();
        }
        using (var store = new NoteStore(directory))
        {
            var book = new NoteBook(store);
            book.Load();
            Check(book.Notes.Count == expected.Length && expected.All(data => SameNoteData(book.Find(data.Id)?.Data, data)),
                "换个实例重新读入，每张便签和退出前一致");
        }
    }

    private static void StateCollection()
    {
        var directory = Path.Combine(root, "state-collection");
        Guid mainId, bId;
        using (var store = new NoteStore(directory))
        {
            var book = new NoteBook(store);
            book.Load();
            var main = book.Add(new NoteData { Text = "主便签" });
            var a = book.Add(new NoteData { Text = "记录 A" });
            var b = book.Add(new NoteData { Text = "记录 B" });
            Check(book.SaveAll() && !book.Collect(a) && book.Main is null, "没有主便签时不能收纳");
            Check(book.SetMain(main) && book.Main == main && !book.SetMain(a) && !a.Data.IsCollectionMain &&
                store.LoadAll().Notes.Count(data => data.IsCollectionMain) == 1, "设为主便签马上存上，只能有一张");
            Check(book.Collect(a) && book.Collect(b) && !book.Collect(main) && book.Collected(main).SequenceEqual([a, b]) &&
                book.Roots.SequenceEqual([main]) && store.LoadAll().Notes.Count(data => data.CollectedBy == main.Id && data.IsHidden) == 2,
                "收纳记下关系并收起、马上存上，主便签不能收进自己");
            using (Lock(store.GetPath(a.Id)))
                Check(!book.Release(a) && a.Data.CollectedBy == main.Id && a.Data.IsHidden, "取出时存不上就保留收纳关系");
            Check(book.Release(a) && a.Data.CollectedBy is null && !a.Data.IsHidden && book.Collected(main).SequenceEqual([b]) &&
                book.Release(a), "取出解除收纳并显示；不是收着的取出也算成功");
            (mainId, bId) = (main.Id, b.Id);
        }
        using (var store = new NoteStore(directory))
        {
            var book = new NoteBook(store);
            book.Load();
            Check(book.Main?.Id == mainId && book.Collected(book.Main).Select(note => note.Id).SequenceEqual([bId]) &&
                book.Find(bId)!.Data.IsHidden, "重新读入，主便签和收纳关系不变");
            File.WriteAllText(store.GetPath(mainId), "主便签读不了");
        }
        using (var store = new NoteStore(directory))
        {
            var book = new NoteBook(store);
            var errors = book.Load();
            book.ReleaseOrphans();
            Check(errors.Count == 1 && book.Main is null && book.Find(bId)!.Data.CollectedBy is null &&
                store.LoadAll().Notes.Single(data => data.Id == bId).CollectedBy is null && File.ReadAllText(store.GetPath(mainId)) == "主便签读不了",
                "主便签读不了时收着的便签放回桌面并存上，读不了的原文件不动");
        }
    }

    private static void StateOrder()
    {
        var directory = Path.Combine(root, "state-order");
        var mainData = new NoteData { IsCollectionMain = true, Text = "主便签" };
        var children = new[] { "记录 A", "记录 B", "记录 C" }.Select((text, index) => new NoteData
            { Text = text, CollectedBy = mainData.Id, IsHidden = true, CreatedAt = mainData.CreatedAt.AddSeconds(index) }).ToArray();
        using (var seed = new NoteStore(directory))
        {
            seed.Save(mainData);
            foreach (var child in children) seed.Save(child);
            var oldRecord = JsonNode.Parse(File.ReadAllText(seed.GetPath(mainData.Id)))!.AsObject();
            oldRecord.Remove(nameof(NoteData.CollectionOrder));
            File.WriteAllText(seed.GetPath(mainData.Id), oldRecord.ToJsonString());
        }
        Guid[] order;
        using (var store = new NoteStore(directory))
        {
            var book = new NoteBook(store);
            book.Load();
            var main = book.Main!;
            var (a, b, c) = (book.Find(children[0].Id)!, book.Find(children[1].Id)!, book.Find(children[2].Id)!);
            Check(main.Data.CollectionOrder.Length == 0 && book.Collected(main).SequenceEqual([a, b, c]), "旧记录没有排序字段时按新建时间排");
            Check(book.Move(main, c.Id, 0) && book.Collected(main).SequenceEqual([c, a, b]) &&
                store.LoadAll().Notes.Single(data => data.Id == main.Id).CollectionOrder.SequenceEqual([c.Id, a.Id, b.Id]),
                "挪到最前，顺序马上存进主便签");
            Check(book.Move(main, a.Id, 3) && book.Collected(main).SequenceEqual([c, b, a]), "可以从前面挪到末尾");
            var unchangedAt = main.Data.UpdatedAt;
            Check(book.Move(main, a.Id, 3) && main.Data.UpdatedAt == unchangedAt && !book.Move(main, Guid.NewGuid(), 0),
                "放回原位置不重复写入，不是它收着的不能挪");
            var d = book.Add(new NoteData { Text = "新增记录 D" });
            book.Save(d);
            Check(book.Collect(d) && book.Collected(main).SequenceEqual([c, b, a, d]), "新收纳的排在已有顺序后面");
            book.Move(main, d.Id, 0);
            book.Release(b);
            book.Delete(a);
            Check(book.Collected(main).SequenceEqual([d, c]) && store.LoadAll().Notes.Single(data => data.Id == c.Id).Text == "记录 C",
                "取出和删除后剩下的顺序和正文不变");
            order = book.Collected(main).Select(note => note.Id).ToArray();
        }
        using (var store = new NoteStore(directory))
        {
            var book = new NoteBook(store);
            book.Load();
            Check(book.Collected(book.Main!).Select(note => note.Id).SequenceEqual(order), "重新读入顺序一致");
        }
    }

    private static void StatePreferences()
    {
        // 0.8.x 的数据：设置里没有深浅色模式，每张便签各存一份 Dark。第一次读入按主便签定下模式、马上存进设置，之后不再写 Dark。
        using (var store = new NoteStore(Path.Combine(root, "state-legacy")))
        {
            var legacyMain = new NoteData { Text = "0.8.x 的深色主便签", IsCollectionMain = true,
                Appearance = new NoteAppearance { Palette = "green", LegacyDark = true } };
            store.Save(legacyMain);
            store.SavePreferences(new NotePreferences { Appearance = new NoteAppearance { LegacyDark = false } });
            var settingsPath = Path.Combine(store.DirectoryPath, "settings.json");
            Check(File.ReadAllText(store.GetPath(legacyMain.Id)).Contains("\"Dark\": true") && File.ReadAllText(settingsPath).Contains("\"Dark\":") &&
                store.LoadPreferences().ThemeMode is null, "准备好 0.8.x 形状的数据");
            var book = new NoteBook(store);
            Check(book.Load().Count == 0 && book.Preferences.ThemeMode == NoteThemeMode.Dark &&
                store.LoadPreferences().ThemeMode == NoteThemeMode.Dark && !File.ReadAllText(settingsPath).Contains("\"Dark\":"),
                "第一次读入按主便签定下深色，马上存进设置，设置里不再写 Dark");
            var main = book.Main!;
            Check(main.Data.Appearance.LegacyDark is null && main.Data.Appearance.Palette == "green", "便签外观里的旧 Dark 读完清掉，配色保留");
            book.MarkDirty(main);
            Check(book.Save(main) && !File.ReadAllText(store.GetPath(main.Id)).Contains("\"Dark\":"), "迁移以后保存便签不再写 Dark");
        }
        using (var store = new NoteStore(Path.Combine(root, "state-legacy-default")))
        {
            store.Save(new NoteData { Text = "没有主便签" });
            store.SavePreferences(new NotePreferences { Appearance = new NoteAppearance { LegacyDark = true } });
            var book = new NoteBook(store);
            book.Load();
            Check(book.Preferences.ThemeMode == NoteThemeMode.Dark && store.LoadPreferences().ThemeMode == NoteThemeMode.Dark,
                "没有主便签时按默认外观定深浅色");
        }
        using (var store = new NoteStore(Path.Combine(root, "state-settings")))
        {
            store.Save(new NoteData { Text = "便签照常读" });
            File.WriteAllText(Path.Combine(store.DirectoryPath, "broken.json"), "不是 JSON");
            var settingsPath = Path.Combine(store.DirectoryPath, "settings.json");
            File.WriteAllText(settingsPath, "设置读坏了");
            var book = new NoteBook(store);
            var errors = book.Load();
            Check(errors.Count == 2 && errors.Any(error => error.StartsWith("默认外观读取失败")) && book.Notes.Count == 1 &&
                book.Preferences.ThemeMode == NoteThemeMode.Light && File.ReadAllText(settingsPath) == "设置读坏了",
                "设置和一张便签读不了：都报告，先用初始设置，原文件不覆盖，其他便签照常读");
            var persisted = book.Preferences;
            using (Lock(settingsPath))
                Check(!book.TrySavePreferences(persisted with { ShowFormatWarnings = !persisted.ShowFormatWarnings }, "格式提示设置未保存", out var error) &&
                    error!.StartsWith("格式提示设置未保存：") && book.Preferences == persisted && File.ReadAllText(settingsPath) == "设置读坏了",
                    "设置写不进去时内存和文件都不变，返回原因");
            var changed = persisted with { ShowFormatWarnings = !persisted.ShowFormatWarnings };
            Check(book.TrySavePreferences(changed, "格式提示设置未保存", out _) && book.Preferences == changed && store.LoadPreferences() == changed,
                "写进去以后内存和文件一致");
        }
    }
}
