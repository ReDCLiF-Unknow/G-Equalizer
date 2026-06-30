namespace GamingEqualizer;

public partial class SavePresetDialog : Window
{
    private readonly IEnumerable<string> _existingNames;

    public string? PresetName { get; private set; }

    public SavePresetDialog(IEnumerable<string> existingNames, string? suggestedName = null)
    {
        InitializeComponent();
        _existingNames = existingNames;

        if (!string.IsNullOrEmpty(suggestedName))
            NameBox.Text = suggestedName;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        NameBox.Focus();
    }

    private void Save_Click(object? sender, RoutedEventArgs e) => TrySave();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) TrySave();
        if (e.Key == Key.Escape) Close(false);
    }

    private void TrySave()
    {
        string name = NameBox.Text?.Trim() ?? "";

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

        PresetName = name;
        Close(true);
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text      = message;
        ErrorLabel.IsVisible = true;
    }
}
