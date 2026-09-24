using Godot;

namespace SurvivalcraftGodot;

/// <summary>
/// 可持久化的设置状态。界面只读写这里，不另存一份。
/// </summary>
public sealed class SettingsState
{
	public const string ConfigPath = "user://settings.cfg";

	const string SectionAudio = "audio";
	const string SectionGraphics = "graphics";
	const string KeyMusicVolume = "music_volume";
	const string KeyViewDistanceName = "view_distance_name";

	public float MusicVolume { get; set; } = 0.8f;
	public string ViewDistanceName { get; set; } = "中";

	public static SettingsState Load()
	{
		var state = new SettingsState();
		var cfg = new ConfigFile();
		Error err = cfg.Load(ConfigPath);
		if (err != Error.Ok)
		{
			return state;
		}

		state.MusicVolume = (float)cfg.GetValue(SectionAudio, KeyMusicVolume, state.MusicVolume);
		state.ViewDistanceName = (string)cfg.GetValue(SectionGraphics, KeyViewDistanceName, state.ViewDistanceName);
		return state;
	}

	public void Save()
	{
		var cfg = new ConfigFile();
		cfg.Load(ConfigPath);
		cfg.SetValue(SectionAudio, KeyMusicVolume, MusicVolume);
		cfg.SetValue(SectionGraphics, KeyViewDistanceName, ViewDistanceName);
		cfg.Save(ConfigPath);
	}

	/// <summary>
	/// 把已加载的音量应用到音频总线（工程未改 project.godot，无独立 Music 总线时落到 Master）。
	/// </summary>
	public void ApplyAudio()
	{
		int bus = AudioServer.GetBusIndex("Music");
		if (bus < 0)
		{
			bus = 0;
		}

		float linear = Mathf.Clamp(MusicVolume, 0f, 1f);
		AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Mathf.Max(linear, 0.0001f)));
		AudioServer.SetBusMute(bus, linear <= 0.0001f);
	}
}
