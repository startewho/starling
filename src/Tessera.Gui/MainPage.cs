using System.Diagnostics;
using Microsoft.Maui.Controls.Shapes;
using Tessera.Engine;
using EngineSize = SixLabors.ImageSharp.Size;
using IOPath = System.IO.Path;

namespace Tessera.Gui;

public sealed class MainPage : ContentPage
{
    private static readonly EngineSize Viewport = new(1200, 900);

    private readonly Entry _addressEntry;
    private readonly Button _goButton;
    private readonly Button _backButton;
    private readonly Button _forwardButton;
    private readonly Button _reloadButton;
    private readonly Image _viewport;
    private readonly Label _statusLabel;
    private readonly Label _titleLabel;
    private readonly Border _placeholder;
    private readonly BrowserSession _session;
    private readonly string _renderPath;
    private bool _busy;

    public MainPage()
    {
        Title = "Tessera";
        BackgroundColor = Palette.Page;

        _session = new BrowserSession();
        _renderPath = IOPath.Combine(IOPath.GetTempPath(), "tessera-gui-render.png");

        _addressEntry = new Entry
        {
            Placeholder = "https://example.com or file:///path/to/page.html",
            TextColor = Palette.Text,
            PlaceholderColor = Palette.Muted,
            BackgroundColor = Palette.Input,
            FontSize = 14,
            ReturnType = ReturnType.Go,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
        };
        _addressEntry.Completed += OnAddressCompleted;

        _backButton = ChromeButton("‹", BackClicked);
        _forwardButton = ChromeButton("›", ForwardClicked);
        _reloadButton = ChromeButton("↻", ReloadClicked);
        _goButton = AccentButton("Go", GoClicked);
        SetNavButtonStates();

        _viewport = new Image
        {
            Aspect = Aspect.AspectFit,
            BackgroundColor = Palette.Editor,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };

        _placeholder = new Border
        {
            BackgroundColor = Palette.Editor,
            Stroke = Palette.Edge,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = new Label
            {
                Text = "Type a URL above and press Go to render a page.",
                TextColor = Palette.Muted,
                FontSize = 14,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
        };

        _titleLabel = new Label
        {
            Text = "Tessera",
            TextColor = Palette.Text,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
        };

        _statusLabel = new Label
        {
            Text = $"Ready. Pure-managed .NET browser; renderer viewport {Viewport.Width}×{Viewport.Height} CSS px.",
            TextColor = Palette.Muted,
            FontSize = 12,
            LineBreakMode = LineBreakMode.TailTruncation,
        };

        Content = BuildLayout();
    }

    private Grid BuildLayout()
    {
        var addressRow = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        addressRow.Add(_backButton, 0, 0);
        addressRow.Add(_forwardButton, 1, 0);
        addressRow.Add(_reloadButton, 2, 0);
        addressRow.Add(_addressEntry, 3, 0);
        addressRow.Add(_goButton, 4, 0);

        var headerBar = new Border
        {
            BackgroundColor = Palette.Panel,
            Stroke = Palette.Edge,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12),
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children = { BuildTitleRow(), addressRow },
            },
        };

        var viewportPanel = new Border
        {
            BackgroundColor = Palette.Panel,
            Stroke = Palette.Edge,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(0),
            Content = new ScrollView
            {
                Orientation = ScrollOrientation.Both,
                BackgroundColor = Palette.Editor,
                Content = _placeholder,
            },
        };

