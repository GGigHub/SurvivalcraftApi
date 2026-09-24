using System;

namespace SurvivalcraftGodot;

public enum WeatherKind
{
    Clear,
    Rain,
    Snow,
    Thunder,
}

/// <summary>
/// 世界时间决定季节和天气。一天是时钟走完 2π。每四季一轮：春雨、夏雷、秋短雨、冬雪，其余时间晴。
/// </summary>
public static class WorldClimate
{
    public const float DayLength = MathF.PI * 2f;

    public static int SeasonIndex(float clock)
    {
        float day = clock / DayLength;
        int season = (int)MathF.Floor(day / 4f) % 4;
        return season < 0 ? season + 4 : season;
    }

    public static string SeasonName(int season) => season switch
    {
        0 => "春",
        1 => "夏",
        2 => "秋",
        _ => "冬",
    };

    public static WeatherKind WeatherAt(float clock)
    {
        float day = clock / DayLength;
        float frac = day - MathF.Floor(day);
        if (frac < 0f)
        {
            frac += 1f;
        }

        return SeasonIndex(clock) switch
        {
            0 when frac is > 0.45f and < 0.75f => WeatherKind.Rain,
            1 when frac is > 0.40f and < 0.65f => WeatherKind.Thunder,
            2 when frac is > 0.50f and < 0.60f => WeatherKind.Rain,
            3 when frac is > 0.35f and < 0.70f => WeatherKind.Snow,
            _ => WeatherKind.Clear,
        };
    }

    public static string WeatherName(WeatherKind weather) => weather switch
    {
        WeatherKind.Rain => "雨",
        WeatherKind.Snow => "雪",
        WeatherKind.Thunder => "雷暴",
        _ => "晴",
    };

    public static float SeasonTemperature(int season) => season switch
    {
        1 => 8f,
        2 => -2f,
        3 => -12f,
        _ => 0f,
    };

    public static bool AdvanceBolt(ref float timer, float dt, float interval = 40f)
    {
        timer += dt;
        if (timer < interval)
        {
            return false;
        }

        timer -= interval;
        return true;
    }
}
