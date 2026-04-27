using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Mathematics;

var nativeSettings = new NativeWindowSettings()
{
    Size = new Vector2i(1920, 1080),
    Title = "SpaceDNA",
    WindowState = WindowState.Fullscreen,
    WindowBorder = WindowBorder.Hidden,
    API = ContextAPI.OpenGL,
    Profile = ContextProfile.Core,
};

using var window = new Game(GameWindowSettings.Default, nativeSettings);
window.Run();

// Объявление partial позволяет IDE видеть тип Game из Game.cs
public partial class Game;
