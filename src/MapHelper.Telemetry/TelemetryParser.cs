using System.Globalization;
using System.Text.Json;
using MapHelper.Core;

namespace MapHelper.Telemetry;

public sealed class TelemetryParser
{
    public Dictionary<string, Affiliation> ColorOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
    public static double? Number(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var p) && p.TryGetDoubleSafe(out var n) && double.IsFinite(n) ? n : null;
    public static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    public static bool Valid(JsonElement e) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("valid", out var p) && p.ValueKind == JsonValueKind.True;
    private static Vec2? Vector(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Array || a.GetArrayLength() != 2) return null;
        return a[0].TryGetDoubleSafe(out var x) && a[1].TryGetDoubleSafe(out var y) && double.IsFinite(x) && double.IsFinite(y) ? new(x, y) : null;
    }
    public static Dictionary<string, JsonElement> Raw(JsonElement e) => e.ValueKind == JsonValueKind.Object
        ? e.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()) : [];
    public MapInfo? ParseMap(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var e = doc.RootElement;
        if (!Valid(e)) return null;
        var min = Vector(e, "map_min"); var max = Vector(e, "map_max");
        if (min == null || max == null) throw new InvalidDataException("Map scale is missing.");
        var gen = (int)(Number(e, "map_generation") ?? 0);
        var key = FormattableString.Invariant($"{gen}:{min.Value.X}:{min.Value.Y}:{max.Value.X}:{max.Value.Y}");
        var map = new MapInfo(key, gen, min.Value, max.Value, Vector(e, "grid_steps") ?? default, Vector(e, "grid_zero") ?? default);
        if (!map.IsUsable) throw new InvalidDataException("Invalid map scale.");
        return map;
    }
    public IReadOnlyList<MapObservation> ParseObjects(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Map objects must be an array.");
        var result = new List<MapObservation>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            var type = Text(e, "type") ?? "unknown";
            var icon = Text(e, "icon") ?? "unknown";
            var x = Number(e, type == "airfield" ? "sx" : "x");
            var y = Number(e, type == "airfield" ? "sy" : "y");
            if (x == null || y == null) continue;
            var color = Text(e, "color") ?? "#AEB9C7";
            var dx = Number(e, "dx"); var dy = Number(e, "dy");
            Vec2? direction = dx != null && dy != null && Math.Abs(dx.Value) + Math.Abs(dy.Value) > 1e-6
                ? new Vec2(dx.Value, dy.Value).Normalized : null;
            var ex = Number(e, "ex"); var ey = Number(e, "ey");
            string? id = e.TryGetProperty("id", out var identifier) && identifier.ValueKind is JsonValueKind.String or JsonValueKind.Number ? identifier.ToString() : null;
            var (human, identityBasis) = Identity(e, type, icon);
            // Blink, team color and array position do not identify AI, death or loadout.
            result.Add(new(id, type, icon, Classify(icon, color), color, new(x.Value, y.Value), direction,
                ex != null && ey != null ? new(ex.Value, ey.Value) : null, human, Raw: Raw(e), IdentityBasis: identityBasis,
                BackgroundIcon: Text(e, "icon_bg")));
        }
        return result;
    }
    public Affiliation Classify(string icon, string color)
    {
        if (icon.Equals("Player", StringComparison.OrdinalIgnoreCase)) return Affiliation.Self;
        if (ColorOverrides.TryGetValue(color, out var affiliation)) return affiliation;
        // Known browser-map palettes, including squad green. Unknown colors remain unknown.
        return color.ToUpperInvariant() switch
        {
            "#00FF00" or "#00C800" or "#00FA00" or "#39D921" or "#67D756" => Affiliation.Squad,
            "#185AFF" or "#174DFF" or "#134AFF" or "#043FFF" or "#1E90FF" or "#00BFFF" or "#3C78FF" => Affiliation.Ally,
            "#FA3200" or "#FA0C00" or "#F00C00" or "#FF0000" or "#FF3300" => Affiliation.Enemy,
            _ => Affiliation.Unknown
        };
    }
    private static (bool? Human, string? Basis) Identity(JsonElement element, string type, string icon)
    {
        if (icon.Equals("Player", StringComparison.OrdinalIgnoreCase)) return (true, "Own player");
        // Explicit flags are optional extensions, not fields guaranteed by localhost:8111.
        // is_player is intentionally excluded: it can mean the own-player marker.
        var flags = new List<(bool Human, string Field)>();
        foreach (var field in new[] { "is_human", "is_ai", "is_bot" })
            if (element.TryGetProperty(field, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                flags.Add((field == "is_human" ? value.GetBoolean() : !value.GetBoolean(), field));
        if (flags.Count > 0)
            return flags.Select(f => f.Human).Distinct().Count() == 1
                ? (flags[0].Human, "API: " + string.Join(", ", flags.Select(f => f.Field)))
                : (null, "Conflicting AI / player flags");
        // These legacy ground-unit icons denote AI columns in the map API client reference.
        // Generic Fighter, Bomber, MediumTank, SPAA etc. must remain unknown.
        if (type == "ground_model" && icon is "Wheeled" or "Tracked") return (false, "AI icon type " + icon);
        return (null, null);
    }
    public PlayerTelemetry ParsePlayer(JsonElement? state, JsonElement? indicators)
    {
        var s = state ?? default; var i = indicators ?? default;
        var sv = Valid(s); var iv = Valid(i);
        var raw = Raw(s);
        foreach (var (key, value) in Raw(i)) raw["indicators." + key] = value;
        var weaponRaw = Raw(i).Where(p => p.Key.StartsWith("weapon", StringComparison.OrdinalIgnoreCase) || p.Key.StartsWith("ammo", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.Key, p => p.Value);
        var ammo = new Dictionary<string, double>();
        if (iv)
            foreach (var (key, value) in weaponRaw.Where(p => p.Key.StartsWith("ammo_counter", StringComparison.Ordinal)))
                if (value.TryGetDoubleSafe(out var n) && double.IsFinite(n) && n >= 0) ammo[key] = n;
        return new(sv || iv, iv ? Text(i, "type") : null, sv ? Number(s, "Mfuel, kg") : null,
            sv ? Number(s, "TAS, km/h") : null, sv ? Number(s, "H, m") : null,
            null, // Bare fuel_consume has vehicle-dependent semantics; use mass decrease until its unit is validated.
            new(ammo, weaponRaw), raw, iv ? Text(i, "army") : null);
    }
    public static (IReadOnlyList<CombatEvent> Events, long LastEvent, long LastDamage) ParseEvents(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid HUD response.");
        var result = new List<CombatEvent>(); long lastEvt = 0, lastDmg = 0;
        foreach (var kind in new[] { "events", "damage" })
        {
            if (!doc.RootElement.TryGetProperty(kind, out var list) || list.ValueKind != JsonValueKind.Array) continue;
            foreach (var e in list.EnumerateArray())
            {
                var id = Number(e, "id"); var text = Text(e, "msg");
                if (id == null || text == null) continue;
                if (kind == "events") lastEvt = Math.Max(lastEvt, (long)id); else lastDmg = Math.Max(lastDmg, (long)id);
                result.Add(new(kind + ":" + id.Value.ToString(CultureInfo.InvariantCulture), text));
            }
        }
        return (result, lastEvt, lastDmg);
    }
}

internal static class JsonHelpers
{
    public static bool TryGetDoubleSafe(this JsonElement e, out double value)
    {
        value = 0;
        return e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out value);
    }
}
