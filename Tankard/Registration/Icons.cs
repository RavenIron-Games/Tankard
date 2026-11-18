using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RavenIron.Tankard.Registration
{
    /// <summary>
    /// Tankard's own item icons: premade PNGs embedded in the DLL as "icon_&lt;PrefabName&gt;.png" (csproj), drawn by
    /// tools\icons\make-icons.js. This is the Blender-Unity kit's recipe (VALHEIM-ASSET-INTEGRATION.md section 3): ship
    /// finished PNGs, never recolour a vanilla icon at runtime, which for them came out BLANK in game.
    ///
    /// UnityEngine.ImageConversion lives in a module built against a newer netstandard than net472 can reference, so
    /// LoadImage is resolved by reflection (the Wubarrk Brain's Fatty write-up hit the same wall). Resolution retries
    /// until it succeeds. An icon that cannot be made is null, and the item keeps the donor mead's icon: a wrong
    /// picture, never a missing item.
    /// </summary>
    internal static class Icons
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> Missing = new HashSet<string>();
        private static MethodInfo _loadImage;
        private static bool _loggedNoLoader;

        private static bool ResolveLoader()
        {
            if (_loadImage != null) return true;
            try
            {
                Type t = AccessTools.TypeByName("UnityEngine.ImageConversion");
                if (t != null) _loadImage = AccessTools.Method(t, "LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
            }
            catch (Exception ex)
            {
                if (!_loggedNoLoader) Plugin.Log.LogError($"ImageConversion.LoadImage did not resolve: {ex.Message}");
            }
            if (_loadImage == null && !_loggedNoLoader)
            {
                _loggedNoLoader = true;
                Plugin.Log.LogError("UnityEngine.ImageConversion.LoadImage(Texture2D, byte[]) is missing, so Tankard's drinks wear the donor's icon.");
            }
            return _loadImage != null;
        }

        /// <summary>The embedded icon for this prefab, or null when there is none (the caller keeps the donor's).</summary>
        public static Sprite For(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            if (Cache.TryGetValue(prefabName, out Sprite cached) && cached != null) return cached;
            if (Missing.Contains(prefabName) || !ResolveLoader()) return null;

            try
            {
                byte[] bytes;
                using (Stream s = typeof(Icons).Assembly.GetManifestResourceStream("icon_" + prefabName + ".png"))
                {
                    if (s == null)
                    {
                        Missing.Add(prefabName);
                        Plugin.Log.LogWarning($"No embedded icon for '{prefabName}'; it keeps the donor's.");
                        return null;
                    }
                    using (var ms = new MemoryStream()) { s.CopyTo(ms); bytes = ms.ToArray(); }
                }

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "tk_icon_" + prefabName };
                if (!(bool)_loadImage.Invoke(null, new object[] { tex, bytes }))
                {
                    Missing.Add(prefabName);
                    Plugin.Log.LogError($"The embedded icon for '{prefabName}' did not decode; it keeps the donor's.");
                    return null;
                }
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.hideFlags |= HideFlags.DontUnloadUnusedAsset; // survives the scene-load clean-up (kit)

                Sprite sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                sp.name = "tk_icon_" + prefabName;
                sp.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                Cache[prefabName] = sp;
                return sp;
            }
            catch (Exception ex)
            {
                Missing.Add(prefabName);
                Plugin.Log.LogError($"Could not make the icon for '{prefabName}': {ex.Message}");
                return null;
            }
        }
    }
}
