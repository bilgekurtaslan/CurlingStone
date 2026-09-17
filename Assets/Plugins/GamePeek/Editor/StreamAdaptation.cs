using System;
using UnityEngine;

namespace GamePeek
{
    /// <summary>
    /// Adaptive stream controller (phase 3): consumes the host phone's STATS
    /// reports (PROTOCOL.md §4.5, 1 Hz) and adjusts three editor-side levers —
    /// chunk pacing, JPEG quality, and the capture fps cap — to keep delivery
    /// clean on congested networks. Pure feedback control, zero wire changes;
    /// a phone that never sends STATS simply gets the static configuration.
    /// <para>
    /// <b>Heuristic.</b> Each report is classified against the editor's true
    /// send rate (<see cref="UdpVideoSender.TotalFramesSent"/> delta — capture
    /// fps overcounts whenever skip-unchanged suppresses static frames):
    /// <list type="bullet">
    ///   <item><b>Abandonment</b> (incomplete reassembly slots) means chunk
    ///         loss — bursty sends overrunning Wi-Fi buffers. First response is
    ///         pacing: spread the burst via
    ///         <see cref="UdpVideoSender.PaceDelayMicros"/> before touching
    ///         image quality at all.</item>
    ///   <item><b>Low delivery ratio</b> (frames rendered vs frames sent)
    ///         despite pacing means the pipe or the decoder can't keep up —
    ///         step quality down (smaller frames), then the fps cap.</item>
    ///   <item>Recovery is deliberately slow: a long streak of clean reports
    ///         restores fps first, then quality, one step at a time, never
    ///         beyond what the phone's CONFIG requested.</item>
    /// </list>
    /// All decisions and levers live on the main thread (fed from
    /// <see cref="ConnectionManager"/>'s update-queue drain); every step is
    /// logged at the info level so degraded sessions are diagnosable.
    /// </para>
    /// </summary>
    public sealed class StreamAdaptation
    {
        // ── Tuning ────────────────────────────────────────────────────────────

        private const int   DegradedStreakToAct = 3;    // seconds of bad reports before stepping down
        private const int   CleanStreakToAct    = 10;   // seconds of clean reports before stepping up
        private const float AbandonRatioBad     = 0.10f; // >10 % abandoned slots = degraded
        private const float AbandonRatioPace    = 0.05f; // >5 % nudges pacing up
        private const float DeliveryRatioBad    = 0.75f; // <75 % of sent frames rendered = degraded
        private const float MinSentFpsToJudge   = 5f;    // below this the ratios are noise

        private const int QualityStepDown = 10, QualityStepUp = 5, QualityFloor = 40;
        private const int FpsStepDown     = 5,  FpsStepUp     = 5, FpsFloor     = 15;
        private const int PaceStepUp      = 25, PaceStepDown  = 10, PaceCeiling = 200; // µs between chunks

        // ── Levers ────────────────────────────────────────────────────────────

        private readonly FrameEncoder   _encoder;
        private readonly FrameCapture   _capture;
        private readonly UdpVideoSender _sender;

        // ── State ─────────────────────────────────────────────────────────────

        private int _baseQuality;   // phone-requested ceiling (CONFIG)
        private int _baseFps;
        private int _quality;       // effective values currently applied
        private int _fps;

        private int    _degradedStreak;
        private int    _cleanStreak;
        private long   _lastSentTotal;
        private double _lastStatsTime;

        /// <summary>Quality currently applied to the encoder (≤ the CONFIG value).</summary>
        public int EffectiveQuality => _quality;

        /// <summary>Fps cap currently applied to the capture (≤ the CONFIG value).</summary>
        public int EffectiveFps => _fps;

        public StreamAdaptation(FrameEncoder encoder, FrameCapture capture, UdpVideoSender sender)
        {
            _encoder = encoder;
            _capture = capture;
            _sender  = sender;
        }

        /// <summary>
        /// Called whenever a CONFIG is applied: the requested quality/fps are
        /// the new ceilings and adaptation restarts from them (the user just
        /// expressed intent — honor it until the network says otherwise).
        /// </summary>
        public void OnConfigApplied(int quality, int fpsCap)
        {
            _baseQuality = quality;
            _baseFps     = fpsCap;
            _quality     = quality;
            _fps         = fpsCap;
            _degradedStreak = 0;
            _cleanStreak    = 0;
            if (_sender != null) _sender.PaceDelayMicros = 0;
        }

