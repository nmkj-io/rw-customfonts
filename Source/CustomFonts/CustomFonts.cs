using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Verse;
using UnityEngine;
using HarmonyLib;
using RimWorld;

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
        private string[] _installedFontNames;
        private bool _hasInstalledFontNames;
        private static readonly Dictionary<GameFont, Font> DefaultFonts = new Dictionary<GameFont, Font>();
        public static readonly Dictionary<GameFont, Font> CurrentFonts = new Dictionary<GameFont, Font>();
        private readonly bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private Vector2 _scrollPosition = Vector2.zero;
        public static Harmony MyHarmony { get; private set; }
        public static float[] LineHeights;
        public static float[] SpaceBetweenLines;

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

            MyHarmony = new Harmony("nmkj.customfonts");
            MyHarmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public override void DoSettingsWindowContents(Rect inRect) // The GUI part to edit the mod settings.
        {
            SetupOSInstalledFontNames();
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
                24f * _installedFontNames.Length);
            Widgets.BeginScrollView(fontScrollRect, ref _scrollPosition, fontListRect);
            listingStandard.Begin(fontListRect);
            if (listingStandard.RadioButton(FontSettings.DefaultFontName, isDefaultFont))
                SaveFont(FontSettings.DefaultFontName);
            foreach (var name in _installedFontNames)
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
            _installedFontNames = Font.GetOSInstalledFontNames();
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
            foreach (GameFont value in Enum.GetValues(typeof(GameFont)))
            {
                UpdateFont(value);
            }
        }

        private static void UpdateFont(GameFont fontIndex)
        {
            if (FontSettings.CurrentFontName == FontSettings.PreviousFontName) return;

            var font = FontSettings.CurrentFontName != FontSettings.DefaultFontName
                ? Font.CreateDynamicFontFromOSFont(FontSettings.CurrentFontName, DefaultFonts[fontIndex].fontSize)
                : DefaultFonts[fontIndex];
            CurrentFonts[fontIndex] = font;
            Text.fontStyles[(int)fontIndex].font = font;
            Text.textFieldStyles[(int)fontIndex].font = font;
            Text.textAreaStyles[(int)fontIndex].font = font;
            Text.textAreaReadOnlyStyles[(int)fontIndex].font = font;
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
        
        [HarmonyPatch(typeof(Text), "StartOfOnGUI")]
        class StartOfOnGUIPatcher
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (_patcherInitialized) return;

                Log.Message("[Custom Fonts] Patcher on StartOfOnGUI initialized");
                _patcherInitialized = true;
                CustomFonts.RecalcCustomLineHeights();
            }
        }

        [HarmonyPatch(typeof(GenScene), "GoToMainMenu")]
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