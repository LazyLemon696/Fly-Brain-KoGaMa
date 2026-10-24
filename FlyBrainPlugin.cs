// "Fly-Brain AI" for OpenKoGaMa: an NPC fly that sits, crawls erratically, panics when it is hurt,
// and brings itself back to its spawn point five seconds after it dies.
// Using real fruit fly main https://github.com/eonsystemspbc/fly-brain
// Build it using this command: dotnet build -c Release -p:ServerRoot="C:\your\directory\to\openkogama-server"
// Put the dll in the server's plugins folder and type !reload.
// In game: !spawnfly (/spawnfly also works) puts a Fly two blocks above you. Run it again for more flies.
// Credits: LazyLemon - Creator of this plugin; Nightus - Creator of OpenKogama and deadzone plugin; FlyWire - Creator of fly's recompilation

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using OpenKogama.Api;

namespace FlyBrain;
internal enum FlyState
{
    Sitting,
    Crawling,
    Panic,
    Dead,
}

public sealed class FlyBrainPlugin : IPlugin
{
    const string FlyName = "Fly";
    const string SkinFile = "fly-skin.kgavatar";
    const string CommandName = "spawnfly";
    const float SpawnHeight = 2f;
    const int MaxFlies = 32;
    const float SweepSeconds = 5f;

    readonly Lock _sync = new();
    readonly List<FlyAgent> _flies = [];

    IServer? _server;
    AvatarSkin? _flySkin;
    float _sweepIn = SweepSeconds;

    public void Load(IServer server)
    {
        _flySkin = LoadFlySkin(server);

        lock (_sync)
        {
            _server = server;
            server.Update += OnUpdate;
            server.NpcDamaged += OnNpcDamaged;
            server.NpcKilled += OnNpcKilled;
            server.AddCommand(CommandName, OnSpawnFly);
        }

        server.Log($"fly-brain: loaded, type !{CommandName} to summon a {FlyName}");
    }

    public void Unload()
    {
        FlyAgent[] flies;
        lock (_sync)
        {
            if (_server is not { } server) return;
            server.Update -= OnUpdate;
            server.NpcDamaged -= OnNpcDamaged;
            server.NpcKilled -= OnNpcKilled;
            _server = null;
            flies = [.. _flies];
            _flies.Clear();
        }

        foreach (FlyAgent fly in flies) fly.Npc.Remove();
    }

    void OnUpdate(float dt)
    {
        lock (_sync)
        {
            if (_flies.Count == 0) return;
            _sweepIn -= dt;
            if (_sweepIn <= 0f)
            {
                _sweepIn = SweepSeconds;
                _flies.RemoveAll(static fly => !fly.Npc.Exists);
            }

            foreach (FlyAgent fly in _flies) fly.Tick(dt);
        }
    }

    void OnNpcDamaged(INpc npc, IPlayer? by, float amount)
    {
        lock (_sync) FlyOf(npc)?.Hurt();
    }

    void OnNpcKilled(INpc npc, IPlayer? by)
    {
        lock (_sync) FlyOf(npc)?.Kill();
    }

    FlyAgent? FlyOf(INpc npc)
    {
        foreach (FlyAgent fly in _flies)
            if (fly.Is(npc)) return fly;

        return null;
    }

    void OnSpawnFly(IPlayer player, string[] arguments)
    {
        if (player.Position is not Vector3 position)
        {
            player.Message("Join the game first, then summon the Fly");
            return;
        }

        lock (_sync)
        {
            if (_server is null) return;
            if (_flies.Count >= MaxFlies)
            {
                player.Message($"There are already {MaxFlies} flies");
                return;
            }
        }

        if (_flySkin is null) player.Message($"{SkinFile} is not embedded in the plugin, so the Fly wears your skin");
        AvatarSkin skin = _flySkin ?? player.Skin;

        Vector3 spawn = position + new Vector3(0f, SpawnHeight, 0f);
        INpc npc = player.World.SpawnNpc(FlyName, spawn, 0f, skin);
        var fly = new FlyAgent(npc, spawn);
        fly.Start();

        bool tracked;
        lock (_sync)
        {
            tracked = _server is not null;
            if (tracked) _flies.Add(fly);
        }

        if (!tracked) npc.Remove();
    }

    static AvatarSkin? LoadFlySkin(IServer server)
    {
        Assembly assembly = typeof(FlyBrainPlugin).Assembly;
        string? resource = assembly.GetManifestResourceNames().FirstOrDefault(name =>
            name.Equals(SkinFile, StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("." + SkinFile, StringComparison.OrdinalIgnoreCase));

        if (resource is null)
        {
            server.Log($"fly-brain: {SkinFile} is not embedded in the plugin, flies will wear the skin of whoever spawns them");
            return null;
        }

        try
        {
            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            return AvatarSkin.FromJson(reader.ReadToEnd());
        }
        catch (Exception error)
        {
            server.Log($"fly-brain: {SkinFile} could not be read ({error.Message}), flies will wear the skin of whoever spawns them");
            return null;
        }
    }
}

internal sealed class FlyAgent(INpc npc, Vector3 home)
{
    // Sitting
    const float SitMinSeconds = 0.5f;
    const float SitMaxSeconds = 2.0f;

