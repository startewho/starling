namespace Tessera.Gui;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage())
        {
            Title = "Tessera",
            MinimumWidth = 1024,
            MinimumHeight = 720,
        };
    }
}
