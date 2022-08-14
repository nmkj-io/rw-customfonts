using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Verse;
using UnityEngine;
using HarmonyLib;

namespace CustomFonts
{
    public class FontSettings : ModSettings
    {
        // Setting values
        public const string DefaultFontName = "Default";
        public static string CurrentFontName = "";
        public static string PreviousFontName = "";

        public override void ExposeData() // Writing settings to the mod file
        {
            Scribe_Values.Look(ref CurrentFontName, "CurrentFontName");
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
        private static readonly Dictionary<string, Font> BundledFonts = new Dictionary<string, Font>();
        private static readonly Dictionary<GameFont, Font> DefaultFonts = new Dictionary<GameFont, Font>();
        public static readonly Dictionary<GameFont, Font> CurrentFonts = new Dictionary<GameFont, Font>();
        private readonly bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private Vector2 _scrollPosition = Vector2.zero;
        public static Harmony MyHarmony { get; private set; }
        public static float[] LineHeights;
        public static float[] SpaceBetweenLines;
        private static ModContentPack _content;

        public CustomFonts(ModContentPack content) :
            base(content) // A mandatory constructor which resolves the reference to the mod settings.
        {
            _settings = GetSettings<FontSettings>();
            foreach (GameFont value in Enum.GetValues(typeof(GameFont)))
            {
                DefaultFonts[value] = Text.fontStyles[(int)value].font;
            }

            var gameFontEnumLength = Enum.GetValues(typeof(GameFont)).Length;
            LineHeights = new float[gameFontEnumLength];
            SpaceBetweenLines = new float[gameFontEnumLength];

            _content = content;

            MyHarmony = new Harmony("nmkj.customfonts");
            MyHarmony.PatchAll();
        }

        public override void DoSettingsWindowContents(Rect inRect) // The GUI part to edit the mod settings.
        {
            SetupOSInstalledFontNames();
            SetupBundledFonts();
            var isDefaultFont = FontSettings.CurrentFontName == FontSettings.DefaultFontName;

            var listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            listingStandard.Label($"Current font: {FontSettings.CurrentFontName}");
            string[] specimen =
            {
                "RimWorld is a sci-fi colony sim driven by an intelligent AI storyteller.",
                !_isWindows && isDefaultFont
                    ? "(The default font cannot render Japanese texts in non-windows environments.)"
                    : "RimWorldは、知性のあるAIストーリーテラーによって織りなされるSFコロニーシミュレーションゲームです。"
            };
            listingStandard.Label(String.Join("\n", specimen));
            listingStandard.GapLine();
            listingStandard.End();
            var fontScrollRect = new Rect(inRect.x, inRect.y + 90f, inRect.width, inRect.height - 90f);
            var fontListRect = new Rect(fontScrollRect.x, fontScrollRect.y, fontScrollRect.width - 30f,
                24f * _fontNames.Count);
            Widgets.BeginScrollView(fontScrollRect, ref _scrollPosition, fontListRect);
            listingStandard.Begin(fontListRect);
            if (listingStandard.RadioButton(FontSettings.DefaultFontName, isDefaultFont))
                SaveFont(FontSettings.DefaultFontName);
            listingStandard.GapLine();
            foreach (var name in BundledFonts.Keys)
            {
                if (listingStandard.RadioButton(name, FontSettings.CurrentFontName == name)) SaveFont(name);
            }
            listingStandard.GapLine();
            foreach (var name in _fontNames)
            {
                if (listingStandard.RadioButton(name, FontSettings.CurrentFontName == name)) SaveFont(name);
            }

            listingStandard.End();
            Widgets.EndScrollView();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory() => "Custom Fonts";

        private void SetupOSInstalledFontNames()
        {
            if (_hasInstalledFontNames) return;
            _hasInstalledFontNames = true;
            _fontNames = Font.GetOSInstalledFontNames().ToList();

#if DEBUG
            var fontPath = Font.GetPathsToOSFonts();
            Log.Message($"[Custom Fonts] Looking up fonts at:\n{string.Join("\n", fontPath)}");
#endif
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

        private static void SaveFont(string fontName)
        {
            if (fontName == FontSettings.CurrentFontName) return;
            FontSettings.PreviousFontName = FontSettings.CurrentFontName;
            FontSettings.CurrentFontName = fontName;
            UpdateFont();
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
            if (FontSettings.CurrentFontName == FontSettings.PreviousFontName) return;

            var isBundled = BundledFonts.ContainsKey(FontSettings.CurrentFontName);
            Font font;

            if (isBundled)
            {
                font = BundledFonts[FontSettings.CurrentFontName];
            }
            else
            {
                font = FontSettings.CurrentFontName != FontSettings.DefaultFontName
                    ? Font.CreateDynamicFontFromOSFont(FontSettings.CurrentFontName, DefaultFonts[fontIndex].fontSize)
                    : DefaultFonts[fontIndex];
            }

#if DEBUG
            Log.Message($"[Custom Fonts] Updating font to {FontSettings.CurrentFontName}");
#endif
            CurrentFonts[fontIndex] = font;
            Text.fontStyles[(int)fontIndex].font = font;
            Text.fontStyles[(int)fontIndex].fontSize = DefaultFonts[fontIndex].fontSize;
            Text.textFieldStyles[(int)fontIndex].font = font;
            Text.textFieldStyles[(int)fontIndex].fontSize = DefaultFonts[fontIndex].fontSize;
            Text.textAreaStyles[(int)fontIndex].font = font;
            Text.textAreaStyles[(int)fontIndex].fontSize = DefaultFonts[fontIndex].fontSize;
            Text.textAreaReadOnlyStyles[(int)fontIndex].font = font;
            Text.textAreaReadOnlyStyles[(int)fontIndex].fontSize = DefaultFonts[fontIndex].fontSize;
            RecalcCustomLineHeights();
        }

        public static void RecalcCustomLineHeights()
        {
            var padding = new RectOffset(0, 0, 0, 0);
            foreach (GameFont fontType in Enum.GetValues(typeof(GameFont)))
            {
                var style = Text.fontStyles[(int)fontType];
                var padding2 = new RectOffset(style.padding.left, style.padding.right, style.padding.top,
                    style.padding.bottom);
                style.padding = padding;
                var currentFontStyle = Text.fontStyles[(int)fontType];
                LineHeights[(int)fontType] = currentFontStyle.CalcHeight(new GUIContent("W"), 999f);
                SpaceBetweenLines[(int)fontType] = currentFontStyle.CalcHeight(new GUIContent("W\nW"), 999f) -
                                                   currentFontStyle.CalcHeight(new GUIContent("W"), 999f) * 2f;
                style.padding = padding2;
            }
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
                CustomFonts.RecalcCustomLineHeights();
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
    }
}