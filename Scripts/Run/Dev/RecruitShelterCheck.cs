namespace Fableland.Run.Dev;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Fableland.Data;
using Fableland.Debug;
using Fableland.Map;
using Fableland.MapCreation.Runtime;
using Fableland.Missions;
using Fableland.Shelter;

/// <summary>
/// Headless check for recruitment (NODES.gdd §3.1, §4.5) and the shelter as a place (§5.0) —
/// each rule was broken once to prove a line here catches it (40-QA.md §2):
///
///     Godot --headless --path . res://Scenes/Run/RecruitShelterCheck.tscn      # exit 0 = clean
///
/// Recruitment: every protagonist has one home world, agreeing with the boss table and with
/// the home medium its sounds and effects wear; a new run starts with its home protagonist
/// alone; a realm's 4-1 recruits the realm's own — the stand-in PumpKing beaten in Starland
/// brings Cleopastar, not himself — once, into an empty team slot (else the bench), at full HP
/// with the roster's Blessings, and the toast and the day's notes say so; a realm with no
/// protagonist recruits nobody; a one-fighter fight has no Switch slot and Tab does nothing, a
/// two-fighter fight switches; a save keeps its roster; the debug path gives all five.
///
/// The shelter: the table is valid; each kind of shelter builds its stations and only those;
/// every station agrees with the one exits rule (<c>ShelterController.Exits</c>) in every case;
/// the lead walks the room with the keys, its kit silent; the interact key opens the station's
/// dialog and freezes the walker; cancelled, nothing is spent; a Blessing confirmed ends the
/// day; a spent hub says why; no stamina closes the road and leaves the lantern; Team Build
/// changes who walks; an Eidolon Shelter has no lantern, crosses on its Blessing and — spent,
/// with no stamina — still lets you out; the Last Shelter; and a station added as a row and a
/// behaviour is built and used.
///
/// It drives the real scenes through real scene changes (Map → Arena → Shelter), so it lives
/// under the root, not as the current scene; saves go to a scratch folder. To LOOK at the
/// rooms, run it in a window with <c>-- --shots=/abs/dir</c> (40-QA §2): it saves a picture of
/// each kind of shelter there. Headless, it saves nothing.
/// </summary>
public partial class RecruitShelterCheck : Node
{
    private readonly List<string> _problems = new();
    private int _checks;
    private const int SpeedUp = 8;
    private readonly List<VirtualPad> _pads = new();
    private GameManager _lastArena;
    private string _shots;

    private static readonly string[] BuiltinStations =
    {
        ShelterTable.Road, ShelterTable.TeamBoard, ShelterTable.Lantern,
        ShelterTable.Campfire, ShelterTable.Whetstone, ShelterTable.Anvil,
    };

    /// <summary>The wonder items' stations (v0.12.10): built at a hub only, never an exit and
    /// never the Blessing — kept apart from the shelter's own six, whose offers are the exits rule.</summary>
    private static readonly string[] ItemStations = { ShelterTable.Plantation, ShelterTable.EnchantingAltar };

    private static HashSet<string> HubStations()
    {
        var all = new HashSet<string>(BuiltinStations);
        all.UnionWith(ItemStations);
        return all;
    }

    public override void _Ready()
    {
        // Pads step before the bodies they drive (this node is the first scene under the root).
        ProcessPriority = -100;
        ProcessPhysicsPriority = -100;
        CallDeferred(MethodName.Begin);
    }

    public override void _Process(double delta)
    {
        foreach (VirtualPad pad in _pads) pad.StepProcess();
    }

    public override void _PhysicsProcess(double delta)
    {
        foreach (VirtualPad pad in _pads) pad.StepPhysics();
    }

    private async void Begin()
    {
        // Scene changes free the current scene; giving that up keeps this node alive across them.
        GetTree().CurrentScene = null;

        int ticksPerSecond = Engine.PhysicsTicksPerSecond;
        double timeScale = Engine.TimeScale;
        string scratch = Path.Combine(Path.GetTempPath(), "fableland-recruit-" + Guid.NewGuid().ToString("N"));
        string savesBefore = SaveGameService.DirectoryOverride;
        bool allBefore = RunState.DebugAllProtagonistsOnNewRun;
        string spareStation = null;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--shots=")) _shots = arg["--shots=".Length..].Trim('"');
        if (_shots != null && DisplayServer.GetName() == "headless") _shots = null;   // nothing is drawn headless
        if (_shots != null) Directory.CreateDirectory(_shots);
        try
        {
            Directory.CreateDirectory(scratch);
            SaveGameService.DirectoryOverride = scratch;      // never the player's own slots
            CombatMapCatalog.UserMapsEnabled = false;         // never the player's own maps
            RunState.DebugAllProtagonistsOnNewRun = false;    // the rules, not a tester's switch
            InputDefaults.Ensure();

            CheckHomes();
            CheckStartingRoster();
            CheckRecruitRules();
            CheckSavesKeepTheirRoster();
            CheckDebugRoster();
            CheckShelterTable();
            CheckStationsAgreeWithExits();

            Engine.PhysicsTicksPerSecond = ticksPerSecond * SpeedUp;
            Engine.TimeScale = SpeedUp;
            await Ticks(1);
            await RecruitAtTheCapitals();
            await WalkTheHub();
            await SpentHubAndNoStamina();
            await CrossTheEidolonShelter();
            await TheLastShelter();
            await TendThePlantation();
            await EnchantAtTheAltar();
            spareStation = await AStationAddedLater();
        }
        catch (Exception e)
        {
            _problems.Add("recruit/shelter check threw: " + e);
        }
        finally
        {
            Engine.PhysicsTicksPerSecond = ticksPerSecond;
            Engine.TimeScale = timeScale;
            SaveGameService.DirectoryOverride = savesBefore;
            CombatMapCatalog.UserMapsEnabled = true;
            RunState.DebugAllProtagonistsOnNewRun = allBefore;
            if (spareStation != null) ShelterStations.Unregister(spareStation);
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch { /* a leftover scratch folder in the temp directory is harmless */ }
        }

        foreach (string problem in _problems) GD.Print("FAIL  " + problem);
        GD.Print(_problems.Count == 0
            ? $"RecruitShelterCheck: OK ({_checks} checks)"
            : $"RecruitShelterCheck: {_problems.Count} problem(s) in {_checks} checks");
        GetTree().Quit(_problems.Count == 0 ? 0 : 1);
    }

    private void Expect(bool ok, string what)
    {
        _checks++;
        if (!ok) _problems.Add(what);
    }

