using System.Collections.Generic;
using AbilitySystem.Authoring;
using AttributeSystem.Authoring;
using AttributeSystem.Components;
using GameplayTag.Authoring;
using UnityEngine;


namespace AbilitySystem
{
    public class AbilitySystemCharacter : MonoBehaviour
    {
        [SerializeField]
        protected AttributeSystemComponent _attributeSystem;
        public AttributeSystemComponent AttributeSystem { get { return _attributeSystem; } set { _attributeSystem = value; } }
        public List<GameplayEffectContainer> AppliedGameplayEffects = new List<GameplayEffectContainer>();
        public List<AbstractAbilitySpec> GrantedAbilities = new List<AbstractAbilitySpec>();
        public float Level;

        public void GrantAbility(AbstractAbilitySpec spec)
        {
            this.GrantedAbilities.Add(spec);
        }

        public void RemoveAbilitiesWithTag(GameplayTagScriptableObject tag)
        {
            for (var i = GrantedAbilities.Count - 1; i >= 0; i--)
            {
                if (GrantedAbilities[i].Ability.AbilityTags.AssetTag == tag)
                {
                    GrantedAbilities.RemoveAt(i);
                }
            }
        }

        public GameplayEffectSpec MakeOutgoingSpec(GameplayEffectScriptableObject gameplayEffect, float? level = 1f)
        {
            level ??= Level;
            return GameplayEffectSpec.CreateNew(
                GameplayEffect: gameplayEffect,
                Source: this,
                Level: level.GetValueOrDefault(1));
        }        

        // This should be called at the start of each turn by the Game Manager.
        // I could use GameplayTags to only apply a GE when it's the owner's turn, or not their turn, or both.
        public void TickGameplayEffects()
        {
            foreach (var t in this.AppliedGameplayEffects)
            {
                var ge = t.spec;

                // Can't tick instant GE
                if (ge.GameplayEffect.gameplayEffect.DurationPolicy == EDurationPolicy.Instant)
                {
                    continue;
                }

                // Update time remaining.  Strictly, it's only really valid for durational GE, but calculating for infinite GE isn't harmful
                ge.UpdateRemainingDuration(1.0f);

                // Tick the periodic component
                ge.TickPeriodic(1.0f, out var executePeriodicTick);
                if (executePeriodicTick)
                {
                    ApplyInstantGameplayEffect(ge);
                }
            }
            
            // Remove expired Gameplay Effects.
            AppliedGameplayEffects.RemoveAll(x => x.spec.GameplayEffect.gameplayEffect.DurationPolicy == EDurationPolicy.HasDuration && x.spec.DurationRemaining <= 0);
        }          
        
        /// <summary>
        /// Applies the gameplay effect spec to self
        /// </summary>
        /// <param name="geSpec">GameplayEffectSpec to apply</param>
        public bool ApplyGameplayEffectSpecToSelf(GameplayEffectSpec geSpec)
        {
            if (geSpec == null)
            {
                return true;
            }
            
            if (!CheckTagRequirementsMet(geSpec))
            {
                return false;
            }
            
            switch (geSpec.GameplayEffect.gameplayEffect.DurationPolicy)
            {
                case EDurationPolicy.HasDuration:
                case EDurationPolicy.Infinite:
                    ApplyDurationalGameplayEffect(geSpec);
                    break;
                case EDurationPolicy.Instant:
                    ApplyInstantGameplayEffect(geSpec);
                    return true;
            }

            // Reset all attributes to 0
            AttributeSystem.ResetAttributeModifiers();
            UpdateAttributeSystem();
            AttributeSystem.UpdateAttributeCurrentValues();     
            
            return true;
        }

        private bool CheckTagRequirementsMet(GameplayEffectSpec geSpec)
        {
            // Build temporary list of all tags currently applied
            var appliedTags = new List<GameplayTagScriptableObject>();
            foreach (var gameplayTag in AppliedGameplayEffects)
            {
                appliedTags.AddRange(gameplayTag.spec.GameplayEffect.gameplayEffectTags.GrantedTags);
            }

            // Every tag in the ApplicationTagRequirements.RequireTags needs to be in the character tags list
            // In other words, if any tag in ApplicationTagRequirements.RequireTags is not present, requirement is not met
            foreach (var gameplayTag in geSpec.GameplayEffect.gameplayEffectTags.ApplicationTagRequirements.RequireTags)
            {
                if (!appliedTags.Contains(gameplayTag))
                {
                    return false;
                }
            }

            // No tag in the ApplicationTagRequirements.IgnoreTags must in the character tags list
            // In other words, if any tag in ApplicationTagRequirements.IgnoreTags is present, requirement is not met
            foreach (var gameplayTag in geSpec.GameplayEffect.gameplayEffectTags.ApplicationTagRequirements.IgnoreTags)
            {
                if (appliedTags.Contains(gameplayTag))
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyInstantGameplayEffect(GameplayEffectSpec spec)
        {
            foreach (var modifier in spec.GameplayEffect.gameplayEffect.Modifiers)
            {
                var magnitude = (modifier.ModifierMagnitude.CalculateMagnitude(spec) * modifier.Multiplier).GetValueOrDefault();
                var attribute = modifier.Attribute;
                AttributeSystem.GetAttributeValue(attribute, out var attributeValue);

                switch (modifier.ModifierOperator)
                {
                    case EAttributeModifier.Add:
                        attributeValue.BaseValue += magnitude;
                        break;
                    case EAttributeModifier.Multiply:
                        attributeValue.BaseValue *= magnitude;
                        break;
                    case EAttributeModifier.Override:
                        attributeValue.BaseValue = magnitude;
                        break;
                }
                this.AttributeSystem.SetAttributeBaseValue(attribute, attributeValue.BaseValue);
            }
        }

        private void ApplyDurationalGameplayEffect(GameplayEffectSpec spec)
        {
            var modifiersToApply = new List<GameplayEffectContainer.ModifierContainer>();
            foreach (var modifier in spec.GameplayEffect.gameplayEffect.Modifiers)
            {
                var magnitude = (modifier.ModifierMagnitude.CalculateMagnitude(spec) * modifier.Multiplier).GetValueOrDefault();
                var attributeModifier = new AttributeModifier();
                switch (modifier.ModifierOperator)
                {
                    case EAttributeModifier.Add:
                        attributeModifier.Add = magnitude;
                        break;
                    case EAttributeModifier.Multiply:
                        attributeModifier.Multiply = magnitude;
                        break;
                    case EAttributeModifier.Override:
                        attributeModifier.Override = magnitude;
                        break;
                }
                modifiersToApply.Add(new GameplayEffectContainer.ModifierContainer() { Attribute = modifier.Attribute, Modifier = attributeModifier });
            }
            AppliedGameplayEffects.Add(new GameplayEffectContainer() { spec = spec, modifiers = modifiersToApply.ToArray() });
        }

        // Should be called whenever a GE is applied.
        private void UpdateAttributeSystem()
        {
            // Set Current Value to Base Value (default position if there are no GE affecting that attribute)
            foreach (var ge in AppliedGameplayEffects)
            {
                var modifiers = ge.modifiers;
                foreach (var modifier in modifiers)
                {
                    AttributeSystem.UpdateAttributeModifiers(modifier.Attribute, modifier.Modifier, out _);
                }
            }
        }
    }
}


namespace AbilitySystem
{
    public class GameplayEffectContainer
    {
        public GameplayEffectSpec spec;
        public ModifierContainer[] modifiers;

        public class ModifierContainer
        {
            public AttributeScriptableObject Attribute;
            public AttributeModifier Modifier;
        }
    }
}