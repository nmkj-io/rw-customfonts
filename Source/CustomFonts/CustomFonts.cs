using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;
using UnityEngine;
using HarmonyLib;
using RimWorld.Planet;
using SettingsHelper;
using TMPro;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace CustomFonts
{
    public class FontSettings : ModSettings
    {
        // Setting values
        public const string DefaultFontName = "Default";
        public static string CurrentUIFontName;
        public static string CurrentWorldFontName;
        public static float ScaleFactor = 1.0f;
        public static int VerticalOffset = 0;

        public override void ExposeData() // Writing settings to the mod file
        {
            Scribe_Values.Look(ref CurrentUIFontName, "CurrentUIFontName", DefaultFontName);
            Scribe_Values.Look(ref CurrentWorldFontName, "CurrentWorldFontName", DefaultFontName);
            Scribe_Values.Look(ref ScaleFactor, "ScaleFactor", 1.0f);
            Scribe_Values.Look(ref VerticalOffset, "VerticalOffset", 0);
            base.ExposeData();
        }
    }

    [StaticConstructorOnStartup]
    static class StartupFontPatcher
    {
        static StartupFontPatcher()
        {
        }
    }


    public class CustomFonts : Mod
    {
        private readonly FontSettings _settings;
        private static List<string> _fontNames = new List<string>();
        private static bool _hasInstalledFontNames;
        private static bool _hasBundledFonts;
        private static bool _hasOSFontAssets;
        public static bool ForceLegacyText;
        public static readonly Dictionary<string, Font> BundledFonts = new Dictionary<string, Font>();
        public static readonly Dictionary<GameFont, Font> DefaultFonts = new Dictionary<GameFont, Font>();
        public static readonly Dictionary<GameFont, Font> CurrentFonts = new Dictionary<GameFont, Font>();
        public static readonly Dictionary<GameFont, GUIStyle> DefaultFontStyle = new Dictionary<GameFont, GUIStyle>();
        public static readonly Dictionary<string, string> OSFontPaths = new Dictionary<string, string>();
        public static TMP_FontAsset DefaultTMPFontAsset;
        private Vector2 _leftScrollPosition = Vector2.zero;
        private Vector2 _rightScrollPosition = Vector2.zero;
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

            ForceLegacyText = FontSettings.CurrentWorldFontName == FontSettings.DefaultFontName;

            // var gameFontEnumLength = Enum.GetValues(typeof(GameFont)).Length;

            _content = content;

            MyHarmony = new Harmony("nmkj.customfonts");
            MyHarmony.PatchAll();
        }

        public override void DoSettingsWindowContents(Rect inRect) // The GUI part to edit the mod settings.
        {
            SetupOSInstalledFontNames();
            SetupOSFontPaths();
            SetupBundledFonts();

            var listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            if (GUILayout.Button("Reset to default", GUILayout.Width(200)))
            {
                FontSettings.ScaleFactor = 1.0f;
                FontSettings.VerticalOffset = 0;
                SaveFont(FontSettings.DefaultFontName, true);
                SaveWorldFont(FontSettings.DefaultFontName);
            }

            listingStandard.Gap(30f);
            listingStandard.Label($"[Current Font] Interface: {FontSettings.CurrentUIFontName}, World: {FontSettings.CurrentWorldFontName}");
            string[] specimen =
            {
                "RimWorld is a sci-fi colony sim driven by an intelligent AI storyteller.",
                "RimWorldは、知性のあるAIストーリーテラーによって織りなされるSFコロニーシミュレーションゲームです。"
            };
            listingStandard.Label(String.Join("\n", specimen));
            listingStandard.GapLine();

            var heightAdjustValue = Math.Max(30f * (FontSettings.ScaleFactor - 1.0f) * 4, 0);
            var scrollHeight = inRect.height - 210f - heightAdjustValue;
            var fontListLeftRect = new Rect();
            var fontListRightRect = new Rect();
            listingStandard.LineRectSpilter(out fontListLeftRect, out fontListRightRect, height: scrollHeight);
            
            var leftFontListScrollOuter = new Rect(fontListLeftRect.x, fontListLeftRect.y + 30f,
                fontListLeftRect.width - 20f, fontListLeftRect.height - 30f);
            var leftFontListScrollInner = new Rect(leftFontListScrollOuter.x, leftFontListScrollOuter.y,
                leftFontListScrollOuter.width - 24f,
                23.6f * (3 + BundledFonts.Count + _fontNames.Count));
            Widgets.Label(fontListLeftRect, "---- General Interface ----");
            Widgets.BeginScrollView(leftFontListScrollOuter, ref _leftScrollPosition, leftFontListScrollInner);
            var leftListingStandard = leftFontListScrollInner.BeginListingStandard();
            if (leftListingStandard.RadioButton(FontSettings.DefaultFontName, FontSettings.CurrentUIFontName == FontSettings.DefaultFontName))
                SaveFont(FontSettings.DefaultFontName);
            leftListingStandard.GapLine();
            foreach (var name in BundledFonts.Keys.OrderBy(x => x))
            {
                if (leftListingStandard.RadioButton(name, FontSettings.CurrentUIFontName == name))
                    SaveFont(name);
            }
            leftListingStandard.GapLine();
            foreach (var name in _fontNames)
            {
                if (leftListingStandard.RadioButton(name, FontSettings.CurrentUIFontName == name))
                    SaveFont(name);
            }
            leftListingStandard.End();
            Widgets.EndScrollView();

            var rightFontListScrollOuter = new Rect(fontListRightRect.x, fontListRightRect.y + 30f,
                fontListRightRect.width - 20f, fontListRightRect.height - 30f);
            var rightFontListScrollInner = new Rect(rightFontListScrollOuter.x, rightFontListScrollOuter.y,
                rightFontListScrollOuter.width - 24f,
                23.6f * (3 + BundledFonts.Count + OSFontPaths.Count));
            Widgets.Label(fontListRightRect, "---- World Map ---- (Reload Save to Apply)");
            Widgets.BeginScrollView(rightFontListScrollOuter, ref _rightScrollPosition, rightFontListScrollInner);
            var rightListingStandard = rightFontListScrollInner.BeginListingStandard();
            if (rightListingStandard.RadioButton(FontSettings.DefaultFontName, FontSettings.CurrentWorldFontName == FontSettings.DefaultFontName))
                SaveWorldFont(FontSettings.DefaultFontName);
            rightListingStandard.GapLine();
            foreach (var name in BundledFonts.Keys.OrderBy(x => x))
            {
                if (rightListingStandard.RadioButton(name, FontSettings.CurrentWorldFontName == name))
                    SaveWorldFont(name);
            }
            rightListingStandard.GapLine();
            foreach (var name in OSFontPaths.Keys.OrderBy(x => x))
            {
                if (rightListingStandard.RadioButton(name, FontSettings.CurrentWorldFontName == name))
                    SaveWorldFont(name);
            }
            rightListingStandard.End();
            Widgets.EndScrollView();

            var offsetValue = (float)FontSettings.VerticalOffset;
            listingStandard.AddLabeledSlider($"Vertical Position Offset: {(FontSettings.VerticalOffset > 0 ? "+" : "")}{FontSettings.VerticalOffset}",
                ref offsetValue, -20.0f, 20.0f);
            var newOffset = (int)Math.Round(offsetValue);
            if (newOffset != FontSettings.VerticalOffset)
            {
                FontSettings.VerticalOffset = newOffset;
                RecalcCustomLineHeights();
            }

            var scaleValue = FontSettings.ScaleFactor;
            listingStandard.AddLabeledSlider($"Font Scaling Factor: {FontSettings.ScaleFactor:F1}", ref scaleValue,
                0.5f, 2.0f);
            var fontSizeScale = (float)Math.Round(scaleValue, 1);
            if (Math.Abs(FontSettings.ScaleFactor - fontSizeScale) > 0.01)
            {
                FontSettings.ScaleFactor = fontSizeScale;
                UpdateFont();
            }

            listingStandard.End();

            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory() => "Custom Fonts";

        public static void SetupOSInstalledFontNames()
        {
            if (_hasInstalledFontNames) return;
            _hasInstalledFontNames = true;
            _fontNames = Font.GetOSInstalledFontNames().ToList();
            _fontNames.Sort();
        }

        public static void SetupOSFontPaths()
        {
            if (_hasOSFontAssets) return;
            _hasOSFontAssets = true;
            foreach (var path in Font.GetPathsToOSFonts())
            {
                var asset = TMP_FontAsset.CreateFontAsset(new Font(path));
                var fontName = $"{asset.faceInfo.familyName} ({asset.faceInfo.styleName})";
                if (!OSFontPaths.ContainsKey(fontName))
                {
                    OSFontPaths.Add(fontName, path);
                }
            }
        }

        public static void SetupBundledFonts()
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
            Log.Message(
                $"[Custom Fonts] Loading font asset at {fontAssetPath}:\n{string.Join("\n", cab.GetAllAssetNames())}");
#endif

            foreach (var font in cab.LoadAllAssets<Font>())
            {
                BundledFonts.Add($"(Bundled) {font.fontNames[0]}", font);
            }
        }

        private static void SaveFont(string fontName, bool forceUpdate = false)
        {
            if (fontName == FontSettings.CurrentUIFontName && !forceUpdate) return;
            FontSettings.CurrentUIFontName = fontName;
            UpdateFont();
        }

        private void SaveWorldFont(string fontName)
        {
            if (fontName == FontSettings.CurrentWorldFontName) return;
            FontSettings.CurrentWorldFontName = fontName;
            ForceLegacyText = fontName == FontSettings.DefaultFontName;
            AccessTools.StaticFieldRefAccess<bool>(typeof(WorldFeatures), "ForceLegacyText") = ForceLegacyText;
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

            var isBundled = BundledFonts.ContainsKey(FontSettings.CurrentUIFontName);
            Font font;

            var fontSize = (int)Math.Round(DefaultFonts[fontIndex].fontSize * FontSettings.ScaleFactor);

            if (isBundled)
            {
                font = BundledFonts[FontSettings.CurrentUIFontName];
            }
            else
            {
                font = FontSettings.CurrentUIFontName != FontSettings.DefaultFontName
                    ? Font.CreateDynamicFontFromOSFont(FontSettings.CurrentUIFontName, fontSize)
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

        public static void RecalcCustomLineHeights()
        {
            foreach (GameFont value in Enum.GetValues(typeof(GameFont)))
            {
                RecalcCustomLineHeights(value);
            }
        }

        public static void RecalcCustomLineHeights(GameFont fontType)
        {
            // var isDefault = FontSettings.CurrentUIFontName == FontSettings.DefaultFontName;
            var offsetVector = new Vector2(0f, FontSettings.VerticalOffset);
            // Text.fontStyles[(int)fontType].clipping = isDefault ? TextClipping.Clip : TextClipping.Overflow;
            Text.fontStyles[(int)fontType].contentOffset = offsetVector;
            // Text.textFieldStyles[(int)fontType].clipping = isDefault ? TextClipping.Clip : TextClipping.Overflow;
            Text.textFieldStyles[(int)fontType].contentOffset = offsetVector;
            // Text.textAreaStyles[(int)fontType].clipping = TextClipping.Clip;
            Text.textAreaStyles[(int)fontType].contentOffset = offsetVector;
            // Text.textAreaReadOnlyStyles[(int)fontType].clipping = TextClipping.Clip;
            Text.textAreaReadOnlyStyles[(int)fontType].contentOffset = offsetVector;
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
                CustomFonts.SetupOSInstalledFontNames();
                CustomFonts.SetupOSFontPaths();
                CustomFonts.SetupBundledFonts();
                CustomFonts.DefaultTMPFontAsset = WorldFeatureTextMesh_TextMeshPro.WorldTextPrefab.GetComponent<TextMeshPro>().font;
                CustomFonts.UpdateFont();
                AccessTools.StaticFieldRefAccess<bool>(typeof(WorldFeatures), "ForceLegacyText") = CustomFonts.ForceLegacyText;
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
            }
        }
        
        [HarmonyPatch(typeof(WorldFeatures), "HasCharacter")]
        private class WorldMapHasCharacterPatcher
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (CustomFonts.ForceLegacyText)
                    return true;
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(WorldFeatureTextMesh_TextMeshPro), "Init")]
        class WorldMapInitPatcher
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                TMP_FontAsset fontAsset;
                
                if (CustomFonts.BundledFonts.ContainsKey(FontSettings.CurrentWorldFontName))
                {
                    fontAsset = TMP_FontAsset.CreateFontAsset(
                        CustomFonts.BundledFonts[FontSettings.CurrentWorldFontName]);
                }
                else if (FontSettings.CurrentWorldFontName == FontSettings.DefaultFontName)
                {
                    fontAsset = CustomFonts.DefaultTMPFontAsset;
                }
                else
                {
                    fontAsset = TMP_FontAsset.CreateFontAsset(
                        new Font(CustomFonts.OSFontPaths[FontSettings.CurrentWorldFontName]));
                }

                if (fontAsset == null)
                {
                    FontSettings.CurrentWorldFontName = FontSettings.DefaultFontName;
                    fontAsset = CustomFonts.DefaultTMPFontAsset;
                }

                var prefab = WorldFeatureTextMesh_TextMeshPro.WorldTextPrefab.GetComponent<TextMeshPro>();
                prefab.font = fontAsset;
                prefab.UpdateFontAsset();
                AccessTools.StaticFieldRefAccess<float>(typeof(WorldFeatureTextMesh_TextMeshPro), "TextScale") =
                    1.75f * FontSettings.ScaleFactor;
            }

        }
    }
}