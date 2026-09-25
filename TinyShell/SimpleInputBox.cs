using System.Windows;
using System.Windows.Controls;

namespace TinyShell;

/// <summary>Tiny modal text prompt - avoids pulling in Microsoft.VisualBasic
/// just for Interaction.InputBox.</summary>
public static class SimpleInputBox
{
    public static string? Show(string title, string prompt, string defaultValue = "")
    {
        var win = new Window
        {
            Title = title,
            Width = 380,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["BgVoid"],
        };

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock
        {
            Text = prompt,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"],
            Margin = new Thickness(0, 0, 0, 8)
        });

        var box = new TextBox
        {
            Text = defaultValue,
            Style = (Style)Application.Current.Resources["NeonTextBox"]
        };
        box.SelectAll();
        stack.Children.Add(box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "OK", Style = (Style)Application.Current.Resources["NeonButtonPrimary"], IsDefault = true };
        var cancel = new Button { Content = "Cancel", Style = (Style)Application.Current.Resources["NeonButtonGhost"], IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        win.Content = stack;

        string? result = null;
        ok.Click += (_, _) => { result = box.Text; win.DialogResult = true; };

        win.Loaded += (_, _) => box.Focus();
        win.ShowDialog();
        return result;
    }
}