    // Crawling
    const float CrawlSpeed = 5f;
    const float CrawlRadius = 8f;
    const float CrawlMinSeconds = 1.0f;
    const float CrawlMaxSeconds = 3.5f;
    const float SitAfterCrawlChance = 0.5f;
    const float HopChancePerTick = 0.04f;

    // Panic
    const float PanicSpeed = 15f;
    const float PanicMinDistance = 10f;
    const float PanicRadius = 20f;
    const float PanicShortSeconds = 1.2f;
    const float PanicLongSeconds = 1.3f;
    const float PanicRetargetSeconds = 0.4f;

    // Dead
    const float RespawnDelaySeconds = 5.0f;

    public INpc Npc { get; } = npc;
    public Vector3 Home { get; } = home;
    public FlyState State { get; private set; }

    static float PanicSeconds => Random.Shared.Next(2) == 0 ? PanicShortSeconds : PanicLongSeconds;

    readonly int _id = npc.Id;
    readonly int _objectId = npc.ObjectId;

    float _timer;
    float _retarget;
    bool _respawning;

    public bool Is(INpc other) => other.Id == _id && other.ObjectId == _objectId && other.Position == Npc.Position;

    public void Start() => BeginCrawl();

    public void Hurt()
    {
        if (State == FlyState.Dead || Npc.Health <= 0f) return;

        if (State == FlyState.Panic) _timer = PanicSeconds;
        else BeginPanic();
    }

    public void Kill()
    {
        if (State == FlyState.Dead) return;

        State = FlyState.Dead;
        _timer = RespawnDelaySeconds;
        _respawning = false;
    }

    public void Tick(float dt)
    {
        if (State == FlyState.Dead)
        {
            TickDead(dt);
            return;
        }

        if (!Npc.Alive)
        {
            Kill();
            return;
        }

        _timer -= dt;
        switch (State)
        {
            case FlyState.Sitting:
                if (_timer <= 0f) BeginCrawl();
                break;

            case FlyState.Crawling:
                TickCrawl();
                break;

            case FlyState.Panic:
                TickPanic(dt);
                break;
        }
    }

    void TickDead(float dt)
    {
        if (Npc.Alive)
        {
            _respawning = false;
            BeginCrawl();
            return;
        }

        if (_respawning) return;

        _timer -= dt;
        if (_timer > 0f) return;

        Npc.Respawn(Home);
        _respawning = true;
    }

    void BeginSit()
    {
        State = FlyState.Sitting;
        _timer = Random.Shared.Between(SitMinSeconds, SitMaxSeconds);
        Npc.Stop();
        Npc.Speed = 0f;
    }

    void BeginCrawl()
    {
        State = FlyState.Crawling;
        _timer = Random.Shared.Between(CrawlMinSeconds, CrawlMaxSeconds);
        Npc.Speed = CrawlSpeed;
        Wander();
    }

    void BeginPanic()
    {
        State = FlyState.Panic;
        _timer = PanicSeconds;
        _retarget = PanicRetargetSeconds;
        Npc.Speed = PanicSpeed;
        Flee();
        Hop();
    }

    void TickCrawl()
    {
        if (_timer <= 0f)
        {
            if (Random.Shared.NextSingle() < SitAfterCrawlChance) BeginSit();
            else BeginCrawl();
            return;
        }

        if (!Npc.Walking) Wander(); // arrived: pick the next destination
        if (Random.Shared.NextSingle() < HopChancePerTick) Hop();
    }

    void TickPanic(float dt)
    {
        if (_timer <= 0f)
        {
            BeginCrawl();
            return;
        }

        _retarget -= dt;
        if (_retarget > 0f) return;

        _retarget = PanicRetargetSeconds;
        Flee();
        Hop();
    }

    void Wander() => Npc.WalkTo(Npc.Position + Random.Shared.RingOffset(0f, CrawlRadius));

    void Flee() => Npc.WalkTo(Npc.Position + Random.Shared.RingOffset(PanicMinDistance, PanicRadius));

    void Hop()
    {
        if (Npc.Grounded) Npc.Jump();
    }
}

internal static class RandomExtensions
{
    extension(Random random)
    {
        public float Between(float min, float max) => min + (max - min) * random.NextSingle();

        public Vector3 RingOffset(float minRadius, float maxRadius)
        {
            float angle = random.NextSingle() * MathF.Tau;
            float distance = MathF.Sqrt(random.Between(minRadius * minRadius, maxRadius * maxRadius));
            return new Vector3(MathF.Cos(angle) * distance, 0f, MathF.Sin(angle) * distance);
        }
    }
}
