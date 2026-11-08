using System.ComponentModel;
using System.Globalization;
using AthanTime.Net.Models;

namespace AthanTime.Net.ViewModels;

/// <summary>Three explicit seasonal-clock modes; selector identities never depend on translated text.</summary>
public enum SummerTimeMode { Automatic, CustomDates, Manual }

/// <summary>A reusable start/end editor over JSON data, with stable translated choices and a parent save callback.</summary>
public sealed class SummerTimeRuleEditor : INotifyPropertyChanged
{
    private readonly Func<SummerTimeRule> _rule;
    private readonly Action<Action> _change;
    private readonly Func<string, string> _text;

    /// <summary>Creates editor state, not a service; the parent supplies settings, translation and persistence.</summary>
    public SummerTimeRuleEditor(Func<SummerTimeRule> rule, Action<Action> change, Func<string, string> text)
    {
        _rule = rule; _change = change; _text = text;
        Months = Enumerable.Range(1, 12).Select(month => new SelectOption<int>(month, "")).ToArray();
        Weekdays = Enum.GetValues<DayOfWeek>().Select(day => new SelectOption<DayOfWeek>(day, "")).ToArray();
        Occurrences = Enumerable.Range(1, 5).Select(week => new SelectOption<int>(week, "")).ToArray();
        Kinds = [new(false, ""), new(true, "")];
    }

    public IReadOnlyList<SelectOption<int>> Months { get; }
    public IReadOnlyList<SelectOption<DayOfWeek>> Weekdays { get; }
    public IReadOnlyList<SelectOption<int>> Occurrences { get; }
    public IReadOnlyList<SelectOption<bool>> Kinds { get; }
    public string this[string key] => _text(key);
    public bool IsWeekday => _rule().UseWeekday;
    public bool IsCalendarDay => !IsWeekday;
    public SelectOption<int> SelectedMonth { get => Months.Single(item => item.Value == _rule().Month); set { if (value is not null) Change(_rule().Month != value.Value, () => _rule().Month = value.Value); } }
    public SelectOption<DayOfWeek> SelectedWeekday { get => Weekdays.Single(item => item.Value == _rule().Weekday); set { if (value is not null) Change(_rule().Weekday != value.Value, () => _rule().Weekday = value.Value); } }
    public SelectOption<int> SelectedOccurrence { get => Occurrences.Single(item => item.Value == _rule().Occurrence); set { if (value is not null) Change(_rule().Occurrence != value.Value, () => _rule().Occurrence = value.Value); } }
    public SelectOption<bool> SelectedKind { get => Kinds.Single(item => item.Value == _rule().UseWeekday); set { if (value is not null) Change(_rule().UseWeekday != value.Value, () => _rule().UseWeekday = value.Value); } }
    public int Day { get => _rule().Day; set { value = Math.Clamp(value, 1, 31); Change(_rule().Day != value, () => _rule().Day = value); } }
    public int Hour { get => _rule().Hour; set { value = Math.Clamp(value, 0, 23); Change(_rule().Hour != value, () => _rule().Hour = value); } }
    public int Minute { get => _rule().Minute; set { value = Math.Clamp(value, 0, 59); Change(_rule().Minute != value, () => _rule().Minute = value); } }

    /// <summary>Updates captions and values in place after a language or location change.</summary>
    public void Refresh(CultureInfo culture)
    {
        foreach (var item in Months) item.Translate(culture.DateTimeFormat.GetMonthName(item.Value));
        foreach (var item in Weekdays) item.Translate(culture.DateTimeFormat.GetDayName(item.Value));
        foreach (var item in Occurrences) item.Translate(_text("SummerWeek" + item.Value));
        foreach (var item in Kinds) item.Translate(_text(item.Value ? "SummerWeekdayRule" : "SummerCalendarDay"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>Raised when bindings or their translated labels change.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Change(bool changed, Action assign)
    {
        if (!changed) return;
        _change(assign);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
