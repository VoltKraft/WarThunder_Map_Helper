using System.Runtime.CompilerServices;
using MapHelper.Core;

namespace MapHelper.Telemetry;

// Deterministic synthetic scenario, explicitly labelled in the UI. It is never presented as game data.
public sealed class DemoTelemetrySource(Func<double> clock) : ITelemetrySource
{
    public async IAsyncEnumerable<TelemetryPacket> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var start = clock();
        var map = new MapInfo("demo", 1, new(0, 0), new(64000, 48000), new(8000, 8000), default);
        yield return new(Endpoint.MapInfo, start, 1, Map: map);
        yield return new(Endpoint.Mission, start, 1, MissionStatus: "running");
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        do
        {
            var now = clock(); var t = now - start; var cycle = t % 24;
            var list = new List<MapObservation>
            {
                new("self", "aircraft", "Player", Affiliation.Self, "#FACC63", new(.40 + t % 120 * .0007, .63 - t % 120 * .0009), new(.6, -.8), IsHuman: true),
                new("ally1", "aircraft", "Fighter", Affiliation.Squad, "#00FF00", new(.31 + t % 120 * .0008, .55), new(1, 0), IsHuman: true),
                new("ally2", "aircraft", "Bomber", Affiliation.Ally, "#185AFF", new(.56, .73 - t % 120 * .0006), new(0, -1), IsHuman: false, IdentityBasis: "Synthetic test scenario"),
                new("enemy2", "aircraft", "Fighter", Affiliation.Enemy, "#FA3200", new(.69 - t % 120 * .0007, .40), new(-1, 0),
                    BackgroundIcon: cycle is >= 14 and < 18 ? "FighterTarget" : null),
                new("ground1", "ground_model", "MediumTank", Affiliation.Ally, "#185AFF", new(.33, .78), IsHuman: false, IdentityBasis: "Synthetic test scenario"),
                new("ground2", "ground_model", "MediumTank", Affiliation.Enemy, "#FA3200", new(.71, .27), IsHuman: false, IdentityBasis: "Synthetic test scenario"),
                new("base1", "bombing_point", "bombing_point", Affiliation.Enemy, "#FA3200", new(.74, .22)),
                new("field1", "airfield", "none", Affiliation.Ally, "#185AFF", new(.22, .79), EndPosition: new(.30, .86)),
                new("field2", "airfield", "none", Affiliation.Enemy, "#FA3200", new(.77, .16), EndPosition: new(.83, .22))
            };
            if (cycle < 10 || cycle > 13) list.Add(new("enemy1", "aircraft", "Assault", Affiliation.Enemy, "#FA3200", new(.61 - t % 120 * .0006, .35 + t % 120 * .0007), new(-.65, .75)));
            if (cycle < 18) list.Add(new("enemy3", "ship", "Ship", Affiliation.Enemy, "#FA3200", new(.25, .24)));
            yield return new(Endpoint.Objects, now, 1, Objects: list);
            yield return new(Endpoint.State, now, 1, Player: new(true, "Demo · Test aircraft", 140 - t % 120 * .45, 690, 3400, .45,
                new(new Dictionary<string, double> { ["ammo_counter1"] = 420, ["ammo_counter2"] = 180 }, new Dictionary<string, System.Text.Json.JsonElement>())));
            if (cycle is >= 18 and < 18.2)
                yield return new(Endpoint.Events, now, 1, Events: [new("demo-kill-" + (int)(t / 24), "DEMO: confirmed destruction of a ship", "enemy3", true)]);
        } while (await timer.WaitForNextTickAsync(cancellationToken));
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
