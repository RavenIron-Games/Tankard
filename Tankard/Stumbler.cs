using System;
using RavenIron.Tankard.Core;
using UnityEngine;

namespace RavenIron.Tankard
{
    /// <summary>
    /// Once you are hammered, now and then you trip over your own feet: vanilla's stagger, the hit-react a hard blow
    /// gives you. Character.Stagger(Vector3) is public and runs locally on the owner (decompiled: RPC_Stagger turns the
    /// character to face away from the force and triggers the "stagger" animation; it does nothing if already staggering).
    ///
    /// Driven from Plugin.Update (house rule 2: one clock, no coroutine). Never while it would be cruel or broken: dead,
    /// swimming, attached (a ship's wheel, a chair, a bed), in a cutscene, already staggering, or in the air.
    /// </summary>
    internal static class Stumbler
    {
        private static readonly System.Random Rng = new System.Random();
        private static float _sinceLast = 1000f;
        private static float _nextBlockedLog;

        public static void Tick(float dt)
        {
            _sinceLast += dt;
            if (!Plugin.Stumbles.Value) return;

            Player p = Player.m_localPlayer;
            if (p == null) return;
            float severity = Meter.SymptomSeverity;
            if (severity <= 0f) return;
            if (!Symptoms.ShouldStumble(severity, Meter.Tier, dt, Rng.NextDouble(), _sinceLast)) return;
            string blocked = p.IsDead() ? "dead" : p.IsSwimming() ? "swimming" : p.IsAttached() ? "attached (seat, bed or helm)"
                : p.InCutscene() ? "in a cutscene" : p.IsStaggering() ? "already staggering" : !p.IsOnGround() ? "not on the ground"
                : p.IsTeleporting() ? "teleporting" : null;
            if (blocked != null)
            {
                // A stumble was due and a safety rule stopped it. Said at most every 20 s, so a whole binge spent sitting
                // reads as one line with a reason, not as silence (the first in-game run had a binge with no stumbles and
                // no way to tell why).
                if (Time.realtimeSinceStartup >= _nextBlockedLog)
                {
                    _nextBlockedLog = Time.realtimeSinceStartup + 20f;
                    Plugin.Log.LogDebug($"Stumble due at {Meter.Tier} but skipped: {blocked}.");
                }
                return;
            }

            try
            {
                // Pushed roughly backwards and to one side, so you lurch and end up facing a little off where you were.
                float yaw = (float)(Rng.NextDouble() * 120.0 - 60.0);
                Vector3 push = Quaternion.Euler(0f, yaw, 0f) * -p.transform.forward;
                p.Stagger(push);
                _sinceLast = 0f;
                Plugin.Log.LogDebug($"Stumbled at {Meter.Tier} (meter {Meter.Level:0.0}).");
                p.Message(MessageHud.MessageType.TopLeft, Meter.Tier == Tier.Blackout ? "The ground comes up to meet you." : "You stumble.");
            }
            catch (Exception ex)
            {
                Plugin.Report("stumble", ex);
                _sinceLast = 0f; // a failing stagger must not retry every frame
            }
        }
    }
}