    private async Task Ticks(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<T> SceneIs<T>(int budgetTicks) where T : Node
    {
        for (int i = 0; i < budgetTicks; i++)
        {
            if (GetTree().CurrentScene is T scene && scene.IsNodeReady()) return scene;
            await Ticks(1);
        }
        return null;
    }

    private static string Names(IEnumerable<ProtagonistState> roster)
    {
        var names = new List<string>();
        foreach (ProtagonistState p in roster) names.Add(p.Id);
        return string.Join(", ", names);
    }

    private static CharacterController Player(SceneTree tree) =>
        tree.GetFirstNodeInGroup(CombatTargets.PlayerGroup) as CharacterController;

    /// <summary>A seed whose run draws every one of these worlds (the home world is in every run).</summary>
    private static string SeedWith(params string[] abbrs)
    {
        for (int i = 0; i < 400; i++)
        {
            string seed = "RECRUIT" + i;
            List<WorldDef> worlds = MapGenerator.Generate(seed).Worlds;
            if (Array.TrueForAll(abbrs, abbr => worlds.Exists(world => world.Abbr == abbr))) return seed;
        }
        return null;
    }

    private static MapNode Capital(RunState run, string abbr) =>
        run.Graph.Nodes.Find(node => node.Zone == abbr && node.Kind == NodeKind.Boss && node.Level == 4);

    private static MapNode FirstOf(RunState run, string abbr, Predicate<MapNode> what)
    {
        MapNode first = null;
        foreach (MapNode node in run.Graph.Nodes)
            if (node.Zone == abbr && what(node) && (first == null || string.CompareOrdinal(node.Id, first.Id) < 0)) first = node;
        return first;
    }

    // ---------------------------------------------------------------- recruitment: data

    /// <summary>One home per protagonist; the boss table's realm rows are their realm's own; the
    /// world data agrees with the home medium the presentation gives each fighter.</summary>
    private void CheckHomes()
    {
        foreach ((string id, string scenePath) in ProtagonistRoster.Entries)
        {
            int homes = WorldDef.Pool.FindAll(world => world.HomeProtagonistId == id).Count;
            Expect(homes == 1, $"homes: {id} must have exactly one home world in the pool (has {homes})");
            WorldDef home = WorldDef.HomeOf(id);
            var body = ResourceLoader.Load<PackedScene>(scenePath)?.Instantiate<CharacterController>();
            string medium = body == null ? null : Fableland.Audio.Medium.HomeFor(body.FeedbackId)?.Id;
            Expect(home != null && medium == home.Abbr,
                $"homes: {id}'s home world is {home?.Abbr ?? "none"} but its effects and sounds wear {medium ?? "no"} home medium — one home per fighter (Feedback.gdd §8)");
            body?.Free();
        }
        foreach (WorldDef world in WorldDef.Pool)
            if (world.HomeProtagonistId != null)
                Expect(ProtagonistRoster.GetScene(world.HomeProtagonistId) != null, $"homes: {world.Abbr}'s home protagonist '{world.HomeProtagonistId}' is not a protagonist");
        foreach (BossDef row in BossTable.RealmBosses())
            Expect(WorldDef.HomeProtagonistOf(row.WorldAbbr) == row.ProtagonistId,
                $"homes: {row.Id} holds {row.WorldAbbr}, whose own protagonist is '{WorldDef.HomeProtagonistOf(row.WorldAbbr) ?? "nobody"}', not '{row.ProtagonistId}'");
        Expect(RunState.StarterOf(MapGenerator.PomegraknightHome) == "Pomegraknight", "homes: a run in VanillaKindom must start with Pomegraknight");
        var lines = new List<string>();
        foreach (WorldDef world in WorldDef.Pool) lines.Add($"{world.Abbr} {world.HomeProtagonistId ?? "—"}");
        GD.Print($"  recruit  homes          {string.Join(" · ", lines)} (each agrees with its home medium)");
    }

    private void CheckStartingRoster()
    {
        RunState run = RunState.Instance;
        run.NewRun("RECRUITSTART");
        string starter = RunState.StarterOf(MapGenerator.PomegraknightHome);
        Expect(run.Owned.Count == 1 && run.Owned[0].Id == starter,
            $"start: a new run owns [{Names(run.Owned)}]; it starts with {starter} alone (NODES §3.1)");
        Expect(run.ActiveBuild.Count == 1 && run.ActiveBuild[0] == starter && run.CaptureBattleTeam().Count == 1,
            $"start: the team is [{string.Join(", ", run.ActiveBuild)}]; it must be {starter} alone");
        Expect(run.ProtagonistsCollected == 1, $"start: {run.ProtagonistsCollected} protagonists counted as collected; the starter is one");
        GD.Print($"  recruit  start          a new run owns {starter} alone, fielded; slots 2 and 3 empty");
    }

    /// <summary>The recruit path the arena uses, without a scene: who joins, where they go, what
    /// they bring, and that it happens once.</summary>
    private void CheckRecruitRules()
    {
        RunState run = RunState.Instance;
        // Who each node recruits: a realm's 4-1 its own protagonist; nothing else, nobody in zone 6.
        string seed = SeedWith("SL", "HC");
        Expect(seed != null, "recruit: no seed among 400 draws both Starland and HollowCastle");
        if (seed == null) return;
        run.NewRun(seed);
        int recruiting = 0;
        foreach (MapNode node in run.Graph.Nodes)
        {
            string who = RunState.RecruitOf(node);
            if (who.Length == 0) continue;
            recruiting++;
            Expect(node.Kind == NodeKind.Boss && node.Level == 4 && node.WorldIndex >= 0,
                $"recruit: {node.Id} ({node.Kind} LV{node.Level}) recruits {who}; only a realm's 4-1 recruits");
        }
        Expect(RunState.RecruitOf(Capital(run, "SL")) == "Cleopastar", "recruit: Starland's 4-1 must recruit Cleopastar (its own), whoever holds it");
        Expect(RunState.RecruitOf(Capital(run, "VK")) == "Pomegraknight", "recruit: VanillaKindom's 4-1 recruits its own — the starter, already owned");
        Expect(RunState.RecruitOf(Capital(run, "HC")) == "", "recruit: HollowCastle has no protagonist yet; its 4-1 recruits nobody");
        Expect(run.Graph.Nodes.TrueForAll(node => node.WorldIndex != -1 || RunState.RecruitOf(node) == ""), "recruit: a zone-6 node recruits somebody");
        int homes = run.Graph.Worlds.FindAll(world => world.HomeProtagonistId != null).Count;
        Expect(recruiting == homes, $"recruit: {recruiting} nodes recruit; this run's worlds have {homes} home protagonists, one capital each");

        // Joining: full HP, the Blessings every fighter shares, the first empty slot.
        run.NewRun("RECRUITRULES");
        run.AddAtk(10);
        run.AddDef(20);
        run.Owned[0].MaxHpPercentPoints = 10;
        run.Owned[0].HpRatio = 0.4f;
        run.DayEndNotes.Clear();
        Expect(run.GrantProtagonist("Cleopastar"), "recruit: Cleopastar was not recruited");
        ProtagonistState cleo = run.FindProtagonist("Cleopastar");
        Expect(cleo != null && cleo.HpRatio == 1f && cleo.BonusAtk == 10 && cleo.BonusDef == 20 && cleo.MaxHpPercentPoints == 10,
            $"recruit: Cleopastar joined with {cleo?.HpRatio:P0} HP, ATK +{cleo?.BonusAtk}, DEF +{cleo?.BonusDef}, max HP +{cleo?.MaxHpPercentPoints} pp; a recruit joins at full HP with the roster's Blessings (ATK +10, DEF +20, +10 pp — NODES §3.3)");
        Expect(string.Join(",", run.ActiveBuild) == "Pomegraknight,Cleopastar", $"recruit: the team is [{string.Join(", ", run.ActiveBuild)}]; a recruit takes the first empty slot");
        Expect(run.DayEndNotes.Contains("Cleopastar joins your team."), $"recruit: the day's notes say [{string.Join(" | ", run.DayEndNotes)}]");
        Expect(!run.GrantProtagonist("Cleopastar") && run.Owned.Count == 2 && run.ProtagonistsCollected == 2,
            $"recruit: a second grant must change nothing (roster {run.Owned.Count}, collected {run.ProtagonistsCollected})");

        // The fighter who opens the next battle stays the same when someone joins.
        run.ActiveProtagonistIndex = 1;
        Expect(run.GrantProtagonist("Pixolotl") && run.CaptureBattleTeam().MemberIds[run.CaptureBattleTeam().InitialIndex] == "Cleopastar",
            "recruit: a recruit changed who opens the next battle");
        // A full team: the next one waits on the bench.
        run.DayEndNotes.Clear();
        Expect(run.GrantProtagonist("PumpKing") && run.ActiveBuild.Count == 3 && !run.ActiveBuild.Contains("PumpKing") && run.Owned.Count == 4,
            $"recruit: with a full team PumpKing must wait on the bench (team [{string.Join(", ", run.ActiveBuild)}], roster {run.Owned.Count})");
        Expect(run.DayEndNotes.Exists(note => note.StartsWith("PumpKing joins your roster") && note.Contains("Team Build")),
            $"recruit: a benched recruit's note must say how to field them ([{string.Join(" | ", run.DayEndNotes)}])");

        // The reward path names a recruit who joined, and only one who did.
        run.CurrentAdventure = null;
        var bundle = new RewardBundle();
        bundle.ProtagonistGrants.Add("Sifu Pangda");
        run.ReportGoal(true, bundle);
        Expect(run.LastRewardLine.Contains("Sifu Pangda joins you"), $"recruit: the reward line '{run.LastRewardLine}' must name the recruit");
        run.ReportGoal(true, bundle);
        Expect(!run.LastRewardLine.Contains("joins you"), $"recruit: '{run.LastRewardLine}' names a recruit who was already there");
        GD.Print("  recruit  rules          only a realm's 4-1 recruits, its own; a recruit joins at full HP with the roster's Blessings, takes the first empty slot (else the bench), once");
    }

    private void CheckSavesKeepTheirRoster()
    {
        RunState run = RunState.Instance;
        Expect(run.StartNewRunInSlot(0, "RECRUITSAVE", out string error), "saves: a new run did not start: " + error);
        Expect(run.TryLoadRunFromSlot(0, out error) && run.Owned.Count == 1 && run.ActiveBuild.Count == 1,
            $"saves: a one-fighter run came back with [{Names(run.Owned)}] ({error}); loading must not top a roster up (until v0.12.9 it gave all five)");
        run.GrantProtagonist("Pixolotl");
        run.ActiveProtagonistIndex = 1;
        Expect(run.SaveActiveRun(out error) && run.TryLoadRunFromSlot(0, out error),
            "saves: the run did not save and load: " + error);
        Expect(Names(run.Owned) == "Pomegraknight, Pixolotl" && string.Join(",", run.ActiveBuild) == "Pomegraknight,Pixolotl"
               && run.ActiveProtagonistIndex == 1 && run.ProtagonistsCollected == 2,
            $"saves: a recruit came back as [{Names(run.Owned)}], team [{string.Join(", ", run.ActiveBuild)}], opener {run.ActiveProtagonistIndex}");

        // An old slot file keeps exactly what it holds — and one with nobody gets the starter.
        string slotThree = Path.Combine(SaveGameService.DirectoryOverride, "slot_3.json");
        File.WriteAllText(slotThree, "{\"version\":3,\"seed\":\"DEBUG\",\"day\":4,\"stamina\":3,\"map\":{\"devouredNodeIds\":[]},"
            + "\"owned\":[{\"id\":\"Pixolotl\",\"hpRatio\":1}],\"activeBuild\":[\"Pixolotl\"]}");
        Expect(run.TryLoadRunFromSlot(2, out error) && Names(run.Owned) == "Pixolotl",
            $"saves: a slot holding Pixolotl alone came back as [{Names(run.Owned)}] ({error})");
        File.WriteAllText(slotThree, "{\"version\":4,\"seed\":\"DEBUG\",\"day\":2,\"stamina\":3,\"map\":{\"devouredNodeIds\":[]},\"owned\":[],\"activeBuild\":[]}");
        Expect(run.TryLoadRunFromSlot(2, out error) && Names(run.Owned) == "Pomegraknight" && string.Join(",", run.ActiveBuild) == "Pomegraknight",
            $"saves: a slot with nobody came back as [{Names(run.Owned)}] ({error}); it gets the run's starter");
        SaveGameService.Delete(2);
        GD.Print("  recruit  saves          a save keeps its roster and team order — one fighter stays one; nobody becomes the starter");
    }

    private void CheckDebugRoster()
    {
        RunState run = RunState.Instance;
        run.NewRun("RECRUITDEBUG");
        run.DayEndNotes.Clear();
        int joined = run.DebugRecruitAll();
        Expect(joined == ProtagonistRoster.Entries.Length - 1 && run.Owned.Count == ProtagonistRoster.Entries.Length,
            $"debug: 'recruit all five' brought {joined}, roster [{Names(run.Owned)}]");
        Expect(run.ActiveBuild.Count == run.PartyCap() && run.ActiveBuild[0] == "Pomegraknight", $"debug: the team is [{string.Join(", ", run.ActiveBuild)}]; empty slots fill, the opener stays");
        Expect(run.DayEndNotes.Count == 0 && run.DebugRecruitAll() == 0, "debug: the debug path must be silent and idempotent");

        RunState.DebugAllProtagonistsOnNewRun = true;
        run.NewRun("RECRUITDEBUG2");
        RunState.DebugAllProtagonistsOnNewRun = false;
        Expect(run.Owned.Count == ProtagonistRoster.Entries.Length && string.Join(",", run.ActiveBuild) == "Sifu Pangda,Pixolotl,Cleopastar",
            $"debug: with the switch on a new run owns [{Names(run.Owned)}] with team [{string.Join(", ", run.ActiveBuild)}]; it must be all five and the prototype's team");
        run.NewRun("RECRUITDEBUG3");
        Expect(run.Owned.Count == 1, "debug: the switch off again, a new run must own one");
        GD.Print("  recruit  debug          'recruit all five' fills the run in progress; the switch starts new runs with all five and the old team");
    }

    // ---------------------------------------------------------------- recruitment: in the arena

    private async Task<GameManager> Enter(string where, MapNode node)
    {
        RunState run = RunState.Instance;
        run.BeginAdventure(node.Id);
        for (int i = 0; i < 600; i++)
        {
            await Ticks(1);
            if (GetTree().CurrentScene is GameManager arena && arena.IsNodeReady() && run.CurrentAdventure?.NodeId == node.Id
                && arena.CurrentMission != null && arena != _lastArena)
            {
                _lastArena = arena;
                return arena;
            }
        }
        Expect(false, $"{where}: entering {node.Id} never reached the arena");
        return null;
    }

    private async Task<bool> Win(GameManager arena)
    {
        arena.CurrentMission.DebugForceComplete();
        for (int i = 0; i < 300 && !arena.GoalResolved; i++) await Ticks(1);
        return arena.GoalResolved;
    }

    /// <summary>Ends the day and returns its summary — read at once: the map shows it and clears it.</summary>
    private async Task<string> EndTheDay(string where)
    {
        RunState.Instance.EndDay();
        string summary = RunState.Instance.LastDayEndSummary;
        Expect(await SceneIs<MapController>(600) != null && !RunState.Instance.RunFinished, $"{where}: the day did not end on the map");
        return summary;
    }

    /// <summary>Starting alone, beating Starland's capital — held by the stand-in PumpKing —
    /// brings Cleopastar; the next fight switches to her; HollowCastle's brings nobody.</summary>
    private async Task RecruitAtTheCapitals()
    {
        RunState run = RunState.Instance;
        string seed = SeedWith("SL", "HC");
        if (seed == null) return;
        run.NewRun(seed);

        // Starland's 4-1, with one fighter.
        MapNode capital = Capital(run, "SL");
        string where = $"capital SL ({capital?.Id})";
        GameManager arena = capital == null ? null : await Enter(where, capital);
        if (arena == null) return;
        var mission = arena.CurrentMission as BossMission;
        for (int i = 0; i < 600 && mission is { FieldsRealBoss: false }; i++) await Ticks(1);
        await Ticks(20);
        Expect(arena.BossId == BossTable.PumpKingId && arena.RecruitId == "Cleopastar",
            $"{where}: the arena fields '{arena.BossId}' and would recruit '{arena.RecruitId}'; the stand-in holds Starland and Starland's own joins (NODES §4.5)");
        var hud = arena.GetNodeOrNull<global::Hud>("Hud");
        Expect(hud != null && !hud.SwitchSlotShown, "one fighter: the Switch slot must be hidden — there is nobody to switch to");
        CharacterController before = Player(GetTree());
        Input.ActionPress("switch_protagonist");
        await Ticks(3);
        Input.ActionRelease("switch_protagonist");
        await Ticks(10);
        Expect(before != null && Player(GetTree()) == before && !before.IsQueuedForDeletion(), "one fighter: Tab must do nothing");
        Expect(await Win(arena), $"{where}: beating the boss did not resolve the fight");
        Expect(run.FindProtagonist("Cleopastar") != null && run.FindProtagonist("PumpKing") == null,
            $"{where}: after the win the roster is [{Names(run.Owned)}]; Cleopastar joins, and the stand-in's own PumpKing does not");
        Expect(string.Join(",", run.ActiveBuild) == "Pomegraknight,Cleopastar", $"{where}: the team is [{string.Join(", ", run.ActiveBuild)}]");
        Expect(mission != null && mission.SuccessToast.Contains("Cleopastar joins you"), $"{where}: the toast says '{mission?.SuccessToast}'");
        Expect(run.LastRewardLine.Contains("Cleopastar joins you"), $"{where}: the reward line says '{run.LastRewardLine}'");
        string summary = await EndTheDay(where);
        Expect(summary.Contains("Cleopastar joins your team"), $"{where}: the day's summary says '{summary}'");

        // A city of Starland, with two: the Switch slot shows her, and Tab brings her in.
        MapNode city = FirstOf(run, "SL", node => node.IsCombat && node.Level == 1);
        where = $"two fighters ({city?.Id})";
        arena = city == null ? null : await Enter(where, city);
        if (arena != null)
        {
            await Ticks(60 * 4);   // past the countdown
            hud = arena.GetNodeOrNull<global::Hud>("Hud");
            Expect(hud != null && hud.SwitchSlotShown && hud.SwitchFace?.ResourcePath == "res://Assets/Sprites/Protagonists/Cleopastar/UI/Mugshot.png",
                $"{where}: the Switch slot must show Cleopastar's face");
            before = Player(GetTree());
            Input.ActionPress("switch_protagonist");
            await Ticks(3);
            Input.ActionRelease("switch_protagonist");
            await Ticks(10);
            Expect(Player(GetTree())?.Name == "Cleopastar", $"{where}: Tab brought in {Player(GetTree())?.Name}, not Cleopastar");
            Expect(await Win(arena), $"{where}: the fight did not resolve");
            await EndTheDay(where);
        }

        // HollowCastle's 4-1: no protagonist lives there yet.
        capital = Capital(run, "HC");
        where = $"capital HC ({capital?.Id})";
        int roster = run.Owned.Count;
        arena = capital == null ? null : await Enter(where, capital);
        if (arena != null)
        {
            mission = arena.CurrentMission as BossMission;
            for (int i = 0; i < 600 && mission is { FieldsRealBoss: false }; i++) await Ticks(1);
            Expect(arena.RecruitId == "", $"{where}: the arena would recruit '{arena.RecruitId}'; HollowCastle has nobody");
            Expect(await Win(arena), $"{where}: beating the boss did not resolve the fight");
            Expect(run.Owned.Count == roster && mission != null && mission.SuccessToast == $"{BossTable.PumpKing.DisplayName} falls!",
                $"{where}: roster {roster} → {run.Owned.Count}, toast '{mission?.SuccessToast}'; a realm with no protagonist recruits nobody");
            await EndTheDay(where);
        }
        GD.Print($"  recruit  capitals       {seed}: alone, Tab does nothing and there is no Switch slot; the stand-in beaten in Starland brings Cleopastar into slot 2; next fight she is in the slot and Tab brings her; HollowCastle's brings nobody");
    }

    // ---------------------------------------------------------------- the shelter: data

    private void CheckShelterTable()
    {
        foreach (string problem in ShelterTable.Validate()) Expect(false, problem);
        foreach (string id in ShelterStations.BuiltinIds)
            Expect(ShelterTable.Station(id) != null, $"shelter: a behaviour is registered for '{id}', which is no station");
        foreach (string id in BuiltinStations)
            Expect(ShelterStations.Get(id) != null, $"shelter: '{id}' has no behaviour — a shelter would lose one of its actions");
        Expect(ShelterTable.ForWorld("SL") == ShelterTable.Default && ShelterTable.ForWorld(null) == ShelterTable.Default,
            "shelter: a world with no room of its own must get the default");
        Expect(!ShelterTable.Station(ShelterTable.Lantern).StandsIn(ShelterKind.Eidolon),
            "shelter: an Eidolon Shelter has no Finish the Day (NODES §1.2), so no lantern");
        Expect(InputMap.HasAction(InputDefaults.Interact) && UI.ControlsCard.KeysOf(InputDefaults.Interact) == "E / W / Up"
               && UI.ControlsCard.FirstKeyOf(InputDefaults.Interact) == "E",
            $"shelter: the interact key reads '{UI.ControlsCard.KeysOf(InputDefaults.Interact)}' (first '{UI.ControlsCard.FirstKeyOf(InputDefaults.Interact)}'); it is E, also W / Up — named as a player reads a key");
        Expect(UI.ControlsCard.KeysOf("jump") == "Space" && !UI.ControlsCard.Text().Contains("Physical") && !UI.ControlsCard.OneLine().Contains("Physical"),
            $"shelter: the controls card reads keys as the engine marks them ('{UI.ControlsCard.KeysOf("jump")}'), not as a player reads them");
        GD.Print($"  shelter  table          {ShelterTable.Stations.Count} stations, {ShelterTable.Layouts.Count} room(s), valid; interact is {UI.ControlsCard.KeysOf(InputDefaults.Interact)}");
    }

    /// <summary>Every station shows the one exits rule, in every case of every kind: the road is
    /// Back, the lantern Finish, the campfire, whetstone and anvil the Blessing; Team Build is
    /// always open — and a closed station always says why.</summary>
    private void CheckStationsAgreeWithExits()
    {
        int cases = 0;
        foreach (ShelterKind kind in new[] { ShelterKind.Hub, ShelterKind.Eidolon, ShelterKind.Last })
            foreach (bool blessed in new[] { true, false })
                foreach (bool canCross in kind == ShelterKind.Eidolon ? new[] { true, false } : new[] { false })
                    foreach (int stamina in new[] { 0, 3 })
                    {
                        if (canCross && !blessed) continue;   // a spent Blessing cannot cross
                        var exits = ShelterController.Exits(kind == ShelterKind.Eidolon, blessed, canCross, stamina);
                        var visit = new ShelterVisit { Kind = kind, Blessed = blessed, CanCross = canCross, Stamina = stamina, Exits = exits, CrossBlocked = canCross ? null : "far shore devoured" };
                        string tag = $"stations: {kind} blessed={blessed} cross={canCross} stamina={stamina}";
                        bool wayOut = false;
                        foreach (string id in BuiltinStations)
                        {
                            if (!ShelterTable.Station(id).StandsIn(kind)) continue;
                            StationOffer offer = ShelterStations.Get(id).Offer(visit);
                            bool expected = id switch
                            {
                                ShelterTable.Road => exits.Back,
                                ShelterTable.Lantern => exits.Finish,
                                ShelterTable.TeamBoard => true,
                                _ => exits.Blessings,
                            };
                            Expect(offer.Enabled == expected, $"{tag}: the {id} is {(offer.Enabled ? "open" : "closed")}; the exits rule says {(expected ? "open" : "closed")}");
                            Expect(offer.Enabled || !string.IsNullOrWhiteSpace(offer.Reason), $"{tag}: the closed {id} does not say why");
                            Expect(!string.IsNullOrWhiteSpace(offer.Line), $"{tag}: the {id} says nothing");
                            if (offer.Enabled && id != ShelterTable.TeamBoard) wayOut = true;
                        }
                        Expect(wayOut, $"{tag}: no station leads out — a dead end (#16)");
                        cases++;
                    }
        GD.Print($"  shelter  exits          {cases} cases of the three kinds: every station shows the one rule (road = Back, lantern = Finish, the three = the Blessing), every closed one says why, none is a dead end");
    }

    // ---------------------------------------------------------------- the shelter: in the room

    private async Task<ShelterController> EnterShelter(string where, MapNode node)
    {
        RunState.Instance.BeginAdventure(node.Id);
        ShelterController shelter = await SceneIs<ShelterController>(600);
        Expect(shelter != null, $"{where}: entering {node.Id} did not open the shelter");
        if (shelter == null) return null;
        await Ticks(40);   // the lead lands on the floor
        return shelter;
    }

    /// <summary>A picture of the screen, when asked for one (<c>--shots</c>, in a window).</summary>
    private async Task Shot(string name)
    {
        if (_shots == null) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        image?.SavePng(Path.Combine(_shots, name + ".png"));
    }

    private VirtualPad Pad(ShelterController shelter)
    {
        var pad = new VirtualPad();
        _pads.Add(pad);
        shelter.UseWalkerSource(pad);
        return pad;
    }

    /// <summary>Walks the lead to a station as a player would: hold toward it, let go once in
    /// its reach, come to rest; correct an overshoot.</summary>
    private async Task<bool> WalkTo(ShelterController shelter, VirtualPad pad, string stationId, List<string> passed = null)
    {
        ShelterStationNode target = shelter.Station(stationId);
        if (target == null) return false;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            float dx = target.Position.X - shelter.Walker.GlobalPosition.X;
            if (Mathf.Abs(dx) <= target.WidthPx * 0.25f && shelter.CurrentStation == target) return true;
            pad.Hold(dx > 0f ? ControlAction.MoveRight : ControlAction.MoveLeft);
            for (int i = 0; i < 60 * 30; i++)
            {
                await Ticks(1);
                string at = shelter.CurrentStation?.Def.Id;
                if (passed != null && at != null && (passed.Count == 0 || passed[^1] != at)) passed.Add(at);
                float d = target.Position.X - shelter.Walker.GlobalPosition.X;
                if (Mathf.Abs(d) <= target.WidthPx * 0.1f || Mathf.Sign(d) != Mathf.Sign(dx)) break;
            }
            pad.ReleaseAll();
            await Ticks(45);
        }
        return shelter.CurrentStation == target;
    }

