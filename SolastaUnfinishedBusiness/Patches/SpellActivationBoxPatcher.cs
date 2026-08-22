using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using SolastaUnfinishedBusiness.Api.GameExtensions;
using SolastaUnfinishedBusiness.Api.Helpers;
using SolastaUnfinishedBusiness.Models;
using UnityEngine;
using UnityEngine.UI;
using static SolastaUnfinishedBusiness.Models.Level20Context;

namespace SolastaUnfinishedBusiness.Patches;

[UsedImplicitly]
public static class SpellActivationBoxPatcher
{
    [HarmonyPatch(typeof(SpellActivationBox), nameof(SpellActivationBox.BindSpell))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class BindSpell_Patch
    {
        private const string SpellMasteryBadgeName = "UBSpellMasteryBadge";

        private static bool UniqueLevelSlots(
            FeatureDefinitionCastSpell featureDefinitionCastSpell,
            RulesetCharacter character)
        {
            //PATCH: offers upcast using higher spell slots on Warlock repertoire
            if (character is not RulesetCharacterHero hero || !SharedSpellsContext.IsMulticaster(hero))
            {
                return featureDefinitionCastSpell.UniqueLevelSlots;
            }

            var sharedSpellLevel = SharedSpellsContext.GetSharedSpellLevel(hero);
            var warlockSpellLevel = SharedSpellsContext.GetWarlockSpellLevel(hero);
            var pactMaxSlots = SharedSpellsContext.GetWarlockMaxSlots(hero);
            var pactUsedSlots = SharedSpellsContext.GetWarlockUsedSlots(hero);
            var pactAvailableSlots = pactMaxSlots - pactUsedSlots;

            return featureDefinitionCastSpell.UniqueLevelSlots &&
                   // this ensures game does std slot calculation when out of pact slots
                   pactAvailableSlots > 0 &&
                   // this ensures game does std slot calculation if we can upcast warlock spells
                   sharedSpellLevel <= warlockSpellLevel;
        }

        [UsedImplicitly]
        public static void MyGetSlotsNumber(
            RulesetSpellRepertoire repertoire,
            int spellLevel,
            out int remaining,
            out int max,
            RulesetCharacter caster,
            SpellActivationBox spellActivationBox)
        {
            if (caster.IsSpellPointsEnabled())
            {
                var canCastSpell = SpellPointsContext.CanCastSpellOfLevel(caster, spellLevel);

                max = 1; // irrelevant
                remaining = canCastSpell ? 1 : 0;

                if (!canCastSpell)
                {
                    spellActivationBox.hasUpcast = false;
                }
            }
            else
            {
                repertoire.GetSlotsNumber(spellLevel, out remaining, out max);
            }
        }

        [UsedImplicitly]
        public static IEnumerable<CodeInstruction> Transpiler([NotNull] IEnumerable<CodeInstruction> instructions)
        {
            var uniqueLevelSlotsMethod = typeof(FeatureDefinitionCastSpell).GetMethod("get_UniqueLevelSlots");
            var myUniqueLevelSlotsMethod =
                new Func<FeatureDefinitionCastSpell, RulesetCharacterHero, bool>(UniqueLevelSlots).Method;

            var getSlotsNumberMethod = typeof(RulesetSpellRepertoire).GetMethod("GetSlotsNumber");
            var myGetSlotsNumberMethod = typeof(BindSpell_Patch).GetMethod("MyGetSlotsNumber");

            return instructions
                .ReplaceCalls(getSlotsNumberMethod, "SpellActivationBox.BindSpell.GetSlotsNumber",
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, myGetSlotsNumberMethod))
                .ReplaceCalls(uniqueLevelSlotsMethod, "SpellActivationBox.BindSpell.UniqueLevelSlots",
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call, myUniqueLevelSlotsMethod));
        }

        [UsedImplicitly]
        public static void Postfix(
            SpellActivationBox __instance,
            RulesetSpellRepertoire spellRepertoire,
            SpellDefinition spellDefinition)
        {
            var showBadge = WizardSpellMastery.IsMasteredSpell(spellRepertoire, spellDefinition);
            var badge = __instance.transform.Find(SpellMasteryBadgeName) as RectTransform;

            if (badge == null && showBadge)
            {
                badge = BuildSpellMasteryBadge(__instance);
            }

            if (badge != null)
            {
                badge.gameObject.SetActive(showBadge);
                badge.SetAsLastSibling();
            }
        }

        private static RectTransform BuildSpellMasteryBadge(SpellActivationBox spellActivationBox)
        {
            var badgeObject = new GameObject(SpellMasteryBadgeName, typeof(RectTransform), typeof(Image));
            var badge = badgeObject.GetComponent<RectTransform>();

            badge.SetParent(spellActivationBox.transform, false);
            badge.anchorMin = Vector2.one;
            badge.anchorMax = Vector2.one;
            badge.pivot = Vector2.one;
            badge.anchoredPosition = new Vector2(-2, -2);
            badge.sizeDelta = new Vector2(22, 22);

            var background = badgeObject.GetComponent<Image>();
            background.sprite = spellActivationBox.background.sprite;
            background.type = Image.Type.Sliced;
            background.color = new Color(0.14f, 0.12f, 0.18f, 0.92f);
            background.raycastTarget = false;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelTransform = labelObject.GetComponent<RectTransform>();

            labelTransform.SetParent(badge, false);
            labelTransform.anchorMin = Vector2.zero;
            labelTransform.anchorMax = Vector2.one;
            labelTransform.offsetMin = Vector2.zero;
            labelTransform.offsetMax = Vector2.zero;

            var label = labelObject.GetComponent<Text>();
            label.text = "M";
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 15;
            label.fontStyle = FontStyle.Normal;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.86f, 0.87f, 0.88f, 0.96f);
            label.raycastTarget = false;

            return badge;
        }
    }

    //PATCH: register on acting character if SHIFT is pressed on spell box activation
    [HarmonyPatch(typeof(SpellActivationBox), nameof(SpellActivationBox.OnActivateCb))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class OnActivateCb_Patch
    {
        [UsedImplicitly]
        public static void Prefix(SpellActivationBox __instance)
        {
            if (__instance.spellRepertoire == null)
            {
                return;
            }

            var rulesetCaster = __instance.tooltip.Context as RulesetCharacter
                                ?? __instance.spellRepertoire.GetCaster();
            var caster = GameLocationCharacter.GetFromActor(rulesetCaster);

            caster?.RegisterShiftState();
        }
    }
}
