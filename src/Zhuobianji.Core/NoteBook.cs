using System.Text.Json;

namespace Zhuobianji.Core;

// 便签的状态层：全部便签的数据、收纳关系、改状态的入口和保存，不碰界面。
// 状态字段只通过两个入口修改：Update 改了等着存（失败也不影响界面的操作），TryCommit 改了马上存、失败恢复原状态。
public sealed class NoteBook
{
    private readonly NoteStore store;
    private readonly List<NoteEntry> notes = [];

    public NoteBook(NoteStore store) => this.store = store;

    public NotePreferences Preferences { get; private set; } = new();
    public IReadOnlyList<NoteEntry> Notes => notes;

    // 一张便签的数据变了或保存了一次（第二个参数是之前的数据）：窗口、标签、托盘靠它跟上。
    public event Action<NoteEntry, NoteData>? Changed;

    public NoteEntry? Find(Guid id) => notes.Find(note => note.Id == id);
    public NoteEntry? Main => notes.Find(note => note.Data.IsCollectionMain);
    public IEnumerable<NoteEntry> Roots => notes.Where(note => note.Data.CollectedBy is null);

    // 收在主便签里的便签，按主便签记的顺序；没记顺序的按新建时间排在后面。
    public IEnumerable<NoteEntry> Collected(NoteEntry main) =>
        notes.Where(note => note.Data.CollectedBy == main.Id)
            .OrderBy(note => Array.IndexOf(main.Data.CollectionOrder, note.Id) is var index && index >= 0 ? index : int.MaxValue)
            .ThenBy(note => note.Data.CreatedAt);

    // 读入全部便签和设置，返回读不了的说明（原文件保留，其他便签照常用）。
    public List<string> Load()
    {
        var loaded = store.LoadAll();
        var errors = loaded.Errors.ToList();
        var preferencesRead = false;
        try { Preferences = store.LoadPreferences(); preferencesRead = true; }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        { errors.Add("默认外观读取失败，暂用初始外观：" + ex.Message); }
        // 0.8.x 的设置里没有深浅色模式，而是每张便签各存一份 Dark：按主便签（没有就按默认外观）定下模式，马上存进设置。
        // 存上了，便签里的旧 Dark 就清掉、之后不再写出。没存上（设置文件读坏了不覆盖它，或者写不进）就先按定下的模式用着，
        // 便签里的旧 Dark 留着、保存时照原样写回，下次启动再定一次；否则主便签一保存，下次就没了依据。
        var keepLegacyDark = false;
        if (Preferences.ThemeMode is null)
        {
            var main = loaded.Notes.FirstOrDefault(data => data.IsCollectionMain);
            var dark = (main?.Appearance ?? Preferences.Appearance).LegacyDark == true;
            var migrated = Preferences with { ThemeMode = dark ? NoteThemeMode.Dark : NoteThemeMode.Light, Appearance = Preferences.Appearance with { LegacyDark = null } };
            string? error = null;
            if (!preferencesRead || !TrySavePreferences(migrated, "深浅色设置未保存，下次启动会再试", out error))
            {
                Preferences = migrated;
                keepLegacyDark = true;
                if (error is not null) errors.Add(error);
            }
        }
        Preferences = Preferences with { Appearance = Preferences.Appearance with { LegacyDark = null } };
        notes.Clear();
        foreach (var data in loaded.Notes)
            notes.Add(new NoteEntry(keepLegacyDark ? data : data with { Appearance = data.Appearance with { LegacyDark = null } }));
        return errors;
    }

    // 收纳它的主便签不在了（删了或读不了）：放回桌面，不再隐藏（收着的便签平时是隐藏的）。
    public void ReleaseOrphans()
    {
        foreach (var note in notes.ToArray())
        {
            if (note.Data.CollectedBy is not Guid parent || Main?.Id == parent) continue;
            Update(note, data => data with { IsCollectionMain = false, CollectedBy = null, IsHidden = false });
            Save(note);
        }
    }

