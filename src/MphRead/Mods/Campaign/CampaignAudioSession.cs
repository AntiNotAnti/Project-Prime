using System;
using MphRead.Mods.Launcher;

namespace MphRead.Mods.Campaign
{
    public enum CampaignAudioPage
    {
        Dock,
        StarMap,
        Ending
    }

    /// <summary>
    /// Original cartridge sequence mapping. The ship remains in the native
    /// music backend; no bundled soundtrack or second playback device.
    /// </summary>
    public static class CampaignAudioCatalog
    {
        public static SeqId Sequence(CampaignAudioPage page, CampaignPlanet? planet = null)
        {
            return page switch
            {
                CampaignAudioPage.StarMap => SeqId.SHIP,
                CampaignAudioPage.Ending => SeqId.CREDITS,
                CampaignAudioPage.Dock when planet.HasValue => planet.Value switch
                {
                    CampaignPlanet.Alinos => SeqId.SHIP_LAND1,
                    CampaignPlanet.CelestialArchives => SeqId.SHIP_LAND2,
                    CampaignPlanet.VesperDefenseOutpost => SeqId.SHIP_LAND3,
                    CampaignPlanet.Arcterra or CampaignPlanet.Oubliette => SeqId.SHIP_LAND4,
                    _ => SeqId.None
                },
                _ => SeqId.None
            };
        }

        internal static Sound.UiFeedbackCue Cue(CampaignAudioPage page) => page switch
        {
            CampaignAudioPage.Dock => Sound.UiFeedbackCue.CampaignDock,
            CampaignAudioPage.StarMap => Sound.UiFeedbackCue.CampaignStarMap,
            CampaignAudioPage.Ending => Sound.UiFeedbackCue.CampaignEnding,
            _ => Sound.UiFeedbackCue.Error
        };
    }

    /// <summary>
    /// Native music contract isolated behind a small value/ownership boundary
    /// so it can be verified without sound devices or private ROM files.
    /// </summary>
    internal interface ICampaignAudioSink
    {
        long Start(SeqId sequence);
        bool Owns(SeqId sequence, long epoch);
        void Tick();
        void Stop(SeqId sequence, long epoch);
        void Cue(Sound.UiFeedbackCue cue);
    }

    /// <summary>
    /// A menu lifetime, never a Scene lifetime. The epoch fences music across
    /// another game's Music.Init/PlaySeq even when the same sequence is chosen.
    /// </summary>
    internal sealed class CampaignAudioSession : IDisposable
    {
        private readonly ICampaignAudioSink _sink;
        private SeqId _sequence = SeqId.None;
        private long _epoch;
        private bool _disposed;
        private CampaignAudioPage? _page;

        public CampaignAudioSession(ICampaignAudioSink? sink = null)
        {
            _sink = sink ?? new NativeSink();
        }

        internal CampaignAudioPage? Page => _page;
        internal SeqId Sequence => _sequence;

        public void Enter(CampaignAudioPage page, CampaignPlanet? planet = null)
        {
            if (_disposed) return;
            SeqId requested = CampaignAudioCatalog.Sequence(page, planet);
            if (requested == SeqId.None) return;
            if (_page == page && _sequence == requested && _sink.Owns(_sequence, _epoch))
                return;

            ReleaseTrack();
            _page = page;
            _epoch = _sink.Start(requested);
            _sequence = _epoch != 0 ? requested : SeqId.None;
            _sink.Cue(CampaignAudioCatalog.Cue(page));
        }

        public void LandingCue()
        {
            if (!_disposed) _sink.Cue(Sound.UiFeedbackCue.CampaignLand);
        }

        public void Tick()
        {
            if (_disposed || _sequence == SeqId.None) return;
            if (!_sink.Owns(_sequence, _epoch))
            {
                _sequence = SeqId.None;
                _epoch = 0;
                return;
            }
            try { _sink.Tick(); }
            catch (Exception ex)
            {
                Console.WriteLine("[campaign] menu music update failed: " + ex.Message);
                ReleaseTrack();
            }
        }

        private void ReleaseTrack()
        {
            if (_sequence != SeqId.None && _sink.Owns(_sequence, _epoch))
            {
                try { _sink.Stop(_sequence, _epoch); }
                catch (Exception ex)
                {
                    Console.WriteLine("[campaign] menu music stop failed: " + ex.Message);
                }
            }
            _sequence = SeqId.None;
            _epoch = 0;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseTrack();
            _page = null;
        }

        private sealed class NativeSink : ICampaignAudioSink
        {
            public long Start(SeqId sequence)
            {
                if (Mods.Headless.Active || Mods.ThumbnailMode.Active
                    || !GameFiles.Ready || !Music.Initialized || !MusicPlayer.Available)
                    return 0;

                // MusicPlayer uses the *existing* SoundFlow output. It loads
                // asynchronously; the UI's animation heartbeat pumps it.
                long prior = Music.PlaybackEpoch;
                try
                {
                    Music.PlaySeq(sequence, notReady: true);
                    return Music.PlaybackEpoch;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[campaign] menu music unavailable: " + ex.Message);
                    if (Music.PlaybackEpoch != prior && Music.ActiveSequence == sequence)
                    {
                        try { Music.Stop(); } catch { }
                    }
                    return 0;
                }
            }

            public bool Owns(SeqId sequence, long epoch) => epoch != 0
                && Music.PlaybackEpoch == epoch && Music.ActiveSequence == sequence;

            public void Tick() => Music.UpdateMusic();

            public void Stop(SeqId sequence, long epoch)
            {
                if (Owns(sequence, epoch)) Music.Stop();
            }

            public void Cue(Sound.UiFeedbackCue cue) => Sound.UiFeedbackAudio.Play(cue);
        }
    }
}
