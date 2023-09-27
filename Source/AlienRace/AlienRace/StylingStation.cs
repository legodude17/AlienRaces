namespace AlienRace;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

public static class StylingStation
{
    private static List<TabRecord> mainTabs = new();
    private static List<TabRecord> raceTabs = new();
    private static MainTab         curMainTab;
    private static RaceTab         curRaceTab;

    private static int selectedIndex = -1;

    private static Dialog_StylingStation        instance;
    private static Pawn                         pawn;
    private static AlienPartGenerator.AlienComp alienComp;
    private static ThingDef_AlienRace           alienRaceDef;

    public static IEnumerable<CodeInstruction> DoWindowContentsTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilg) =>
        instructions.MethodReplacer(AccessTools.Method(typeof(Dialog_StylingStation), "DrawTabs"), AccessTools.Method(typeof(StylingStation), nameof(DoRaceAndCharacterTabs)));

    public static void DoRaceAndCharacterTabs(Dialog_StylingStation gotInstance, Rect inRect)
    {
        instance = gotInstance;
        pawn     = CachedData.stationPawn(instance);

        if (pawn.def is not ThingDef_AlienRace alienRace || pawn.TryGetComp<AlienPartGenerator.AlienComp>() is not { } comp)
        {
            CachedData.drawTabs(instance, inRect);
            return;
        }

        alienRaceDef = alienRace;
        alienComp    = comp;

        mainTabs.Clear();
        mainTabs.Add(new TabRecord("HAR.CharacterFeatures".Translate(), () => curMainTab = MainTab.CHARACTER, curMainTab == MainTab.CHARACTER));
        mainTabs.Add(new TabRecord("HAR.RaceFeatures".Translate(),      () => curMainTab = MainTab.RACE,      curMainTab == MainTab.RACE));
        Widgets.DrawMenuSection(inRect);
        TabDrawer.DrawTabs(inRect, mainTabs);
        inRect.yMin += 40;
        switch (curMainTab)
        {
            case MainTab.CHARACTER:
                CachedData.drawTabs(instance, inRect);
                break;
            case MainTab.RACE:
                DoRaceTabs(inRect);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static Dictionary<AlienPartGenerator.BodyAddon, Dictionary<bool, List<Color>>> availableColorsCache = new Dictionary<AlienPartGenerator.BodyAddon, Dictionary<bool, List<Color>>>();

    public static List<Color> AvailableColors(AlienPartGenerator.BodyAddon ba, bool first = true)
    {
        if (availableColorsCache.TryGetValue(ba, out Dictionary<bool, List<Color>> firstEntry))
            if (firstEntry.TryGetValue(first, out List<Color> colors))
                return colors;

        List<Color> availableColors = new();

        AlienPartGenerator.ColorChannelGenerator channelGenerator = alienRaceDef.alienRace.generalSettings?.alienPartGenerator.colorChannels?.Find(ccg => ccg.name == ba.ColorChannel);


        if (channelGenerator != null)
            foreach (AlienPartGenerator.ColorChannelGeneratorCategory entry in channelGenerator.entries)
            {
                ColorGenerator cg = first ? entry.first : entry.second;

                switch (cg)
                {
                    case ColorGenerator_CustomAlienChannel cgCustomAlien:

                        break;
                    case ColorGenerator_SkinColorMelanin cgMelanin:

                        if (cgMelanin.naturalMelanin)
                        {
                            foreach (GeneDef geneDef in PawnSkinColors.SkinColorGenesInOrder)
                                if (geneDef.skinColorBase.HasValue)
                                    availableColors.Add(geneDef.skinColorBase.Value);
                        }
                        else
                        {
                            for (int i = 0; i < PawnSkinColors.SkinColorGenesInOrder.Count; i++)
                            {
                                float currentMelanin = Mathf.Lerp(cgMelanin.minMelanin, cgMelanin.maxMelanin, 1f / PawnSkinColors.SkinColorGenesInOrder.Count * i);

                                int     nextIndex = PawnSkinColors.SkinColorGenesInOrder.FirstIndexOf(gd => gd.minMelanin >= currentMelanin);
                                GeneDef lastGene  = PawnSkinColors.SkinColorGenesInOrder[nextIndex - 1];
                                GeneDef nextGene  = PawnSkinColors.SkinColorGenesInOrder[nextIndex];
                                availableColors.Add(Color.Lerp(lastGene.skinColorBase.Value, nextGene.skinColorBase.Value,
                                                               Mathf.InverseLerp(lastGene.minMelanin, nextGene.minMelanin, currentMelanin)));
                            }
                        }

                        break;
                    case ColorGenerator_Options cgOptions:
                        foreach (ColorOption co in cgOptions.options)
                            if (co.only.a >= 0f)
                            {
                                availableColors.Add(co.only);
                            }
                            else
                            {
                                List<Color> colorOptions = new List<Color>();

                                Color diff = co.max - co.min;

                                //int steps = Math.Min(100, Mathf.RoundToInt((Mathf.Abs(diff.r) + Mathf.Abs(diff.g) + Mathf.Abs(diff.b) + Mathf.Abs(diff.a)) / 0.01f));

                                float redStep   = Mathf.Max(0.0001f, diff.r / 2);
                                float greenStep = Mathf.Max(0.0001f, diff.g / 2);
                                float blueStep  = Mathf.Max(0.0001f, diff.b / 2);
                                float alphaStep = Mathf.Max(0.0001f, diff.a / 2);

                                for (float r = co.min.r; r <= co.max.r; r += redStep)
                                {
                                    for (float g = co.min.g; g <= co.max.g; g += greenStep)
                                    {
                                        for (float b = co.min.b; b <= co.max.b; b += blueStep)
                                        {
                                            for (float a = co.min.a; a <= co.max.a; a += alphaStep)
                                                colorOptions.Add(new Color(r, g, b, a));
                                        }
                                    }
                                }

                                availableColors.AddRange(colorOptions.OrderBy(c =>
                                                                              {
                                                                                  Color.RGBToHSV(c, out _, out float s, out float v);
                                                                                  return s + v;
                                                                              }));

                                //for (int i = 0; i < steps; i++)
                                //availableColors.Add(Color.Lerp(co.min, co.max, 1f / steps * i));
                            }

                        break;
                    case ColorGenerator_Single:
                    case ColorGenerator_White:
                        availableColors.Add(cg.NewRandomizedColor());
                        break;
                    default:
                        //availableColors.AddRange(DefDatabase<ColorDef>.AllDefs.Select(cd => cd.color));
                        break;
                }
            }

        if (!availableColorsCache.ContainsKey(ba))
            availableColorsCache.Add(ba, new Dictionary<bool, List<Color>>());
        availableColorsCache[ba].Add(first, availableColors);

        return availableColors;
    }
    // new List<Color>();

    public static void DoRaceTabs(Rect inRect)
    {
        raceTabs.Clear();
        raceTabs.Add(new TabRecord("HAR.BodyAddons".Translate(), () => curRaceTab = RaceTab.BODY_ADDONS, curRaceTab == RaceTab.BODY_ADDONS));
        Widgets.DrawMenuSection(inRect);
        TabDrawer.DrawTabs(inRect, raceTabs);
        switch (curRaceTab)
        {
            case RaceTab.BODY_ADDONS:
                DrawBodyAddonTab(inRect);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public static void DrawBodyAddonTab(Rect inRect)
    {
        List<AlienPartGenerator.BodyAddon> bodyAddons = alienRaceDef.alienRace.generalSettings.alienPartGenerator.bodyAddons.Concat(Utilities.UniversalBodyAddons).ToList();
        DoAddonList(inRect.LeftPartPixels(260), bodyAddons);
        inRect.xMin += 260;
        if (selectedIndex != -1)
            DoAddonInfo(inRect, bodyAddons[selectedIndex], bodyAddons);
    }

    private static Vector2 addonsScrollPos;

    private static void DoAddonList(Rect inRect, List<AlienPartGenerator.BodyAddon> addons)
    {
        if (selectedIndex > addons.Count)
            selectedIndex = -1;

        Widgets.DrawMenuSection(inRect);
        Rect viewRect = new(0, 0, 250, addons.Count * 54 + 4);
        Widgets.BeginScrollView(inRect, ref addonsScrollPos, viewRect);
        for (int i = 0; i < addons.Count; i++)
        {
            Rect rect = new Rect(10, i * 54f + 4, 240f, 50f).ContractedBy(2);
            if (i == selectedIndex)
            {
                Widgets.DrawOptionSelected(rect);
            }
            else
            {
                GUI.color = Widgets.WindowBGFillColor;
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
                GUI.color = Color.white;

                bool groupSelected = false;
                int  index         = i;

                while (index >= 0 && addons[index].linkVariantIndexWithPrevious)
                {
                    index--;
                    if (selectedIndex == index)
                        groupSelected = true;
                }

                index = i + 1;

                while (index <= addons.Count - 1 && addons[index].linkVariantIndexWithPrevious)
                {
                    //Log.Message($"{index} is linked, selected is {selectedIndex}");
                    if (selectedIndex == index)
                        groupSelected = true;
                    index++;
                }

                if (groupSelected)
                {
                    GUI.color = new ColorInt(135, 135, 135).ToColor;
                    Widgets.DrawBox(rect);
                    GUI.color = Color.white;
                }
            }

            Widgets.DrawHighlightIfMouseover(rect);

            if (Widgets.ButtonInvisible(rect))
            {
                selectedIndex = i;
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            Rect      position     = rect.LeftPartPixels(rect.height).ContractedBy(2);
            int       addonVariant = alienComp.addonVariants[i];
            Texture2D image        = ContentFinder<Texture2D>.Get(addons[i].GetPath(pawn, ref addonVariant, addonVariant) + "_south");
            GUI.color = Widgets.MenuSectionBGFillColor;
            GUI.DrawTexture(position, BaseContent.WhiteTex);
            GUI.color = Color.white;
            GUI.DrawTexture(position, image);
            rect.xMin += rect.height;
            Widgets.Label(rect.ContractedBy(4), addons[i].Name);

            if (addons[i].linkVariantIndexWithPrevious)
            {
                GUI.color = new ColorInt(135, 135, 135).ToColor;
                GUI.DrawTexture(new Rect(rect.x - rect.height - 6, rect.center.y,     6, 2),  BaseContent.WhiteTex);
                GUI.DrawTexture(new Rect(rect.x - rect.height - 6, (i - 1) * 54 + 27, 6, 2),  BaseContent.WhiteTex);
                GUI.DrawTexture(new Rect(rect.x - rect.height - 6, (i - 1) * 54 + 27, 2, 56), BaseContent.WhiteTex);
                GUI.color = Color.white;
            }

            AlienPartGenerator.ExposableValueTuple<Color, Color> channelColors = alienComp.GetChannel(addons[i].ColorChannel);
            (Color, Color)                                       colors        = (addons[i].colorOverrideOne ?? channelColors.first, addons[i].colorOverrideTwo ?? channelColors.second);

            Rect colorRect = new(rect.xMax - 44, rect.yMax - 22, 18, 18);
            Widgets.DrawLightHighlight(colorRect);
            Widgets.DrawBoxSolid(colorRect.ContractedBy(2), colors.Item1);

            colorRect = new Rect(rect.xMax - 22, rect.yMax - 22, 18, 18);
            Widgets.DrawLightHighlight(colorRect);
            Widgets.DrawBoxSolid(colorRect.ContractedBy(2), colors.Item2);
        }

        Widgets.EndScrollView();
    }

    private static Vector2 variantsScrollPos;
    private static bool    editingFirstColor = true;
    private static Vector2 colorsScrollPos;

    private static void DoAddonInfo(Rect inRect, AlienPartGenerator.BodyAddon addon, List<AlienPartGenerator.BodyAddon> addons)
    {
        List<Color>                                          firstColors   = AvailableColors(addon);
        List<Color>                                          secondColors  = AvailableColors(addon, false);
        AlienPartGenerator.ExposableValueTuple<Color, Color> channelColors = alienComp.GetChannel(addon.ColorChannel);
        (Color, Color)                                       colors        = (addon.colorOverrideOne ?? channelColors.first, addon.colorOverrideTwo ?? channelColors.second);
        Rect                                                 viewRect;
        if (firstColors.Any() || secondColors.Any())
        {
            Rect colorsRect = inRect.BottomPart(0.4f);
            inRect.yMax -= colorsRect.height;

            Widgets.DrawMenuSection(colorsRect);


            List<Color> availableColors = editingFirstColor ? firstColors : secondColors;

            colorsRect = colorsRect.ContractedBy(6);

            Vector2 size = new(14, 18);
            viewRect = new Rect(0, 0, colorsRect.width - 16, (Mathf.Ceil(availableColors.Count / ((colorsRect.width - 14) / size.x)) + 1) * size.y + 35);

            Widgets.BeginScrollView(colorsRect, ref colorsScrollPos, viewRect);


            Rect headerRect = viewRect.TopPartPixels(30).ContractedBy(4);
            viewRect.yMin += 30;

            Widgets.Label(headerRect, "HAR.Colors".Translate());

            Rect colorRect;
            if (firstColors.Any())
            {
                colorRect = new Rect(headerRect.xMax - 44, headerRect.y, 18, 18);
                Widgets.DrawLightHighlight(colorRect);
                Widgets.DrawHighlightIfMouseover(colorRect);
                Widgets.DrawBoxSolid(colorRect.ContractedBy(2), colors.Item1);

                if (editingFirstColor)
                    Widgets.DrawBox(colorRect);

                if (Widgets.ButtonInvisible(colorRect))
                    editingFirstColor = true;
            }
            else
            {
                editingFirstColor = false;
            }

            if (secondColors.Any())
            {
                colorRect = new Rect(headerRect.xMax - 22, headerRect.y, 18, 18);
                Widgets.DrawLightHighlight(colorRect);
                Widgets.DrawHighlightIfMouseover(colorRect);
                Widgets.DrawBoxSolid(colorRect.ContractedBy(2), colors.Item2);

                if (!editingFirstColor)
                    Widgets.DrawBox(colorRect);

                if (Widgets.ButtonInvisible(colorRect))
                    editingFirstColor = false;
            }
            else
            {
                editingFirstColor = true;
            }

            Vector2 pos = new(0, 30);

            foreach (Color color in availableColors)
            {
                Rect rect = new Rect(pos, size).ContractedBy(1);
                Widgets.DrawLightHighlight(rect);
                Widgets.DrawHighlightIfMouseover(rect);
                Widgets.DrawBoxSolid(rect.ContractedBy(1), color);

                if (editingFirstColor)
                {
                    if (colors.Item1.IndistinguishableFrom(color))
                        Widgets.DrawBox(rect);
                    if (Widgets.ButtonInvisible(rect))
                        if (addon.ColorChannel == "hair")
                        {
                            pawn.story.HairColor = color;
                            pawn.style.Notify_StyleItemChanged();
                            pawn.style.ResetNextStyleChangeAttemptTick();
                            pawn.style.nextHairColor                     = null;
                            CachedData.stationDesiredHairColor(instance) = color;
                        }
                        else
                        {
                            alienComp.OverwriteColorChannel(addon.ColorChannel, color);
                        }
                    //addon.colorOverrideOne = color;
                }
                else
                {
                    if (colors.Item2.IndistinguishableFrom(color))
                        Widgets.DrawBox(rect);
                    if (Widgets.ButtonInvisible(rect))
                        alienComp.OverwriteColorChannel(addon.ColorChannel, second: color);
                    //addon.colorOverrideTwo = color;
                }

                pos.x += size.x;
                if (pos.x + size.x >= viewRect.xMax)
                {
                    pos.y += size.y;
                    pos.x =  0;
                }
            }

            Widgets.EndScrollView();
        }

        int   variantCount = addon.GetVariantCount();
        int   countPerRow  = 4;
        float width        = inRect.width - 20;
        float itemSize     = width / countPerRow;
        while (itemSize > 92)
        {
            countPerRow++;
            itemSize = width / countPerRow;
        }

        viewRect = new(0, 0, width, Mathf.Ceil((float)variantCount / countPerRow) * itemSize);

        Widgets.DrawMenuSection(inRect);
        Widgets.BeginScrollView(inRect, ref variantsScrollPos, viewRect);

        for (int i = 0; i < variantCount; i++)
        {
            Rect rect  = new Rect(i % countPerRow * itemSize, Mathf.Floor((float)i / countPerRow) * itemSize, itemSize, itemSize).ContractedBy(2);
            int  index = i;

            GUI.color = Widgets.WindowBGFillColor;
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = Color.white;
            Widgets.DrawHighlightIfMouseover(rect);

            if (alienComp.addonVariants[selectedIndex] == i)
                Widgets.DrawBox(rect);

            string    addonPath = addon.GetPath(pawn, ref index, i);
            Texture2D image     = ContentFinder<Texture2D>.Get(addonPath + "_south", false);

            if (image != null)
                GUI.DrawTexture(rect, image);

            if (Widgets.ButtonInvisible(rect))
            {
                alienComp.addonVariants[selectedIndex] = i;

                index = selectedIndex;

                while (index >= 0 && addons[index].linkVariantIndexWithPrevious)
                {
                    index--;
                    alienComp.addonVariants[index] = i;
                }

                index = selectedIndex + 1;

                while (index <= addons.Count - 1 && addons[index].linkVariantIndexWithPrevious)
                {
                    alienComp.addonVariants[index] = i;
                    index++;
                }
            }
        }

        Widgets.EndScrollView();
    }

    private enum MainTab
    {
        CHARACTER,
        RACE
    }

    private enum RaceTab
    {
        BODY_ADDONS
    }
}