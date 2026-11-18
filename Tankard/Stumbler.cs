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

        public static void Tick(float dt)
        {
            _sinceLast += dt;
            if (!Plugin.Stumbles.Value) return;

            Player p = Player.m_localPlayer;
            if (p == null) return;
            float severity = Meter.SymptomSeverity;
            if (severity <= 0f) return;
            if (!Symptoms.ShouldStumble(severity, Meter.Tier, dt, Rng.NextDouble(), _sinceLast)) return;
            if (p.IsDead() || p.IsSwimming() || p.IsAttached() || p.InCutscene() || p.IsStaggering() || !p.IsOnGround()) return;

            try
            {
                // Pushed roughly backwards and to one side, so you lurch and end up facing a little off where you were.
                float yaw = (float)(Rng.NextDouble() * 120.0 - 60.0);
                Vector3 push = Quaternion.Euler(0f, yaw, 0f) * -p.transform.forward;
                p.Stagger(push);
                _sinceLast = 0f;
                Plugin.Log.LogInfo($"Stumbled at {Meter.Tier} (meter {Meter.Level:0.0}).");
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