    private void PushInteract()
    {
        GetViewport().PushInput(new InputEventAction { Action = InputDefaults.Interact, Pressed = true });
        GetViewport().PushInput(new InputEventAction { Action = InputDefaults.Interact, Pressed = false });
    }

    private HashSet<string> Built(ShelterController shelter)
    {
        var built = new HashSet<string>();
        foreach (ShelterStationNode station in shelter.Stations)
            if (station.Usable) built.Add(station.Def.Id);
        return built;
    }

    /// <summary>A blessed hub, walked with the keys: the stations in their places, the kit
    /// silent, the key's dialog, a cancel, Team Build's new lead, and a Blessing that ends the day.</summary>
    private async Task WalkTheHub()
    {
        RunState run = RunState.Instance;
        run.NewRun("RECRUITHUB");
        run.GrantProtagonist("Pixolotl");
        MapNode hub = FirstOf(run, MapGenerator.PomegraknightHome, node => node.Kind == NodeKind.TransportHub);
        string where = $"hub {hub?.Id}";
        ShelterController shelter = hub == null ? null : await EnterShelter(where, hub);
        if (shelter == null) return;

        Expect(shelter.Kind == ShelterKind.Hub, $"{where}: opened as {shelter.Kind}");
        var built = Built(shelter);
        Expect(built.SetEquals(HubStations()), $"{where}: built [{string.Join(", ", built)}]; a hub has the road, the team board, the lantern, the campfire, the whetstone, the anvil, the plantation and the enchanting altar — and no reserved spot without a behaviour");
        foreach (ShelterSpot spot in shelter.Layout.Spots)
        {
            ShelterStationNode node = shelter.Station(spot.StationId);
            if (node != null) Expect(Mathf.IsEqualApprox(node.Position.X, Units.Px(spot.XM)), $"{where}: the {spot.StationId} stands at {node.Position.X / Units.PixelsPerMeter:0.#} m, its spot is {spot.XM} m");
        }
        CharacterController walker = shelter.Walker;
        Expect(walker != null && walker.Name == "Pomegraknight" && walker.IsOnFloor() && walker.GlobalPosition.X > 0f && walker.GlobalPosition.X < Units.Px(shelter.Layout.WidthM),
            $"{where}: the lead ({walker?.Name}) must stand on the room's floor ({walker?.GlobalPosition})");
        Expect(GetTree().GetNodesInGroup(CombatTargets.PlayerGroup).Count == 1, "hub: one body in group player");
        // The arrival point is between the road and the team board: out of every station's reach.
        Expect(shelter.CurrentStation == null && shelter.PromptText == "",
            $"{where}: at the arrival point ({walker?.GlobalPosition.X / Units.PixelsPerMeter:0.0} m) nothing is in reach, yet '{shelter.CurrentStation?.Def.Id}' answers ('{shelter.PromptText}')");
        Expect(GetViewport().GetCamera2D() == walker?.GetNodeOrNull<Camera2D>("Camera2D"), $"{where}: the view is not the lead's own camera");

        await Shot("hub-arrival");

        // The kit is silent: no attack, no skill, no ultimate reaches the fighter in here.
        VirtualPad pad = Pad(shelter);
        await WalkTo(shelter, pad, ShelterTable.TeamBoard);
        int things = shelter.GetNode("World").GetChildCount();
        var magazine = walker.Magazine;
        foreach (ControlAction button in new[] { ControlAction.Attack, ControlAction.Skill1, ControlAction.Skill2, ControlAction.Ult }) pad.Tap(button);
        await Ticks(20);
        Expect(walker.ShiftCooldown.Remaining <= 0f && walker.ESkillCooldown.Remaining <= 0f && walker.Magazine == magazine
               && shelter.GetNode("World").GetChildCount() == things,
            $"{where}: a skill or an attack fired in a shelter (Shift {walker.ShiftCooldown.Remaining:0.0} s, E {walker.ESkillCooldown.Remaining:0.0} s, magazine {walker.Magazine}, {shelter.GetNode("World").GetChildCount() - things} new nodes)");
        // …and the same tap would have fired with nobody holding it shut — the probe can see one.
        var raw = new VirtualPad();
        _pads.Add(raw);
        walker.UseControls(raw);
        raw.Tap(ControlAction.Skill2);
        await Ticks(6);
        Expect(walker.ESkillCooldown.Remaining > 0f, $"{where}: control — Skill 2 on a bare pad started no cooldown, so the silence above proves nothing");
        shelter.UseWalkerSource(pad);
        await Ticks(30);

        // From the arrival point to the anvil, the stations come in their order.
        var passed = new List<string>();
        bool there = await WalkTo(shelter, pad, ShelterTable.Anvil, passed);
        Expect(there, $"{where}: the lead could not walk to the anvil (stands at {shelter.Walker.GlobalPosition.X / Units.PixelsPerMeter:0.0} m)");
        Expect(string.Join(",", passed).EndsWith("lantern,campfire,whetstone,anvil"), $"{where}: walking right passed [{string.Join(", ", passed)}]");
        Expect(shelter.PromptText.StartsWith("[E]") && shelter.PromptText.Contains("Sharpen Armor") && shelter.PromptText.Contains("ends the day"),
            $"{where}: at the anvil the prompt says '{shelter.PromptText}'");
        await Shot("hub-anvil");

        // The key opens the dialog; the walker stands still under it; cancelled, nothing is spent.
        int day = run.Day;
        PushInteract();
        await Ticks(3);
        ConfirmationDialog dialog = shelter.OpenDialog();
        Expect(dialog != null && dialog.DialogText.Contains("Sharpen Armor") && shelter.WalkerFrozen,
            $"{where}: the interact key at the anvil opened {(dialog == null ? "nothing" : $"'{dialog.DialogText}'")} (walker frozen: {shelter.WalkerFrozen})");
        float x = shelter.Walker.GlobalPosition.X;
        pad.Hold(ControlAction.MoveRight);
        await Ticks(40);
        pad.ReleaseAll();
        Expect(Mathf.Abs(shelter.Walker.GlobalPosition.X - x) < 2f, $"{where}: the walker moved {shelter.Walker.GlobalPosition.X - x:0} px under an open dialog");
        Expect(!shelter.TryUseStation(), $"{where}: a second use went through with a dialog open");
        dialog?.EmitSignal(AcceptDialog.SignalName.Canceled);
        await Ticks(4);
        Expect(shelter.OpenDialog() == null && !shelter.WalkerFrozen && run.IsShelterBlessed(hub.Id) && run.Day == day,
            $"{where}: cancelled, the dialog must close and nothing be spent (frozen {shelter.WalkerFrozen}, blessed {run.IsShelterBlessed(hub.Id)}, day {run.Day})");

        // The team board: Team Build; a new lead walks on from the same spot.
        Expect(await WalkTo(shelter, pad, ShelterTable.TeamBoard), $"{where}: could not walk to the team board");
        Expect(shelter.TryUseStation() && shelter.TeamBuildOpen && shelter.WalkerFrozen, $"{where}: the team board did not open Team Build");
        await Shot("hub-team-build");
        x = shelter.Walker.GlobalPosition.X;
        Expect(run.TrySetActiveBuild(new[] { "Pixolotl", "Pomegraknight" }, out string error), "hub: Team Build could not field Pixolotl: " + error);
        shelter.CloseTeamBuild();
        await Ticks(30);
        Expect(shelter.Walker?.Name == "Pixolotl" && Mathf.Abs(shelter.Walker.GlobalPosition.X - x) < Units.Px(1f) && !shelter.WalkerFrozen,
            $"{where}: after Team Build the lead must be Pixolotl, where Pomegraknight stood ({shelter.Walker?.Name} at {shelter.Walker?.GlobalPosition.X:0}, was {x:0})");
        Expect(GetTree().GetNodesInGroup(CombatTargets.PlayerGroup).Count == 1 && GetViewport().GetCamera2D() == shelter.Walker?.GetNodeOrNull<Camera2D>("Camera2D"),
            $"{where}: after the change of lead there must be one body and its own camera");

        // A Blessing confirmed: every fighter's DEF, the Blessing spent, the day over.
        Expect(await WalkTo(shelter, pad, ShelterTable.Anvil), $"{where}: could not walk back to the anvil");
        int defBefore = run.Owned[0].BonusDef;
        Expect(shelter.TryUseStation(), $"{where}: the anvil could not be used");
        dialog = shelter.OpenDialog();
        dialog?.EmitSignal(AcceptDialog.SignalName.Confirmed);
        Expect(await SceneIs<MapController>(600) != null, $"{where}: the confirmed Blessing did not end the day on the map");
        Expect(run.Day == day + 1 && !run.IsShelterBlessed(hub.Id) && run.Owned.TrueForAll(p => p.BonusDef == defBefore + 10),
            $"{where}: after Sharpen Armor: day {day} → {run.Day}, blessed {run.IsShelterBlessed(hub.Id)}, DEF [{string.Join(", ", run.Owned.ConvertAll(p => p.BonusDef))}]");
        GD.Print($"  shelter  hub            {hub.Id}: six stations in their spots; the lead walks there, its kit silent; E at the anvil asks, freezes the walker, cancels clean; Team Build changes who walks; confirmed, +10 DEF and the day ends");
    }

