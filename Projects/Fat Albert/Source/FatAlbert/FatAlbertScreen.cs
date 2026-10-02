using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using TimShared;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace FatAlbert;

/// <summary>
/// The Fat Albert window: pick a ship, the way it lifts and how far it has to go, and see whether it gets off the
/// ground and out of the gravity well, and what it burns on the way. Refreshes once a second; the climb itself is
/// worked out on a background thread.
/// </summary>
public class FatAlbertScreen : MyGuiScreenBase
{
	private enum View
	{
		Check,
		Directions,
		Thrusters,
		Fuel,
		Planets,
		Names,
		Help
	}

	// In the same order as View.
	private static readonly string[] ViewNames = { "Lift-off check", "Thrust by direction", "Thrusters", "Fuel & power", "All planets", "Planet names (setup)", "Help" };

	private static readonly string[] DirNames = { "Up", "Down", "Forward", "Backward", "Left", "Right" };

	private class Column
	{
		public string Name;

		public float Width;

		public bool RightAligned;

		public Column(string name, float width, bool rightAligned = false)
		{
			Name = name;
			Width = width;
			RightAligned = rightAligned;
		}
	}

	private class RowData
	{
		public string[] Texts;

		public Color? Color;

		/// <summary>Planet id for rows that can be picked (Planet names view).</summary>
		public long Key;
	}

	private const int RefreshFrames = 60;

	private const double ShipRange = 5000.0;

	private const float Left = -0.42f;

	private const float Row1Y = -0.375f;

	private const float Row2Y = -0.325f;

	private const float Row3Y = -0.28f;

	private const float TableTop = -0.245f;

	private const float EditorY = 0.255f;

	/// <summary>Planet names view: the row for giving a planet a GPS by hand, above the name row.</summary>
	private const float GpsY = 0.205f;

	/// <summary>All planets view: top of the planet summary under the table.</summary>
	private const float SummaryY = 0.045f;

	private const float StatusY = 0.315f;

	private const float ButtonsY = 0.39f;

	private const float ColumnGap = 0.01f;

	private static readonly Thickness RightCellMargin = new Thickness(2f * ColumnGap, 0f, 0f, 0f);

	// Remembered while the game runs; a new session starts with the ship you're in.
	private static long s_shipKey;

	private static View s_viewBeforeHelp = View.Check;

	/// <summary>Planet to visit (land on, then climb back out of); 0 = where the ship is now.</summary>
	private static long s_planetId;

	private MyGuiControlCombobox _shipCombo;

	private MyGuiControlCombobox _planetCombo;

	/// <summary>Planet names view: the planet picked in the table.</summary>
	private static long s_namePlanetId;

	private MyGuiControlTextbox _nameBox;

	private MyGuiControlCombobox _gpsCombo;

	private readonly List<PlanetNames.GpsPoint> _gpsPoints = new List<PlanetNames.GpsPoint>();

	/// <summary>All planets view: the planet whose summary shows under the table.</summary>
	private static long s_summaryPlanetId;

	private MyGuiControlMultilineText _summary;

	private string _summaryText;

	private string _message;

	private Color _messageColor;

	private double _messageUntil;

	private List<PlanetInfo> _planets = new List<PlanetInfo>();

	/// <summary>All planets view: one trip per planet, by planet id.</summary>
	private Dictionary<long, AscentResult> _planetResults;

	private Dictionary<long, Dir> _planetDirs;

	private MyGuiControlCombobox _viewCombo;

	private MyGuiControlCombobox _dirCombo;

	private MyGuiControlTable _table;

	private MyGuiControlMultilineText _helpText;

	private MyGuiControlLabel _status;

	private readonly List<long> _shipKeys = new List<long>();

	private bool[] _rightAligned = new bool[0];

	private ShipSnapshot _ship;

	private AscentResult _result;

	private AscentPlan _plan;

	private Dir _dir;

	private bool _simRunning;

	private bool _simAgain;

	private int _frames;

	private bool _recreatePending;

	private bool _suppressEvents;

	private bool _closed;

	private static Color MutedColor => new Color(150, 160, 170);

	private static Color WarningColor => new Color(255, 190, 90);

	private static Color BadColor => new Color(255, 120, 110);

	private static Color GoodColor => new Color(140, 230, 140);

	private static View CurrentView
	{
		get => (View)MathHelper.Clamp(Settings.View, 0, ViewNames.Length - 1);
		set => Settings.View = (int)value;
	}

