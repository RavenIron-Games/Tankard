using System;
using HarmonyLib;
using RavenIron.Tankard.Core;

namespace RavenIron.Tankard.Patches
{
    /// <summary>
    /// Garbles what the local player says while drunk. Plain chat, /s shout and /w whisper all reach the world through
    /// Chat.SendText(Talker.Type, string) (decompiled from 1.0.x: the "say", "s" and "w" console commands each call it),
    /// and a leading slash is a console command that never gets here, so this is the one place the words leave the
    /// player. The sender sees their own garbled line too, because it comes back to them as a chat message.
    ///
    /// A behaviour prefix, so by house rule 1 it runs at Priority.Low, always returns true ("no opinion": the original and
    /// every other mod still run) and only rewrites the text argument. If another mod rewrites the text after us it wins.
    /// </summary>
    [HarmonyPatch(typeof(Chat), nameof(Chat.SendText))]
    internal static class Patch_Chat
    {
        [HarmonyPriority(Priority.Low)]
        [HarmonyPrefix]
        private static bool Prefix(Talker.Type type, ref string text)
        {
            try
            {
                if (!Plugin.Enabled.Value || !Plugin.GarbleChat.Value) return true;
                if (type != Talker.Type.Normal && type != Talker.Type.Shout && type != Talker.Type.Whisper) return true;

                float severity = Intoxication.Severity(Meter.Level);
                if (severity <= 0f) return true; // sober, or only buzzed: the words come out as typed

                string before = text;
                text = DrunkText.Garble(before, severity, Rng, Plugin.GarbleStrength.Value);
                if (!ReferenceEquals(before, text) && before != text)
                    Plugin.Log.LogDebug($"Slurred a {type} line at {Meter.Tier} (severity {severity:0.00}): \"{before}\" -> \"{text}\"");
            }
            catch (Exception ex)
            {
                // Never lose the player's message to a bug in a gag: send what they typed.
                Plugin.Log.LogWarning($"Chat garble failed, sending the text as typed: {ex.Message}");
            }
            return true;
        }

        private static readonly Random Rng = new Random();
    }
}
