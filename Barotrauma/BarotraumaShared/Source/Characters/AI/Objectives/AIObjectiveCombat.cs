﻿using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using FarseerPhysics;

namespace Barotrauma
{
    class AIObjectiveCombat : AIObjective
    {
        public override string DebugTag => "combat";
        public bool useCoolDown = true;

        const float coolDown = 10.0f;

        public Character Enemy { get; private set; }
        
        private Item _weapon;
        private Item Weapon
        {
            get { return _weapon; }
            set
            {
                _weapon = value;
                _weaponComponent = null;
                if (reloadWeaponObjective != null)
                {
                    if (subObjectives.Contains(reloadWeaponObjective))
                    {
                        subObjectives.Remove(reloadWeaponObjective);
                    }
                    reloadWeaponObjective = null;
                }
            }
        }
        private ItemComponent _weaponComponent;
        private ItemComponent WeaponComponent
        {
            get
            {
                if (Weapon == null) { return null; }
                if (_weaponComponent == null)
                {
                    _weaponComponent =
                        Weapon.GetComponent<RangedWeapon>() as ItemComponent ??
                        Weapon.GetComponent<MeleeWeapon>() as ItemComponent ??
                        Weapon.GetComponent<RepairTool>() as ItemComponent;
                }
                return _weaponComponent;
            }
        }

        public override bool ConcurrentObjectives => true;

        private readonly AIObjectiveFindSafety findSafety;
        private readonly HashSet<RangedWeapon> rangedWeapons = new HashSet<RangedWeapon>();
        private readonly HashSet<MeleeWeapon> meleeWeapons = new HashSet<MeleeWeapon>();
        private readonly HashSet<Item> adHocWeapons = new HashSet<Item>();

        private AIObjectiveContainItem reloadWeaponObjective;
        private AIObjectiveGoTo retreatObjective;
        private AIObjectiveGoTo followTargetObjective;

        private Hull retreatTarget;
        private float coolDownTimer;
        private IEnumerable<FarseerPhysics.Dynamics.Body> myBodies;
        private float aimTimer;

        public enum CombatMode
        {
            Defensive,
            Offensive,
            Retreat
        }

        public CombatMode Mode { get; private set; }

        public AIObjectiveCombat(Character character, Character enemy, CombatMode mode, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            Enemy = enemy;
            coolDownTimer = coolDown;
            findSafety = objectiveManager.GetObjective<AIObjectiveFindSafety>();
            if (findSafety != null)
            {
                findSafety.Priority = 0;
                findSafety.unreachable.Clear();
            }
            Mode = mode;
            if (Enemy == null)
            {
                Mode = CombatMode.Retreat;
            }
        }

        public override float GetPriority() => (Enemy != null && (Enemy.Removed || Enemy.IsDead)) ? 0 : Math.Min(100 * PriorityModifier, 100);

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            if (!(otherObjective is AIObjectiveCombat objective)) return false;
            return objective.Enemy == Enemy;
        }

        public override void OnSelected() => Weapon = null;

        public override bool IsCompleted()
        {
            bool completed = (Enemy != null && (Enemy.Removed || Enemy.IsDead)) || (useCoolDown && coolDownTimer <= 0);
            if (completed)
            {
                if (objectiveManager.CurrentOrder == this && Enemy != null && Enemy.IsDead)
                {
                    character.Speak(TextManager.Get("DialogTargetDown"), null, 3.0f, "targetdown", 30.0f);
                }
                if (Weapon != null)
                {
                    Unequip();
                }
            }
            return completed;
        }

        protected override void Act(float deltaTime)
        {
            if (useCoolDown)
            {
                coolDownTimer -= deltaTime;
            }
            if (abandon) { return; }
            Arm(deltaTime);
            Move();
        }

