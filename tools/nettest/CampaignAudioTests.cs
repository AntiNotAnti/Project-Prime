using System;
using System.Collections.Generic;
using System.Text;
using MphRead.Mods.Campaign;
using MphRead.Mods.Sound;

namespace MphRead.NetTest;

internal static class CampaignAudioTests
{
    private sealed class FakeAudioSink : ICampaignAudioSink
    {
        internal readonly List<SeqId> Started = new();
        internal readonly List<SeqId> Stopped = new();
        internal readonly List<UiFeedbackCue> Cues = new();
        internal SeqId Active = SeqId.None;
        internal long Epoch;
        internal int Ticks;
        internal bool Enabled = true;

        public long Start(SeqId sequence)
        {
            if (!Enabled) return 0;
            Started.Add(sequence);
            Active = sequence;
            return ++Epoch;
        }

        public bool Owns(SeqId sequence, long epoch)
            => Active == sequence && epoch != 0 && Epoch == epoch;

        public void Tick() => Ticks++;

        public void Stop(SeqId sequence, long epoch)
        {
            if (!Owns(sequence, epoch)) throw new InvalidOperationException("Stale audio owner attempted to stop music.");
            Stopped.Add(sequence);
            Active = SeqId.None;
            Epoch++;
        }

        public void Cue(UiFeedbackCue cue) => Cues.Add(cue);

        internal void Preempt(SeqId sequence)
        {
            // Simulate another newly launched scene choosing the exact same
            // sequence: matching the ID alone must not authorize a Stop.
            Active = sequence;
            Epoch++;
        }
    }

    internal static int Run()
    {
        int checks = 0;
        void Check(bool predicate, string message)
        {
            if (!predicate) throw new InvalidOperationException("CAMP-06 FAIL " + message);
            checks++;
            Console.WriteLine("CAMP-06 PASS " + message);
        }

        try
        {
            var expectedDockTracks = new[]
            {
                SeqId.SHIP_LAND1, SeqId.SHIP_LAND2, SeqId.SHIP_LAND3,
                SeqId.SHIP_LAND4, SeqId.SHIP_LAND4
            };
            for (int i = 0; i < expectedDockTracks.Length; i++)
                Check(CampaignAudioCatalog.Sequence(CampaignAudioPage.Dock, (CampaignPlanet)i)
                    == expectedDockTracks[i], "original cockpit music for " + (CampaignPlanet)i);
            Check(CampaignAudioCatalog.Sequence(CampaignAudioPage.StarMap) == SeqId.SHIP
                && CampaignAudioCatalog.Sequence(CampaignAudioPage.Ending) == SeqId.CREDITS,
                "star map and Gorea credits follow original cartridge sequence IDs");
            Check(CampaignAudioCatalog.Sequence(CampaignAudioPage.Dock) == SeqId.None
                && CampaignAudioCatalog.Sequence(CampaignAudioPage.Dock, (CampaignPlanet)99) == SeqId.None,
                "unknown or absent dock cannot start an arbitrary music sequence");

            var fake = new FakeAudioSink();
            using (var session = new CampaignAudioSession(fake))
            {
                session.Enter(CampaignAudioPage.Dock, CampaignPlanet.Alinos);
                Check(fake.Started.Count == 1 && fake.Active == SeqId.SHIP_LAND1,
                    "boarding starts exactly one original cockpit track");
                Check(fake.Cues.Count == 1 && fake.Cues[0] == UiFeedbackCue.CampaignDock,
                    "boarding plays the authored dock cue");
                session.Tick(); session.Tick();
                Check(fake.Ticks == 2, "UI music heartbeat drives a live owned decoder");
                session.Enter(CampaignAudioPage.Dock, CampaignPlanet.Alinos);
                Check(fake.Started.Count == 1 && fake.Cues.Count == 1,
                    "reopening the same state does not restart its music or replay its cue");

                session.Enter(CampaignAudioPage.StarMap);
                Check(fake.Stopped.Count == 1 && fake.Stopped[0] == SeqId.SHIP_LAND1
                    && fake.Started[^1] == SeqId.SHIP,
                    "launching ship switches cockpit music to star-map music in order");
                Check(fake.Cues[^1] == UiFeedbackCue.CampaignStarMap,
                    "star-map transition has a distinct low-latency cue");
                session.LandingCue();
                Check(fake.Cues[^1] == UiFeedbackCue.CampaignLand,
                    "confirmed landing has an action-specific cue");
                session.Enter(CampaignAudioPage.Dock, CampaignPlanet.Alinos);
                Check(fake.Stopped[^1] == SeqId.SHIP && fake.Active == SeqId.SHIP_LAND1,
                    "returning to cockpit restores planet-specific music");
            }
            Check(fake.Active == SeqId.None && fake.Stopped.Count == 3,
                "closing ship stops the last owned sequence exactly once");

            var preempt = new FakeAudioSink();
            var obsolete = new CampaignAudioSession(preempt);
            obsolete.Enter(CampaignAudioPage.StarMap);
            preempt.Preempt(SeqId.SHIP);
            obsolete.Tick();
            obsolete.Dispose();
            obsolete.Dispose();
            Check(preempt.Ticks == 0 && preempt.Active == SeqId.SHIP
                && preempt.Stopped.Count == 0,
                "old UI cannot tick or stop a newer scene, even with identical music ID");

            var unavailable = new FakeAudioSink { Enabled = false };
            using (var silent = new CampaignAudioSession(unavailable))
            {
                silent.Enter(CampaignAudioPage.Dock, CampaignPlanet.Arcterra);
                silent.Tick();
                silent.Enter(CampaignAudioPage.Ending);
                Check(unavailable.Started.Count == 0 && unavailable.Stopped.Count == 0
                    && unavailable.Ticks == 0, "no audio device safely falls back to silence");
                Check(unavailable.Cues.Count == 2
                    && unavailable.Cues[^1] == UiFeedbackCue.CampaignEnding,
                    "audio-disabled flow still exposes the ending UI intent");
            }

            var ending = new FakeAudioSink();
            using (var credits = new CampaignAudioSession(ending))
            {
                credits.Enter(CampaignAudioPage.Ending);
                Check(ending.Active == SeqId.CREDITS
                    && ending.Cues[0] == UiFeedbackCue.CampaignEnding,
                    "ending results use cartridge credits soundtrack and completion cue");
            }
            Check(ending.Stopped.Count == 1 && ending.Stopped[0] == SeqId.CREDITS,
                "credits close releases its own soundtrack");

            foreach (UiFeedbackCue cue in new[]
            {
                UiFeedbackCue.CampaignDock, UiFeedbackCue.CampaignStarMap,
                UiFeedbackCue.CampaignLand, UiFeedbackCue.CampaignEnding
            })
            {
                byte[] pcm = UiFeedbackAudio.Build(cue);
                Check(pcm.Length > 44
                    && Encoding.ASCII.GetString(pcm, 0, 4) == "RIFF"
                    && Encoding.ASCII.GetString(pcm, 8, 4) == "WAVE"
                    && BitConverter.ToInt32(pcm, 24) == 22050,
                    "asset-free ship cue is valid PCM WAV: " + cue);
            }

            Console.WriteLine($"CAMP-06 PASS total={checks}: ship/credits music ownership and fallback");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("CAMP-06 FAIL " + ex);
            return 1;
        }
    }
}
