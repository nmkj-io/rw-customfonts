using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;
using UnityEngine;
using HarmonyLib;
using RimWorld.Planet;
using TMPro;
using UnityEngine.TextCore.LowLevel;

namespace CustomFonts
{
    public class FontSettings : ModSettings
    {
        // Setting values
        public const string DefaultFontName = "Default";
        public static string CurrentFontName = "";
        public static float ScaleFactor = 1.0f;
        public static int VerticalOffset = 0;

        public override void ExposeData() // Writing settings to the mod file
        {
            Scribe_Values.Look(ref CurrentFontName, "CurrentFontName");
            Scribe_Values.Look(ref ScaleFactor, "ScaleFactor");
            Scribe_Values.Look(ref VerticalOffset, "VerticalOffset");
            base.ExposeData();
        }
    }

    [StaticConstructorOnStartup]
    static class StartupFontPatcher
    {
        static StartupFontPatcher()
        {
            CustomFonts.UpdateFont();
        }
    }


    public class CustomFonts : Mod
    {
        private readonly FontSettings _settings;
        private List<string> _fontNames = new List<string>();
        private bool _hasInstalledFontNames;
        private static bool _hasBundledFonts;
        private static bool _hasOSFontAssets;
        private static readonly Dictionary<string, Font> BundledFonts = new Dictionary<string, Font>();
        private static readonly Dictionary<GameFont, Font> DefaultFonts = new Dictionary<GameFont, Font>();
        public static readonly Dictionary<GameFont, Font> CurrentFonts = new Dictionary<GameFont, Font>();
        public static readonly Dictionary<GameFont, GUIStyle> DefaultFontStyle = new Dictionary<GameFont, GUIStyle>();
        public static readonly List<TMP_FontAsset> OSFontAssets = new List<TMP_FontAsset>();
        public static TMP_FontAsset DefaultTMPFontAsset;
        private Vector2 _scrollPosition = Vector2.zero;
        public static Harmony MyHarmony { get; private set; }
        private static ModContentPack _content;

        public CustomFonts(ModContentPack content) :
            base(content) // A mandatory constructor which resolves the reference to the mod settings.
        {
            _settings = GetSettings<FontSettings>();
            
            foreach (GameFont value in Enum.GetValues(typeof(GameFont)))
            {
                DefaultFonts[value] = Text.fontStyles[(int)value].font;
                DefaultFontStyle[value] = Text.fontStyles[(int)value];
            }
            var gameFontEnumLength = Enum.GetValues(typeof(GameFont)).Length;

            _content = content;

            MyHarmony = new Harmony("nmkj.customfonts");
            MyHarmony.PatchAll();
        }

