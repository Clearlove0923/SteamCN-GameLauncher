using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SteamCNGameLauncher.Services;
using Windows.Foundation;
using Windows.UI;

namespace SteamCNGameLauncher.Views.Pages;

/// <summary>Starward-style statistics layout with the requested line chart.</summary>
internal sealed class PlayTimeStatsDialog : ContentDialog
{
    private const double BodyWidth = 960;
    private readonly IReadOnlyList<PlaySession> _sessions;
    private readonly Dictionary<DateTime, TimeSpan> _days = [];
    private readonly Canvas _chart = new() { Width = BodyWidth, Height = 168 };
    private readonly TextBlock _rangeTotal = new() { FontSize = 12, Foreground = MutedText };
    private readonly Button[] _rangeButtons = new Button[3];

    private static SolidColorBrush MutedText => Brush(205, 205, 205);

    public PlayTimeStatsDialog(string presetId)
    {
        _sessions = PlayTimeService.Instance.GetSessions(presetId);
        foreach (var session in _sessions)
        {
            var at = session.StartedAt.LocalDateTime;
            var end = session.EndedAt.LocalDateTime;
            while (at < end)
            {
                var until = end < at.Date.AddDays(1) ? end : at.Date.AddDays(1);
                _days[at.Date] = _days.GetValueOrDefault(at.Date) + (until - at);
                at = until;
            }
        }

        Background = Brush(62, 62, 62);
        BorderThickness = new Thickness(0);
        CornerRadius = new CornerRadius(8);
        MaxWidth = 1200;
        Resources["ContentDialogMaxWidth"] = 1200d;
        Resources["ContentDialogPadding"] = new Thickness(0);
        Resources["ContentDialogSmokeFill"] = Brush(0, 0, 0, 0);
        Resources["ContentDialogTopOverlay"] = Brush(0, 0, 0, 0);
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new Grid { Width = 1008, Height = 600 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        root.RowDefinitions.Add(new RowDefinition());

        var header = new Grid { Margin = new Thickness(24, 12, 16, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = "游戏时长统计", FontSize = 20,
            FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var info = new Button { Width = 24, Height = 24, Padding = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE946", FontSize = 12, Foreground = MutedText },
            Background = Brush(0, 0, 0, 0), BorderThickness = new Thickness(0) };
        ToolTipService.SetToolTip(info, "按本机游戏进程统计；重新打开时接续仍在运行的游戏进程。");
        title.Children.Add(info);
        header.Children.Add(title);
        var close = new Button { Width = 32, Height = 32, Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8), Background = Brush(0, 0, 0, 0),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = "\uE711", FontSize = 16, Foreground = MutedText } };
        close.Click += (_, _) => Hide();
        ToolTipService.SetToolTip(close, "关闭");
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        root.Children.Add(header);

        var body = new StackPanel { Width = BodyWidth, Margin = new Thickness(24, 8, 24, 24) };
        Grid.SetRow(body, 1);
        body.Children.Add(BuildCards());
        body.Children.Add(BuildRangeSelector());
        _chart.Margin = new Thickness(0, 17, 0, 0);
        body.Children.Add(_chart);
        var heatmap = BuildHeatmap();
        heatmap.Margin = new Thickness(0, 24, 0, 0);
        body.Children.Add(heatmap);
        root.Children.Add(body);
        RenderChart(0);
        return root;
    }

    private UIElement BuildCards()
    {
        var total = TimeSpan.FromTicks(_sessions.Sum(x => (x.EndedAt - x.StartedAt).Ticks));
        var played = _days.Where(x => x.Value > TimeSpan.Zero).OrderBy(x => x.Key).ToArray();
        var longestStreak = 0;
        var streak = 0;
        DateTime streakStart = default, bestStart = default, bestEnd = default;
        DateTime? previous = null;
        foreach (var day in played)
        {
            if (previous != day.Key.AddDays(-1)) { streak = 0; streakStart = day.Key; }
            streak++;
            if (streak > longestStreak) { longestStreak = streak; bestStart = streakStart; bestEnd = day.Key; }
            previous = day.Key;
        }
        var longest = _sessions.OrderByDescending(x => x.EndedAt - x.StartedAt).FirstOrDefault();
        var bestDay = played.OrderByDescending(x => x.Value).FirstOrDefault();
        var last = _sessions.OrderByDescending(x => x.StartedAt).FirstOrDefault();
        var cards = new Grid { Width = BodyWidth, Height = 80, ColumnSpacing = 4 };
        for (var i = 0; i < 6; i++) cards.ColumnDefinitions.Add(new ColumnDefinition());
        AddCard(cards, 0, "总游戏时长", Format(total), $"已启动 {_sessions.Count} 次");
        AddCard(cards, 1, "平均每天时长",
            Format(played.Length == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(total.Ticks / played.Length)),
            $"已游玩 {played.Length} 天");
        AddCard(cards, 2, "最长连续天数", $"{longestStreak} 天",
            longestStreak == 0 ? "暂无记录" : $"{bestStart:yyyy/MM/dd} - {bestEnd:yyyy/MM/dd}");
        AddCard(cards, 3, "最长单次时长", Format(longest is null ? TimeSpan.Zero : longest.EndedAt - longest.StartedAt),
            longest?.StartedAt.ToLocalTime().ToString("yyyy-MM-dd") ?? "暂无记录");
        AddCard(cards, 4, "最长单日时长", Format(bestDay.Value),
            bestDay.Key == default ? "暂无记录" : bestDay.Key.ToString("yyyy-MM-dd"));
        AddCard(cards, 5, "上次启动", last is null ? "0m" : Format(last.EndedAt - last.StartedAt),
            last?.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "暂无记录");
        return cards;
    }

    private static void AddCard(Grid grid, int column, string title, string value, string detail)
    {
        var stack = new StackPanel { Padding = new Thickness(12, 9, 8, 8), Spacing = 3 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = MutedText,
            TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = MutedText,
            TextTrimming = TextTrimming.CharacterEllipsis });
        var card = new Border { Background = Brush(76, 76, 76),
            BorderBrush = Brush(98, 98, 98), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Child = stack };
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }

