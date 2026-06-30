using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GamingEqualizer;

public static class MsgBox
{
    public static async Task Info(string text, string title = "G Equalizer", Window? owner = null)
        => await Show(text, title, false, owner);

    public static async Task<bool> Confirm(string text, string title = "G Equalizer", Window? owner = null)
        => await Show(text, title, true, owner);

    private static async Task<bool> Show(string text, string title, bool confirm, Window? owner)
    {
        var result = false;
        var tcs    = new TaskCompletionSource<bool>();

        var okBtn = new Button
        {
            Content = confirm ? "Yes" : "OK",
            Classes = { "primary" },
            MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button? cancelBtn = null;
        if (confirm)
        {
            cancelBtn = new Button
            {
                Content = "No",
                MinWidth = 80,
                Margin = new Thickness(0, 0, 8, 0)
            };
        }

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (cancelBtn != null) btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);

        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16
        };
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#c4c4d0"))
        });
        panel.Children.Add(btnRow);

        var dlg = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#06060e")),
            Content = panel,
            ShowInTaskbar = false
        };

        okBtn.Click     += (_, _) => { result = true;  dlg.Close(); };
        if (cancelBtn != null)
            cancelBtn.Click += (_, _) => { result = false; dlg.Close(); };

        dlg.Closed += (_, _) => tcs.TrySetResult(result);

        if (owner != null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();

        return await tcs.Task;
    }
}
