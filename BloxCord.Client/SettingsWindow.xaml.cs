using System.Windows;
using System.Windows.Media;
using BloxCord.Client.Services;
using BloxCord.Client.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text;

namespace BloxCord.Client;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly MainViewModel _viewModel;

    public SettingsWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _viewModel = (MainViewModel)mainWindow.DataContext;
        DataContext = _viewModel;
        
        // Initialize state based on current background
        if (_mainWindow.MainGrid.Background is LinearGradientBrush gradient)
        {
            GradientCheckBox.IsChecked = true;
            ColorPanel.Visibility = Visibility.Collapsed;
            GradientPanel.Visibility = Visibility.Visible;
            
            if (gradient.GradientStops.Count >= 2)
            {
                GradientStartInput.Text = gradient.GradientStops[0].Color.ToString();
                GradientEndInput.Text = gradient.GradientStops[1].Color.ToString(); // Assuming 2nd stop is the end or close to it
            }
        }
        else if (_mainWindow.MainGrid.Background is SolidColorBrush solid)
        {
            GradientCheckBox.IsChecked = false;
            ColorPanel.Visibility = Visibility.Visible;
            GradientPanel.Visibility = Visibility.Collapsed;
            ColorInput.Text = solid.Color.ToString();
        }

        // Subscribe to events after initialization to avoid null reference during InitializeComponent
        GradientCheckBox.Checked += GradientCheckBox_Checked;
        GradientCheckBox.Unchecked += GradientCheckBox_Checked;
    }

    private async void LoadSession_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StatusMessage = "Reading Roblox logs...";

        try
        {
            var session = await RobloxLogParser.TryReadLatestAsync();

            if (session is null)
            {
                _viewModel.StatusMessage = "No active Roblox session detected.";
                MessageBox.Show("No active Roblox session detected in logs.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!string.IsNullOrWhiteSpace(session.Username))
                _viewModel.Username = session.Username;

            _viewModel.JobId = session.JobId;
            _viewModel.UserId = session.UserId?.ToString() ?? string.Empty;
            _viewModel.SessionUserId = session.UserId;
            _viewModel.PlaceId = session.PlaceId; // Ensure PlaceId is updated
            _viewModel.StatusMessage = "Session info loaded from Roblox logs.";
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Failed to read logs: {ex.Message}";
            MessageBox.Show($"Failed to read logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GradientCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (GradientCheckBox.IsChecked == true)
        {
            ColorPanel.Visibility = Visibility.Collapsed;
            GradientPanel.Visibility = Visibility.Visible;
            ApplyGradient();
        }
        else
        {
            ColorPanel.Visibility = Visibility.Visible;
            GradientPanel.Visibility = Visibility.Collapsed;
            ApplyColor();
        }
    }


    private void ApplyColor_Click(object sender, RoutedEventArgs e)
    {
        ApplyColor();
    }
    
    private void ApplyGradient_Click(object sender, RoutedEventArgs e)
    {
        ApplyGradient();
    }

    private void ApplyColor()
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(ColorInput.Text);
            _mainWindow.MainGrid.Background = new SolidColorBrush(color);
            
            ConfigService.Current.UseGradient = false;
            ConfigService.Current.SolidColor = ColorInput.Text;
            ConfigService.Save();
        }
        catch
        {
            MessageBox.Show("Invalid color format. Use hex (e.g. #000000).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyGradient()
    {
        try 
        {
            var startColor = (Color)ColorConverter.ConvertFromString(GradientStartInput.Text);
            var endColor = (Color)ColorConverter.ConvertFromString(GradientEndInput.Text);

            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            gradient.GradientStops.Add(new GradientStop(startColor, 0));
            gradient.GradientStops.Add(new GradientStop(endColor, 1));

            _mainWindow.MainGrid.Background = gradient;
            
            ConfigService.Current.UseGradient = true;
            ConfigService.Current.GradientStart = GradientStartInput.Text;
            ConfigService.Current.GradientEnd = GradientEndInput.Text;
            ConfigService.Save();
        }
        catch
        {
             // Fallback to default if parsing fails
            if (_mainWindow.Resources["AppBackground"] is LinearGradientBrush defaultGradient)
            {
                _mainWindow.MainGrid.Background = defaultGradient;
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void MintToken_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _mainWindow.RequestTokenMintAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to mint token: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void VoteLanguage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var code = VoteLanguageInput?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code))
                return;

            await _mainWindow.VoteLanguageAsync(code);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to vote language: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenCustomEmojis_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "RoChat");
            var filePath = Path.Combine(folder, "custom_emojis.json");

            Directory.CreateDirectory(folder);
            if (!File.Exists(filePath))
            {
                var json = JsonSerializer.Serialize(new System.Collections.Generic.Dictionary<string, long>(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open custom emoji file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReloadCustomEmojis_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CustomEmojiService.Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to reload emojis: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Donate_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Solana:");
        sb.AppendLine("5NR4u5pyGXDFf7m2Ad32n4rNPycY5ZYnYLngY6aK2rKj");
        sb.AppendLine();
        sb.AppendLine("Litecoin (LTC):");
        sb.AppendLine("LUqjTW9fjaWPWpMQ6EtBKmDmKHJfm7TnEo");
        sb.AppendLine();
        sb.AppendLine("Ethereum (ETH):");
        sb.AppendLine("0xf80e3f6C3AD31Fd4Da7fe6708f179316D2b3cB1B");

        var text = sb.ToString().TrimEnd();

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard might be unavailable (RDP/permissions). Ignore.
        }

        MessageBox.Show(text + "\n\n(Copied to clipboard)", "Donate", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
