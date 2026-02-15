public class AppSettings
{
    public bool AutoRecordOnPlay { get; set; } = false;
    public string RecordingOutputPath { get; set; } = "";
    public string RecordingFileName { get; set; } = "";
    public int RecordingWidth { get; set; } = 720;
    public int RecordingHeight { get; set; } = 1280;
    public double LyricOffset { get; set; } = 0;

    private static readonly string SettingsFilePath = System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "TikTokMusicPlayer",
        "settings.json"
    );

    public void Save()
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            var json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            System.IO.File.WriteAllText(SettingsFilePath, json);
        }
        catch { }
    }

    public static AppSettings Load()
    {
        try
        {
            if (System.IO.File.Exists(SettingsFilePath))
            {
                var json = System.IO.File.ReadAllText(SettingsFilePath);
                return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }
}
