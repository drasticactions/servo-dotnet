using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Servo.AvaloniaUI;

namespace Servo.AvaloniaUI.Demo;

public partial class MainWindow : Window
{
    private const string HomePage = "https://servo.org";

    private bool _addressBarFocused;

    public MainWindow()
    {
        InitializeComponent();

        AddressBar.GotFocus += (_, _) => _addressBarFocused = true;
        AddressBar.LostFocus += (_, _) => _addressBarFocused = false;

        WebView.Navigated += OnNavigated;
        WebView.TitleChanged += OnTitleChanged;
        WebView.LoadStatusChanged += OnLoadStatusChanged;
        WebView.HistoryChanged += OnHistoryChanged;
        WebView.StatusTextChanged += OnStatusTextChanged;
        WebView.ConsoleMessage += OnConsoleMessage;
        WebView.Crashed += OnCrashed;

        UpdateNavigationButtons();

        AddressBar.Text = HomePage;
        WebView.Source = new Uri(HomePage);
    }

    // ----- Toolbar actions -----

    private void OnBackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => WebView.GoBack();

    private void OnForwardClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => WebView.GoForward();

    private void OnReloadClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => WebView.Reload();

    private void OnGoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => NavigateToAddressBar();

    private void OnAddressBarKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateToAddressBar();
            e.Handled = true;
        }
    }

    private void NavigateToAddressBar()
    {
        var url = NormalizeUrl(AddressBar.Text);
        if (url is null) return;
        WebView.Focus();
        WebView.Navigate(url);
    }

    /// <summary>
    /// Turns raw address-bar input into a navigable URL: passes through anything with a
    /// scheme, prefixes bare host-like input with https://, and routes everything else to
    /// a web search.
    /// Basically a very simple version of what browsers do in their address bars,
    /// servoshell defaults to duckduckgo for web searches.
    /// </summary>
    private static string? NormalizeUrl(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp ||
             absolute.Scheme == Uri.UriSchemeHttps ||
             absolute.Scheme == Uri.UriSchemeFile ||
             absolute.Scheme == "about" ||
             absolute.Scheme == "servo" ||
             absolute.Scheme == "data"))
        {
            return text;
        }

        var looksLikeHost = !text.Contains(' ') && text.Contains('.');
        if (looksLikeHost)
            return "https://" + text;

        return "https://duckduckgo.com/?q=" + Uri.EscapeDataString(text);
    }

    private void OnNavigated(object? sender, UrlChangedEventArgs e)
    {
        if (!_addressBarFocused)
            AddressBar.Text = e.Url;
    }

    private void OnTitleChanged(object? sender, TitleChangedEventArgs e)
    {
        Title = string.IsNullOrEmpty(e.Title)
            ? "Servo.AvaloniaUI Demo"
            : $"{e.Title} — Servo.AvaloniaUI Demo";
    }

    private void OnLoadStatusChanged(object? sender, LoadStatusChangedEventArgs e)
    {
        var loading = e.Status != LoadStatus.Complete;
        LoadingBar.IsVisible = loading;
        StatusText.Text = loading ? "Loading…" : "Done";
    }

    private void OnHistoryChanged(object? sender, HistoryChangedEventArgs e) => UpdateNavigationButtons();

    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = WebView.CanGoBack;
        ForwardButton.IsEnabled = WebView.CanGoForward;
    }

    private void OnStatusTextChanged(object? sender, StatusTextChangedEventArgs e)
    {
        // Servo reports the hovered-link target here; fall back to "Ready" when cleared.
        StatusText.Text = string.IsNullOrEmpty(e.StatusText) ? "Ready" : e.StatusText;
    }

    private void OnConsoleMessage(object? sender, ConsoleMessageEventArgs e) =>
        Debug.WriteLine($"[console:{e.Level}] {e.Message}");

    private void OnCrashed(object? sender, CrashedEventArgs e)
    {
        StatusText.Text = $"Servo crashed: {e.Reason}";
        Debug.WriteLine($"[crash] {e.Reason}\n{e.Backtrace}");
    }
}