        public override void DoSettingsWindowContents(Rect inRect) // The GUI part to edit the mod settings.
        {
            SetupOSInstalledFontNames();
            SetupOSFontAssets();
            SetupBundledFonts();
            var isDefaultFont = FontSettings.CurrentFontName == FontSettings.DefaultFontName;

            var listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            if (GUILayout.Button("Reset to default", GUILayout.Width(200)))
            {
                FontSettings.ScaleFactor = 1.0f;
                FontSettings.VerticalOffset = 0;
                SaveFont(FontSettings.DefaultFontName, true);
            }
            listingStandard.Gap(30f);
            listingStandard.Label($"Current font: {FontSettings.CurrentFontName}");
            string[] specimen =
            {
                "RimWorld is a sci-fi colony sim driven by an intelligent AI storyteller.",
                "RimWorldは、知性のあるAIストーリーテラーによって織りなされるSFコロニーシミュレーションゲームです。"
            };
            listingStandard.Label(String.Join("\n", specimen));

            var heightAdjustValue = Math.Max(30f * (FontSettings.ScaleFactor - 1.0f) * 5, 0);
            var section = listingStandard.BeginSection(inRect.height - 240f - heightAdjustValue);
            var fontScrollRect = new Rect(inRect.x + 10f, inRect.y - 30f, section.ColumnWidth - 20f, inRect.height - 260f - heightAdjustValue);
            var fontListRect = new Rect(fontScrollRect.x, fontScrollRect.y, fontScrollRect.width - 30f,
                23.6f * _fontNames.Count);
            Widgets.BeginScrollView(fontScrollRect, ref _scrollPosition, fontListRect);
            section.Begin(fontListRect);
            if (section.RadioButton(FontSettings.DefaultFontName, isDefaultFont))
                SaveFont(FontSettings.DefaultFontName);
            section.GapLine();
            foreach (var name in BundledFonts.Keys)
            {
                if (section.RadioButton(name, FontSettings.CurrentFontName == name))
                {
                    SaveFont(name);
                }
            }
            section.GapLine();
            foreach (var name in _fontNames)
            {
                if (section.RadioButton(name, FontSettings.CurrentFontName == name))
                {
                    SaveFont(name);
                }
            }
            section.End();
            Widgets.EndScrollView();
            listingStandard.EndSection(section);

            listingStandard.Gap();

            var sliders = listingStandard.BeginSection(100f + (heightAdjustValue * 0.4f));
            var slidersRect = sliders.GetRect(100f + (heightAdjustValue * 0.4f));
            sliders.Begin(slidersRect);
            sliders.Label($"Vertical Position Offset: {FontSettings.VerticalOffset:D}");
            var offsetValue = sliders.Slider(FontSettings.VerticalOffset, -30f, 10f);
            // var offsetValue = listingStandard.SliderLabeled($"Vertical Position Offset: {FontSettings.VerticalOffset:D}", FontSettings.VerticalOffset, -30.0f, 10.0f);
            var newOffset = (int)Math.Round(offsetValue);
            if (newOffset != FontSettings.VerticalOffset)
            {
                FontSettings.VerticalOffset = newOffset;
                RecalcCustomLineHeights();
            }
            sliders.Label($"Font Scaling Factor: {FontSettings.ScaleFactor:F1}", tooltip: "[CAUTION]\nValue other than 1.0 can break the entire UI!");
            var scaleValue = sliders.Slider(FontSettings.ScaleFactor, 0.5f, 2.0f);
            // var scaleValue = listingStandard.SliderLabeled($"Scaling Factor: {FontSettings.ScaleFactor:F1}", FontSettings.ScaleFactor, 0.1f, 2.0f, tooltip: "[CAUTION]\nValue other than 1.0 can break the entire UI!");
            var fontSizeScale = (float)Math.Round(scaleValue, 1);
            if (Math.Abs(FontSettings.ScaleFactor - fontSizeScale) > 0.01)
            {
                FontSettings.ScaleFactor = fontSizeScale;
                UpdateFont();
                UpdateWorldFont(FontSettings.CurrentFontName);
            }
            
            sliders.End();
            listingStandard.EndSection(sliders);
            listingStandard.End();
            
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory() => "Custom Fonts";

        private void SetupOSInstalledFontNames()
        {
            if (_hasInstalledFontNames) return;
            _hasInstalledFontNames = true;
            _fontNames = Font.GetOSInstalledFontNames().ToList();
        }

        private static void SetupOSFontAssets()
        {
            if (_hasOSFontAssets) return;
            _hasOSFontAssets = true;
            foreach (var path in Font.GetPathsToOSFonts())
            {
                var asset = TMP_FontAsset.CreateFontAsset(new Font(path));
                if (asset.fallbackFontAssetTable == null)
                    asset.fallbackFontAssetTable = new List<TMP_FontAsset>();
                OSFontAssets.Add(asset);
            }
        }

        private static void SetupBundledFonts()
        {
            if (_hasBundledFonts) return;
            _hasBundledFonts = true;
            var fontAssetPath = Path.Combine(_content.RootDir, "rimfonts");
            var cab = AssetBundle.LoadFromFile(fontAssetPath);
            if (cab == null)
            {
                Log.Message("[Custom Fonts] Unable to load bundled fonts.");
                return;
            }
#if DEBUG
            Log.Message($"[Custom Fonts] Loading font asset at {fontAssetPath}:\n{string.Join("\n", cab.GetAllAssetNames())}");
#endif

            foreach (var font in cab.LoadAllAssets<Font>())
            {
                BundledFonts.Add($"(Bundled) {font.fontNames[0]}", font);
            }
        }

        private static void SaveFont(string fontName, bool forceUpdate = false)
        {
            if (fontName == FontSettings.CurrentFontName && !forceUpdate) return;
            FontSettings.CurrentFontName = fontName;
            UpdateFont();
            UpdateWorldFont(fontName);
        }

        public static void UpdateFont()
        {
            SetupBundledFonts();
            foreach (GameFont value in Enum.GetValues(typeof(GameFont)))
            {
                UpdateFont(value);
            }
        }

        private static void UpdateFont(GameFont fontIndex)
        {
            // if (FontSettings.CurrentFontName == FontSettings.PreviousFontName && !forceUpdate) return;

            var isBundled = BundledFonts.ContainsKey(FontSettings.CurrentFontName);
            Font font;

            var fontSize = (int)Math.Round(DefaultFonts[fontIndex].fontSize * FontSettings.ScaleFactor);

            if (isBundled)
            {
                font = BundledFonts[FontSettings.CurrentFontName];
            }
            else
            {
                font = FontSettings.CurrentFontName != FontSettings.DefaultFontName
                    ? Font.CreateDynamicFontFromOSFont(FontSettings.CurrentFontName, fontSize)
                    : DefaultFonts[fontIndex];
            }

#if DEBUG
            Log.Message($"[Custom Fonts] Updating font to {string.Join(", ", font.fontNames)}");
#endif
            
            CurrentFonts[fontIndex] = font;
            Text.fontStyles[(int)fontIndex].font = font;
            Text.fontStyles[(int)fontIndex].fontSize = fontSize;
            Text.textFieldStyles[(int)fontIndex].font = font;
            Text.textFieldStyles[(int)fontIndex].fontSize = fontSize;
            Text.textAreaStyles[(int)fontIndex].font = font;
            Text.textAreaStyles[(int)fontIndex].fontSize = fontSize;
            Text.textAreaReadOnlyStyles[(int)fontIndex].font = font;
            Text.textAreaReadOnlyStyles[(int)fontIndex].fontSize = fontSize;
            RecalcCustomLineHeights(fontIndex);
        }

        public static void UpdateWorldFont(string fontName, bool forceUpdate = false)
        {
            AccessTools.StaticFieldRefAccess<float>(typeof(WorldFeatureTextMesh_TextMeshPro), "TextScale") = FontSettings.ScaleFactor;
            if (fontName == FontSettings.CurrentFontName && !forceUpdate) return;
            if (fontName == FontSettings.DefaultFontName)
            {
                WorldFeatureTextMesh_TextMeshPro.WorldTextPrefab.GetComponent<TextMeshPro>().font = DefaultTMPFontAsset;
                return;
            }
            if (BundledFonts.ContainsKey(fontName))
            {
                var bundleTMP = TMP_FontAsset.CreateFontAsset(BundledFonts[fontName]);
                WorldFeatureTextMesh_TextMeshPro.WorldTextPrefab.GetComponent<TextMeshPro>().font = bundleTMP;
                return;
            }
            
            SetupOSFontAssets();

            var candidates = OSFontAssets.Where(asset => fontName.Contains(asset.faceInfo.familyName));
            TMP_FontAsset fontAsset = null;
            foreach (var candidate in candidates)
            {
                if (fontName.Contains(candidate.faceInfo.styleName))
                {
                    fontAsset = candidate;
                    break;
                }

                if (fontName == candidate.faceInfo.familyName)
                {
                    fontAsset = candidate;
                    break;
                }
            }
            WorldFeatureTextMesh_TextMeshPro.WorldTextPrefab.GetComponent<TextMeshPro>().font = fontAsset ?? DefaultTMPFontAsset;
        }

        public static void RecalcCustomLineHeights()
        {
            foreach (GameFont value in Enum.GetValues(typeof(GameFont)))
            {
                RecalcCustomLineHeights(value);
            }
        }

        public static void RecalcCustomLineHeights(GameFont fontType)
        {
            Text.fontStyles[(int)fontType].clipping = TextClipping.Overflow;
            Text.fontStyles[(int)fontType].contentOffset = new Vector2(0f, FontSettings.VerticalOffset);
        }
    }