    // 新便签：加进来，等着第一次保存。
    public NoteEntry Add(NoteData data)
    {
        var note = new NoteEntry(data) { IsDirty = true };
        notes.Add(note);
        return note;
    }

    // 只从内存里拿掉（内容读不了的便签），原文件保留。
    public void Forget(NoteEntry note) => notes.Remove(note);

    // 只改字段、标成待保存、发 Changed，不排保存：调用的地方要么接着 Save，要么经窗口的 Update（它会排上 400ms 的防抖保存）。
    public void Update(NoteEntry note, Func<NoteData, NoteData> change)
    {
        var before = note.Data;
        var next = change(before);
        if (next == before) return;
        note.Data = next;
        note.IsDirty = true;
        Changed?.Invoke(note, before);
    }

    public bool TryCommit(NoteEntry note, Func<NoteData, NoteData> change)
    {
        var before = note.Data;
        Update(note, change);
        if (Save(note)) return true;
        Update(note, _ => before);
        return false;
    }

    // 窗口里有还没交出来的改动（正文、位置），下次保存时取。
    public void MarkDirty(NoteEntry note) => note.IsDirty = true;

    public bool Save(NoteEntry note)
    {
        if (!note.IsDirty) return true;
        var before = note.Data;
        var snapshot = (note.View?.Capture(before) ?? before) with { UpdatedAt = DateTimeOffset.UtcNow };
        try { store.Save(snapshot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            note.SaveError = ex.Message;
            note.View?.Saved(ex.Message);
            Changed?.Invoke(note, before);
            return false;
        }
        note.Data = snapshot;
        note.IsDirty = false;
        note.SaveError = null;
        note.View?.Saved(null);
        Changed?.Invoke(note, before);
        return true;
    }

    // 退出、会话结束前：每张都存一遍，任何一张没存上返回 false。
    public bool SaveAll()
    {
        var succeeded = true;
        foreach (var note in notes.ToArray()) if (!Save(note)) succeeded = false;
        return succeeded;
    }

    // 永久删除：文件和副本删掉以后才从内存里拿掉；删不掉时抛出，便签留着。收着子便签的主便签要先释放它们。
    public void Delete(NoteEntry note)
    {
        store.Delete(note.Id);
        notes.Remove(note);
        note.View = null;
    }

    public bool SetMain(NoteEntry note)
    {
        if (Main is { } main && main != note || note.Data.CollectedBy is not null) return false;
        return TryCommit(note, data => data with { IsCollectionMain = true, CollectedBy = null });
    }

    public bool Collect(NoteEntry note)
    {
        if (Main is not { } main || main == note || note.Data.IsCollectionMain) return false;
        return TryCommit(note, data => data with { CollectedBy = main.Id, IsHidden = true });
    }

    public bool Release(NoteEntry note) =>
        note.Data.CollectedBy is null || TryCommit(note, data => data with { CollectedBy = null, IsHidden = false });

    // 把一张收纳便签挪到 insertionIndex（按挪动之前的位置算，插在这个位置的标签前面）。
    public bool Move(NoteEntry main, Guid id, int insertionIndex)
    {
        var order = Collected(main).Select(note => note.Id).ToList();
        var oldIndex = order.IndexOf(id);
        if (oldIndex < 0) return false;
        insertionIndex = Math.Clamp(insertionIndex, 0, order.Count);
        if (insertionIndex > oldIndex) insertionIndex--;
        if (insertionIndex == oldIndex) return true;
        order.RemoveAt(oldIndex);
        order.Insert(insertionIndex, id);
        return TryCommit(main, data => data with { CollectionOrder = order.ToArray() });
    }

    public bool TrySavePreferences(NotePreferences preferences, string failure, out string? error)
    {
        try { store.SavePreferences(preferences); Preferences = preferences; error = null; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { error = failure + "：" + ex.Message; return false; }
    }
}
