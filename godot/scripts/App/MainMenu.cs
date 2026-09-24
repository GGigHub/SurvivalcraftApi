using Godot;

namespace SurvivalcraftGodot;

/// <summary>
/// 主菜单：进入世界、设置、退出。设置读写 <see cref="SettingsState"/>。
/// </summary>
public partial class MainMenu : Control
{
	static readonly string[] ViewDistanceNames = ["近", "中", "远", "极远"];

	SettingsState _settings = new();

	VBoxContainer? _menuColumn;
	PanelContainer? _settingsPanel;
	CenterContainer? _settingsLayer;
	HSlider? _musicSlider;
	OptionButton? _viewDistanceOption;
	Label? _musicValueLabel;

	public override void _Ready()
	{
		if (Acceptance.Requested())
		{
			int code = Acceptance.Run();
			GetTree().Quit(code);
			return;
		}

		_settings = SettingsState.Load();
		_settings.ApplyAudio();
		BuildUi();
		RefreshSettingsUi();
		ShowMenu(true);
	}

	void BuildUi()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		GrowHorizontal = GrowDirection.Both;
		GrowVertical = GrowDirection.Both;

		var bg = new ColorRect
		{
			Color = new Color(0.08f, 0.1f, 0.14f, 1f),
			MouseFilter = MouseFilterEnum.Ignore
		};
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		_menuColumn = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(320, 0),
			Alignment = BoxContainer.AlignmentMode.Center
		};
		_menuColumn.AddThemeConstantOverride("separation", 16);
		center.AddChild(_menuColumn);

		var title = new Label
		{
			Text = "Survivalcraft",
			HorizontalAlignment = HorizontalAlignment.Center
		};
		title.AddThemeFontSizeOverride("font_size", 42);
		_menuColumn.AddChild(title);

        _menuColumn.AddChild(MakeMenuButton("新的世界", OnNewWorld));
        _menuColumn.AddChild(MakeMenuButton("继续", OnContinue));
        _menuColumn.AddChild(MakeMenuButton("设置", OnOpenSettings));
		_menuColumn.AddChild(MakeMenuButton("退出", OnQuit));

		var settingsCenter = new CenterContainer();
		settingsCenter.SetAnchorsPreset(LayoutPreset.FullRect);
		settingsCenter.MouseFilter = MouseFilterEnum.Ignore;
		_settingsLayer = settingsCenter;
		AddChild(settingsCenter);

		_settingsPanel = new PanelContainer
		{
			Visible = false,
			CustomMinimumSize = new Vector2(420, 0)
		};
		settingsCenter.AddChild(_settingsPanel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		_settingsPanel.AddChild(margin);

		var inner = new VBoxContainer();
		inner.AddThemeConstantOverride("separation", 12);
		margin.AddChild(inner);

		var settingsTitle = new Label
		{
			Text = "设置",
			HorizontalAlignment = HorizontalAlignment.Center
		};
		settingsTitle.AddThemeFontSizeOverride("font_size", 28);
		inner.AddChild(settingsTitle);

		var musicRow = new HBoxContainer();
		musicRow.AddThemeConstantOverride("separation", 12);
		inner.AddChild(musicRow);

		musicRow.AddChild(new Label { Text = "音乐音量", CustomMinimumSize = new Vector2(100, 0) });

		_musicSlider = new HSlider
		{
			MinValue = 0,
			MaxValue = 1,
			Step = 0.01,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(180, 0)
		};
		_musicSlider.ValueChanged += OnMusicVolumeChanged;
		musicRow.AddChild(_musicSlider);

		_musicValueLabel = new Label
		{
			CustomMinimumSize = new Vector2(48, 0),
			HorizontalAlignment = HorizontalAlignment.Right
		};
		musicRow.AddChild(_musicValueLabel);

		var viewRow = new HBoxContainer();
		viewRow.AddThemeConstantOverride("separation", 12);
		inner.AddChild(viewRow);

		viewRow.AddChild(new Label { Text = "视距", CustomMinimumSize = new Vector2(100, 0) });

		_viewDistanceOption = new OptionButton
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		foreach (string name in ViewDistanceNames)
		{
			_viewDistanceOption.AddItem(name);
		}
		_viewDistanceOption.ItemSelected += OnViewDistanceSelected;
		viewRow.AddChild(_viewDistanceOption);

		var backButton = new Button { Text = "返回" };
		backButton.Pressed += OnCloseSettings;
		inner.AddChild(backButton);
	}

	static Button MakeMenuButton(string text, System.Action handler)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(0, 44)
		};
		button.Pressed += handler;
		return button;
	}

	void RefreshSettingsUi()
	{
		if (_musicSlider != null)
		{
			_musicSlider.SetValueNoSignal(_settings.MusicVolume);
		}

		UpdateMusicValueLabel(_settings.MusicVolume);

		if (_viewDistanceOption != null)
		{
			int index = System.Array.IndexOf(ViewDistanceNames, _settings.ViewDistanceName);
			_viewDistanceOption.Selected = index >= 0 ? index : 1;
			if (index < 0)
			{
				_settings.ViewDistanceName = ViewDistanceNames[_viewDistanceOption.Selected];
			}
		}
	}

	void UpdateMusicValueLabel(float volume)
	{
		if (_musicValueLabel != null)
		{
			_musicValueLabel.Text = $"{Mathf.RoundToInt(volume * 100)}%";
		}
	}

	void ShowMenu(bool showMenu)
	{
		if (_menuColumn != null)
		{
			_menuColumn.Visible = showMenu;
		}

		if (_settingsPanel != null)
		{
			_settingsPanel.Visible = !showMenu;
		}

		if (_settingsLayer != null)
		{
			_settingsLayer.MouseFilter = showMenu ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
		}
	}

    void OnNewWorld()
    {
        WorldSession.Continue = false;
        WorldSession.Inventory = null;
        WorldSnapshot.ClearPose();
        WorldSave.Delete();
        GetTree().ChangeSceneToFile("res://scenes/game/Game.tscn");
    }

    void OnContinue()
    {
        if (!WorldSave.Exists)
        {
            return;
        }

        WorldSession.Continue = true;
        GetTree().ChangeSceneToFile("res://scenes/game/Game.tscn");
    }

    void OnOpenSettings()
	{
		RefreshSettingsUi();
		ShowMenu(false);
	}

	void OnCloseSettings()
	{
		_settings.Save();
		ShowMenu(true);
	}

	void OnQuit()
	{
		GetTree().Quit();
	}

	void OnMusicVolumeChanged(double value)
	{
		_settings.MusicVolume = (float)value;
		UpdateMusicValueLabel(_settings.MusicVolume);
		_settings.ApplyAudio();
		_settings.Save();
	}

	void OnViewDistanceSelected(long index)
	{
		if (index < 0 || index >= ViewDistanceNames.Length)
		{
			return;
		}

		_settings.ViewDistanceName = ViewDistanceNames[index];
		_settings.Save();
	}
}