    /// <summary>The same hub, its Blessing spent: the three say why; the lantern ends the day.
    /// With no stamina the road is closed and says so; the lantern is the way out.</summary>
    private async Task SpentHubAndNoStamina()
    {
        RunState run = RunState.Instance;
        MapNode hub = FirstOf(run, MapGenerator.PomegraknightHome, node => node.Kind == NodeKind.TransportHub);
        if (hub == null || run.IsShelterBlessed(hub.Id)) return;   // WalkTheHub spent it
        string where = $"spent hub {hub.Id}";
        ShelterController shelter = await EnterShelter(where, hub);
        if (shelter == null) return;
        VirtualPad pad = Pad(shelter);
        Expect(await WalkTo(shelter, pad, ShelterTable.Campfire), $"{where}: could not walk to the campfire");
        Expect(!shelter.CurrentStation.Offer.Enabled && shelter.CurrentStation.Offer.Reason.Contains("spent") && !shelter.TryUseStation() && shelter.OpenDialog() == null,
            $"{where}: the campfire of a spent hub must be closed and say why ('{shelter.CurrentStation?.Offer.Reason}')");
        await Shot("hub-spent-campfire");
        Expect(shelter.Station(ShelterTable.Road).Offer.Enabled, $"{where}: with stamina the road must be open");
        int day = run.Day;
        Expect(await WalkTo(shelter, pad, ShelterTable.Lantern) && shelter.TryUseStation(), $"{where}: the lantern could not be used");
        ConfirmationDialog dialog = shelter.OpenDialog();
        Expect(dialog != null && dialog.DialogText.Contains("Finish the Day"), $"{where}: the lantern asked '{dialog?.DialogText}'");
        dialog?.EmitSignal(AcceptDialog.SignalName.Confirmed);
        Expect(await SceneIs<MapController>(600) != null && run.Day == day + 1, $"{where}: Finish the Day did not end the day ({day} → {run.Day})");

        run.Stamina = 0;
        where = $"hub {hub.Id} with no stamina";
        shelter = await EnterShelter(where, hub);
        if (shelter == null) return;
        pad = Pad(shelter);
        StationOffer road = shelter.Station(ShelterTable.Road).Offer;
        Expect(!road.Enabled && road.Reason.Contains("stamina"), $"{where}: the road must be closed and say why ('{road.Reason}')");
        Expect(await WalkTo(shelter, pad, ShelterTable.Road) && !shelter.TryUseStation(), $"{where}: the closed road was used");
        shelter.RequestLeave();
        await Ticks(10);
        Expect(GetTree().CurrentScene == shelter, $"{where}: asking the screen to leave with no stamina left it");
        Expect(shelter.Station(ShelterTable.Lantern).Offer.Enabled, $"{where}: the lantern must stay open — the one way out");
        run.Stamina = RunState.MaxStamina;
        run.ReturnToMap();
        await SceneIs<MapController>(600);
        GD.Print($"  shelter  spent, tired   a spent hub's three say why and the lantern ends the day; with no stamina the road is shut and says so, the lantern stays");
    }

