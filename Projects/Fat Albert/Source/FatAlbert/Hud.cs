using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace FatAlbert;

/// <summary>
/// The HUD overlay: three lines at the left of the screen with the answer for the ship you're flying (or the one
/// picked last in the window) and the trip picked in the window. Turned on and off with the hotkey (Ctrl+Alt+F by
/// default, HudKey in the settings file), /fat hud, or the HUD button in the window. Rechecked every two seconds
/// on a background thread.
/// </summary>
internal static class Hud
{
	private const int RefreshTicks = 120;

	private const double PlanetsSeconds = 10.0;

	private static HudScreen s_screen;

	private static bool s_running;

	private static int s_tick;

	private static List<PlanetInfo> s_planets;

	private static double s_planetsAt = double.MinValue;

	private static string s_keyText;

	private static bool s_moveRequested;

	private static MyKeys s_key;

	private static bool s_ctrl;

	private static bool s_alt;

	private static bool s_shift;

	public static void Toggle()
	{
		Settings.Hud = !Settings.Hud;
		Settings.Save();
		s_tick = RefreshTicks - 1;
		MyAPIGateway.Utilities.ShowNotification($"Fat Albert HUD {(Settings.Hud ? "on" : "off")} ({KeyName})", 2000);
	}

	public static string KeyName => Settings.HudKey;

	/// <summary>Called every tick from the session.</summary>
	/// <summary>
	/// Starts placing the HUD with the mouse. The placing screen opens once the Fat Albert window has closed:
	/// opening a screen in the same click that closes another leaves it without proper focus.
	/// </summary>
	public static void RequestMove()
	{
		if (!Settings.Hud)
		{
			Settings.Hud = true;
			Settings.Save();
		}
		s_moveRequested = true;
	}

	public static void Update()
	{
		if (s_moveRequested && !FatAlbertScreen.IsOpen)
		{
			s_moveRequested = false;
			if (s_screen == null || s_screen.State == MyGuiScreenState.CLOSED)
			{
				s_screen = new HudScreen();
				MyGuiSandbox.AddScreen(s_screen);
			}
			MyGuiSandbox.AddScreen(new MoveScreen(s_screen));
		}
		CheckHotkey();
		if (!Settings.Hud)
		{
			Close();
			return;
		}
		if (s_screen == null || s_screen.State == MyGuiScreenState.CLOSED)
		{
			s_screen = new HudScreen();
			MyGuiSandbox.AddScreen(s_screen);
		}
		// The game's own HUD hidden (Tab) hides this too.
		s_screen.Hidden = MyAPIGateway.Session?.Config?.HudState == 0;
		if (++s_tick < RefreshTicks || s_running)
		{
			return;
		}
		s_tick = 0;
		Refresh();
	}

	public static void Close()
	{
		if (s_screen != null)
		{
			s_screen.CloseScreen();
			s_screen = null;
		}
	}

	public static void Unload()
	{
		Close();
		s_planets = null;
		s_planetsAt = double.MinValue;
		s_running = false;
	}

	private static void Refresh()
	{
		long key = ShipReader.ControlledShipKey();
		if (key == 0)
		{
			key = FatAlbertScreen.LastShipKey;
		}
		ShipSnapshot ship = key == 0 ? null : ShipReader.Read(key, Settings.CountOff, rebuild: false);
		if (ship == null || ship.IsStatic)
		{
			s_screen?.SetLines(("Fat Albert: sit in a ship, or pick one in the window (/fat)", MutedColor));
			return;
		}
		double now = FatAlbertSession.Now;
		if (s_planets == null || now - s_planetsAt > PlanetsSeconds)
		{
			s_planets = ShipReader.ReadPlanets();
			s_planetsAt = now;
		}
		PlanetInfo visiting = FatAlbertScreen.VisitPlanetId == 0 ? null : s_planets.FirstOrDefault(p => p.Id == FatAlbertScreen.VisitPlanetId);
		AscentPlan plan = FatAlbertScreen.BuildPlan(ship, visiting);
		string planetName = (plan.Planet == null ? null : s_planets.FirstOrDefault(p => p.Id == plan.Planet.Id)?.Name) ?? plan.Planet?.Name ?? "";
		AscentResult result = null;
		s_running = true;
		MyAPIGateway.Parallel.StartBackground(() => result = Ascent.Solve(ship, plan), () =>
		{
			s_running = false;
			if (result == null || s_screen == null)
			{
				return;
			}
			try
			{
				ShowResult(ship, plan, planetName, result);
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLineAndConsole($"[Fat Albert] HUD: {ex}");
			}
		});
	}

