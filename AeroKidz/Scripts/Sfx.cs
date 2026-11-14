using Godot;
using System;
using System.Collections.Generic;

namespace AeroKidz;

/// <summary>
/// SOUND EFFECTS made in code (no audio files needed).
/// Usage from any node:  Sfx.Play(this, Sfx.Ding);
///
/// Have real .wav / .ogg files later? Replace Build() for that name with GD.Load&lt;AudioStream&gt;("res://...").
/// </summary>
public static class Sfx
{
    public const string Ding = "ding";       // correct!
    public const string Zap = "zap";         // electric short-circuit
    public const string Pop = "pop";         // catch / click
    public const string Bad = "bad";         // wrong / hit
    public const string Alarm = "alarm";     // alerts
    public const string Beep = "beep";       // countdown tick
    public const string Launch = "launch";   // rocket rumble
    public const string Boom = "boom";       // meteor impact

    private const int Rate = 22050;
    private static readonly Dictionary<string, AudioStreamWav> Cache = new();

    public static void Play(Node owner, string name, float volumeDb = -6f)
    {
        if (owner == null || !owner.IsInsideTree())
            return;
        var player = new AudioStreamPlayer { Stream = Get(name), VolumeDb = volumeDb };
        owner.AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    private static AudioStreamWav Get(string name)
    {
        if (!Cache.TryGetValue(name, out var s))
        {
            s = Build(name);
            Cache[name] = s;
        }
        return s;
    }

    private static AudioStreamWav Build(string name)
    {
        var rng = new Random(name.GetHashCode());
        float[] samples = name switch
        {
            Ding => Synth(0.45f, t =>
                (t < 0.12f ? Sine(t, 880) : Sine(t, 1318)) * Decay(t < 0.12f ? t : t - 0.12f, 6f)),
            Zap => Synth(0.35f, t =>
                (float)(rng.NextDouble() * 2 - 1) * (Sine(t, 55) > 0 ? 1f : 0.3f) * Decay(t, 5f)),
            Pop => Synth(0.08f, t => Sine(t, 660 + 900 * t * 10) * Decay(t, 30f)),
            Bad => Synth(0.25f, t => Square(t, 150) * 0.6f * Decay(t, 8f)),
            Alarm => Synth(0.8f, t => Square(t, t % 0.4f < 0.2f ? 780 : 620) * 0.35f),
            Beep => Synth(0.15f, t => Sine(t, 1000) * Decay(t, 12f)),
            Launch => Synth(2.6f, t =>
                (float)(rng.NextDouble() * 2 - 1) * Mathf.Min(t * 2f, 1f) * (1f - t / 2.6f) * 0.8f),
            Boom => Synth(0.6f, t => ((float)(rng.NextDouble() * 2 - 1) * 0.7f + Sine(t, 60)) * Decay(t, 6f)),
            _ => Synth(0.05f, _ => 0f),
        };
        return ToWav(samples);
    }

    private static float Sine(float t, float hz) => Mathf.Sin(Mathf.Tau * hz * t);
    private static float Square(float t, float hz) => Sine(t, hz) >= 0 ? 1f : -1f;
    private static float Decay(float t, float k) => Mathf.Exp(-k * t);

    private static float[] Synth(float seconds, Func<float, float> f)
    {
        var data = new float[(int)(seconds * Rate)];
        for (int i = 0; i < data.Length; i++)
            data[i] = f((float)i / Rate);
        return data;
    }

    private static AudioStreamWav ToWav(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32000f);
            bytes[i * 2] = (byte)(v & 0xFF);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = bytes,
        };
    }
}
