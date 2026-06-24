using System.IO;
using System.Windows;
using System.Windows.Input;

namespace GamingEqualizer;

public partial class SavePresetDialog : Window
{
    private readonly IEnumerable<string> _existingNames;

    public string? PresetName { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DwmHelper.ApplyDarkTitlebar(this);
    }

    public SavePresetDialog(IEnumerable<string> existingNames)
    {
        InitializeComponent();
        _existingNames = existingNames;
        NameBox.TextChanged += (_, _) =>
            PlaceholderText.Visibility = string.IsNullOrEmpty(NameBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        NameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => TrySave();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  TrySave();
        if (e.Key == Key.Escape) DialogResult = false;
    }

    private void TrySave()
    {
        string name = NameBox.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            ShowError("Please enter a name.");
            return;
        }

        if (name.Any(c => Path.GetInvalidFileNameChars().Contains(c)))
        {
            ShowError("Name contains invalid characters.");
            return;
        }

        if (_existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            ShowError($"A preset named '{name}' already exists.");
            return;
        }

        PresetName   = name;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text       = message;
        ErrorLabel.Visibility = Visibility.Visible;
    }
}
