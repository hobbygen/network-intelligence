using NetworkIntelligence.Contracts;
namespace NetworkIntelligence.Application;

public static class QuietHours
{
    public static bool IsQuiet(AppSettings settings, int hour) => settings.QuietHoursEnabled && (settings.QuietStartHour == settings.QuietEndHour
        || (settings.QuietStartHour < settings.QuietEndHour ? hour >= settings.QuietStartHour && hour < settings.QuietEndHour : hour >= settings.QuietStartHour || hour < settings.QuietEndHour));
}
