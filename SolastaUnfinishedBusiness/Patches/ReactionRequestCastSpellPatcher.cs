using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using SolastaUnfinishedBusiness.CustomUI;
using SolastaUnfinishedBusiness.Models;
using static SolastaUnfinishedBusiness.Models.Level20Context;

namespace SolastaUnfinishedBusiness.Patches;

//PATCH: removes low-level sub-option for spell reactions if caster is not-multiclass warlock (MULTICLASS)
[UsedImplicitly]
public static class ReactionRequestCastSpellPatcher
{
    [HarmonyPatch(typeof(ReactionRequestCastSpell), nameof(ReactionRequestCastSpell.BuildSlotSubOptions))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class BuildSlotSubOptions_Patch
    {
        [UsedImplicitly]
        public static void Prefix(ReactionRequestCastSpell __instance)
        {
            if (__instance.ReactionParams.RulesetEffect is not RulesetEffectSpell rulesetEffectSpell)
            {
                return;
            }

            // this is a collateral case to support spells from race repertoires
            // but UI for some reason still displays slots from the caster class
            var repertoire = __instance.Character.RulesetCharacter.SpellRepertoires.FirstOrDefault(x =>
                x.KnownSpells.Contains(rulesetEffectSpell.SpellDefinition));

            if (repertoire != null)
            {
                rulesetEffectSpell.spellRepertoire = repertoire;
            }
        }

        [UsedImplicitly]
        public static void Postfix(ReactionRequestCastSpell __instance)
        {
            if (__instance.Character.RulesetCharacter is not RulesetCharacterHero hero
                || (SharedSpellsContext.GetWarlockSpellRepertoire(hero) != null
                    && !SharedSpellsContext.IsMulticaster(hero)))
            {
                return;
            }

            var optionsAvailability = __instance.SubOptionsAvailability;
            var reactionParams = __instance.ReactionParams;
            var repertoire = reactionParams.SpellRepertoire
                             ?? (reactionParams.RulesetEffect as RulesetEffectSpell)?.SpellRepertoire;

            if (repertoire == null)
            {
                return;
            }

            optionsAvailability.Clear();

            if (__instance.ReactionParams.RulesetEffect is not RulesetEffectSpell rulesetEffectSpell)
            {
                return;
            }

            var spellLevel = rulesetEffectSpell.SpellDefinition.SpellLevel;
            var hasMasteryUse = WizardSpellMastery.IsMasteredSpell(
                repertoire,
                rulesetEffectSpell.SpellDefinition);

            if (hasMasteryUse)
            {
                optionsAvailability.Add(0, true);
            }

            var selected = MulticlassGameUi.AddAvailableSubLevels(
                optionsAvailability,
                hero,
                repertoire,
                hasMasteryUse ? spellLevel + 1 : spellLevel);

            if (hasMasteryUse)
            {
                selected = 0;
            }

            if (selected >= 0)
            {
                __instance.SelectSubOption(selected);
            }
        }
    }

    [HarmonyPatch(typeof(ReactionRequestCastSpell), nameof(ReactionRequestCastSpell.SelectSubOption))]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class SelectSubOption_Patch
    {
        [UsedImplicitly]
        public static bool Prefix(ReactionRequestCastSpell __instance, int option)
        {
            //this should always be false
            if (__instance.ReactionParams.RulesetEffect is not RulesetEffectSpell spellEffect)
            {
                return true;
            }

            if (__instance.Character.RulesetCharacter is not RulesetCharacterHero hero
                || (SharedSpellsContext.GetWarlockSpellRepertoire(hero) != null
                    && !SharedSpellsContext.IsMulticaster(hero)))
            {
                return true;
            }

            var selectedSlotLevel = __instance.SubOptionsAvailability.Keys.ToArray()[option];

            // Zero is only the UI key for the Free row. Cast the spell at its real
            // base level so effect advancement and spell accounting remain correct.
            if (selectedSlotLevel == 0 &&
                WizardSpellMastery.IsMasteredSpell(
                    spellEffect.SpellRepertoire,
                    spellEffect.SpellDefinition))
            {
                selectedSlotLevel = spellEffect.SpellDefinition.SpellLevel;
            }

            spellEffect.SlotLevel = selectedSlotLevel;
            return false;
        }
    }

    [HarmonyPatch(typeof(ReactionRequestCastSpell), nameof(ReactionRequestCastSpell.SelectedSubOption),
        MethodType.Getter)]
    [SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Patch")]
    [UsedImplicitly]
    public static class SelectedSubOption_Getter_Patch
    {
        [UsedImplicitly]
        public static bool Prefix(ReactionRequestCastSpell __instance, ref int __result)
        {
            //this should always be false
            if (__instance.ReactionParams.RulesetEffect is not RulesetEffectSpell spellEffect)
            {
                return true;
            }

            if (__instance.Character.RulesetCharacter is not RulesetCharacterHero hero
                || (SharedSpellsContext.GetWarlockSpellRepertoire(hero) != null
                    && !SharedSpellsContext.IsMulticaster(hero)))
            {
                return true;
            }

            var selectedSlotLevel = spellEffect.SlotLevel;

            if (__instance.SubOptionsAvailability.ContainsKey(0) &&
                selectedSlotLevel == spellEffect.SpellDefinition.SpellLevel &&
                WizardSpellMastery.IsMasteredSpell(
                    spellEffect.SpellRepertoire,
                    spellEffect.SpellDefinition))
            {
                selectedSlotLevel = 0;
            }

            __result = Array.IndexOf([.. __instance.SubOptionsAvailability.Keys], selectedSlotLevel);

            return false;
        }
    }
}