        /// <summary>
        /// Consumes one host STATS report (main thread, ≈1 Hz). Reports from
        /// non-host sessions are filtered by the caller.
        /// </summary>
        public void OnStats(StatsMessage stats)
        {
            if (stats == null || _sender == null) return;

            // True send rate over the report interval — the denominator for
            // the delivery ratio. Wall-clock scaled: STATS cadence is the
            // phone's 1 Hz, not exactly ours.
            double now     = UnityEditor.EditorApplication.timeSinceStartup;
            double dt      = _lastStatsTime > 0 ? now - _lastStatsTime : 1.0;
            long   total   = _sender.TotalFramesSent;
            float  sentFps = dt > 0.1 ? (float)((total - _lastSentTotal) / dt) : 0f;
            _lastSentTotal = total;
            _lastStatsTime = now;

            int   attempted     = stats.completeFrames + stats.abandonedFrames;
            float abandonRatio  = attempted > 0 ? (float)stats.abandonedFrames / attempted : 0f;
            float deliveryRatio = sentFps > 0.5f ? stats.deliveredFps / sentFps : 1f;

            // ── Pacing: the cheap lever, driven directly by abandonment ──────
            if (abandonRatio > AbandonRatioPace)
            {
                int pace = Math.Min(_sender.PaceDelayMicros + PaceStepUp, PaceCeiling);
                if (pace != _sender.PaceDelayMicros)
                {
                    _sender.PaceDelayMicros = pace;
                    GamePeekConstants.Log(
                        $"[Adapt] {abandonRatio:P0} abandoned frames — chunk pacing → {pace} µs.");
                }
            }
            else if (_sender.PaceDelayMicros > 0 && abandonRatio == 0f)
            {
                _sender.PaceDelayMicros = Math.Max(0, _sender.PaceDelayMicros - PaceStepDown);
            }

            // ── Classification ────────────────────────────────────────────────
            bool degraded =
                abandonRatio > AbandonRatioBad ||
                (sentFps >= MinSentFpsToJudge && deliveryRatio < DeliveryRatioBad);

            if (degraded)
            {
                _cleanStreak = 0;
                if (++_degradedStreak < DegradedStreakToAct) return;
                _degradedStreak = 0;
                StepDown(abandonRatio, deliveryRatio);
            }
            else
            {
                _degradedStreak = 0;
                if (++_cleanStreak < CleanStreakToAct) return;
                _cleanStreak = 0;
                StepUp();
            }
        }

        // ── Steps ─────────────────────────────────────────────────────────────

        private void StepDown(float abandonRatio, float deliveryRatio)
        {
            if (_quality > QualityFloor)
            {
                _quality = Math.Max(QualityFloor, _quality - QualityStepDown);
                _encoder?.SetQuality(_quality);
                GamePeekConstants.Log(
                    $"[Adapt] Sustained degradation (abandoned {abandonRatio:P0}, delivered {deliveryRatio:P0}) — quality → {_quality}.");
            }
            else if (_fps > FpsFloor)
            {
                _fps = Math.Max(FpsFloor, _fps - FpsStepDown);
                _capture?.SetFpsCap(_fps);
                GamePeekConstants.Log(
                    $"[Adapt] Sustained degradation at quality floor — fps cap → {_fps}.");
            }
            // Both at floor: nothing left to give — pacing keeps adjusting above.
        }

        private void StepUp()
        {
            if (_fps < _baseFps)
            {
                _fps = Math.Min(_baseFps, _fps + FpsStepUp);
                _capture?.SetFpsCap(_fps);
                GamePeekConstants.Log($"[Adapt] Network clean — fps cap restored → {_fps}.");
            }
            else if (_quality < _baseQuality)
            {
                _quality = Math.Min(_baseQuality, _quality + QualityStepUp);
                _encoder?.SetQuality(_quality);
                GamePeekConstants.Log($"[Adapt] Network clean — quality restored → {_quality}.");
            }
        }
    }
}