        var statusBar = new Border
        {
            BackgroundColor = Palette.Panel,
            Stroke = Palette.Edge,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 8),
            Content = _statusLabel,
        };

        var root = new Grid
        {
            Padding = new Thickness(16),
            RowSpacing = 12,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
        };
        root.Add(headerBar, 0, 0);
        root.Add(viewportPanel, 0, 1);
        root.Add(statusBar, 0, 2);
        return root;
    }

    private Grid BuildTitleRow()
    {
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Children =
            {
                _titleLabel,
                BuildPill("M2 — static rendering · live HTTPS · keep-alive · WHATWG encoding"),
            },
        };
    }

    private static Border BuildPill(string text)
    {
        var pill = new Border
        {
            BackgroundColor = Palette.Pill,
            Stroke = Palette.Edge,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 6),
            Content = new Label
            {
                Text = text,
                TextColor = Palette.Accent,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
            },
        };
        Grid.SetColumn(pill, 1);
        return pill;
    }

    private static Button ChromeButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Palette.Button,
            TextColor = Palette.Text,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            WidthRequest = 38,
            HeightRequest = 38,
            Padding = new Thickness(0),
            CornerRadius = 8,
        };
        button.Clicked += handler;
        return button;
    }

    private static Button AccentButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Palette.Accent,
            TextColor = Colors.Black,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(18, 8),
            CornerRadius = 8,
        };
        button.Clicked += handler;
        return button;
    }

    private async void OnAddressCompleted(object? sender, EventArgs e)
        => await NavigateAsync(_addressEntry.Text, ignoreEmpty: false);

    private async void GoClicked(object? sender, EventArgs e)
        => await NavigateAsync(_addressEntry.Text, ignoreEmpty: false);

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (!_session.History.CanGoBack || _busy) return;
        await RunNavigation(ct => _session.BackAsync(BuildOptions(), _renderPath, ct), "Back");
    }

    private async void ForwardClicked(object? sender, EventArgs e)
    {
        if (!_session.History.CanGoForward || _busy) return;
        await RunNavigation(ct => _session.ForwardAsync(BuildOptions(), _renderPath, ct), "Forward");
    }

    private async void ReloadClicked(object? sender, EventArgs e)
    {
        if (_session.History.Current is null || _busy) return;
        await RunNavigation(ct => _session.ReloadAsync(BuildOptions(), _renderPath, ct), "Reload");
    }

    private async Task NavigateAsync(string? rawUrl, bool ignoreEmpty)
    {
        if (_busy) return;
        var url = (rawUrl ?? string.Empty).Trim();
        if (url.Length == 0)
        {
            if (!ignoreEmpty) SetStatus("Enter a URL first.", isError: true);
            return;
        }
        _addressEntry.Text = url;
        await RunNavigation(ct => _session.NavigateAsync(url, BuildOptions(), _renderPath, ct), $"GET {url}");
    }

    private async Task RunNavigation(Func<CancellationToken, Task<Common.Result<RenderOutcome, RenderError>>> navigate, string opLabel)
    {
        BeginBusy(opLabel);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await navigate(CancellationToken.None);
            stopwatch.Stop();
            if (result.IsErr)
            {
                SetStatus($"{opLabel} failed: {result.Error.Message}", isError: true);
                return;
            }

            ShowRender(result.Value);
            var current = _session.History.Current ?? "(no url)";
            SetStatus(
                $"{opLabel} → {result.Value.Width}×{result.Value.Height} px, " +
                $"{stopwatch.ElapsedMilliseconds} ms · {current}",
                isError: false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or HttpRequestException)
        {
            SetStatus($"{opLabel} threw: {ex.Message}", isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private void ShowRender(RenderOutcome outcome)
    {
        // ImageSource.FromFile caches by path; we always write to the same temp
        // file, so swap in a fresh stream from the bytes we just wrote.
        var bytes = File.ReadAllBytes(outcome.OutputPath);
        _viewport.Source = ImageSource.FromStream(() => new MemoryStream(bytes));

        if (((Border)((Grid)Content!).Children[1]).Content is ScrollView scroll && !ReferenceEquals(scroll.Content, _viewport))
        {
            scroll.Content = _viewport;
        }
    }

    private void BeginBusy(string label)
    {
        _busy = true;
        _goButton.IsEnabled = false;
        _backButton.IsEnabled = false;
        _forwardButton.IsEnabled = false;
        _reloadButton.IsEnabled = false;
        SetStatus($"{label}…", isError: false);
    }

    private void EndBusy()
    {
        _busy = false;
        _goButton.IsEnabled = true;
        SetNavButtonStates();
    }

    private void SetNavButtonStates()
    {
        _backButton.IsEnabled = _session.History.CanGoBack;
        _forwardButton.IsEnabled = _session.History.CanGoForward;
        _reloadButton.IsEnabled = _session.History.Current is not null;
    }

    private void SetStatus(string text, bool isError)
    {
        _statusLabel.Text = text;
        _statusLabel.TextColor = isError ? Palette.Danger : Palette.Muted;
    }

    private static RenderOptions BuildOptions()
        => new(Viewport, FontSize: 16f);

    private static class Palette
    {
        public static readonly Color Page = Color.FromArgb("#0E1115");
        public static readonly Color Panel = Color.FromArgb("#181D22");
        public static readonly Color Editor = Color.FromArgb("#0B0E10");
        public static readonly Color Input = Color.FromArgb("#11161A");
        public static readonly Color Edge = Color.FromArgb("#303941");
        public static readonly Color Text = Color.FromArgb("#E9EEF2");
        public static readonly Color Muted = Color.FromArgb("#9AA7B2");
        public static readonly Color Accent = Color.FromArgb("#9EE493");
        public static readonly Color Danger = Color.FromArgb("#FF7A7A");
        public static readonly Color Button = Color.FromArgb("#24313A");
        public static readonly Color Pill = Color.FromArgb("#142116");
    }
}