    /// <summary>A bridge's Eidolon Shelter: no lantern; the road shut while the Blessing can
    /// carry you; Rest crosses without ending the day. Spent, and with no stamina, the road still
    /// opens — the old Back button refused there and the screen was a dead end.</summary>
    private async Task CrossTheEidolonShelter()
    {
        RunState run = RunState.Instance;
        run.NewRun("RECRUITBRIDGE");
        List<WorldDef> worlds = run.Graph.Worlds;
        MapNode a = FirstOf(run, worlds[0].Abbr, node => node.IsCombat && node.Level == 1);
        MapNode b = FirstOf(run, worlds[1].Abbr, node => node.IsCombat && node.Level == 1);
        Expect(a != null && b != null && run.Graph.AddRealityBridge(a, b, out MapNode bridge), "eidolon: no bridge could be built");
        bridge = run.Graph.Nodes.Find(node => !string.IsNullOrEmpty(node.RealityBridgeId) && node.Kind == NodeKind.Shelter);
        if (bridge == null) return;
        run.RebuildNodeIndex();
        run.CompletedNodeIds.Add(a.Id);
        run.CompletedNodeIds.Add(b.Id);           // the far shore is conquered: the crossing lands on the map
        run.CurrentNodeId = run.PreviousNodeId = a.Id;
        string where = $"eidolon {bridge.Id}";
        ShelterController shelter = await EnterShelter(where, bridge);
        if (shelter == null) return;
        Expect(shelter.Kind == ShelterKind.Eidolon, $"{where}: opened as {shelter.Kind}");
        var built = Built(shelter);
        Expect(!built.Contains(ShelterTable.Lantern) && built.Contains(ShelterTable.Road) && built.Contains(ShelterTable.Campfire) && built.Contains(ShelterTable.TeamBoard),
            $"{where}: built [{string.Join(", ", built)}]; an Eidolon Shelter has its Blessing, Team Build and the road — no lantern");
        StationOffer road = shelter.Station(ShelterTable.Road).Offer;
        Expect(!road.Enabled && road.Reason.Contains("way across"), $"{where}: while the Blessing can cross, the road must be shut and say why ('{road.Reason}')");
        foreach (ProtagonistState p in run.Owned) p.HpRatio = 0.5f;
        int day = run.Day;
        VirtualPad pad = Pad(shelter);
        Expect(await WalkTo(shelter, pad, ShelterTable.Campfire), $"{where}: could not walk to the campfire");
        Expect(shelter.PromptText.Contains("crosses the bridge"), $"{where}: the campfire's prompt says '{shelter.PromptText}'");
        await Shot("eidolon-campfire");
        Expect(shelter.TryUseStation(), $"{where}: the campfire could not be used");
        ConfirmationDialog dialog = shelter.OpenDialog();
        Expect(dialog != null && dialog.Title == "Eidolon Shelter", $"{where}: the campfire asked '{dialog?.Title}'");
        dialog?.EmitSignal(AcceptDialog.SignalName.Confirmed);
        Expect(await SceneIs<MapController>(600) != null && run.CurrentNodeId == b.Id && run.Day == day && !run.IsShelterBlessed(bridge.Id)
               && run.Owned.TrueForAll(p => Mathf.IsEqualApprox(p.HpRatio, 0.8f)),
            $"{where}: Rest must cross to {b.Id} without ending the day and heal (on {run.CurrentNodeId}, day {day} → {run.Day}, HP [{string.Join(", ", run.Owned.ConvertAll(p => p.HpRatio))}])");

        // Spent, and no stamina left: the road opens and takes you to the map (#16, v0.12.9).
        run.Stamina = 0;
        run.PreviousNodeId = b.Id;
        where = $"spent eidolon {bridge.Id}, no stamina";
        shelter = await EnterShelter(where, bridge);
        if (shelter == null) return;
        road = shelter.Station(ShelterTable.Road).Offer;
        Expect(road.Enabled && !shelter.Station(ShelterTable.Campfire).Offer.Enabled, $"{where}: the road must be open and the Blessing closed ({road.Enabled}, '{road.Reason}')");
        pad = Pad(shelter);
        Expect(await WalkTo(shelter, pad, ShelterTable.Road) && shelter.TryUseStation(), $"{where}: the road could not be used");
        Expect(await SceneIs<MapController>(600) != null, $"{where}: the road did not lead to the map — the room was a dead end");
        run.Stamina = RunState.MaxStamina;
        GD.Print($"  shelter  eidolon        no lantern; the road shut while the Blessing can carry you; Rest crosses to the far shore, heals, keeps the day; spent with no stamina, the road still leads out");
    }