    internal static class HarmoneyPatchers
    {
        private static bool _patcherInitialized;

        [HarmonyPatch(typeof(Text), nameof(Text.StartOfOnGUI))]
        class StartOfOnGUIPatcher
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (_patcherInitialized) return;

                _patcherInitialized = true;
                CustomFonts.DefaultTMPFontAsset = WorldFeatureTextMesh_TextMeshPro.WorldTextPrefab.GetComponent<TextMeshPro>().font;
                CustomFonts.UpdateFont();
                CustomFonts.UpdateWorldFont(FontSettings.CurrentFontName, true);
                // AccessTools.StaticFieldRefAccess<float>(typeof(WorldFeatureTextMesh_TextMeshPro), "TextScale") = FontSettings.ScaleFactor;
#if DEBUG
                Log.Message("[Custom Fonts] Font patcher initialised");
#endif
            }
        }

        [HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))]
        class GoToMainMenuPatcher
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                CustomFonts.UpdateFont();
                CustomFonts.UpdateWorldFont(FontSettings.CurrentFontName, true);
            }
        }
        
        [HarmonyPatch(typeof(WorldFeatures), "HasCharacter")]
        private class WorldMapHasCharacterPatcher
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (FontSettings.CurrentFontName == FontSettings.DefaultFontName)
                    return true;
                __result = true;
                return false;
            }
        }
        
        [HarmonyPatch(typeof(WorldFeatureTextMesh_TextMeshPro), "Init")]
        class WorldMapInitPatcher
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                CustomFonts.UpdateWorldFont(FontSettings.CurrentFontName, true);
            }
        }

    }
}