	private static void ShowResult(ShipSnapshot ship, AscentPlan plan, string planetName, AscentResult result)
	{
		string trip = plan.Land ? $"land on {planetName}, then out" : plan.Planet != null ? $"lift off {planetName}" : "here";
		string title = $"Fat Albert: {ship.Name} - {trip}";
		if (result.Outcome == AscentOutcome.NoGravity)
		{
			s_screen.SetLines((title, Color.White), ("No gravity here. Pick a planet to visit in the window", MutedColor));
			return;
		}
		string answer = result.Success ? $"YES: out of the gravity well in {Format.Time(result.Seconds)}" : FatAlbertScreen.ShortVerdict(result);
		List<string> details = new List<string>
		{
			$"Thrust/weight {(result.Outcome == AscentOutcome.NoThrusters ? "none" : Format.Ratio(result.StartTwr))}",
			$"spare {FatAlbertScreen.SignedMass(result.MaxMass - ship.Mass)}"
		};
		foreach (GasPool pool in ship.Gas.Values.Where(p => result.GasUsed.ContainsKey(p.Key)))
		{
			details.Add($"{pool.Name} left {Format.Percent(Math.Max(0, pool.Litres - result.GasUsed[pool.Key]), pool.Litres)}");
		}
		double stored = ship.Sources.Where(s => s.Kind == SourceKind.Battery).Sum(s => s.StoredMWh);
		if (result.BatteryUsed > 0)
		{
			details.Add($"batteries left {Format.Percent(Math.Max(0, stored - result.BatteryUsed), stored)}");
		}
		s_screen.SetLines((title, Color.White), (answer, result.Success ? GoodColor : BadColor), (string.Join("  |  ", details), Color.White));
	}

	private static Color MutedColor => new Color(150, 160, 170);

	private static Color GoodColor => new Color(140, 230, 140);

	private static Color BadColor => new Color(255, 120, 110);

	/// <summary>The hotkey from the settings ("Ctrl+Alt+F"); ignored while chat or a menu with a cursor is open.</summary>
	private static void CheckHotkey()
	{
		VRage.ModAPI.IMyInput input = MyAPIGateway.Input;
		if (input == null || MyAPIGateway.Gui == null || MyAPIGateway.Gui.ChatEntryVisible || MyAPIGateway.Gui.IsCursorVisible)
		{
			return;
		}
		if (s_keyText != Settings.HudKey)
		{
			ParseKey(Settings.HudKey);
		}
		if (s_key == MyKeys.None || !input.IsNewKeyPressed(s_key))
		{
			return;
		}
		if (s_ctrl == input.IsAnyCtrlKeyPressed() && s_alt == input.IsAnyAltKeyPressed() && s_shift == input.IsAnyShiftKeyPressed())
		{
			Toggle();
		}
	}

	private static void ParseKey(string text)
	{
		s_keyText = text;
		s_key = MyKeys.None;
		s_ctrl = s_alt = s_shift = false;
		foreach (string part in (text ?? "").Split('+').Select(p => p.Trim()).Where(p => p.Length > 0))
		{
			switch (part.ToLowerInvariant())
			{
			case "ctrl":
			case "control":
				s_ctrl = true;
				break;
			case "alt":
				s_alt = true;
				break;
			case "shift":
				s_shift = true;
				break;
			default:
				if (Enum.TryParse(part, ignoreCase: true, out MyKeys key))
				{
					s_key = key;
				}
				else
				{
					MyLog.Default.WriteLineAndConsole($"[Fat Albert] HUD key '{text}': unknown key '{part}'");
				}
				break;
			}
		}
	}

	/// <summary>
	/// A screen that only draws: set up like the game's own HUD screen (MyGuiScreenHudBase), so it never takes focus
	/// or input and stays under menus.
	/// </summary>
	private class HudScreen : MyGuiScreenBase
	{
		private const float LineHeight = 0.03f;