    /// <summary>The Last Shelter (a Shelter node that is no bridge): the road into the VOID, the
    /// lantern, the Blessing.</summary>
    private async Task TheLastShelter()
    {
        RunState run = RunState.Instance;
        run.NewRun("RECRUITLAST");
        var last = new MapNode { Id = "XX-S", Kind = NodeKind.Shelter, WorldIndex = -1, Zone = "XX", LevelTag = "S", Level = 5, Color = Colors.White };
        run.Graph.Nodes.Add(last);
        run.RebuildNodeIndex();
        try
        {
            string where = "last shelter XX-S";
            ShelterController shelter = await EnterShelter(where, last);
            if (shelter == null) return;
            Expect(shelter.Kind == ShelterKind.Last && ShelterController.KindOf(last) == ShelterKind.Last, $"{where}: opened as {shelter.Kind}");
            var built = Built(shelter);
            Expect(built.SetEquals(BuiltinStations), $"{where}: built [{string.Join(", ", built)}]");
            StationOffer road = shelter.Station(ShelterTable.Road).Offer;
            Expect(road.Enabled && road.Line == "Continue into the VOID", $"{where}: the road says '{road.Line}' ({road.Enabled})");
            Expect(shelter.Station(ShelterTable.Campfire).Offer.Enabled && shelter.Station(ShelterTable.Lantern).Offer.Enabled, $"{where}: the Blessing and the lantern must be open");
            Expect(shelter.StatusText.Contains("singularity"), $"{where}: the status says '{shelter.StatusText}'");
            await Shot("last-shelter");
        }
        finally
        {
            // The node is the check's own: take it away before the map draws this run again.
            run.Graph.Nodes.Remove(last);
            run.RebuildNodeIndex();
            run.NewRun("RECRUITLAST");
            run.ReturnToMap();
            await SceneIs<MapController>(600);
        }
        GD.Print("  shelter  last shelter   the road leads into the VOID; the lantern and the Blessing are there");
    }