        private void Arm(float deltaTime)
        {
            switch (Mode)
            {
                case CombatMode.Offensive:
                case CombatMode.Defensive:
                    if (Weapon != null && !character.Inventory.Items.Contains(_weapon) || _weaponComponent != null && !_weaponComponent.HasRequiredContainedItems(false))
                    {
                        Weapon = null;
                    }
                    if (Weapon == null)
                    {
                        Weapon = GetWeapon();
                    }
                    if (Weapon == null)
                    {
                        Mode = CombatMode.Retreat;
                    }
                    else if (Equip())
                    {
                        if (Reload())
                        {
                            Attack(deltaTime);
                        }
                    }
                    break;
                case CombatMode.Retreat:
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private void Move()
        {
            switch (Mode)
            {
                case CombatMode.Offensive:
                    Engage();
                    break;
                case CombatMode.Defensive:
                case CombatMode.Retreat:
                    Retreat();
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private Item GetWeapon()
        {
            rangedWeapons.Clear();
            meleeWeapons.Clear();
            adHocWeapons.Clear();
            Item weapon = null;
            _weaponComponent = null;
            foreach (var item in character.Inventory.Items)
            {
                if (item == null) { continue; }
                foreach (var component in item.Components)
                {
                    if (component is RangedWeapon rw)
                    {
                        if (rw.HasRequiredContainedItems(false))
                        {
                            rangedWeapons.Add(rw);
                        }
                    }
                    else if (component is MeleeWeapon mw)
                    {
                        if (mw.HasRequiredContainedItems(false))
                        {
                            meleeWeapons.Add(mw);
                        }
                    }
                    else
                    {
                        var effects = component.statusEffectLists;
                        if (effects != null)
                        {
                            foreach (var statusEffects in effects.Values)
                            {
                                foreach (var statusEffect in statusEffects)
                                {
                                    if (statusEffect.Afflictions.Any())
                                    {
                                        if (component.HasRequiredContainedItems(false))
                                        {
                                            adHocWeapons.Add(item);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            var rangedWeapon = rangedWeapons.OrderByDescending(w => w.CombatPriority).FirstOrDefault();
            var meleeWeapon = meleeWeapons.OrderByDescending(w => w.CombatPriority).FirstOrDefault();
            if (rangedWeapon != null)
            {
                weapon = rangedWeapon.Item;
            }
            else if (meleeWeapon != null)
            {
                weapon = meleeWeapon.Item;
            }
            if (weapon == null)
            {
                weapon = adHocWeapons.GetRandom(Rand.RandSync.Server);
            }
            return weapon;
        }

        private void Unequip()
        {
            if (character.SelectedItems.Contains(Weapon))
            {
                if (!Weapon.AllowedSlots.Contains(InvSlotType.Any) || !character.Inventory.TryPutItem(Weapon, character, new List<InvSlotType>() { InvSlotType.Any }))
                {
                    Weapon.Drop(character);
                }
            }
        }

        private bool Equip()
        {
            if (!character.SelectedItems.Contains(Weapon))
            {
                var slots = Weapon.AllowedSlots.FindAll(s => s == InvSlotType.LeftHand || s == InvSlotType.RightHand || s == (InvSlotType.LeftHand | InvSlotType.RightHand));
                if (character.Inventory.TryPutItem(Weapon, character, slots))
                {
                    Weapon.Equip(character);
                    aimTimer = Rand.Range(1f, 2f);
                }
                else
                {
                    Mode = CombatMode.Retreat;
                    return false;
                }
            }
            return true;
        }

        private void Retreat()
        {
            if (followTargetObjective != null)
            {
                if (subObjectives.Contains(followTargetObjective))
                {
                    subObjectives.Remove(followTargetObjective);
                }
                followTargetObjective = null;
            }
            if (retreatObjective != null && retreatObjective.Target != retreatTarget)
            {
                retreatObjective = null;
            }
            if (retreatTarget == null || (retreatObjective != null && !retreatObjective.CanBeCompleted))
            {
                retreatTarget = findSafety.FindBestHull(new List<Hull>() { character.CurrentHull });
            }
            TryAddSubObjective(ref retreatObjective, () => new AIObjectiveGoTo(retreatTarget, character, objectiveManager, false, true));
        }

        private void Engage()
        {
            retreatTarget = null;
            if (retreatObjective != null)
            {
                if (subObjectives.Contains(retreatObjective))
                {
                    subObjectives.Remove(retreatObjective);
                }
                retreatObjective = null;
            }
            TryAddSubObjective(ref followTargetObjective,
                constructor: () => new AIObjectiveGoTo(Enemy, character, objectiveManager, repeat: true, getDivingGearIfNeeded: true)
                {
                    AllowGoingOutside = true,
                    IgnoreIfTargetDead = true,
                    CheckVisibility = true,
                    CloseEnough =
                        WeaponComponent is RangedWeapon ? 3 :
                        WeaponComponent is MeleeWeapon mw ? ConvertUnits.ToSimUnits(mw.Range) :
                        WeaponComponent is RepairTool rt ? ConvertUnits.ToSimUnits(rt.Range) : 0.5f
                },
                onAbandon: () =>
                {
                    SteeringManager.Reset();
                    Mode = CombatMode.Retreat;
                });
        }

        private bool Reload()
        {
            if (WeaponComponent != null && WeaponComponent.requiredItems.ContainsKey(RelatedItem.RelationType.Contained))
            {
                var containedItems = Weapon.ContainedItems;
                foreach (RelatedItem requiredItem in WeaponComponent.requiredItems[RelatedItem.RelationType.Contained])
                {
                    Item containedItem = containedItems.FirstOrDefault(it => it.Condition > 0.0f && requiredItem.MatchesItem(it));
                    if (containedItem == null)
                    {
                        TryAddSubObjective(ref reloadWeaponObjective, 
                            constructor: () => new AIObjectiveContainItem(character, requiredItem.Identifiers, Weapon.GetComponent<ItemContainer>(), objectiveManager),
                            onAbandon: () => 
                            {
                                SteeringManager.Reset();
                                Mode = CombatMode.Retreat;
                            });
                    }
                }
            }
            return reloadWeaponObjective == null || reloadWeaponObjective.IsCompleted();
        }

        private void Attack(float deltaTime)
        {
            float squaredDistance = Vector2.DistanceSquared(character.Position, Enemy.Position);
            character.CursorPosition = Enemy.Position;
            float engageDistance = 500;
            if (squaredDistance > engageDistance * engageDistance) { return; }
            bool canSeeTarget = character.CanSeeCharacter(Enemy);
            if (!canSeeTarget && character.CurrentHull != Enemy.CurrentHull) { return; }
            if (Weapon.RequireAimToUse)
            {
                bool isOperatingButtons = false;
                if (SteeringManager == PathSteering)
                {
                    var door = PathSteering.CurrentPath?.CurrentNode?.ConnectedDoor;
                    if (door != null && !door.IsOpen)
                    {
                        isOperatingButtons = door.HasIntegratedButtons || door.Item.GetConnectedComponents<Controller>(true).Any();
                    }
                }
                if (!isOperatingButtons && character.SelectedConstruction == null)
                {
                    character.SetInput(InputType.Aim, false, true);
                }
            }
            bool isFacing = character.AnimController.Dir > 0 && Enemy.WorldPosition.X > character.WorldPosition.X || character.AnimController.Dir < 0 && Enemy.WorldPosition.X < character.WorldPosition.X;
            if (!isFacing)
            {
                aimTimer = Rand.Range(1f, 2f);
            }
            if (aimTimer > 0)
            {
                aimTimer -= deltaTime;
                return;
            }
            if (WeaponComponent is MeleeWeapon meleeWeapon)
            {
                if (squaredDistance <= meleeWeapon.Range * meleeWeapon.Range)
                {
                    character.SetInput(InputType.Shoot, false, true);
                    Weapon.Use(deltaTime, character);
                }
            }
            else
            {
                if (WeaponComponent is RepairTool repairTool)
                {
                    if (squaredDistance > repairTool.Range * repairTool.Range) { return; }
                }
                if (VectorExtensions.Angle(VectorExtensions.Forward(Weapon.body.TransformedRotation), Enemy.Position - character.Position) < MathHelper.PiOver4)
                {
                    if (myBodies == null)
                    {
                        myBodies = character.AnimController.Limbs.Select(l => l.body.FarseerBody);
                    }
                    var collisionCategories = Physics.CollisionCharacter | Physics.CollisionWall;
                    var pickedBody = Submarine.PickBody(character.SimPosition, Enemy.SimPosition, myBodies, collisionCategories);
                    if (pickedBody != null)
                    {
                        Character target = null;
                        if (pickedBody.UserData is Character c)
                        {
                            target = c;
                        }
                        else if (pickedBody.UserData is Limb limb)
                        {
                            target = limb.character;
                        }
                        if (target != null && target == Enemy)
                        {
                            character.SetInput(InputType.Shoot, false, true);
                            Weapon.Use(deltaTime, character);
                            aimTimer = Rand.Range(0.5f, 1f);
                        }
                    }
                }
            }
        }

        //private float CalculateEnemyStrength()
        //{
        //    float enemyStrength = 0;
        //    AttackContext currentContext = character.GetAttackContext();
        //    foreach (Limb limb in Enemy.AnimController.Limbs)
        //    {
        //        if (limb.attack == null) continue;
        //        if (!limb.attack.IsValidContext(currentContext)) { continue; }
        //        if (!limb.attack.IsValidTarget(AttackTarget.Character)) { continue; }
        //        enemyStrength += limb.attack.GetTotalDamage(false);
        //    }
        //    return enemyStrength;
        //}
    }
}
