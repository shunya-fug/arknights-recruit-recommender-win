using System.Windows;
using System.Windows.Threading;
using ArknightsRecruitRecommender.Models;

namespace ArknightsRecruitRecommender.Views;

public partial class NotificationWindow : Window
{
    private static readonly TimeSpan AutoHideDelay = TimeSpan.FromSeconds(12);
    private readonly DispatcherTimer _autoHideTimer;

    public NotificationWindow()
    {
        InitializeComponent();
        _autoHideTimer = new DispatcherTimer { Interval = AutoHideDelay };
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            Hide();
        };

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - 380;
        Top = workArea.Bottom - 200;
    }

    public void ShowResults(IReadOnlyList<CombinationResult> results)
    {
        ResultsList.ItemsSource = results.Select(r =>
            $"[{r.Tags.Aggregate((a, b) => $"{a} / {b}")}] -> {r.GuaranteedMinRarity}★以上確定 ({r.MatchingOperators.Count}件)");

        Show();
        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }
}