    // ---------------------------------------------------------------- the wonder items' stations

    /// <summary>The panel a station showed in the shelter's UI layer, if one is up.</summary>
    private static T PanelOf<T>(ShelterController shelter) where T : Control
    {
        foreach (Node child in shelter.GetNode("Ui").GetChildren())
            if (child is T panel && panel.Visible && !panel.IsQueuedForDeletion()) return panel;
        return null;
    }

    private static void Press(Node panel, string buttonName)
    {
        if (panel?.FindChild(buttonName, true, false) is Button button && !button.Disabled)
            button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    /// <summary>The plantation (ITEMS §4.3, v0.12.10): walked to and used with the keys, its panel
    /// sows a Rotten Mushroom from the backpack; four day ends later the station says it is ripe
    /// and the panel harvests four Weird Mushrooms. Done frees the walker.</summary>
    private async Task TendThePlantation()
    {
        RunState run = RunState.Instance;
        run.NewRun("SHELTERPLOTS");
        run.AddItem("rotten_mushroom");
        MapNode hub = FirstOf(run, MapGenerator.PomegraknightHome, node => node.Kind == NodeKind.TransportHub);
        string where = $"plantation {hub?.Id}";
        ShelterController shelter = hub == null ? null : await EnterShelter(where, hub);
        if (shelter == null) return;
        VirtualPad pad = Pad(shelter);
        Expect(await WalkTo(shelter, pad, ShelterTable.Plantation) && shelter.TryUseStation(), $"{where}: the plantation could not be used");
        PlantationPanel panel = PanelOf<PlantationPanel>(shelter);
        Expect(panel != null && shelter.WalkerFrozen, $"{where}: using the plantation must open its panel and hold the walker still");
        Press(panel, "Plant0_rotten_mushroom");
        PlotState plot = run.Ledger.PlotsAt(hub.Id)[0];
        Expect(plot.DefId == "rotten_mushroom" && plot.DaysLeft == 4 && !run.Items.Exists(item => item.DefId == "rotten_mushroom"),
            $"{where}: the panel's Plant must sow the backpack's Rotten Mushroom for 4 days ({plot.DefId}, {plot.DaysLeft})");
        Press(panel, "Done");
        await Ticks(2);
        Expect(!shelter.WalkerFrozen, $"{where}: Done must free the walker");

        for (int day = 0; day < 4; day++) run.RunItemDay();
        Expect(await WalkTo(shelter, pad, ShelterTable.Plantation) && shelter.TryUseStation(), $"{where}: the plantation could not be used again");
        string line = shelter.Station(ShelterTable.Plantation).Offer.Line;
        Expect(line.Contains("1 ripe"), $"{where}: after four days the station must say a plot is ripe ('{line}')");
        panel = PanelOf<PlantationPanel>(shelter);
        Press(panel, "Harvest0");
        int mushrooms = run.Items.FindAll(item => item.DefId == "weird_mushroom").Count;
        Expect(mushrooms == 4 && run.Ledger.PlotsAt(hub.Id)[0].Empty && panel.NewsText.Contains("Harvested"),
            $"{where}: Harvest must give four Weird Mushrooms and free the plot ({mushrooms}; '{panel?.NewsText}')");
        await Shot("hub-plantation");
        Press(panel, "Done");
        await Ticks(2);
        run.ReturnToMap();
        await SceneIs<MapController>(600);
        GD.Print("  shelter  plantation     walked to and used: a Rotten Mushroom sown from the panel, ripe after four day ends, harvested as four Weird Mushrooms");
    }

    /// <summary>The enchanting altar (ITEMS §7.1, v0.12.10): closed, and saying why, until a team
    /// member holds the Blessing of Shieldon; then its panel refuses THE VOID (Eternal), asks
    /// once, and enchants FanChen's Heart — spending the Blessing, which closes the altar again.</summary>
    private async Task EnchantAtTheAltar()
    {
        RunState run = RunState.Instance;
        run.NewRun("SHELTERALTAR");
        ProtagonistState lead = run.FindProtagonist(run.ActiveBuild[0]);
        run.AddItem("fanchens_heart");
        run.AddItem(RunState.VoidItemId);
        string heart = run.Items.Find(item => item.DefId == "fanchens_heart").InstanceId;
        string theVoid = run.Items.Find(item => item.DefId == RunState.VoidItemId).InstanceId;
        MapNode hub = FirstOf(run, MapGenerator.PomegraknightHome, node => node.Kind == NodeKind.TransportHub);
        string where = $"altar {hub?.Id}";
        ShelterController shelter = hub == null ? null : await EnterShelter(where, hub);
        if (shelter == null) return;
        VirtualPad pad = Pad(shelter);
        Expect(await WalkTo(shelter, pad, ShelterTable.EnchantingAltar), $"{where}: could not walk to the altar");
        StationOffer closed = shelter.Station(ShelterTable.EnchantingAltar).Offer;
        Expect(!closed.Enabled && closed.Reason.Contains("Blessing of Shieldon") && !shelter.TryUseStation(),
            $"{where}: with no Blessing held the altar must be closed and say why ('{closed.Reason}')");

        run.AddItem(RunState.BlessingOfShieldonId);
        run.HoldItem(lead, RunState.BlessingOfShieldonId, fromBackpack: true);
        Expect(shelter.TryUseStation(), $"{where}: with the Blessing held the altar must open");
        EnchantPanel panel = PanelOf<EnchantPanel>(shelter);
        Expect(panel?.FindChild("Enchant_" + theVoid, true, false) is Button { Disabled: true },
            $"{where}: THE VOID is Eternal — its Enchant must be refused");
        Press(panel, "Enchant_" + heart);
        Expect(panel?.Asking != null, $"{where}: enchanting must ask once before it spends the Blessing");
        panel?.Asking?.EmitSignal(AcceptDialog.SignalName.Confirmed);
        await Ticks(2);
        Expect(run.Ledger.EnchantedPossession.Contains(heart) && lead.HeldItemDefId == null && panel.NewsText.Contains("whole team"),
            $"{where}: confirming must enchant FanChen's Heart and spend the Blessing ('{panel?.NewsText}')");
        await Shot("hub-altar");
        Press(panel, "Done");
        await Ticks(2);
        Expect(!shelter.Station(ShelterTable.EnchantingAltar).Offer.Enabled, $"{where}: with the Blessing spent the altar must close again");
        run.ReturnToMap();
        await SceneIs<MapController>(600);
        GD.Print("  shelter  altar          closed without the Blessing (says why); with it, refuses THE VOID, asks once, enchants FanChen's Heart and closes again");
    }

    /// <summary>A reserved station given a behaviour — a row and a callback — is built at its
    /// spot and used; nothing else changes. Returns the station it registered (unregistered by
    /// the caller).</summary>
    private async Task<string> AStationAddedLater()
    {
        string spare = null;
        foreach (string id in new[] { ShelterTable.Plantation, ShelterTable.EnchantingAltar, ShelterTable.Wanderer })
            if (ShelterStations.Get(id) == null && ShelterTable.Station(id).StandsIn(ShelterKind.Hub)) { spare = id; break; }
        if (spare == null)
        {
            GD.Print("  shelter  added later    every reserved station has a behaviour now — nothing left to add");
            return null;
        }
        int used = 0;
        ShelterStations.Register(spare, new ShelterStationBehaviour
        {
            Offer = _ => StationOffer.Open("Check on it"),
            Use = visit => { used++; visit.Shelter.Say("Checked."); },
        });
        RunState run = RunState.Instance;
        run.NewRun("RECRUITADD");
        MapNode hub = FirstOf(run, MapGenerator.PomegraknightHome, node => node.Kind == NodeKind.TransportHub);
        string where = $"added later ({spare})";
        ShelterController shelter = hub == null ? null : await EnterShelter(where, hub);
        if (shelter == null) return spare;
        ShelterStationNode node = shelter.Station(spare);
        float spot = float.NaN;
        foreach (ShelterSpot s in shelter.Layout.Spots)
            if (s.StationId == spare) spot = Units.Px(s.XM);
        Expect(node != null && Mathf.IsEqualApprox(node.Position.X, spot), $"{where}: the registered station was not built at its spot");
        VirtualPad pad = Pad(shelter);
        Expect(await WalkTo(shelter, pad, spare) && shelter.TryUseStation() && used == 1 && shelter.StatusText == "Checked.",
            $"{where}: walking to it and using it ran its behaviour {used} time(s)");
        run.ReturnToMap();
        await SceneIs<MapController>(600);
        GD.Print($"  shelter  added later    the {spare}, given a behaviour, is built at its spot and used — a row and a callback");
        return spare;
    }
}
