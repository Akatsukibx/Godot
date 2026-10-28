using Godot;
using System.Collections.Generic;

// Placeholder sound: every effect and ambience loop is synthesised in code, so no audio files are needed yet.
public partial class Audio : Node
{
    const int Rate = 22050;

    static Audio _instance;
    static readonly Dictionary<string, AudioStreamWav> Cache = new();
    static readonly System.Random Rng = new(7);

    AudioStreamPlayer _music, _rain;
    string _musicName;

    public override void _EnterTree()
    {
        _instance = this;
        ProcessMode = ProcessModeEnum.Always;
    }

    public static void Play(string name, float volumeDb = 0f, float pitchJitter = 0.08f)
    {
        if (_instance == null)
            return;
        var player = new AudioStreamPlayer
        {
            Stream = Get(name),
            Bus = "SFX",
            VolumeDb = volumeDb,
            PitchScale = 1f + (float)GD.RandRange(-pitchJitter, pitchJitter),
        };
        _instance.AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    // Crossfades to a looping ambience track.
    public static void PlayMusic(string name)
    {
        if (_instance == null || _instance._musicName == name)
            return;
        _instance._musicName = name;
        var old = _instance._music;
        if (old != null)
        {
            var fadeOut = old.CreateTween();
            fadeOut.TweenProperty(old, "volume_db", -40f, 2.0);
            fadeOut.TweenCallback(Callable.From(old.QueueFree));
        }
        var player = new AudioStreamPlayer { Stream = Get(name), Bus = "Music", VolumeDb = -40f };
        _instance.AddChild(player);
        player.Play();
        player.CreateTween().TweenProperty(player, "volume_db", -6f, 2.0);
        _instance._music = player;
    }

    static AudioStreamWav Get(string name)
    {
        if (Cache.TryGetValue(name, out var cached))
            return cached;
        float[] s = name switch
        {
            "swing" => Swing(),
            "throw" => Sweep(0.14f, 500f, 1300f, 0.35f, 0.25f),
            "hit" => Hit(),
            "hurt" => Hurt(),
            "dash" => Whoosh(),
            "skill" => Shimmer(0.45f, new[] { 523f, 659f, 784f }),
            "heal" => Shimmer(0.6f, new[] { 659f, 880f, 1047f }),
            "levelup" => Arpeggio(new[] { 523f, 659f, 784f, 1047f }, 0.11f),
            "banish" => Sweep(0.5f, 620f, 140f, 0.45f, 0.15f),
            "release" => Shimmer(0.9f, new[] { 880f, 1320f, 1760f }),
            "pickup" => Sweep(0.08f, 1000f, 1600f, 0.3f, 0f),
            "ghost" => Wail(),
            "ui" => Sweep(0.04f, 900f, 700f, 0.25f, 0f),
            "amb_day" => AmbientDay(),
            "amb_night" => AmbientNight(),
            "amb_rain" => AmbientRain(),
            _ => new float[1],
        };
        var wav = ToWav(s, name.StartsWith("amb_"));
        Cache[name] = wav;
        return wav;
    }


    // Looping rain bed; intensity 0..1 fades it in and out.
    public static void SetRain(float intensity)
    {
        if (_instance == null)
            return;
        if (_instance._rain == null)
        {
            _instance._rain = new AudioStreamPlayer { Stream = Get("amb_rain"), Bus = "Music", VolumeDb = -60f };
            _instance.AddChild(_instance._rain);
        }
        var p = _instance._rain;
        p.VolumeDb = intensity > 0.01f ? Mathf.LinearToDb(intensity) - 4f : -60f;
        if (intensity > 0.01f && !p.Playing)
            p.Play();
        else if (intensity <= 0.01f && p.Playing)
            p.Stop();
    }

    // Soft low-passed hiss with scattered drops.
    static float[] AmbientRain()
    {
        int n = Rate * 4;
        var s = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            lp += (Noise() - lp) * 0.25f;
            s[i] = lp * 0.35f;
        }
        for (int d = 0; d < 260; d++)
        {
            int at = Rng.Next(n - 400);
            float amp = (float)Rng.NextDouble() * 0.25f;
            for (int k = 0; k < 300; k++)
                s[at + k] += Noise() * amp * Mathf.Exp(-k / 40f);
        }
        return s;
    }
    static AudioStreamWav ToWav(float[] samples, bool loop)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32000f);
            bytes[i * 2] = (byte)(v & 0xff);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xff);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = bytes,
            LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = loop ? samples.Length : 0,
        };
    }

    static float Noise() => (float)(Rng.NextDouble() * 2.0 - 1.0);

    // Fast attack, quadratic decay.
    static float Env(float p, float attack = 0.03f) =>
        p < attack ? p / attack : Mathf.Pow(1f - (p - attack) / (1f - attack), 2f);

    static float[] Sweep(float seconds, float f0, float f1, float volume, float noise)
    {
        int n = (int)(seconds * Rate);
        var s = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float p = i / (float)n;
            phase += Mathf.Tau * Mathf.Lerp(f0, f1, p) / Rate;
            s[i] = (Mathf.Sin(phase) * (1f - noise) + Noise() * noise) * Env(p) * volume;
        }
        return s;
    }

    static float[] Swing()
    {
        int n = (int)(0.18f * Rate);
        var s = new float[n];
        float y = 0f;
        for (int i = 0; i < n; i++)
        {
            float p = i / (float)n;
            float cutoff = Mathf.Lerp(0.08f, 0.45f, Mathf.Sin(p * Mathf.Pi));
            y += cutoff * (Noise() - y);
            s[i] = y * Env(p, 0.2f) * 0.8f;
        }
        return s;
    }

    static float[] Whoosh()
    {
        int n = (int)(0.25f * Rate);
        var s = new float[n];
        float y = 0f;
        for (int i = 0; i < n; i++)
        {
            float p = i / (float)n;
            y += Mathf.Lerp(0.03f, 0.3f, p) * (Noise() - y);
            s[i] = y * Mathf.Sin(p * Mathf.Pi) * 0.9f;
        }
        return s;
    }

    static float[] Hit()
    {
        var thump = Sweep(0.14f, 160f, 55f, 0.6f, 0f);
        for (int i = 0; i < thump.Length; i++)
            thump[i] += Noise() * Env(i / (float)thump.Length, 0.01f) * 0.25f;
        return thump;
    }

    static float[] Hurt()
    {
        int n = (int)(0.22f * Rate);
        var s = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float p = i / (float)n;
            phase += Mathf.Tau * Mathf.Lerp(240f, 110f, p) / Rate;
            s[i] = (Mathf.Sin(phase) > 0f ? 1f : -1f) * Env(p) * 0.18f;
        }
        return s;
    }

    static float[] Shimmer(float seconds, float[] freqs)
    {
        int n = (int)(seconds * Rate);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, p = i / (float)n, v = 0f;
            for (int k = 0; k < freqs.Length; k++)
                v += Mathf.Sin(Mathf.Tau * freqs[k] * t) * Mathf.Clamp(p * 8f - k * 0.6f, 0f, 1f);
            s[i] = v / freqs.Length * Env(p, 0.05f) * 0.4f;
        }
        return s;
    }

    static float[] Arpeggio(float[] notes, float noteSec)
    {
        int per = (int)(noteSec * Rate);
        var s = new float[per * (notes.Length + 3)];
        for (int k = 0; k < notes.Length; k++)
        for (int i = 0; i < per * 4 && k * per + i < s.Length; i++)
        {
            float t = i / (float)Rate;
            s[k * per + i] += Mathf.Sin(Mathf.Tau * notes[k] * t) * Env(i / (float)(per * 4), 0.02f) * 0.22f;
        }
        return s;
    }

    static float[] Wail()
    {
        int n = (int)(0.9f * Rate);
        var s = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float p = i / (float)n, t = i / (float)Rate;
            float f = Mathf.Lerp(340f, 220f, p) + Mathf.Sin(Mathf.Tau * 6f * t) * 12f;
            phase += Mathf.Tau * f / Rate;
            s[i] = Mathf.Sin(phase) * Mathf.Sin(p * Mathf.Pi) * 0.18f;
        }
        return s;
    }

    // 8-second loops; every frequency fits a whole number of cycles so the seam doesn't click.
    static float[] AmbientNight()
    {
        int n = 8 * Rate;
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float drone = (Mathf.Sin(Mathf.Tau * 55f * t) + 0.6f * Mathf.Sin(Mathf.Tau * 82.5f * t))
                * (0.7f + 0.3f * Mathf.Sin(Mathf.Tau * t / 8f)) * 0.1f;
            float gate1 = (t % 1f < 0.5f && (t * 16f) % 1f < 0.5f) ? 1f : 0f;
            float gate2 = ((t + 0.37f) % 1.6f < 0.6f && (t * 20f) % 1f < 0.5f) ? 1f : 0f;
            float crickets = Mathf.Sin(Mathf.Tau * 4200f * t) * gate1 * 0.035f + Mathf.Sin(Mathf.Tau * 3600f * t) * gate2 * 0.025f;
            s[i] = drone + crickets;
        }
        return s;
    }

    static float[] AmbientDay()
    {
        int n = 8 * Rate;
        var s = new float[n];
        float y = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            y += 0.02f * (Noise() - y);
            s[i] = y * (0.6f + 0.4f * Mathf.Sin(Mathf.Tau * t / 4f)) * 0.9f;
        }
        foreach (float start in new[] { 1.3f, 1.55f, 4.1f, 6.2f, 6.4f })
        {
            int a = (int)(start * Rate);
            var chirp = Sweep(0.12f, 2600f, 3600f, 0.08f, 0f);
            for (int i = 0; i < chirp.Length && a + i < n; i++)
                s[a + i] += chirp[i];
        }
        return s;
    }
}
