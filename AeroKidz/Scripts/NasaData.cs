using Godot;
using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AeroKidz;

/// <summary>
/// NASA DONKI SPACE WEATHER DATA (Autoload "NasaData").
///
/// At start-up it downloads the last 60 days from NASA's DONKI API (no key needed):
///   GET https://ccmc.gsfc.nasa.gov/DONKI-API/get/FLR            -> strongest solar flare  (classType, e.g. "X2.1")
///   GET https://ccmc.gsfc.nasa.gov/DONKI-API/get/notifications  -> text alerts, searched for:
///        "Kp index ... 7"              -> geomagnetic storm strength (0-9)   -> POWER drain
///        "Estimated speed: ~1048 km/s" -> fastest CME                        -> SHIELD drain
///        proton flux warnings / SEP    -> radiation                          -> OXYGEN drain
///
/// No internet? It keeps the SAVED values below (a real DONKI event from May 2014) and
/// IsLive stays false, so the game always works at a hackathon demo.
/// </summary>
public partial class NasaData : Node
{
    public static NasaData Instance { get; private set; }

    private const string Server = "https://ccmc.gsfc.nasa.gov/DONKI-API";
    private const int WindowDays = 59; // the API allows a 60-day window at most

    // ---- Values used by the game (start as the saved example data) ----
    public string FlareClass { get; private set; } = "M5.2";
    public float KpIndex { get; private set; } = 4f;
    public int CmeSpeedKms { get; private set; } = 1110;
    public bool ProtonFluxHigh { get; private set; } = true;

    /// <summary>True once real data has been downloaded this session.</summary>
    public bool IsLive { get; private set; }

    /// <summary>One line for the pop-ups: where the numbers came from.</summary>
    public string SourceText => IsLive
        ? "Live NASA data from the last 60 days"
        : "Saved NASA data - no internet";

    public override void _EnterTree() => Instance = this;

    public override void _Ready() => _ = FetchAsync();

    // ==================================================================
    // GAME RULES BUILT ON THE DATA (tune here)
    // ==================================================================

    /// <summary>The Day 9 storm is only 2 minutes, so every drain is this much faster (the nervous part!).</summary>
    public const float StormSpeedUp = 1.6f;

    /// <summary>Power drain %/s on Day 9. The Kp index multiplies it: Kp 4 = 4x the normal drain.</summary>
    public float PowerDrain => StormSpeedUp * 0.08f * Mathf.Clamp(KpIndex, 1f, 9f);

    /// <summary>Shield drain %/s on Day 9. CMEs over 1000 km/s hit 3x harder.</summary>
    public float ShieldDrain => StormSpeedUp * 0.2f * (CmeSpeedKms >= 1000 ? 3f : Mathf.Clamp(CmeSpeedKms / 500f, 1f, 2f));

    /// <summary>Oxygen drain %/s on Day 9. High proton flux doubles the leak.</summary>
    public float OxygenDrain => StormSpeedUp * 0.22f * (ProtonFluxHigh ? 2f : 1f);

    /// <summary>X-class flares make the power flicker (extra sudden drops).</summary>
    public bool IsXClass => FlareClass.StartsWith("X", StringComparison.OrdinalIgnoreCase);

    // ---- What Nova SAYS OUT LOUD during the storm (short - the player is busy!) ----
    // (real science words are kept - Kp index, CME, proton flux - with a tiny explanation)
    public string PowerLine => $"Kp index {KpIndex:0}! A magnetic storm! Power is going down!";
    public string ShieldLine => $"The CME hits at {CmeSpeedKms} kilometers per second! Shield is going down!";
    public string OxygenLine => ProtonFluxHigh
        ? "Proton flux is high! Radiation is hurting the air pipes! Oxygen is going down!"
        : "Proton flux is low. Oxygen is only leaking a little.";

    // ---- Even shorter, for the "REAL NASA DATA" pop-up list ----
    public string PowerShort => $"Kp index {KpIndex:0} of 9 (magnetic storm) → POWER drops";
    public string ShieldShort => $"CME speed {CmeSpeedKms} km/s (Sun cloud) → SHIELD drops";
    public string OxygenShort => ProtonFluxHigh ? "Proton flux HIGH (radiation) → OXYGEN leaks" : "Proton flux low (radiation) → OXYGEN leaks a little";

