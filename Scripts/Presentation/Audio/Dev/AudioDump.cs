namespace Fableland.Audio.Dev;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;
using Fableland.Audio.Synth;

/// <summary>
/// Dump mode (<c>Docs/Audio.gdd</c> §12, <c>40-QA.md</c>): writes every sound the game can
/// make to WAV files, so a person can play them and <c>Tools/audio/inspect.py</c> can draw
/// them. Nothing here needs a sound card:
///
///     Godot --headless --path . res://Scenes/Dev/AudioDump.tscn -- --out=/abs/folder
///           [--music=30] [--seed=1] [--only=VK,SL] [--no-sfx] [--no-music]
///
///   sfx/&lt;MEDIUM&gt;/&lt;event id&gt;.v&lt;n&gt;.wav   every row, every case, every variant — class and
///                                      realm rows in all ten media, a character's own rows
///                                      in its home medium
///   music/&lt;profile&gt;/i15.wav · i50.wav · i90.wav   the stems mixed at three intensities
///   music/&lt;profile&gt;/stem_&lt;name&gt;.wav             each stem, one loop
///   index.tsv                          one line per file with its measurements
/// </summary>
public partial class AudioDump : Node
{
    public override void _Ready()
    {
        string outDir = null;
        float musicSeconds = 30f;
        ulong seed = 1;
        HashSet<string> only = null;
        bool sfx = true, music = true;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--out=")) outDir = arg.Substring(6);
            else if (arg.StartsWith("--music=")) musicSeconds = float.Parse(arg.Substring(8), CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--seed=")) seed = ulong.Parse(arg.Substring(7));
            else if (arg.StartsWith("--only=")) only = new HashSet<string>(arg.Substring(7).Split(','));
            else if (arg == "--no-sfx") sfx = false;
            else if (arg == "--no-music") music = false;
        }
        if (string.IsNullOrEmpty(outDir))
        {
            GD.PrintErr("AudioDump: name a folder with -- --out=/abs/folder (it is never written inside the project or user://)");
            GetTree().Quit(2);
            return;
        }
        try
        {
            Run(outDir, musicSeconds, seed, only, sfx, music);
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PrintErr("AudioDump failed: " + e);
            GetTree().Quit(1);
        }
    }

    private static void Run(string outDir, float musicSeconds, ulong seed, HashSet<string> only, bool sfx, bool music)
    {
        var index = new StringBuilder("kind\tmedium\tid\tvariant\tfile\tseconds\tpeak_db\trms_db\tdc\tcentroid_hz\tbelow120\tabove5k\tabove8k\tattack_ms\tloop\n");
        int files = 0;
        long bytes = 0;
        var clock = Stopwatch.StartNew();

        if (sfx)
        {
            foreach (SfxDef def in SfxTable.All)
            {
                foreach (Medium medium in SfxTable.MediaOf(def))
                {
                    if (only != null && !only.Contains(medium.Id)) continue;
                    var cases = new SfxClip[def.CaseCount][];
                    AudioCache.RenderCases(def, medium, cases);
                    int tags = def.Tags.Length + 1;
                    for (int c = 0; c < cases.Length; c++)
                    {
                        string id = def.CaseId(c / tags, c % tags);
                        for (int v = 0; v < cases[c].Length; v++)
                        {
                            SfxClip clip = cases[c][v];
                            string rel = $"sfx/{medium.Id}/{id}.v{v + 1}.wav";
                            WavFile.Write(Path.Combine(outDir, rel), clip.Pcm, clip.SampleRate);
                            Line(index, "sfx", medium.Id, id, v + 1, rel, clip);
                            files++;
                            bytes += clip.Bytes;
                        }
                    }
                }
            }
            GD.Print($"AudioDump: {files} sound files ({bytes / 1048576.0:0.0} MB of PCM) rendered in {clock.Elapsed.TotalSeconds:0.0} s");
        }

        if (music)
        {
            foreach (MusicProfile profile in MusicProfiles.All)
            {
                if (only != null && !only.Contains(profile.Id) && !only.Contains(profile.Medium.Id)) continue;
                var bounce = Stopwatch.StartNew();
                MusicSet set = MusicBounce.Bounce(profile, seed);
                double took = bounce.Elapsed.TotalSeconds;
                for (int s = 0; s < set.Stems.Length; s++)
                {
                    string rel = $"music/{profile.Id}/stem_{profile.Stems[s].Name}.wav";
                    WavFile.Write(Path.Combine(outDir, rel), set.Stems[s].Pcm, set.Stems[s].SampleRate);
                    Line(index, "stem", profile.Medium.Id, profile.Id + "." + profile.Stems[s].Name, 1, rel, set.Stems[s]);
                }
                foreach (float intensity in new[] { 0.15f, 0.5f, 0.9f })
                {
                    float[] mix = MusicBounce.Mixdown(set, intensity, musicSeconds);
                    var rng = new SynthRng(3);
                    var clip = new SfxClip { Pcm = Finish.Pack16(mix, ref rng), SampleRate = profile.SampleRate, Seconds = musicSeconds };
                    string rel = $"music/{profile.Id}/i{(int)(intensity * 100):00}.wav";
                    WavFile.Write(Path.Combine(outDir, rel), clip.Pcm, clip.SampleRate);
                    Line(index, "music", profile.Medium.Id, profile.Id + "@" + intensity.ToString("0.00", CultureInfo.InvariantCulture), 1, rel, clip);
                    files++;
                }
                GD.Print($"AudioDump: music {profile.Id,-7} {profile.Bpm,3:0} BPM {profile.Metre,-26} {set.Stems.Length} stems, {profile.TotalBars} bars = " +
                         $"{profile.TotalBars * profile.SecondsPerBar,4:0.0} s, {set.Bytes / 1024} KB, repeats after {profile.RepeatBars} bars " +
                         $"({profile.RepeatBars * profile.SecondsPerBar / 60.0:0.0} min) · bounced in {took * 1000:0} ms");
            }
        }

        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "index.tsv"), index.ToString());
        GD.Print($"AudioDump: OK — {files} files under {outDir} in {clock.Elapsed.TotalSeconds:0.0} s");
    }

    private static void Line(StringBuilder index, string kind, string medium, string id, int variant, string rel, SfxClip clip)
    {
        SoundStats s = Analysis.Measure(clip.Pcm, clip.SampleRate);
        index.Append(string.Join("\t", kind, medium, id, variant.ToString(), rel,
            s.Seconds.ToString("0.000", CultureInfo.InvariantCulture),
            Dsp.LinToDb(s.Peak).ToString("0.0", CultureInfo.InvariantCulture),
            Dsp.LinToDb(s.Rms).ToString("0.0", CultureInfo.InvariantCulture),
            s.Dc.ToString("0.0000", CultureInfo.InvariantCulture),
            s.Centroid.ToString("0", CultureInfo.InvariantCulture),
            s.Below120.ToString("0.000", CultureInfo.InvariantCulture),
            s.Above5k.ToString("0.000", CultureInfo.InvariantCulture),
            s.Above8k.ToString("0.000", CultureInfo.InvariantCulture),
            (s.AttackSeconds * 1000f).ToString("0.0", CultureInfo.InvariantCulture),
            clip.Loop ? "1" : "0")).Append('\n');
    }
}