	public FatAlbertScreen(long shipKey)
		: base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.9f, 0.95f))
	{
		long controlled = ShipReader.ControlledShipKey();
		if (shipKey != 0)
		{
			s_shipKey = shipKey;
		}
		else if (controlled != 0)
		{
			s_shipKey = controlled;
		}
		EnabledBackgroundFade = true;
		m_closeOnEsc = true;
		CanHideOthers = true;
		CloseButtonEnabled = true;
		IsOpen = true;
		RecreateControls(constructor: true);
	}

	public override string GetFriendlyName()
	{
		return "FatAlbertScreen";
	}

	protected override void OnClosed()
	{
		_closed = true;
		IsOpen = false;
		Settings.Save();
		base.OnClosed();
	}

	public override void RecreateControls(bool constructor)
	{
		base.RecreateControls(constructor);
		_suppressEvents = true;
		try
		{
			CreateControls();
		}
		finally
		{
			_suppressEvents = false;
		}
		Refresh(rebuild: constructor);
	}

	private void CreateControls()
	{
		PluginSwitcher.AddSwitcher(this, AddCaption("Fat Albert - can it make orbit?"));

		AddLabel(Left, Row1Y, "Ship");
		_shipCombo = AddCombo(-0.36f, Row1Y, 0.43f, 12, "Your ships within 5 km. The one you're sitting in comes first.");
		_shipCombo.ItemSelected += OnShipSelected;
		FillShipCombo();

		AddLabel(0.1f, Row1Y, "View");
		_viewCombo = AddCombo(0.16f, Row1Y, 0.26f, ViewNames.Length);
		for (int i = 0; i < ViewNames.Length; i++)
		{
			_viewCombo.AddItem(i, ViewNames[i], i, null, sort: false);
		}
		_viewCombo.SelectItemByKey((long)CurrentView, sendEvent: false);
		_viewCombo.ItemSelected += () => SwitchView((View)_viewCombo.GetSelectedKey());

		AddLabel(Left, Row2Y, "Lift with");
		_dirCombo = AddCombo(-0.33f, Row2Y, 0.24f, 7, "Which thrusters lift the ship, named from the cockpit: Up means the ones that push the ship up (their flames point down).");
		_dirCombo.AddItem(-1, "Auto", 0, "Here: the side facing away from the planet now. Another planet: the side with the most thrust at its sea level.", sort: false);
		for (int i = 0; i < DirNames.Length; i++)
		{
			_dirCombo.AddItem(i, DirNames[i] + " thrusters", i + 1, null, sort: false);
		}
		_dirCombo.SelectItemByKey(Settings.Direction, sendEvent: false);
		_dirCombo.ItemSelected += () =>
		{
			if (!_suppressEvents)
			{
				Settings.Direction = (int)_dirCombo.GetSelectedKey();
				Recompute();
			}
		};

		AddLabel(-0.07f, Row2Y, "Climb km");
		AddNumberBox(0.025f, Row2Y, Settings.DistanceKm, "How far to climb from here, in km. Leave empty to climb to where the planet's gravity ends.", text => Settings.DistanceKm = text);

		AddLabel(0.155f, Row2Y, "Speed m/s");
		AddNumberBox(0.26f, Row2Y, Settings.Speed, "Climb speed in m/s. Leave empty for the world's speed limit. Slower climbs burn more: the thrusters hold the ship up for longer.", text => Settings.Speed = text);

		MyGuiControlCheckbox countOff = new MyGuiControlCheckbox(new Vector2(Left + 0.01f, Row3Y), null, "On: thrusters, tanks and power blocks that are off, stockpiling or recharging count as if you'll switch them on before lift-off. Off: only what works right now counts.", Settings.CountOff);
		countOff.IsCheckedChanged = box =>
		{
			Settings.CountOff = box.IsChecked;
			Refresh(rebuild: false);
		};
		Controls.Add(countOff);
		Controls.Add(new MyGuiControlLabel(new Vector2(Left + 0.03f, Row3Y), null, "Count off blocks", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));

		MyGuiControlCheckbox chutes = new MyGuiControlCheckbox(new Vector2(-0.215f, Row3Y), null, "Open the ship's parachutes when landing on a planet with air thick enough for them. Each needs its canopy material (canvas) on board.", Settings.UseChutes);
		chutes.IsCheckedChanged = box =>
		{
			Settings.UseChutes = box.IsChecked;
			Recompute();
		};
		Controls.Add(chutes);
		Controls.Add(new MyGuiControlLabel(new Vector2(-0.195f, Row3Y), null, "Parachutes", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));

		AddLabel(-0.04f, Row3Y, "Planet");
		_planetCombo = AddCombo(0.03f, Row3Y, 0.39f, 12, "Where I am now: lift off from here. A planet: fall in at the speed limit (free), brake to land at its sea level, then climb back out with what's left.");
		_planetCombo.ItemSelected += () =>
		{
			if (!_suppressEvents)
			{
				s_planetId = _planetCombo.GetSelectedKey();
				Recompute();
			}
		};
		FillPlanetCombo();

		List<Column> columns = ColumnsFor(CurrentView);
		_table = new MyGuiControlTable
		{
			Position = new Vector2(0f, TableTop),
			Size = new Vector2(0.84f, 0.5f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
			ColumnsCount = columns.Count,
			VisibleRowsCount = CurrentView == View.Names ? 9 : CurrentView == View.Planets ? 7 : 12
		};
		_table.SetCustomColumnWidths(columns.Select(c => c.Width).ToArray());
		_rightAligned = columns.Select(c => c.RightAligned).ToArray();
		for (int i = 0; i < columns.Count; i++)
		{
			_table.SetColumnName(i, new StringBuilder(columns[i].Name));
			if (columns[i].RightAligned)
			{
				_table.SetColumnAlign(i, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
				_table.SetHeaderColumnAlign(i, MyGuiDrawAlignEnum.HORISONTAL_RIGHT_AND_VERTICAL_CENTER);
				// Same trick as the Stockpile Manager: a negative header margin lines right-aligned headers up with their cells.
				_table.SetHeaderColumnMargin(i, new Thickness(-ColumnGap, 0f, ColumnGap, 0f));
			}
		}
		_table.Visible = CurrentView != View.Help;
		Controls.Add(_table);

		_summary = null;
		_summaryText = null;
		if (CurrentView == View.Planets)
		{
			// Under the table: the full answer for the planet picked in it.
			_summary = new MyGuiControlMultilineText(new Vector2(0f, SummaryY), new Vector2(0.84f, StatusY - 0.03f - SummaryY), null, "Blue", 0.75f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP)
			{
				OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
			};
			Controls.Add(_summary);
			_table.ItemSelected += (MyGuiControlTable table, MyGuiControlTable.EventArgs args) =>
			{
				if (_table.SelectedRow?.UserData is long id && id != 0 && id != s_summaryPlanetId)
				{
					s_summaryPlanetId = id;
					ShowSummary();
				}
			};
		}

		_helpText = null;
		if (CurrentView == View.Help)
		{
			_helpText = new MyGuiControlMultilineText(new Vector2(0f, TableTop), new Vector2(0.84f, 0.54f), null, "Blue", 0.8f, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP, null, drawScrollbarV: true, drawScrollbarH: false, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP)
			{
				OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP
			};
			_helpText.AppendText(HelpText);
			Controls.Add(_helpText);
		}

		_nameBox = null;
		if (CurrentView == View.Names)
		{
			CreateNameEditor();
		}

		_status = new MyGuiControlLabel(new Vector2(Left, StatusY), null, "", null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(_status);

		AddButton(-0.315f, "Rescan", () =>
		{
			FillShipCombo();
			FillPlanetCombo();
			Refresh(rebuild: true);
		});
		if (CurrentView == View.Names)
		{
			AddButton(-0.105f, "Paste GPS list", PasteGps);
		}
		else if (CurrentView == View.Help)
		{
			AddButton(-0.105f, "Reset inputs", ResetInputs);
		}
		else
		{
			AddButton(-0.105f, "Move HUD", () =>
			{
				Hud.RequestMove();
				CloseScreen();
			}).SetToolTip("Closes this window; then move the mouse to place the HUD and left click. Reset inputs is on the Help page.");
		}
		AddButton(0.105f, Settings.Hud ? "HUD: on" : "HUD: off", () =>
		{
			Hud.Toggle();
			_recreatePending = true;
		}).SetToolTip($"Show the answer on your HUD while you fly. Hotkey {Hud.KeyName}, or /fat hud in chat.");
		if (CurrentView == View.Help)
		{
			AddButton(0.315f, "Back", () => SwitchView(s_viewBeforeHelp));
		}
		else
		{
			AddButton(0.315f, "Help", () => SwitchView(View.Help));
		}
	}

	// ---- Planet names (setup) ----

	/// <summary>Name box and buttons under the table; the row picked in the table is the planet they act on.</summary>
	private void CreateNameEditor()
	{
		AddLabel(Left, EditorY, "Name");
		_nameBox = new MyGuiControlTextbox(new Vector2(-0.36f, EditorY), "", PlanetNames.MaxLength)
		{
			Size = new Vector2(0.34f, 0.045f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		_nameBox.SetToolTip("Your name for the planet picked in the table. Press Set name to keep it.");
		Controls.Add(_nameBox);
		AddButton(0.105f, "Set name", SetPlanetName, EditorY);
		AddButton(0.315f, "Game's name", ResetPlanetName, EditorY);

		AddLabel(Left, GpsY, "GPS");
		_gpsCombo = AddCombo(-0.36f, GpsY, 0.57f, 12, "GPS from your last paste (matched or not), then your own GPS list. Pick one and press Use this GPS to name the planet picked in the table after it, wherever the GPS is.");
		FillGpsCombo();
		AddButton(0.315f, "Use this GPS", UseGps, GpsY);

		_table.ItemSelected += (MyGuiControlTable table, MyGuiControlTable.EventArgs args) =>
		{
			// The table re-selects the same planet on every refresh; only a different pick replaces what's typed.
			if (_table.SelectedRow?.UserData is long id && id != 0 && id != s_namePlanetId)
			{
				s_namePlanetId = id;
				_nameBox.Text = _planets.FirstOrDefault(p => p.Id == id)?.Name ?? "";
			}
		};
		PlanetInfo selected = _planets.FirstOrDefault(p => p.Id == s_namePlanetId);
		if (selected != null)
		{
			_nameBox.Text = selected.Name;
		}
	}

	/// <summary>Each GPS with where it lands: the planet whose gravity it's in, or the nearest planet and how far off it is.</summary>
	private void FillGpsCombo()
	{
		if (_gpsCombo == null)
		{
			return;
		}
		_gpsCombo.ClearItems();
		_gpsPoints.Clear();
		foreach (PlanetNames.GpsPoint point in PlanetNames.Candidates())
		{
			PlanetInfo match = PlanetNames.Match(point.Coords, _planets);
			string where;
			if (match != null)
			{
				where = $"in {match.GameName}'s gravity";
			}
			else
			{
				PlanetInfo nearest = PlanetNames.Nearest(point.Coords, _planets);
				where = nearest == null ? "no planet" : $"{Format.Distance(Vector3D.Distance(point.Coords, nearest.Center) - nearest.GravityLimit)} outside {nearest.GameName}'s gravity";
			}
			_gpsCombo.AddItem(_gpsPoints.Count, $"{point.Name}  ({(point.Pasted ? "pasted" : "your GPS")}, {where})", _gpsPoints.Count, null, sort: false);
			_gpsPoints.Add(point);
		}
		if (_gpsPoints.Count == 0)
		{
			_gpsCombo.AddItem(-1, "No GPS yet: paste a GPS list, or add them to your GPS list", 0, null, sort: false);
		}
		_gpsCombo.SelectItemByIndex(0);
	}

	/// <summary>Names the planet picked in the table after the GPS picked in the list, however far off the GPS is.</summary>
	private void UseGps()
	{
		PlanetInfo planet = SelectedNamePlanet();
		int index = (int)(_gpsCombo?.GetSelectedKey() ?? -1);
		if (planet == null)
		{
			return;
		}
		if (index < 0 || index >= _gpsPoints.Count)
		{
			ShowMessage("Pick a GPS in the GPS list first.", WarningColor);
			return;
		}
		PlanetNames.GpsPoint point = _gpsPoints[index];
		PlanetNames.Set(planet.Id, point.Name, $"GPS {point.Name} (picked)");
		AfterRename($"{planet.GameName} is now called {PlanetNames.Get(planet.Id)}.");
	}

	private PlanetInfo SelectedNamePlanet()
	{
		PlanetInfo planet = _planets.FirstOrDefault(p => p.Id == s_namePlanetId);
		if (planet == null)
		{
			ShowMessage("Pick a planet in the table first.", WarningColor);
		}
		return planet;
	}

	private void SetPlanetName()
	{
		PlanetInfo planet = SelectedNamePlanet();
		if (planet == null)
		{
			return;
		}
		string name = (_nameBox?.Text ?? "").Trim();
		if (name.Length == 0 || name == planet.GameName)
		{
			ResetPlanetName();
			return;
		}
		PlanetNames.Set(planet.Id, name, "typed");
		AfterRename($"{planet.GameName} is now called {PlanetNames.Get(planet.Id)}.");
	}

	private void ResetPlanetName()
	{
		PlanetInfo planet = SelectedNamePlanet();
		if (planet == null)
		{
			return;
		}
		PlanetNames.Reset(planet.Id);
		if (_nameBox != null)
		{
			_nameBox.Text = planet.GameName;
		}
		AfterRename($"{planet.GameName} uses the game's name again.");
	}

	/// <summary>
	/// Reads GPS from the clipboard (copy the server's planet list from Discord or a web page, in the game's usual
	/// GPS:Name:X:Y:Z: format) and names each planet after the GPS that's inside its gravity.
	/// </summary>
	private void PasteGps()
	{
		string text = PlanetNames.ReadClipboard();
		if (string.IsNullOrWhiteSpace(text))
		{
			ShowMessage("The clipboard is empty. Copy the server's planet GPS list first.", WarningColor);
			return;
		}
		PlanetNames.ImportReport report = PlanetNames.Import(text, _planets);
		FillGpsCombo();
		if (report.Found == 0)
		{
			ShowMessage("No GPS found on the clipboard. They need the game's format: GPS:Name:X:Y:Z:", WarningColor);
			return;
		}
		StringBuilder message = new StringBuilder($"{report.Found} GPS read, {report.Named.Count} planets named.");
		if (report.NoPlanet.Count > 0)
		{
			message.Append($" Not in any planet's gravity (give them to a planet with the GPS list): {string.Join(", ", report.NoPlanet.Take(4))}{(report.NoPlanet.Count > 4 ? "..." : "")}.");
		}
		if (report.Clashes.Count > 0)
		{
			message.Append($" Same planet twice (the one nearest its centre kept): {string.Join(", ", report.Clashes.Take(2))}.");
		}
		AfterRename(message.ToString(), report.NoPlanet.Count > 0 || report.Clashes.Count > 0 ? WarningColor : GoodColor);
	}

	private void AfterRename(string message, Color? color = null)
	{
		FillPlanetCombo();
		PlanetInfo planet = _planets.FirstOrDefault(p => p.Id == s_namePlanetId);
		if (planet != null && _nameBox != null)
		{
			_nameBox.Text = planet.Name;
		}
		if (_ship != null)
		{
			_ship.Planet = _ship.Planet == null ? null : _planets.FirstOrDefault(p => p.Id == _ship.Planet.Id) ?? _ship.Planet;
		}
		ShowMessage(message, color ?? GoodColor);
		ShowRows();
	}

	private void ShowMessage(string text, Color color)
	{
		_message = text;
		_messageColor = color;
		_messageUntil = FatAlbertSession.Now + 10.0;
		UpdateStatus();
	}

	private void AddNameRows(List<RowData> rows)
	{
		Vector3D position = MyAPIGateway.Session?.Player?.GetPosition() ?? Vector3D.Zero;
		foreach (PlanetInfo planet in _planets)
		{
			string source = PlanetNames.Source(planet.Id);
			bool renamed = PlanetNames.Get(planet.Id) != null;
			RowData row = Row(renamed ? (Color?)null : MutedColor,
				planet.Name,
				planet.GameName,
				Format.Distance(Math.Max(0.0, Vector3D.Distance(position, planet.Center) - planet.AverageRadius)),
				Format.Gravity(planet.Intensity * 9.81),
				renamed ? source ?? "" : "game's name");
			row.Key = planet.Id;
			rows.Add(row);
		}
		if (rows.Count == 0)
		{
			rows.Add(Row(MutedColor, "No planets in this world"));
		}
	}

	private static List<Column> ColumnsFor(View view)
	{
		switch (view)
		{
		case View.Names:
			return new List<Column>
			{
				new Column("Your name", 0.25f),
				new Column("Game's name", 0.19f),
				new Column("Away", 0.13f, true),
				new Column("Gravity", 0.1f, true),
				new Column("Set from", 0.33f)
			};
		case View.Directions:
			return new List<Column>
			{
				new Column("Lift with", 0.2f),
				new Column("Thrusters", 0.11f, true),
				new Column("At start", 0.17f, true),
				new Column("In space", 0.17f, true),
				new Column("Thrust/weight", 0.15f, true),
				new Column("Burns", 0.2f)
			};
		case View.Thrusters:
			return new List<Column>
			{
				new Column("Thruster", 0.3f),
				new Column("Count", 0.08f, true),
				new Column("Pushes", 0.13f),
				new Column("Burns", 0.13f),
				new Column("Thrust each", 0.14f, true),
				new Column("At start", 0.1f, true),
				new Column("Off", 0.08f, true)
			};
		case View.Planets:
			// Few, wide columns; the summary under the table has the rest.
			return new List<Column>
			{
				new Column("Planet", 0.22f),
				new Column("Away", 0.13f, true),
				new Column("Gravity", 0.1f, true),
				new Column("Thrust/weight", 0.14f, true),
				new Column("Land and get out?", 0.23f),
				new Column("Spare mass", 0.18f, true)
			};
		case View.Fuel:
			return new List<Column>
			{
				new Column("Supply", 0.26f),
				new Column("Blocks", 0.08f, true),
				new Column("Stored", 0.17f, true),
				new Column("Output", 0.13f, true),
				new Column("Used to climb", 0.17f, true),
				new Column("Left", 0.11f, true)
			};
		default:
			return new List<Column>
			{
				new Column("", 0.25f),
				new Column("", 0.75f)
			};
		}
	}

	// ---- Controls ----

	private void AddLabel(float x, float y, string text)
	{
		Controls.Add(new MyGuiControlLabel(new Vector2(x, y), null, text, null, 0.8f, "Blue", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
	}

	private MyGuiControlCombobox AddCombo(float x, float y, float width, int openItems, string toolTip = null)
	{
		MyGuiControlCombobox combo = new MyGuiControlCombobox(new Vector2(x, y), new Vector2(width, 0.04f), null, null, openItems, null, false, toolTip, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
		Controls.Add(combo);
		return combo;
	}

	private void AddNumberBox(float x, float y, string text, string toolTip, Action<string> changed)
	{
		MyGuiControlTextbox box = new MyGuiControlTextbox(new Vector2(x, y), text, 10)
		{
			Size = new Vector2(0.09f, 0.045f),
			OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER
		};
		box.SetToolTip(toolTip);
		box.TextChanged += b =>
		{
			changed(b.Text ?? "");
			Recompute();
		};
		Controls.Add(box);
	}

	private MyGuiControlButton AddButton(float x, string text, Action onClick, float y = ButtonsY)
	{
		MyGuiControlButton button = new MyGuiControlButton(new Vector2(x, y), MyGuiControlButtonStyleEnum.Default, null, null, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), 0.8f, MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, (MyGuiControlButton _) =>
		{
			try
			{
				onClick();
			}
			catch (Exception ex)
			{
				SetStatus("Something went wrong: " + ex.Message, BadColor);
				MyLog.Default.WriteLineAndConsole($"[Fat Albert] Button '{text}': {ex}");
			}
		});
		Controls.Add(button);
		return button;
	}

	private void SetStatus(string text, Color? color = null)
	{
		if (_status != null)
		{
			_status.Text = text;
			_status.ColorMask = (color ?? MutedColor).ToVector4();
		}
	}

	public override bool Update(bool hasFocus)
	{
		bool result = base.Update(hasFocus);
		try
		{
			if (_recreatePending)
			{
				// Rebuilt here rather than inside a combobox event, which is still going through the controls.
				_recreatePending = false;
				RecreateControls(constructor: false);
			}
			else if (++_frames >= RefreshFrames)
			{
				Refresh(rebuild: false);
			}
		}
		catch (Exception ex)
		{
			MyLog.Default.WriteLineAndConsole($"[Fat Albert] Screen update: {ex}");
		}
		return result;
	}

	private void SwitchView(View view)
	{
		if (_suppressEvents || view == CurrentView)
		{
			return;
		}
		if (view == View.Help)
		{
			s_viewBeforeHelp = CurrentView;
		}
		CurrentView = view;
		_recreatePending = true;
	}

	private void ResetInputs()
	{
		Settings.Direction = -1;
		Settings.DistanceKm = "";
		Settings.Speed = "";
		Settings.CountOff = true;
		_recreatePending = true;
	}

	private void FillShipCombo()
	{
		_shipCombo.ClearItems();
		_shipKeys.Clear();
		foreach (ShipReader.ShipEntry ship in ShipReader.FindShips(ShipRange))
		{
			_shipCombo.AddItem(_shipKeys.Count, $"{ship.Name}  ({Format.Distance(ship.Distance)})", _shipKeys.Count, null, sort: false);
			_shipKeys.Add(ship.Key);
		}
		if (_shipKeys.Count > 0 && !_shipKeys.Contains(s_shipKey))
		{
			s_shipKey = _shipKeys[0];
		}
		if (_shipKeys.Contains(s_shipKey))
		{
			_shipCombo.SelectItemByKey(_shipKeys.IndexOf(s_shipKey), sendEvent: false);
		}
	}

	private void FillPlanetCombo()
	{
		_planets = ShipReader.ReadPlanets();
		_planetCombo.ClearItems();
		_planetCombo.AddItem(0, "Where I am now", 0, null, sort: false);
		Vector3D position = MyAPIGateway.Session?.Player?.GetPosition() ?? Vector3D.Zero;
		for (int i = 0; i < _planets.Count; i++)
		{
			PlanetInfo planet = _planets[i];
			double away = Math.Max(0.0, Vector3D.Distance(position, planet.Center) - planet.AverageRadius);
			_planetCombo.AddItem(planet.Id, $"Visit {planet.Name}  ({Format.Distance(away)})", i + 1, null, sort: false);
		}
		if (s_planetId != 0 && !_planets.Any(p => p.Id == s_planetId))
		{
			s_planetId = 0;
		}
		_planetCombo.SelectItemByKey(s_planetId, sendEvent: false);
	}

	/// <summary>The planet picked to visit, or null for "where I am now".</summary>
	private PlanetInfo Visiting => s_planetId == 0 ? null : _planets.FirstOrDefault(p => p.Id == s_planetId);

	private PlanetInfo Named(PlanetInfo planet)
	{
		return planet == null ? null : _planets.FirstOrDefault(p => p.Id == planet.Id) ?? planet;
	}

	private static Dir PickDir(ShipSnapshot ship, PlanetInfo visiting)
	{
		return Settings.Direction >= 0 ? (Dir)Settings.Direction : Ascent.AutoDirection(ship, visiting);
	}

	private AscentPlan BuildPlan(ShipSnapshot ship)
	{
		return BuildPlan(ship, Visiting);
	}

	/// <summary>The trip the window's inputs describe: lift off from here, or visit a planet. Also used by the HUD.</summary>
	internal static AscentPlan BuildPlan(ShipSnapshot ship, PlanetInfo visiting)
	{
		Dir dir = PickDir(ship, visiting);
		double speed = Settings.ClimbSpeed(ship.SpeedLimit);
		return visiting != null ? AscentPlan.Visit(ship, visiting, dir, Settings.Distance, speed, Settings.UseChutes) : AscentPlan.Here(ship, dir, Settings.Distance, speed);
	}

	/// <summary>
	/// True when the ship is sitting on this planet's ground, so the trip there is just a lift-off. Flying or in
	/// orbit inside its gravity still counts as a visit (land at sea level, then climb out).
	/// </summary>
	private static bool IsOn(ShipSnapshot ship, PlanetInfo planet)
	{
		return ship.Planet != null && ship.Planet.Id == planet.Id && Ascent.OnGround(ship);
	}

	/// <summary>All planets view: from here for the planet the ship is sitting on, a visit for every other one.</summary>
	private static AscentPlan PlanFor(ShipSnapshot ship, PlanetInfo planet)
	{
		double speed = Settings.ClimbSpeed(ship.SpeedLimit);
		return IsOn(ship, planet)
			? AscentPlan.Here(ship, PickDir(ship, null), Settings.Distance, speed)
			: AscentPlan.Visit(ship, planet, PickDir(ship, planet), Settings.Distance, speed, Settings.UseChutes);
	}

	/// <summary>Planet picked under Planet (0 = where I am now) and the ship picked last, for the HUD.</summary>
	internal static long VisitPlanetId => s_planetId;

	internal static long LastShipKey => s_shipKey;

	/// <summary>True while the window is open, so the HUD placing screen waits for it to close.</summary>
	internal static bool IsOpen { get; private set; }

	private void OnShipSelected()
	{
		int index = (int)_shipCombo.GetSelectedKey();
		if (_suppressEvents || index < 0 || index >= _shipKeys.Count)
		{
			return;
		}
		s_shipKey = _shipKeys[index];
		_result = null;
		Refresh(rebuild: true);
	}

	// ---- Reading the ship and running the climb ----

	private void Refresh(bool rebuild)
	{
		_frames = 0;
		_ship = s_shipKey == 0 ? null : ShipReader.Read(s_shipKey, Settings.CountOff, rebuild);
		Recompute();
	}

	/// <summary>Runs the climb in the background; if one is already running, runs again once it's done.</summary>
	private void Recompute()
	{
		if (_suppressEvents)
		{
			return;
		}
		ShowRows();
		if (_ship == null || _ship.IsStatic)
		{
			_result = null;
			return;
		}
		if (_simRunning)
		{
			_simAgain = true;
			return;
		}
		ShipSnapshot ship = _ship;
		AscentPlan plan = BuildPlan(ship);
		_plan = plan;
		_dir = plan.Dir;
		// The All planets view runs a trip for every planet too; the plans are made here, on the game thread.
		List<KeyValuePair<PlanetInfo, AscentPlan>> others = CurrentView == View.Planets ? _planets.Select(p => new KeyValuePair<PlanetInfo, AscentPlan>(p, PlanFor(ship, p))).ToList() : null;
		AscentResult result = null;
		Dictionary<long, AscentResult> planetResults = null;
		_simRunning = true;
		MyAPIGateway.Parallel.StartBackground(() =>
		{
			result = Ascent.Solve(ship, plan);
			if (others != null)
			{
				planetResults = others.ToDictionary(p => p.Key.Id, p => Ascent.Solve(ship, p.Value));
			}
		}, () =>
		{
			_simRunning = false;
			if (_closed)
			{
				return;
			}
			if (ship.Key == s_shipKey)
			{
				_result = result;
				if (planetResults != null)
				{
					_planetResults = planetResults;
					_planetDirs = others.ToDictionary(p => p.Key.Id, p => p.Value.Dir);
				}
			}
			if (_simAgain)
			{
				_simAgain = false;
				Recompute();
			}
			else
			{
				ShowRows();
			}
		});
	}

	// ---- Tables ----

	private void ShowRows()
	{
		if (_table == null)
		{
			return;
		}
		List<RowData> rows = new List<RowData>();
		if (CurrentView == View.Names)
		{
			// Setup doesn't need a ship.
			AddNameRows(rows);
		}
		else if (_ship == null)
		{
			rows.Add(Row(BadColor, "No ship", s_shipKey == 0 ? "None of your ships is within 5 km. Sit in one, or press Rescan." : "That ship isn't loaded any more. Press Rescan."));
		}
		else if (_ship.IsStatic)
		{
			rows.Add(Row(BadColor, "Station", "This grid is a station (or joined to one), so it can't fly. Convert it to a ship first."));
		}
		else
		{
			switch (CurrentView)
			{
			case View.Check:
				AddCheckRows(rows);
				break;
			case View.Directions:
				AddDirectionRows(rows);
				break;
			case View.Thrusters:
				AddThrusterRows(rows);
				break;
			case View.Fuel:
				AddFuelRows(rows);
				break;
			case View.Planets:
				AddPlanetRows(rows);
				break;
			}
		}

		float scroll = _table.ScrollBar?.Value ?? 0f;
		_table.Clear();
		foreach (RowData data in rows)
		{
			MyGuiControlTable.Row row = new MyGuiControlTable.Row(data.Key);
			for (int i = 0; i < _table.ColumnsCount; i++)
			{
				string text = (i < data.Texts.Length ? data.Texts[i] ?? "" : "").Replace("\r", "").Replace('\n', ' ');
				// Long text shrinks to fit, then ends in "..."; the full text is in the cell's tooltip.
				MyGuiControlTable.Cell cell = new MyGuiControlTable.Cell(text, null, text.Length > 25 ? text : null, data.Color)
				{
					IsAutoScaleEnabled = true
				};
				if (i < _rightAligned.Length && _rightAligned[i])
				{
					cell.Margin = RightCellMargin;
				}
				row.AddCell(cell);
			}
			_table.Add(row);
		}
		if (CurrentView == View.Planets && !_planets.Any(p => p.Id == s_summaryPlanetId))
		{
			// Nothing picked yet: the planet you're visiting, else the one you're at, else the nearest.
			s_summaryPlanetId = (Visiting ?? (_ship?.Planet != null ? _planets.FirstOrDefault(p => p.Id == _ship.Planet.Id) : null) ?? _planets.FirstOrDefault())?.Id ?? 0;
		}
		long selectedId = CurrentView == View.Names ? s_namePlanetId : CurrentView == View.Planets ? s_summaryPlanetId : 0;
		if (selectedId != 0)
		{
			// Keeps the picked planet selected when the table is rebuilt.
			for (int i = 0; i < _table.RowsCount; i++)
			{
				if (_table.GetRow(i).UserData is long id && id == selectedId)
				{
					_table.SelectedRowIndex = i;
					break;
				}
			}
		}
		if (_table.ScrollBar != null)
		{
			_table.ScrollBar.Value = scroll;
		}
		ShowSummary();
		UpdateStatus();
	}

	private void UpdateStatus()
	{
		if (_message != null && FatAlbertSession.Now < _messageUntil)
		{
			SetStatus(_message, _messageColor);
			return;
		}
		_message = null;
		if (CurrentView == View.Names)
		{
			SetStatus("Pick a planet and type a name, or copy the server's planet GPS list and press Paste GPS list.");
			return;
		}
		if (_ship == null)
		{
			SetStatus("");
			return;
		}
		string seat = _ship.ReferenceName != null ? $"Directions are from {_ship.ReferenceName}." : "No cockpit or remote control: directions are from the grid itself.";
		SetStatus(_simRunning && _result == null ? "Working it out..." : seat);
	}

	private static RowData Row(Color? color, params string[] texts)
	{
		return new RowData { Texts = texts, Color = color };
	}

	private void AddCheckRows(List<RowData> rows)
	{
		AddCheckRows(rows, _plan ?? BuildPlan(_ship), _result);
	}

	/// <summary>The full answer for one trip; also used for the planet summary in All planets.</summary>
	private void AddCheckRows(List<RowData> rows, AscentPlan plan, AscentResult result)
	{
		ShipSnapshot ship = _ship;
		PlanetInfo planet = Named(plan.Planet);
		double radius = plan.StartRadius;
		string there = plan.Land ? "there" : "here";

		if (result == null)
		{
			rows.Add(Row(MutedColor, "Answer", "Working it out..."));
		}
		else
		{
			rows.Add(Verdict(result, plan));
		}

		rows.Add(Row(null, "Ship", $"{ship.Name}: {Format.Mass(ship.Mass)}{(ship.Grids > 1 ? $" ({ship.Grids} grids joined by rotors, pistons or hinges)" : "")}"));
		if (planet == null || planet.GravityAt(radius) <= 0)
		{
			rows.Add(Row(null, "Gravity", "None here: you're already out of every gravity well. Pick a planet to visit under Planet."));
			return;
		}
		double gravity = planet.GravityAt(radius) * 9.81;
		if (plan.Land)
		{
			double away = Math.Max(0.0, Vector3D.Distance(ship.Position, planet.Center) - planet.AverageRadius);
			rows.Add(Row(null, "Planet", $"{planet.Name}, {Format.Distance(away)} away: you land at sea level, {Format.Gravity(gravity)}, {(planet.HasAtmosphere ? "with air" : "no air")}"));
		}
		else
		{
			rows.Add(Row(null, "Planet", $"{planet.Name}: {Format.Gravity(gravity)} here, {Format.Distance(radius - planet.AverageRadius)} above sea level"));
		}

		string why = Settings.Direction >= 0 ? "" : plan.Land ? " (auto: most thrust there)" : " (auto: they face up now)";
		int count = CountPushing(ship, plan.Dir);
		rows.Add(Row(null, "Lift with", $"{DirNames[(int)plan.Dir]} thrusters{why}: {count} pushing that way, flames pointing {DirNames[(int)Opposite(plan.Dir)].ToLowerInvariant()}"));
		if (result == null || result.Outcome == AscentOutcome.NoThrusters)
		{
			return;
		}

		if (plan.Land && result.Landed)
		{
			string fall = result.ChuteCount > 0
				? $"{result.ChuteCount} parachute{(result.ChuteCount == 1 ? "" : "s")} bring it down at {result.ChuteSpeed:0.#} m/s"
				: $"falls in at {plan.FallSpeed:0} m/s (no fuel)";
			string brake = result.LandingSeconds > 0 ? $", then brakes for {Format.Time(result.LandingSeconds)} from {Format.Distance(result.LandingHeight)} up, using {LandingText(ship, result)}" : ", no braking burn needed";
			string touch = result.TouchdownSpeed > 0 ? $"; touches down at {result.TouchdownSpeed:0.#} m/s on the parachutes" : "";
			rows.Add(Row(result.TouchdownSpeed > 0 ? WarningColor : (Color?)null, "Landing", fall + brake + touch));
		}
		if (plan.Land && Settings.UseChutes && ship.Parachutes.Count > 0 && result.ChuteCount == 0 && result.Outcome != AscentOutcome.NoThrusters)
		{
			string reason = !planet.HasAtmosphere ? "this planet has no air"
				: plan.Chutes == null || plan.Chutes.Count == 0 ? "there's no canopy material (canvas) for them on board"
				: "the air at sea level is too thin to open them";
			rows.Add(Row(MutedColor, "Parachutes", $"not used: {reason}"));
		}

		rows.Add(Row(result.StartTwr >= 1.0 ? null : (Color?)BadColor, "Thrust/weight", $"{Format.Ratio(result.StartTwr)} {there}: {Format.Force(result.StartThrust)} of thrust holds up {Format.Mass(result.StartThrust / gravity)} at {Format.Gravity(gravity)}; the ship is {Format.Mass(ship.Mass)}"));
		rows.Add(Row(null, "Lift-off limit", SpareText(result.MaxLiftoffMass, ship.Mass, $"to lift off {there}")));
		rows.Add(Row(result.MaxMass >= ship.Mass ? null : (Color?)BadColor, "Reach-space limit", SpareText(result.MaxMass, ship.Mass, plan.Land ? "to land and get back out" : "to get out of the gravity well")));

		double climb = result.TargetRadius - radius;
		string from = plan.Land ? "from sea level " : "";
		string end = Settings.Distance.HasValue ? "the distance you set" : $"where gravity ends ({Format.Distance(planet.GravityLimit - planet.AverageRadius)} above sea level)";
		rows.Add(Row(null, "Climb", $"{Format.Distance(climb)} {from}up to {end}, at up to {plan.Speed:0} m/s"));
		if (result.Success)
		{
			rows.Add(Row(null, "Time", $"{Format.Time(result.Seconds)} to climb out"));
		}
		if (result.Outcome != AscentOutcome.CantLand)
		{
			rows.Add(Row(result.MinTwr < 1.0 ? BadColor : result.MinTwr < 1.2 ? WarningColor : (Color?)null, "Weakest point", $"thrust/weight {Format.Ratio(result.MinTwr)} at {Format.Distance(radius + result.MinTwrHeight - planet.AverageRadius)} above sea level"));
		}

		if (planet.HasAtmosphere)
		{
			double top = planet.AtmosphereTop - planet.AverageRadius;
			int atmospheric = ship.Thrusters.Count(t => t.NeedsAtmosphere && Vector3D.Dot(t.Push, ship.Axis(plan.Dir)) > 0.05);
			rows.Add(Row(null, "Atmosphere", atmospheric > 0 ? $"ends {Format.Distance(top)} above sea level; your {atmospheric} atmospheric thrusters fade out on the way up" : $"ends {Format.Distance(top)} above sea level"));
		}

		string uses = plan.Land ? "landing and climb use" : "uses";
		foreach (GasPool pool in ship.Gas.Values.OrderBy(p => p.Name))
		{
			// Only the gases the trip burns; oxygen and empty tanks are covered elsewhere.
			if (!result.GasUsed.TryGetValue(pool.Key, out double used))
			{
				continue;
			}
			rows.Add(Row(used >= pool.Litres - 1 && used > 0 ? WarningColor : (Color?)null, pool.Name, $"{uses} {Format.Litres(used)} of {Format.Litres(pool.Litres)} ({Format.Percent(Math.Max(0, pool.Litres - used), pool.Litres)} left of what's in the tanks)"));
		}
		double stored = ship.Sources.Where(s => s.Kind == SourceKind.Battery).Sum(s => s.StoredMWh);
		if (result.BatteryUsed > 0 || stored > 0 && ship.Thrusters.Any(t => t.Electric))
		{
			rows.Add(Row(null, "Batteries", $"{uses} {Format.Energy(result.BatteryUsed)} of {Format.Energy(stored)} ({Format.Percent(Math.Max(0, stored - result.BatteryUsed), stored)} left)"));
		}
		foreach (KeyValuePair<string, double> fuel in ship.ItemFuel)
		{
			result.ItemUsed.TryGetValue(fuel.Key, out double used);
			if (used > 0)
			{
				rows.Add(Row(null, ship.FuelName(fuel.Key), $"{uses} {used:#,0.##} kg of {fuel.Value:#,0.##} kg"));
			}
		}
		if (result.PowerShort)
		{
			rows.Add(Row(WarningColor, "Power", $"short from {Format.Distance(radius + result.PowerShortHeight - planet.AverageRadius)} above sea level: the electric thrusters want more than the ship can make, so they push less"));
		}
		foreach (GasPool pool in ship.Gas.Values.Where(p => p.Litres <= 0 && ship.Thrusters.Any(t => t.FuelKey == p.Key)))
		{
			rows.Add(Row(BadColor, pool.Name, "the tanks are empty, so those thrusters give nothing"));
		}
		if (ship.Thrusters.Any(t => !t.Electric && !ship.Gas.ContainsKey(t.FuelKey)))
		{
			rows.Add(Row(BadColor, "Fuel", "some thrusters burn a gas this ship has no tanks for, so they give nothing"));
		}
		if (ship.BlocksOff > 0)
		{
			rows.Add(Row(WarningColor, "Switched off", Settings.CountOff
				? $"{ship.BlocksOff} blocks are off, stockpiling or recharging; they're counted as if you'll switch them on"
				: $"{ship.BlocksOff} blocks are off, stockpiling or recharging and aren't counted"));
		}
	}

	/// <summary>What the landing burn uses, e.g. "12,000 L Hydrogen, 0.5 MWh".</summary>
	private static string LandingText(ShipSnapshot ship, AscentResult result)
	{
		List<string> parts = new List<string>();
		parts.AddRange(result.LandingGas.Where(g => g.Value > 0).Select(g => $"{Format.Litres(g.Value)} {ship.FuelName(g.Key)}"));
		if (result.LandingBattery > 0)
		{
			parts.Add(Format.Energy(result.LandingBattery));
		}
		parts.AddRange(result.LandingItems.Where(i => i.Value > 0).Select(i => $"{i.Value:#,0.##} kg {ship.FuelName(i.Key)}"));
		return parts.Count > 0 ? string.Join(", ", parts) : "almost nothing";
	}

	private RowData Verdict(AscentResult result, AscentPlan plan)
	{
		string where = Named(plan.Planet)?.Name ?? "there";
		switch (result.Outcome)
		{
		case AscentOutcome.Made:
			return Row(GoodColor, "Answer", plan.Land
				? $"YES: it can land on {where} and climb back out of its gravity in {Format.Time(result.Seconds)}"
				: $"YES: it lifts off and gets out of the gravity well in {Format.Time(result.Seconds)}");
		case AscentOutcome.NoMass:
			return Row(BadColor, "Answer", "Couldn't read the ship's mass (it came back as 0 kg). Press Rescan; if it stays at 0, please report it");
		case AscentOutcome.NoGravity:
			return Row(GoodColor, "Answer", "You're not in any gravity: nothing to climb out of. Pick a planet to visit under Planet");
		case AscentOutcome.NoThrusters:
			return Row(BadColor, "Answer", $"NO: no working thrusters push {DirNames[(int)result.Direction].ToLowerInvariant()}. Pick another direction under Lift with");
		case AscentOutcome.CantLand:
			return Row(BadColor, "Answer", $"NO: it can't slow down to land on {where} (thrust/weight {Format.Ratio(result.StartTwr)} at sea level) and would crash");
		case AscentOutcome.TooHeavy:
			return Row(BadColor, "Answer", $"NO: too heavy to lift off{(plan.Land ? " again" : "")}. Lose {Format.Mass(result.Mass - result.MaxLiftoffMass)} or add {Format.Force(result.StartGravity * result.Mass - result.StartThrust)} of thrust");
		case AscentOutcome.OutOfFuel:
			return Row(BadColor, "Answer", $"NO: {result.RanOut} runs out {Format.Distance(result.RanOutHeight)} up and it falls back");
		case AscentOutcome.Stalled:
			return Row(BadColor, "Answer", $"NO: it stalls {Format.Distance(result.Height)} up, where thrust drops below its weight");
		default:
			return Row(BadColor, "Answer", "NO: it climbs so slowly it would take over 4 hours");
		}
	}

	/// <summary>The answer in a few words, for the All planets table.</summary>
	internal static string ShortVerdict(AscentResult result)
	{
		switch (result.Outcome)
		{
		case AscentOutcome.Made:
			return "YES";
		case AscentOutcome.NoMass:
			return "? ship mass reads 0 kg";
		case AscentOutcome.NoThrusters:
			return "NO: no thrusters that way";
		case AscentOutcome.CantLand:
			return "NO: can't brake to land";
		case AscentOutcome.TooHeavy:
			return "NO: too heavy to lift off";
		case AscentOutcome.OutOfFuel:
			return $"NO: {result.RanOut} runs out";
		case AscentOutcome.Stalled:
			return $"NO: stalls {Format.Distance(result.Height)} up";
		case AscentOutcome.TooSlow:
			return "NO: too slow";
		default:
			return "-";
		}
	}

	private void AddPlanetRows(List<RowData> rows)
	{
		ShipSnapshot ship = _ship;
		if (_planets.Count == 0)
		{
			rows.Add(Row(MutedColor, "No planets in this world"));
			return;
		}
		foreach (PlanetInfo planet in _planets)
		{
			bool on = IsOn(ship, planet);
			AscentResult result = null;
			_planetResults?.TryGetValue(planet.Id, out result);
			double away = Math.Max(0.0, Vector3D.Distance(ship.Position, planet.Center) - planet.AverageRadius);
			Color? color = result == null ? MutedColor : result.Success ? GoodColor : (Color?)BadColor;
			RowData row = Row(color,
				planet.Name + (on ? " (on it)" : ""),
				on ? "here" : Format.Distance(away),
				Format.Gravity(planet.Intensity * 9.81),
				result == null ? "..." : result.Outcome == AscentOutcome.NoThrusters ? "none" : Format.Ratio(result.StartTwr),
				result == null ? "working it out..." : ShortVerdict(result),
				result == null || result.Outcome == AscentOutcome.NoThrusters || result.Outcome == AscentOutcome.NoMass ? "-" : SignedMass(result.MaxMass - ship.Mass));
			row.Key = planet.Id;
			rows.Add(row);
		}
	}

	/// <summary>All planets view: the full answer for the planet picked in the table, under the table.</summary>
	private void ShowSummary()
	{
		if (_summary == null)
		{
			return;
		}
		PlanetInfo planet = _planets.FirstOrDefault(p => p.Id == s_summaryPlanetId);
		List<RowData> lines = new List<RowData>();
		if (_ship == null || _ship.IsStatic)
		{
			lines.Add(Row(MutedColor, "", "No ship to check."));
		}
		else if (planet == null)
		{
			lines.Add(Row(MutedColor, "", "Click a planet in the table to see its summary here."));
		}
		else
		{
			AscentResult result = null;
			_planetResults?.TryGetValue(planet.Id, out result);
			lines.Add(Row(null, "", planet.Name == planet.GameName ? planet.Name : $"{planet.Name} ({planet.GameName})"));
			string atmosphere = planet.HasAtmosphere ? $"air {planet.AirDensity:0.##} up to {Format.Distance(planet.AtmosphereAltitude)} above sea level" : "no air";
			lines.Add(Row(null, "Planet", $"surface gravity {Format.Gravity(planet.Intensity * 9.81)}, {atmosphere}, gravity ends {Format.Distance(planet.GravityLimit - planet.AverageRadius)} above sea level"));
			AddCheckRows(lines, PlanFor(_ship, planet), result);
		}
		StringBuilder signature = new StringBuilder();
		foreach (RowData line in lines)
		{
			signature.Append(line.Color?.PackedValue ?? 0).Append(string.Join("|", line.Texts)).Append('\n');
		}
		// Rebuilt only when it changes, so the scroll position stays put between refreshes.
		if (signature.ToString() == _summaryText)
		{
			return;
		}
		_summaryText = signature.ToString();
		_summary.Clear();
		foreach (RowData line in lines)
		{
			string label = line.Texts.Length > 1 && line.Texts[0].Length > 0 ? line.Texts[0] + ": " : "";
			string text = line.Texts.Length > 1 ? line.Texts[1] : line.Texts.Length > 0 ? line.Texts[0] : "";
			_summary.AppendText(label + text, "Blue", 0.75f, (line.Color ?? Color.White).ToVector4());
			_summary.AppendLine();
		}
	}

	internal static string SignedMass(double kg)
	{
		return kg >= 0 ? "+" + Format.Mass(kg) : "-" + Format.Mass(-kg);
	}

	private static string SpareText(double limit, double mass, string what)
	{
		return limit >= mass
			? $"up to {Format.Mass(limit)} {what} ({Format.Mass(limit - mass)} to spare)"
			: $"up to {Format.Mass(limit)} {what} ({Format.Mass(mass - limit)} too heavy)";
	}

	private static int CountPushing(ShipSnapshot ship, Dir dir)
	{
		Vector3D axis = ship.Axis(dir);
		return ship.Thrusters.Count(t => Vector3D.Dot(t.Push, axis) > 0.05);
	}

	private static Dir Opposite(Dir dir)
	{
		switch (dir)
		{
		case Dir.Up:
			return Dir.Down;
		case Dir.Down:
			return Dir.Up;
		case Dir.Forward:
			return Dir.Backward;
		case Dir.Backward:
			return Dir.Forward;
		case Dir.Left:
			return Dir.Right;
		default:
			return Dir.Left;
		}
	}

	private void AddDirectionRows(List<RowData> rows)
	{
		ShipSnapshot ship = _ship;
		// Measured where the climb starts: here, or sea level on the planet you're visiting.
		AscentPlan plan = _plan ?? BuildPlan(ship);
		double gravity = (plan.Planet?.GravityAt(plan.StartRadius) ?? 0.0) * 9.81;
		double weight = ship.Mass * gravity;
		foreach (Dir dir in (Dir[])Enum.GetValues(typeof(Dir)))
		{
			Vector3D axis = ship.Axis(dir);
			List<ThrusterInfo> pushing = ship.Thrusters.Where(t => Vector3D.Dot(t.Push, axis) > 0.05).ToList();
			double here = Ascent.ThrustAt(ship, dir, plan.Planet, plan.StartRadius);
			string fuels = string.Join(", ", pushing.Select(t => t.Electric ? "Power" : ship.FuelName(t.FuelKey)).Distinct().OrderBy(s => s));
			Color? color = dir == _dir ? GoodColor : (Color?)null;
			rows.Add(Row(color,
				DirNames[(int)dir] + (dir == _dir ? "  (lifting)" : ""),
				pushing.Count.ToString(),
				Format.Force(here),
				Format.Force(Ascent.VacuumThrust(ship, dir)),
				weight > 0 ? Format.Ratio(here / weight) : "-",
				fuels.Length > 0 ? fuels : "-"));
		}
	}

	private void AddThrusterRows(List<RowData> rows)
	{
		ShipSnapshot ship = _ship;
		AscentPlan plan = _plan ?? BuildPlan(ship);
		double air = plan.Planet?.AirAt(plan.StartRadius) ?? 0.0;
		bool atmosphere = plan.Planet?.HasAtmosphere ?? false;
		var groups = ship.Thrusters.GroupBy(t => new { t.TypeName, Dir = DirOf(ship, t), t.FuelKey, t.Force });
		foreach (var group in groups.OrderBy(g => g.Key.Dir).ThenBy(g => g.Key.TypeName))
		{
			ThrusterInfo first = group.First();
			rows.Add(Row(group.Key.Dir == DirNames[(int)_dir] ? GoodColor : (Color?)null,
				group.Key.TypeName,
				group.Count().ToString(),
				group.Key.Dir,
				first.Electric ? "Power" : ship.FuelName(first.FuelKey),
				Format.Force(first.Force),
				$"{first.Effectiveness(air, atmosphere) * 100.0:0}%",
				group.Count(t => !t.On).ToString()));
		}
		if (rows.Count == 0)
		{
			rows.Add(Row(BadColor, "No working thrusters on this ship"));
		}
	}

	/// <summary>The cockpit direction a thruster pushes, or "Angled" when it's well off all six (on a rotor or hinge).</summary>
	private static string DirOf(ShipSnapshot ship, ThrusterInfo thruster)
	{
		foreach (Dir dir in (Dir[])Enum.GetValues(typeof(Dir)))
		{
			if (Vector3D.Dot(thruster.Push, ship.Axis(dir)) > 0.95)
			{
				return DirNames[(int)dir];
			}
		}
		return "Angled";
	}

	private void AddFuelRows(List<RowData> rows)
	{
		ShipSnapshot ship = _ship;
		AscentResult result = _result;
		foreach (GasPool pool in ship.Gas.Values.OrderBy(p => p.Name))
		{
			double used = 0.0;
			result?.GasUsed.TryGetValue(pool.Key, out used);
			rows.Add(Row(null, $"{pool.Name} tanks" + (pool.TanksOff > 0 ? $" ({pool.TanksOff} off)" : ""), pool.Tanks.ToString(), Format.Litres(pool.Litres), "", result == null ? "..." : Format.Litres(used), Format.Litres(Math.Max(0, pool.Litres - used))));
		}
		List<PowerSource> batteries = ship.Sources.Where(s => s.Kind == SourceKind.Battery).ToList();
		if (batteries.Count > 0)
		{
			double stored = batteries.Sum(b => b.StoredMWh);
			double used = result?.BatteryUsed ?? 0.0;
			rows.Add(Row(null, "Batteries" + OffText(batteries), batteries.Count.ToString(), Format.Energy(stored), Format.Power(batteries.Sum(b => b.MaxOutput)), result == null ? "..." : Format.Energy(used), Format.Energy(Math.Max(0, stored - used))));
		}
		foreach (var group in ship.Sources.Where(s => s.Kind == SourceKind.Reactor).GroupBy(s => s.FuelKey))
		{
			ship.ItemFuel.TryGetValue(group.Key, out double kg);
			double used = 0.0;
			result?.ItemUsed.TryGetValue(group.Key, out used);
			rows.Add(Row(kg <= 0 ? BadColor : (Color?)null, $"Reactors ({ship.FuelName(group.Key)})" + OffText(group), group.Count().ToString(), $"{kg:#,0.##} kg", Format.Power(group.Sum(s => s.MaxOutput)), result == null ? "..." : $"{used:#,0.##} kg", $"{Math.Max(0, kg - used):#,0.##} kg"));
		}
		foreach (var group in ship.Sources.Where(s => s.Kind == SourceKind.Engine).GroupBy(s => s.FuelKey))
		{
			rows.Add(Row(null, $"{ship.FuelName(group.Key)} engines" + OffText(group), group.Count().ToString(), "from tanks", Format.Power(group.Sum(s => s.MaxOutput)), "in tanks above", ""));
		}
		List<PowerSource> others = ship.Sources.Where(s => s.Kind == SourceKind.Other).ToList();
		if (others.Count > 0)
		{
			rows.Add(Row(null, "Solar, wind and others" + OffText(others), others.Count.ToString(), "-", Format.Power(others.Sum(s => s.MaxOutput)), "-", "-"));
		}
		double demand = ship.Thrusters.Where(t => t.Electric && Vector3D.Dot(t.Push, ship.Axis(_dir)) > 0.05).Sum(t => t.MaxPower);
		if (demand > 0)
		{
			double supply = ship.Sources.Sum(s => s.MaxOutput);
			rows.Add(Row(supply < demand ? WarningColor : MutedColor, "Lifting thrusters need", "", "", Format.Power(demand), supply < demand ? "more than the ship makes" : "", ""));
		}
		if (rows.Count == 0)
		{
			rows.Add(Row(MutedColor, "No tanks or power blocks on this ship"));
		}
	}

	private static string OffText(IEnumerable<PowerSource> sources)
	{
		int off = sources.Count(s => !s.On);
		return off > 0 ? $" ({off} off)" : "";
	}

	private const string HelpText =
		"WHAT IT DOES\n" +
		"Fat Albert flies your ship straight up from where it sits, on paper, and tells you whether it lifts off and gets out of the planet's gravity, and what it burns on the way. Nothing on the ship is touched, so it works on any server.\n\n" +
		"LIFT WITH\n" +
		"The thrusters that lift the ship, named from the cockpit. Up means the thrusters that push the ship up (their flames point down, under the ship). Auto: sitting on the ground (within 1 km of the highest ground), the side facing away from the planet, since the ship can't turn over; anywhere else (flying, in orbit, or visiting a planet), the side with the most thrust where the climb starts, since you'll point that one up. Thrusters on rotors or hinges count for the part of their push that points the chosen way.\n\n" +
		"PLANET: VISITING ANOTHER PLANET\n" +
		"Where I am now checks lift-off from where the ship is. Pick a planet to check a trip there from space: the ship falls in at the speed limit (the game caps falling speed, so that costs no fuel), brakes at full thrust to land at sea level, then climbs back out with whatever fuel and power are left. If it can't brake hard enough to land, the answer is NO. The All planets view runs this for every planet and moon in the world at once, with how much mass you have to spare for each; click a planet to see its full answer under the table (landing, climb, fuel used, limits). The Thrust by direction and Thrusters views show thrust at the start of the climb, so at sea level on the planet you picked.\n\n" +
		"CLIMB KM / SPEED M/S\n" +
		"Leave Climb empty to climb to where the planet's gravity ends; the plugin reads that from the world. Type a number to climb that many km from where you are instead. Speed is how fast to climb; empty uses the world's speed limit. The ship goes full thrust until it reaches that speed, then holds it, as the dampeners do. A slower climb burns more, because the thrusters hold the ship up for longer.\n\n" +
		"PLANET NAMES (SETUP)\n" +
		"Servers often call planets something other than the game does. In the Planet names view, copy the server's list of planet GPS (the usual GPS:Name:X:Y:Z: format, as many as you like, from Discord or anywhere) and press Paste GPS list. Each GPS names the planet whose gravity it's in, so the coordinates only need to be roughly right: a point on the surface, in orbit or at the centre all work. A GPS that isn't inside any planet's gravity can still be given to a planet by hand: pick the planet in the table, pick the GPS in the GPS list (your last paste, then your own GPS list, each showing which planet it's near) and press Use this GPS. To fix a name, pick the planet, type a new one and press Set name; Game's name puts the original back. Names are kept on your computer, separately for each world, and show everywhere in the window.\n\n" +
		"THE ANSWER\n" +
		"Thrust/weight above 1 means it lifts off. Lift-off limit is the heaviest the ship can be and still leave the ground here. Reach-space limit is the heaviest it can be and still get out of the gravity well with the fuel and power it has. Weakest point is where thrust is closest to the ship's weight; atmospheric thrusters lose thrust as the air thins and ion thrusters gain it.\n\n" +
		"MODDED THRUSTERS\n" +
		"Every thruster is read from its own block definition: thrust, power, the gas it burns and how air changes it. Modded thrusters and modded gases work the same way as vanilla ones, as long as they're normal thruster blocks.\n\n" +
		"FUEL AND POWER\n" +
		"Hydrogen (or any gas) comes from the tanks on the ship. Electric thrusters get power from solar and wind first, then batteries, then reactors and hydrogen engines, as the game does it. If the electric thrusters want more power than the ship can make, they push less, and the answer shows it.\n\n" +
		"NOT COUNTED\n" +
		"Power the rest of the ship uses (turn off what you don't need), ice in oxygen/hydrogen generators, ships docked by connector, tanks and thrusters that aren't joined by conveyors, and other planets' or moons' gravity. Solar and wind count what they give right now for the whole climb. Gas has no weight in the game, so the ship's mass stays the same all the way up.\n\n" +
		"OPENING IT\n" +
		"/fat in chat, the Fat Albert action on a cockpit's toolbar, or the plugin list at the top left of any of our plugins' windows.\n\n" +
		"PARACHUTES\n" +
		"With Parachutes ticked, a landing on a planet with air opens every parachute that has its canopy material (canvas) on board, using the game's own drag formula at sea level. The ship comes down at the parachutes' speed instead of the speed limit, so the braking burn is shorter, and the parachutes keep pulling while it brakes. If they alone get it down to 5 m/s or less, it lands even when the thrusters couldn't hold it up (lifting off again is another matter). Parachutes need air at least as thick as their opening level (0.2 for vanilla ones).\n\n" +
		"HUD OVERLAY\n" +
		"Three lines at the left of your screen with the answer for the ship you're flying (or the one picked last here) and the trip picked under Planet, rechecked every two seconds. Turn it on and off with Ctrl+Alt+F, /fat hud in chat, or the HUD button below. It hides when you hide the game's HUD. To move it, press Move HUD: the window closes, the HUD follows your mouse, and a left click puts it there (Esc or a right click puts it back). Reset inputs is on this Help page. To use another key, change HudKey in FatAlbert_Settings.txt (for example HudKey=Ctrl+Shift+H) while the game is closed.";
}