    private UIElement BuildRangeSelector()
    {
        var row = new Grid { Height = 32, Margin = new Thickness(4, 20, 4, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var labels = new[] { "最近 15 天", "最近 12 周", "最近 12 月" };
        for (var i = 0; i < labels.Length; i++)
        {
            var index = i;
            var button = new Button { Content = labels[i], MinWidth = 86, Height = 32,
                Padding = new Thickness(10, 0, 10, 0), FontSize = 14,
                Foreground = MutedText, Background = Brush(0, 0, 0, 0),
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(5) };
            button.Click += (_, _) => RenderChart(index);
            _rangeButtons[i] = button;
            choices.Children.Add(button);
        }
        row.Children.Add(choices);
        Grid.SetColumn(_rangeTotal, 1);
        _rangeTotal.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(_rangeTotal);
        return row;
    }

    private void RenderChart(int range)
    {
        _chart.Children.Clear();
        for (var i = 0; i < _rangeButtons.Length; i++)
        {
            _rangeButtons[i].Background = i == range ? Brush(92, 92, 92) : Brush(0, 0, 0, 0);
            _rangeButtons[i].Foreground = i == range ? Brush(255, 255, 255) : MutedText;
        }
        var buckets = GetBuckets(range);
        _rangeTotal.Text = $"总计 {Format(TimeSpan.FromTicks(buckets.Sum(x => x.Value.Ticks)))}";
        var axisMax = Math.Max(1, Math.Ceiling(buckets.Max(x => x.Value.TotalHours) * 2) / 2);
        const double top = 10, bottom = 135, left = 48, right = 949;
        for (var tick = 0; tick < 3; tick++)
        {
            var y = bottom - tick * (bottom - top) / 2;
            _chart.Children.Add(new Line { X1 = left, X2 = right, Y1 = y, Y2 = y,
                Stroke = Brush(105, 105, 105), StrokeThickness = 1 });
            var label = new TextBlock { Text = FormatAxis(axisMax * tick / 2), FontSize = 11,
                Foreground = MutedText, Width = 42, TextAlignment = TextAlignment.Right };
            Canvas.SetTop(label, y - 8);
            _chart.Children.Add(label);
        }
        var points = buckets.Select((x, i) => new Point(
            left + (right - left) * (i + 0.5) / buckets.Count,
            bottom - (bottom - top) * x.Value.TotalHours / axisMax)).ToArray();
        var figure = new PathFigure { StartPoint = points[0] };
        for (var i = 1; i < points.Length; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var mid = (a.X + b.X) / 2;
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Point(mid, a.Y), Point2 = new Point(mid, b.Y), Point3 = b
            });
        }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _chart.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry, Stroke = Brush(157, 191, 236), StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round
        });
        for (var i = 0; i < buckets.Count; i++)
        {
            var point = points[i];
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Brush(181, 208, 244) };
            Canvas.SetLeft(dot, point.X - 4);
            Canvas.SetTop(dot, point.Y - 4);
            ToolTipService.SetToolTip(dot, $"{buckets[i].Label}  {Format(buckets[i].Value)}");
            _chart.Children.Add(dot);
            var label = new TextBlock { Text = buckets[i].Label, FontSize = 11,
                Foreground = MutedText, Width = (right - left) / buckets.Count,
                TextAlignment = TextAlignment.Center };
            Canvas.SetLeft(label, point.X - label.Width / 2);
            Canvas.SetTop(label, 144);
            _chart.Children.Add(label);
        }
    }

    private List<(string Label, TimeSpan Value)> GetBuckets(int range)
    {
        var today = DateTime.Today;
        var buckets = new List<(string, TimeSpan)>();
        if (range == 0)
        {
            for (var i = 14; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                buckets.Add((day.ToString("MM-dd"), _days.GetValueOrDefault(day)));
            }
        }
        else if (range == 1)
        {
            var monday = today.AddDays(-MondayOffset(today));
            for (var i = 11; i >= 0; i--)
            {
                var start = monday.AddDays(-7 * i);
                buckets.Add((start.ToString("MM-dd"), SumDays(start, start.AddDays(7))));
            }
        }
        else
        {
            var first = new DateTime(today.Year, today.Month, 1);
            for (var i = 11; i >= 0; i--)
            {
                var start = first.AddMonths(-i);
                buckets.Add((start.ToString("yy/MM"), SumDays(start, start.AddMonths(1))));
            }
        }
        return buckets;
    }

    private TimeSpan SumDays(DateTime start, DateTime end) =>
        TimeSpan.FromTicks(_days.Where(x => x.Key >= start && x.Key < end).Sum(x => x.Value.Ticks));

    private FrameworkElement BuildHeatmap()
    {
        var canvas = new Canvas { Width = BodyWidth, Height = 152 };
        var today = DateTime.Today;
        var firstMonday = today.AddDays(-MondayOffset(today) - 52 * 7);
        const double labelWidth = 27, pitch = 17.55, cellSize = 15.55;
        foreach (var (row, text) in new[] { (0, "周一"), (6, "周日") })
        {
            var label = new TextBlock { Text = text, FontSize = 11, Foreground = MutedText };
            Canvas.SetTop(label, row * pitch - 2);
            canvas.Children.Add(label);
        }
        var months = new HashSet<int>();
        for (var week = 0; week < 53; week++)
        {
            for (var weekday = 0; weekday < 7; weekday++)
            {
                var day = firstMonday.AddDays(week * 7 + weekday);
                var x = labelWidth + week * pitch;
                var value = _days.GetValueOrDefault(day);
                var cell = new Border { Width = cellSize, Height = cellSize,
                    CornerRadius = new CornerRadius(2), Background = HeatBrush(value.TotalHours),
                    Visibility = day > today ? Visibility.Collapsed : Visibility.Visible };
                Canvas.SetLeft(cell, x);
                Canvas.SetTop(cell, weekday * pitch);
                ToolTipService.SetToolTip(cell, $"{day:yyyy-MM-dd}  {Format(value)}");
                canvas.Children.Add(cell);
                var monthKey = day.Year * 12 + day.Month;
                if (day.Day <= 7 && months.Add(monthKey))
                {
                    var month = new TextBlock { Text = $"{day.Month}月", FontSize = 11,
                        Foreground = MutedText };
                    Canvas.SetLeft(month, x);
                    Canvas.SetTop(month, 128);
                    canvas.Children.Add(month);
                }
            }
        }
        return canvas;
    }

    private static SolidColorBrush HeatBrush(double hours) => hours switch
    {
        <= 0 => Brush(82, 82, 82),
        < .5 => Brush(64, 66, 112, 168),
        < 2 => Brush(100, 75, 132, 202),
        < 5 => Brush(115, 91, 157, 225),
        _ => Brush(179, 145, 187, 239)
    };

    private static int MondayOffset(DateTime day) => ((int)day.DayOfWeek + 6) % 7;
    private static string FormatAxis(double hours) => hours == 0 ? "0h" : $"{hours:0.#}h";

    public static string Format(TimeSpan time) => time.TotalHours >= 1
        ? $"{(int)time.TotalHours}h {time.Minutes}m" : $"{(int)time.TotalMinutes}m";

    private static SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(Color.FromArgb(255, r, g, b));
    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));
}