		private readonly MyGuiControlLabel[] _lines = new MyGuiControlLabel[3];

		private bool _hidden;

		/// <summary>Controls sit relative to the screen's centre (0.5, 0.5); the settings hold the top-left in screen coordinates.</summary>
		private static Vector2 LinePosition(float x, float y, int line)
		{
			return new Vector2(x - 0.5f, y - 0.5f + line * LineHeight);
		}

		public void MoveTo(Vector2 topLeft)
		{
			for (int i = 0; i < _lines.Length; i++)
			{
				if (_lines[i] != null)
				{
					_lines[i].Position = LinePosition(topLeft.X, topLeft.Y, i);
				}
			}
		}

		public HudScreen()
			: base(new Vector2(0.5f, 0.5f))
		{
			CanBeHidden = true;
			CanHideOthers = false;
			CanHaveFocus = false;
			m_drawEvenWithoutFocus = true;
			m_closeOnEsc = false;
			RecreateControls(constructor: true);
		}

		public override string GetFriendlyName()
		{
			return "FatAlbertHud";
		}

		public override void RecreateControls(bool constructor)
		{
			base.RecreateControls(constructor);
			for (int i = 0; i < _lines.Length; i++)
			{
				_lines[i] = new MyGuiControlLabel(LinePosition(Settings.HudX, Settings.HudY, i), null, "", null, 0.75f, "White", MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP);
				Controls.Add(_lines[i]);
			}
			SetLines(("Fat Albert: working it out...", MutedColor));
		}

		public bool Hidden
		{
			set
			{
				if (_hidden == value)
				{
					return;
				}
				_hidden = value;
				foreach (MyGuiControlLabel line in _lines)
				{
					line.Visible = !value;
				}
			}
		}

		public void SetLines(params (string Text, Color Color)[] lines)
		{
			for (int i = 0; i < _lines.Length; i++)
			{
				_lines[i].Text = i < lines.Length ? lines[i].Text : "";
				_lines[i].ColorMask = (i < lines.Length ? lines[i].Color : Color.White).ToVector4();
				_lines[i].Visible = !_hidden;
			}
		}
	}

	/// <summary>
	/// Placing the HUD: the HUD follows the mouse; a left click puts it there and saves the spot, Esc or a right click
	/// puts it back where it was.
	/// </summary>
	private class MoveScreen : MyGuiScreenBase
	{
		private readonly HudScreen _hud;

		private readonly Vector2 _start;

		private bool _placed;

		public MoveScreen(HudScreen hud)
			: base(new Vector2(0.5f, 0.5f), null, new Vector2(1f, 1f))
		{
			_hud = hud;
			_start = new Vector2(Settings.HudX, Settings.HudY);
			CanHideOthers = false;
			EnabledBackgroundFade = false;
			m_closeOnEsc = true;
			RecreateControls(constructor: true);
		}

		public override string GetFriendlyName()
		{
			return "FatAlbertHudMove";
		}

		public override void RecreateControls(bool constructor)
		{
			base.RecreateControls(constructor);
			Controls.Add(new MyGuiControlLabel(new Vector2(0f, -0.4f), null, "Fat Albert HUD: move the mouse to place it, left click to keep it there. Esc or right click puts it back.", null, 0.9f, "White", MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER));
		}

		public override void HandleInput(bool receivedFocusInThisUpdate)
		{
			base.HandleInput(receivedFocusInThisUpdate);
			Vector2 mouse = MyGuiManager.MouseCursorPosition;
			Vector2 topLeft = new Vector2(MathHelper.Clamp(mouse.X, 0f, 0.95f), MathHelper.Clamp(mouse.Y, 0f, 0.95f));
			_hud?.MoveTo(topLeft);
			if (receivedFocusInThisUpdate)
			{
				return;
			}
			if (MyInput.Static.IsNewLeftMousePressed())
			{
				_placed = true;
				Settings.HudX = topLeft.X;
				Settings.HudY = topLeft.Y;
				Settings.Save();
				CloseScreen();
			}
			else if (MyInput.Static.IsNewRightMousePressed())
			{
				CloseScreen();
			}
		}

		protected override void OnClosed()
		{
			if (!_placed)
			{
				_hud?.MoveTo(_start);
			}
			base.OnClosed();
		}
	}
}
