using System;
using RavenIron.Tankard.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RavenIron.Tankard
{
    /// <summary>
    /// After a blackout the big map opens under a fog, and only LOOKING at it clears the fog: you start remembering.
    /// About 5 minutes of looking when sober, slower while still drunk (MapRememberSeconds), across as many glances as you like, brings it all back, and then the
    /// minimap returns too. Walking back to the drinking spot still restores everything at once. Progress is saved in
    /// Player.m_customData per world, so a relog keeps what you have remembered.
    ///
    /// The fog is one plain UI Image laid over Minimap.m_largeRoot (public; decompiled), drawn last so it covers the
    /// map, the pins and your own marker. No texture or material is touched. Purely cosmetic, so every frame of it is in
    /// its own try/catch (house rule 3); if it fails the map simply stays shut, as it did before this feature.
    /// </summary>
    internal static class MapMemory
    {
        private static float _progress;
        private static bool _loaded;
        private static float _saveTimer;
        private static bool _wasLooking;
        private static bool _stackedThisOpen;
        private static GameObject _fog;
        private static Image _fogImage;

        private static readonly Color FogColour = new Color(0.07f, 0.055f, 0.045f, 1f); // dark, like a page you can't read

        public static float Progress => _progress;

        /// <summary>A fresh blackout: nothing remembered.</summary>
        public static void Start(Player p)
        {
            _progress = 0f;
            _loaded = true;
            Save(p);
        }

        /// <summary>The blackout is over (home, or fully remembered): forget the progress and lift the fog.</summary>
        public static void Clear(Player p)
        {
            _progress = 0f;
            _loaded = false;
            if (p != null) p.m_customData.Remove(Blackout.MemoryKeyFor(BlackoutRunner.WorldId()));
            DestroyFog();
        }

        public static void Tick(float dt)
        {
            try
            {
                Player p = Player.m_localPlayer;
                if (p == null || !BlackoutRunner.MapHidden) { _loaded = false; DestroyFog(); return; }

                if (!_loaded)
                {
                    p.m_customData.TryGetValue(Blackout.MemoryKeyFor(BlackoutRunner.WorldId()), out string saved);
                    _progress = Blackout.ParseProgress(saved);
                    _loaded = true;
                }

                bool looking = Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large;
                if (looking != _wasLooking)
                {
                    // One line per open and close: the first fog run produced no line at all, so whether the fog was even
                    // drawn could not be told from the log. The fog's on-screen size is in it: 0 x 0 would mean it is
                    // parented to something with no area and is invisible.
                    _wasLooking = looking;
                    var rt = _fog != null ? (RectTransform)_fog.transform : null;
                    Plugin.Log.LogInfo(looking
                        ? $"Map opened during a blackout: {_progress:P0} remembered, fog opacity {Blackout.FogAlpha(_progress):0.00}, " +
                          $"fog rect {(rt != null ? rt.rect.width.ToString("0") + " x " + rt.rect.height.ToString("0") : "not built yet")}."
                        : $"Map closed: {_progress:P0} remembered.");
                }
                if (looking)
                {
                    float before = _progress;
                    _progress = Blackout.Remember(_progress, dt, Plugin.MapRememberSeconds.Value, Meter.Level);
                    int m = Blackout.MilestoneCrossed(before, _progress);
                    if (m >= 0)
                    {
                        Plugin.Log.LogInfo($"Remembering the map: {_progress:P0}.");
                        p.Message(MessageHud.MessageType.TopLeft, Blackout.MemoryLines[m]);
                    }
                    _saveTimer -= dt;
                    if (_saveTimer <= 0f) { _saveTimer = 2f; Save(p); }

                    if (_progress >= 1f)
                    {
                        Plugin.Log.LogInfo("Remembered the whole map: blackout over.");
                        p.Message(MessageHud.MessageType.Center, "It all comes back to you.");
                        BlackoutRunner.EndBlackout(p, restoreMinimap: false); // you are looking at the big map right now
                        return;
                    }
                }

                UpdateFog(looking);
            }
            catch (Exception ex)
            {
                Plugin.Report("map memory", ex);
            }
        }

        private static void Save(Player p)
        {
            if (p == null) return;
            p.m_customData[Blackout.MemoryKeyFor(BlackoutRunner.WorldId())] = Blackout.FormatProgress(_progress);
        }

        private static void UpdateFog(bool looking)
        {
            Minimap mm = Minimap.instance;
            if (mm == null || mm.m_largeRoot == null) return;
            if (_fog == null)
            {
                _fog = new GameObject("Tankard_MapFog", typeof(RectTransform));
                _fog.transform.SetParent(mm.m_largeRoot.transform, worldPositionStays: false);
                var rt = (RectTransform)_fog.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                _fogImage = _fog.AddComponent<Image>();
                _fogImage.raycastTarget = false; // the map still takes your clicks; you just can't see what you click
                _fog.transform.SetAsLastSibling();
            }
            // Re-stacked only when the map opens, not every frame: moving a sibling dirties the canvas, and doing that
            // per frame forces a UI rebuild of the whole map panel for as long as it is open.
            if (looking && !_stackedThisOpen) { _fog.transform.SetAsLastSibling(); _stackedThisOpen = true; }
            if (!looking) _stackedThisOpen = false;
            Color c = FogColour;
            c.a = Blackout.FogAlpha(_progress);
            _fogImage.color = c;
        }

        private static void DestroyFog()
        {
            if (_fog != null) UnityEngine.Object.Destroy(_fog);
            _fog = null;
            _fogImage = null;
        }
    }
}
