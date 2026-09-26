using System.Globalization;
using System.Text.Json;

namespace MapHelper.Core;

public sealed record ContactDetails(string Title, string Summary, string RawValues);

public static class ContactInformation
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    public static string Distance(double meters) => meters < 1000 ? meters.ToString("0.#", English) + " m" : (meters / 1000).ToString("0.00", English) + " km";
    public static string Duration(double seconds)
    {
        seconds = Math.Ceiling(seconds);
        return seconds < 60 ? FormattableString.Invariant($"{seconds:0} s")
            : seconds < 3600 ? FormattableString.Invariant($"{Math.Floor(seconds / 60):0}:{seconds % 60:00} min")
            : FormattableString.Invariant($"{Math.Floor(seconds / 3600):0} h {Math.Floor(seconds % 3600 / 60):00} min");
    }
    public static string Team(Affiliation team) => team switch
    { Affiliation.Self => "Own player", Affiliation.Squad => "Own squad", Affiliation.Ally => "Ally", Affiliation.Enemy => "Enemy", _ => "Unknown affiliation" };
    public static ContactDetails Build(MapScene scene, Contact contact)
    {
        var observation = contact.Observation;
        var lines = new List<string> { Team(observation.Affiliation) };
        var identity = observation.IsHuman switch { true => "Player", false => "AI", _ => "AI / player unknown" };
        if (observation.IsMobile) lines.Add(identity + (observation.IdentityBasis is { } basis ? " · " + basis : ""));
        lines.Add(contact.Phase switch
        {
            ContactPhase.Predicted => $"Estimated position · last seen {contact.Age.ToString("0.0", English)} s ago",
            ContactPhase.Stale => "Stale data · disconnected",
            ContactPhase.Destroyed => "Destroyed",
            _ => "Currently visible"
        });
        if (contact.Ambiguous) lines.Add("Uncertain movement association");
        if (observation.ApiTargetMarked)
            lines.Add(contact.Phase == ContactPhase.Live ? "API target marker: " + observation.BackgroundIcon
                : "Previous API target marker: " + observation.BackgroundIcon);
        lines.Add("Type: " + observation.Type + " · Icon: " + observation.Icon);
        if (observation.SourceId != null) lines.Add("Object ID: " + observation.SourceId);
        lines.Add($"Map position: X {contact.Position.X.ToString("N0", English)} m · Y {contact.Position.Y.ToString("N0", English)} m");
        if (contact.Velocity is { IsFinite: true } velocity && !contact.Ambiguous)
            lines.Add("Ground speed: " + (velocity.Length * 3.6).ToString("0", English) + " km/h");
        if (contact.Direction is { IsFinite: true, Length: > 0 } direction)
            lines.Add("Course: " + ((Math.Atan2(direction.X, -direction.Y) * 180 / Math.PI + 360) % 360).ToString("0", English) + "°");
        var own = scene.Contacts.FirstOrDefault(c => c.Observation.Affiliation == Affiliation.Self && c.Phase == ContactPhase.Live);
        if (observation.Affiliation != Affiliation.Self && own != null && scene.Live && contact.Phase is ContactPhase.Live or ContactPhase.Predicted)
        {
            var destination = observation.Type == "airfield" && observation.EndPosition is { } end && scene.Map != null
                ? (contact.Position + scene.Map.ToWorld(end)) / 2 : contact.Position;
            var distance = (destination - own.Position).Length;
            lines.Add("Distance: " + (contact.Phase == ContactPhase.Predicted ? "≈ " : "") + Distance(distance));
            lines.Add("Time to target position: " + (own.Velocity is { IsFinite: true, Length: > .5 } speed && !own.Ambiguous
                ? "≈ " + Duration(distance / speed.Length) : "unavailable"));
        }
        if (observation.Affiliation == Affiliation.Self)
        {
            if (scene.Player is { Valid: true } player)
            {
                if (player.Vehicle != null) lines.Add("Aircraft / vehicle: " + player.Vehicle);
                AddNumber(lines, "Fuel", player.FuelKg, " kg");
                AddNumber(lines, "Altitude", player.AltitudeM, " m");
                AddNumber(lines, "True airspeed", player.TrueAirspeedKmh, " km/h");
                var consumption = scene.Range?.ConsumptionKgS ?? player.VerifiedConsumptionKgS;
                AddNumber(lines, "Consumption", consumption * 60, " kg/min");
                if (scene.Range is { } range)
                {
                    lines.Add("Remaining range: ≈ " + Distance(range.Meters));
                    lines.Add("Remaining flight time: ≈ " + Duration(range.Seconds));
                    lines.Add("One-way distance without a return reserve · measurement window " + range.WindowSeconds.ToString("0", English) + " s");
                }
                foreach (var (counter, amount) in player.Weapons.Ammunition) AddNumber(lines, "Ammunition · " + counter, amount, "");
            }
            if (scene.Events.Count > 0) lines.Add("\nRecent HUD messages:\n" + string.Join("\n", scene.Events));
        }
        var raw = new List<string>();
        AddRaw(raw, "Map object", observation.Raw);
        if (observation.Affiliation == Affiliation.Self && scene.Player is { } telemetry)
        {
            AddRaw(raw, "Own telemetry", telemetry.Raw);
            AddRaw(raw, "Weapon readings (meaning unclassified)", telemetry.Weapons.Raw);
        }
        var name = observation.Raw?.FirstOrDefault(p => p.Key is "name" or "player_name" && p.Value.ValueKind == JsonValueKind.String).Value;
        var title = name is { ValueKind: JsonValueKind.String } n && !string.IsNullOrWhiteSpace(n.GetString()) ? n.GetString()! : observation.Icon;
        return new(title, string.Join("\n", lines), string.Join("\n", raw));
    }
    private static void AddNumber(List<string> lines, string label, double? value, string unit)
    { if (value is { } number && double.IsFinite(number)) lines.Add(label + ": " + number.ToString("0.##", English) + unit); }
    private static void AddRaw(List<string> lines, string title, IReadOnlyDictionary<string, JsonElement>? values)
    {
        if (values?.Count is not > 0) return;
        lines.Add(title);
        foreach (var (key, value) in values.OrderBy(p => p.Key, StringComparer.Ordinal)) lines.Add(key + ": " + value);
        lines.Add("");
    }
}
