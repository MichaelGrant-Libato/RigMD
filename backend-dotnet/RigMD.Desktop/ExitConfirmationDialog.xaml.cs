using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RigMD.Desktop;

public partial class ExitConfirmationDialog : Window
{
    private readonly bool _reportIsSaving;
    private bool _closing;
    private bool _allowClose;

    public ExitConfirmationDialog(bool reportIsSaving = false)
    {
        InitializeComponent();
        _reportIsSaving = reportIsSaving;
        MaxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 24);
        MaxHeight = Math.Max(160, SystemParameters.WorkArea.Height - 24);
        Width = Math.Min(Width, MaxWidth);
        Card.SizeChanged += (_, _) =>
        {
            // Keep the question and actions visible on short displays and at high Windows scaling.
            var compact = Card.ActualHeight < 330;
            ExitIllustration.Height = compact ? 48 : 140;
            ExitIllustration.Width = compact ? 60 : 174;
        };
        if (reportIsSaving)
        {
            Title = "Saving your report";
            Heading.Text = "Your PDF is still saving";
            Explanation.Text = "Please wait for the save to finish before exiting RigMD. Your report can continue saving.";
            StayButton.Content = "Back to RigMD";
            StayButton.Margin = new Thickness(0);
            ExitButton.Visibility = Visibility.Collapsed;
            ButtonRow.ColumnDefinitions[1].Width = new GridLength(0);
        }
        Loaded += (_, _) =>
        {
            StayButton.Focus();
            if (!SystemParameters.ClientAreaAnimation) return;
            Card.BeginAnimation(OpacityProperty, Motion(0, 1, 180));
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion(0.94, 1, 180));
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion(0.94, 1, 180));
            CardSlide.BeginAnimation(TranslateTransform.YProperty, Motion(10, 0, 180));
        };
    }

    private static DoubleAnimation Motion(double from, double to, int milliseconds) =>
        new(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };

    private void Finish(bool exit)
    {
        if (_closing || (exit && _reportIsSaving)) return;
        _closing = true;
        Card.IsHitTestVisible = false;
        if (!SystemParameters.ClientAreaAnimation)
        {
            _allowClose = true;
            DialogResult = exit;
            return;
        }
        // Preserve the selected outcome while ignoring repeated clicks during the closing animation.
        var fade = Motion(Card.Opacity, 0, 120);
        fade.Completed += (_, _) => { _allowClose = true; DialogResult = exit; };
        Card.BeginAnimation(OpacityProperty, fade);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion(CardScale.ScaleX, 0.97, 120));
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion(CardScale.ScaleY, 0.97, 120));
        CardSlide.BeginAnimation(TranslateTransform.YProperty, Motion(CardSlide.Y, 5, 120));
    }

    private void Stay_Click(object sender, RoutedEventArgs e) => Finish(false);
    private void Exit_Click(object sender, RoutedEventArgs e) => Finish(true);

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Finish(false);
    }

    private void Dialog_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        // Alt+F4 and other window-close requests are cancellation, never permission to exit RigMD.
        e.Cancel = true;
        Finish(false);
    }
}
