using System.Globalization;

namespace AthanTime.Net.Services;

/// <summary>Formats adjustable civil Hijri dates for display.</summary>
public interface IHijriDateService
{
    /// <summary>Formats a Gregorian date using the Hijri calendar and requested day adjustment.</summary>
    string Format(DateOnly date, string language, int adjustment);
}

/// <inheritdoc />
public sealed class HijriDateService : IHijriDateService
{
    private static readonly string[] _englishMonths =
    [
        "Muharram", "Safar", "Rabi al-Awwal", "Rabi al-Thani", "Jumada al-Awwal", "Jumada al-Thani",
        "Rajab", "Sha'ban", "Ramadan", "Shawwal", "Dhu al-Qi'dah", "Dhu al-Hijjah"
    ];

    private static readonly string[] _arabicMonths =
    [
        "مُحَرَّم", "صَفَر", "ربيع الأول", "ربيع الآخر", "جمادى الأولى", "جمادى الآخرة",
        "رجب", "شعبان", "رمضان", "شوّال", "ذو القعدة", "ذو الحجة"
    ];

    /// <inheritdoc />
    public string Format(DateOnly date, string language, int adjustment)
    {
        var adjusted = date.AddDays(Math.Clamp(adjustment, -2, 2)).ToDateTime(TimeOnly.MinValue);
        var calendar = new HijriCalendar();
        var day = calendar.GetDayOfMonth(adjusted);
        var month = calendar.GetMonth(adjusted);
        var year = calendar.GetYear(adjusted);
        var useArabicNames = language.Equals("ar", StringComparison.OrdinalIgnoreCase)
            || language.Equals("ur", StringComparison.OrdinalIgnoreCase);
        var monthName = useArabicNames ? _arabicMonths[month - 1] : _englishMonths[month - 1];
        return $"{day} {monthName} {year}";
    }
}
