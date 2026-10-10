using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SwbfHudEditor.Core.Events;

namespace SwbfHudEditor.App.Views;

/// <summary>One event in the Events panel: what it is, the value to send and what it holds now.</summary>
public sealed class EventRow : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required HudEventType Type { get; init; }
    public required HudEventSource Source { get; init; }
    public required string Details { get; init; }
    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();

    public string TypeName => Type.ToString();

    public Brush NameBrush => Source switch
    {
        HudEventSource.GameExt => Brushes.MediumAquamarine,
        HudEventSource.Transform => Brushes.SandyBrown,
        HudEventSource.Unknown => Brushes.Gray,
        _ => Brushes.LightSkyBlue,
    };

    private string _valueText = string.Empty;
    public string ValueText { get => _valueText; set => Set(ref _valueText, value); }

    private string _order = string.Empty;
    /// <summary>Its place among the sends, or an arrow when a transform published it.</summary>
    public string Order { get => _order; set => Set(ref _order, value); }

    private string _current = string.Empty;
    /// <summary>The value it holds now, sent or published.</summary>
    public string Current { get => _current; set => Set(ref _current, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// The events the loaded files use, to send by hand and watch: the scenario the canvas draws when
/// simulation is on.
/// </summary>
public partial class EventsPanel : UserControl
{
    private HudEventState? _state;
    private readonly ObservableCollection<EventRow> _rows = new();
    private readonly ICollectionView _view;

    public event EventHandler<bool>? SimulateChanged;
    public event EventHandler? ShowSelectedRequested;
    public event EventHandler? OnFootRequested;

    /// <summary>A scenario picked to send, one to save the sends under, one to forget.</summary>
    public event EventHandler<string>? ScenarioPicked;
    public event EventHandler<string>? ScenarioSaveRequested;
    public event EventHandler<string>? ScenarioDeleteRequested;

    private bool _listingScenarios;

    /// <summary>The saved scenarios' names, in the box's list.</summary>
    public void ShowScenarios(IEnumerable<string> names, string? current = null)
    {
        _listingScenarios = true;
        ScenarioBox.ItemsSource = names.ToList();
        ScenarioBox.Text = current ?? ScenarioBox.Text;
        _listingScenarios = false;
    }

    private void ScenarioBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listingScenarios || e.AddedItems.Count == 0 || e.AddedItems[0] is not string name) return;
        ScenarioPicked?.Invoke(this, name);
    }

    private void SaveScenario_Click(object sender, RoutedEventArgs e)
    {
        var name = ScenarioBox.Text.Trim();
        if (name.Length > 0) ScenarioSaveRequested?.Invoke(this, name);
    }

    private void DeleteScenario_Click(object sender, RoutedEventArgs e)
    {
        var name = ScenarioBox.Text.Trim();
        if (name.Length > 0) ScenarioDeleteRequested?.Invoke(this, name);
    }

    public EventsPanel()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = item => item is EventRow row &&
                               (string.IsNullOrWhiteSpace(FilterBox.Text) ||
                                row.Name.Contains(FilterBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        EventList.ItemsSource = _view;
    }

    public bool Simulate => SimulateBox.IsChecked == true;

    public void Bind(HudEventState state)
    {
        if (_state is not null)
        {
            _state.ClockChanged -= State_ClockChanged;
            _state.Changed -= State_ClockChanged;
        }
        _state = state;
        _state.ClockChanged += State_ClockChanged;
        _state.Changed += State_ClockChanged;
        ShowClock();
    }

    // ---- The clock ---------------------------------------------------------------------------------

    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly System.Diagnostics.Stopwatch _watch = new();
    private bool _showingClock;

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (PlayButton.IsChecked == true) Play();
        else Pause();
    }

    private void Play()
    {
        if (_state is null) return;
        if (_timer.Tag is null)
        {
            _timer.Tick += Timer_Tick;
            _timer.Tag = true;
        }
        _watch.Restart();
        _timer.Start();
        PlayButton.IsChecked = true;
        PlayIcon.Text = "\uE769";
    }

    private void Pause()
    {
        _timer.Stop();
        PlayButton.IsChecked = false;
        PlayIcon.Text = "\uE768";
    }

    /// <summary>Wall-clock time, as the game measures a frame, at most 0.1 s a tick.</summary>
    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_state is null) return;
        var elapsed = Math.Min(0.1, _watch.Elapsed.TotalSeconds);
        _watch.Restart();
        _state.SetTime(_state.Now + elapsed);
    }

    private void Rewind_Click(object sender, RoutedEventArgs e) => _state?.SetTime(0);

    private void ClockSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e) => Pause();

    private void ClockSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_showingClock || _state is null) return;
        _state.SetTime(e.NewValue);
    }

    private void State_ClockChanged(object? sender, EventArgs e) => ShowClock();

    /// <summary>The time, on a slider that reaches a few seconds past the last send.</summary>
    private void ShowClock()
    {
        if (_state is null) return;
        _showingClock = true;
        var last = _state.History.Count > 0 ? _state.History.Max(s => s.Time) : 0;
        ClockSlider.Maximum = Math.Max(10, Math.Max(last + 5, Math.Ceiling(_state.Now + 1)));
        ClockSlider.Value = _state.Now;
        ClockText.Text = _state.Now.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s";
        ClockText.ToolTip = _state.Now <= 0
            ? "At 0 s every send shows what it ends with: fades done, colours settled, lerps there. Play, or drag the slider, to see them over time."
            : "The clock's time: the HUD as it stands this long after the first send's frame.";
        _showingClock = false;
    }

    /// <summary>List the events in use, keeping what was typed for any already listed.</summary>
    public void SetEvents(IReadOnlyList<HudEventUse> uses, Func<HudEventUse, IReadOnlyList<string>> suggestions)
    {
        var typed = _rows.ToDictionary(r => r.Name, r => r.ValueText, StringComparer.OrdinalIgnoreCase);
        _rows.Clear();
        foreach (var use in uses.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase))
        {
            var hints = suggestions(use);
            _rows.Add(new EventRow
            {
                Name = use.Name,
                Type = use.Type,
                Source = use.Source,
                Details = Describe(use),
                Suggestions = hints,
                ValueText = typed.TryGetValue(use.Name, out var text) ? text
                    : hints.Count > 0 && use.Type is HudEventType.Uint or HudEventType.Model ? hints[0]
                    : HudEventText.Format(HudEventValue.Default(use.Type)),
            });
        }
    }

    private static string Describe(HudEventUse use)
    {
        var lines = new List<string> { $"{use.Name}: {use.Type}, {use.Source switch { HudEventSource.Stock => "sent by the game", HudEventSource.GameExt => "sent by BF2GameExt", HudEventSource.Transform => "published by a transform", _ => "nothing known sends it" }}" };
        if (use.Note is { } note) lines.Add(note);
        foreach (var w in use.WrittenBy.Take(4)) lines.Add($"from {w}");
        foreach (var r in use.ReadBy.Take(8)) lines.Add($"read by {r}");
        if (use.ReadBy.Count > 8) lines.Add($"and {use.ReadBy.Count - 8} more");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Show what each event holds after the last simulation.</summary>
    public void ShowSimulation(HudSimulation? simulation)
    {
        foreach (var row in _rows)
        {
            if (simulation is not null && simulation.Effective.TryGetValue(row.Name, out var send))
            {
                row.Order = send.From is null ? $"#{send.Seq}" : "→";
                row.Current = send.Value.ToString();
            }
            else
            {
                row.Order = string.Empty;
                row.Current = string.Empty;
            }
        }
    }

    private void Send(EventRow row)
    {
        if (_state is null) return;
        if (!HudEventText.TryParse(row.Type, row.ValueText, out var value))
        {
            row.Current = $"not a {row.Type}";
            return;
        }
        _state.Send(row.Name, value);
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EventRow row }) Send(row);
    }

    private void Value_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not FrameworkElement { DataContext: EventRow row }) return;
        Send(row);
        e.Handled = true;
    }

    private void Value_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Picking a suggestion sends it.
        if (sender is ComboBox { DataContext: EventRow row, SelectedItem: string picked } && e.AddedItems.Count > 0)
        {
            row.ValueText = picked;
            Send(row);
        }
    }

    private void Initialize_Click(object sender, RoutedEventArgs e) => _state?.Send("initialize", HudEventValue.FromBool(true));

    private void OnFoot_Click(object sender, RoutedEventArgs e) => OnFootRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Show and hide as the game does, as sending a whole scenario implies.</summary>
    public void StartSimulating() => SimulateBox.IsChecked = true;

    private void ShowSelected_Click(object sender, RoutedEventArgs e) => ShowSelectedRequested?.Invoke(this, EventArgs.Empty);

    private void Clear_Click(object sender, RoutedEventArgs e) => _state?.Clear();

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    private void SimulateBox_Changed(object sender, RoutedEventArgs e) => SimulateChanged?.Invoke(this, Simulate);

    /// <summary>The value typed for an event, if it is listed and parses.</summary>
    public HudEventValue? TypedValue(string name)
    {
        var row = _rows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        return row is not null && HudEventText.TryParse(row.Type, row.ValueText, out var v) ? v : null;
    }
}