    // ==================================================================
    // DOWNLOAD + PARSE
    // ==================================================================
    private async Task FetchAsync()
    {
        string end = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string start = DateTime.UtcNow.AddDays(-WindowDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            string flr = await http.GetStringAsync($"{Server}/get/FLR?startDate={start}&endDate={end}");
            string notes = await http.GetStringAsync($"{Server}/get/notifications?startDate={start}&endDate={end}&type=ALL");

            bool gotSomething = false;
            gotSomething |= ParseFlares(flr);
            gotSomething |= ParseNotifications(notes);
            IsLive = gotSomething;
            GD.Print($"[NasaData] live={IsLive} flare={FlareClass} kp={KpIndex} cme={CmeSpeedKms}km/s protons={ProtonFluxHigh}");
        }
        catch (Exception e)
        {
            GD.Print($"[NasaData] offline, using saved data ({e.Message})");
        }
    }

    /// <summary>Strongest flare class in the window (A &lt; B &lt; C &lt; M &lt; X, then by number).</summary>
    private bool ParseFlares(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return false;

        string best = null;
        double bestValue = 0;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("classType", out var c) || c.ValueKind != JsonValueKind.String)
                continue;
            string cls = c.GetString();
            double v = FlareValue(cls);
            if (v > bestValue)
            {
                bestValue = v;
                best = cls;
            }
        }
        if (best == null)
            return false;
        FlareClass = best;
        return true;
    }

    private static double FlareValue(string cls)
    {
        if (string.IsNullOrEmpty(cls))
            return 0;
        double letter = char.ToUpperInvariant(cls[0]) switch
        {
            'A' => 1e-8, 'B' => 1e-7, 'C' => 1e-6, 'M' => 1e-5, 'X' => 1e-4, _ => 0,
        };
        double.TryParse(cls.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double n);
        return letter * Math.Max(n, 1);
    }

    private static readonly Regex KpExact = new(
        @"Kp\s*(?:index\s*)?(?:=|has reached level|reached|of)\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
    private static readonly Regex KpRange = new(@"Kp index (?:is )?(\d)\s*-\s*(\d)", RegexOptions.IgnoreCase);
    private static readonly Regex CmeSpeed = new(@"Estimated speed:\s*~?\s*(\d{2,4})\s*km/s", RegexOptions.IgnoreCase);
    private static readonly Regex ProtonWarning = new(
        @"(?:Increased energetic proton flux|proton flux[^.]{0,200}?(?:elevated|exceed|above (?:the )?threshold))",
        RegexOptions.IgnoreCase);

    private bool ParseNotifications(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return false;

        float maxKp = 0, maxKpRange = 0;
        int maxSpeed = 0;
        bool protons = false;
        int count = 0;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            count++;
            string type = item.TryGetProperty("messageType", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : "";
            string body = item.TryGetProperty("messageBody", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : "";

            if (type == "SEP")
                protons = true; // Solar Energetic Particle event = high proton flux

            foreach (Match m in KpExact.Matches(body))
                if (float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float kp) && kp <= 9f)
                    maxKp = Mathf.Max(maxKp, kp);
            foreach (Match m in KpRange.Matches(body))
                if (float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float kp) && kp <= 9f)
                    maxKpRange = Mathf.Max(maxKpRange, kp);
            foreach (Match m in CmeSpeed.Matches(body))
                if (int.TryParse(m.Groups[1].Value, out int s))
                    maxSpeed = Math.Max(maxSpeed, s);
            if (ProtonWarning.IsMatch(body))
                protons = true;
        }

        if (count == 0)
            return false;

        float kpFinal = maxKp > 0 ? maxKp : maxKpRange;
        if (kpFinal > 0) KpIndex = kpFinal;
        if (maxSpeed > 0) CmeSpeedKms = maxSpeed;
        ProtonFluxHigh = protons;
        return true;
    }
}
