using System;

namespace SurvivalcraftGodot;

public sealed class Vitals
{
    public float Health = 20f;
    public float Hunger = 20f;
    public float Temperature = 20f;
    public float Wetness;
    public float Sickness;
    public float Air = 15f;
    public float Level = 1f;
    public bool Dead;
    public string DeathCause = "";

    public void Tick(float dt, bool inWater, bool inMagma, float sun, bool headUnderwater = false, bool raining = false, int season = 0)
    {
        if (Dead)
        {
            return;
        }

        Hunger -= dt * 0.015f;
        if (Hunger < 0f)
        {
            Hunger = 0f;
            Health -= dt * 0.4f;
            DeathCause = "饥饿";
        }

        if (inWater)
        {
            Wetness += dt * 0.8f;
        }
        else
        {
            Wetness -= dt * 0.25f;
        }

        Wetness = Clamp01(Wetness);
        if (raining && !headUnderwater)
        {
            Wetness += dt * 0.35f;
            Wetness = Clamp01(Wetness);
        }

        float seasonBias = WorldClimate.SeasonTemperature(season);
        Temperature += ((sun > 0.45f ? 22f : 8f) + seasonBias - Temperature) * dt * 0.15f;
        if (Temperature < 12f || Wetness > 0.7f)
        {
            Sickness += dt * 0.04f;
        }
        else
        {
            Sickness -= dt * 0.05f;
        }

        if (Sickness < 0f)
        {
            Sickness = 0f;
        }

        if (Sickness > 4f)
        {
            Health -= dt;
            DeathCause = "生病";
        }

        if (inMagma)
        {
            Health -= dt * 8f;
            DeathCause = "岩浆";
        }

        if (headUnderwater)
        {
            Air -= dt * 2f;
            if (Air < 0f)
            {
                Air = 0f;
                Health -= dt * 2f;
                DeathCause = "溺水";
            }
        }
        else if (Air < 15f)
        {
            Air += dt * 6f;
            if (Air > 15f)
            {
                Air = 15f;
            }
        }

        if (Health <= 0f)
        {
            Health = 0f;
            Dead = true;
        }
    }

    public void Eat()
    {
        Hunger += 6f;
        if (Hunger > 20f)
        {
            Hunger = 20f;
        }

        Health += 2f;
        if (Health > 20f)
        {
            Health = 20f;
        }
    }

    public void Wake()
    {
        Health += 4f;
        if (Health > 20f)
        {
            Health = 20f;
        }

        Sickness *= 0.5f;
    }

    public void AddExperience(int count)
    {
        for (int i = 0; i < count; i++)
        {
            float gain = 0.012f / MathF.Pow(1.08f, MathF.Floor(Level - 1f));
            Level += gain;
        }
    }

    public int LevelRank => (int)MathF.Floor(Level);

    static float Clamp01(float value)
    {
        if (value < 0f)
        {
            return 0f;
        }

        return value > 1f ? 1f : value;
    }
}
