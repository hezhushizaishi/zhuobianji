using System.Text.Json;

namespace Zhuobianji.Core;

public sealed class NoteStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly FileStream instanceLock;
    public string DirectoryPath { get; }

    public NoteStore(string directoryPath)
    {
        DirectoryPath = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(DirectoryPath);
        // 同一数据目录只允许一个写入者，避免重复启动覆盖便签。
        instanceLock = new FileStream(Path.Combine(DirectoryPath, ".instance.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    public (IReadOnlyList<NoteData> Notes, IReadOnlyList<string> Errors) LoadAll()
    {
        var notes = new List<NoteData>();
        var errors = new List<string>();
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json").Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(path) == "settings.json") continue;
            try
            {
                using var stream = File.OpenRead(path);
                var data = JsonSerializer.Deserialize<NoteData>(stream) ?? throw new InvalidDataException("文件内容为空。");
                Validate(data);
                if (!Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var fileId) || fileId != data.Id)
                    throw new InvalidDataException("文件名与便签编号不一致。");
                notes.Add(data);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(path)}：{ex.Message}");
            }
        }
        return (notes.OrderBy(n => n.CreatedAt).ToArray(), errors);
    }

    public void Save(NoteData data)
    {
        Validate(data);
        WriteJson(GetPath(data.Id), data);
    }

    public NotePreferences LoadPreferences()
    {
        var path = Path.Combine(DirectoryPath, "settings.json");
        if (!File.Exists(path)) return new();
        using var stream = File.OpenRead(path);
        var result = JsonSerializer.Deserialize<NotePreferences>(stream) ?? throw new InvalidDataException("设置文件为空。");
        ValidateAppearance(result.Appearance);
        ValidateHotkeys(result.Hotkeys);
        if (!Enum.IsDefined(result.DefaultFormat)) throw new InvalidDataException("默认编辑格式无效。");
        return result;
    }

    public void SavePreferences(NotePreferences preferences)
    {
        ValidateAppearance(preferences.Appearance);
        ValidateHotkeys(preferences.Hotkeys);
        if (!Enum.IsDefined(preferences.DefaultFormat)) throw new InvalidDataException("默认编辑格式无效。");
        WriteJson(Path.Combine(DirectoryPath, "settings.json"), preferences);
    }

    private static void WriteJson<T>(string destination, T value)
    {
        var temporary = destination + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, JsonOptions);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(destination))
            File.Replace(temporary, destination, destination + ".bak");
        else
            File.Move(temporary, destination);
    }

    public void Delete(Guid id)
    {
        var path = GetPath(id);
        // 先清理副本，任何一步失败都保留活动记录，窗口可继续使用。
        File.Delete(path + ".bak");
        File.Delete(path + ".tmp");
        var oldDeleted = Path.Combine(DirectoryPath, "Deleted");
        if (Directory.Exists(oldDeleted))
            foreach (var copy in Directory.EnumerateFiles(oldDeleted, $"{id:N}.*.json"))
                File.Delete(copy);
        File.Delete(path);
    }
    public string GetPath(Guid id) => Path.Combine(DirectoryPath, $"{id:N}.json");

    private static void Validate(NoteData data)
    {
        if (data.Version is not (1 or 2)) throw new InvalidDataException($"不支持的数据版本：{data.Version}。");
        if (data.Id == Guid.Empty || data.Text is null) throw new InvalidDataException("缺少便签编号或文本。");
        if (!Enum.IsDefined(data.Format)) throw new InvalidDataException("便签编辑格式无效。");
        ValidateAppearance(data.Appearance);
        if (!double.IsFinite(data.Left) || !double.IsFinite(data.Top) || !double.IsFinite(data.Width) ||
            !double.IsFinite(data.Height) || data.Width <= 0 || data.Height <= 0)
            throw new InvalidDataException("窗口位置或尺寸无效。");
    }

    private static void ValidateAppearance(NoteAppearance appearance)
    {
        if (appearance is null || string.IsNullOrWhiteSpace(appearance.FontFamily) ||
            !double.IsFinite(appearance.FontSize) || appearance.FontSize is < 12 or > 32 ||
            !double.IsFinite(appearance.Opacity) || appearance.Opacity is < 0 or > 1)
            throw new InvalidDataException("便签外观设置无效。");
    }

    private static void ValidateHotkeys(NoteHotkeys hotkeys)
    {
        if (hotkeys is null || hotkeys.NewNote is null || hotkeys.ToggleNotes is null)
            throw new InvalidDataException("快捷键设置无效。");
    }

    public void Dispose() => instanceLock.Dispose();
}
