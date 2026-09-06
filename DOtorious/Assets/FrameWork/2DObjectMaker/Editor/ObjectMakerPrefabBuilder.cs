using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace JYW.Game.ObjectMaker.Editor
{
    public readonly struct ObjectMakerBuildResult
    {
        public readonly ObjectDefinitionSO Definition;
        public readonly GameObject Prefab;
        public readonly AnimatorController AnimatorController;
        public readonly string DefinitionPath;
        public readonly string PrefabPath;
        public readonly string AnimatorControllerPath;

        public ObjectMakerBuildResult(
            ObjectDefinitionSO definition,
            GameObject prefab,
            AnimatorController animatorController,
            string definitionPath,
            string prefabPath,
            string animatorControllerPath)
        {
            Definition = definition;
            Prefab = prefab;
            AnimatorController = animatorController;
            DefinitionPath = definitionPath;
            PrefabPath = prefabPath;
            AnimatorControllerPath = animatorControllerPath;
        }
    }

    public static class ObjectMakerPrefabBuilder
    {
        public static ObjectMakerBuildResult Make(
            ObjectDefinitionData source,
            ObjectDefinitionSO existingDefinition = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            ObjectMakerPaths.EnsureStructure();
            ObjectMakerPaths.EnsureGeneratedStructure();
            ObjectDefinitionData data = source.Clone();
            data.Sanitize();
            data.previewSprite = null;
            data.UseCommonAnimatorLayout();

            bool createdDefinition = existingDefinition == null;
            ObjectDefinitionData previousDefinitionData = existingDefinition != null
                ? existingDefinition.Data.Clone()
                : null;
            string safeName = ObjectMakerPaths.SafeFileName(data.displayName);
            bool createdAnimatorController;
            string animatorControllerPath;
            AnimatorController animatorController = ResolveAnimatorController(
                existingDefinition,
                safeName,
                out animatorControllerPath,
                out createdAnimatorController);
            EnsureCommonAnimatorLayout(animatorController, data);
            data.animatorController = animatorController;

            string definitionPath;
            ObjectDefinitionSO definition = existingDefinition;
            if (createdDefinition)
            {
                definitionPath = AssetDatabase.GenerateUniqueAssetPath(
                    ObjectMakerPaths.GeneratedDefinitions + "/" + safeName + ".asset");
                definition = ScriptableObject.CreateInstance<ObjectDefinitionSO>();
                definition.ReplaceData(data);
                AssetDatabase.CreateAsset(definition, definitionPath);
            }
            else
            {
                definitionPath = AssetDatabase.GetAssetPath(definition);
                if (string.IsNullOrEmpty(definitionPath))
                    throw new InvalidOperationException("선택한 ObjectDefinitionSO가 프로젝트 에셋이 아닙니다.");
                Undo.RecordObject(definition, "Update Object Definition");
                definition.ReplaceData(data);
                EditorUtility.SetDirty(definition);
            }

            string prefabPath = definition.GeneratedPrefab != null
                ? AssetDatabase.GetAssetPath(definition.GeneratedPrefab)
                : string.Empty;
            bool createdPrefab = string.IsNullOrEmpty(prefabPath);
            if (string.IsNullOrEmpty(prefabPath))
            {
                prefabPath = AssetDatabase.GenerateUniqueAssetPath(
                    (data.kind == ObjectKind.Player
                        ? ObjectMakerPaths.GeneratedPlayerPrefabs
                        : ObjectMakerPaths.GeneratedEnemyPrefabs) + "/" + safeName + ".prefab");
            }

            GameObject root = null;
            bool loadedPrefabContents = false;
            try
            {
                if (createdPrefab)
                {
                    root = new GameObject(data.displayName);
                }
                else
                {
                    root = PrefabUtility.LoadPrefabContents(prefabPath);
                    loadedPrefabContents = true;
                }

                if (root == null)
                    throw new InvalidOperationException(
                        "2DObjectMaker 프리팹을 수정용으로 열지 못했습니다: " + prefabPath);

                ConfigureRoot(root, definition);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (prefab == null)
                    throw new InvalidOperationException("2DObjectMaker 프리팹 저장에 실패했습니다: " + prefabPath);

                definition.SetGeneratedPrefab(prefab);
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(definitionPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(animatorControllerPath, ImportAssetOptions.ForceUpdate);
                return new ObjectMakerBuildResult(
                    definition,
                    prefab,
                    animatorController,
                    definitionPath,
                    prefabPath,
                    animatorControllerPath);
            }
            catch
            {
                if (!createdDefinition && previousDefinitionData != null && definition != null)
                {
                    definition.ReplaceData(previousDefinitionData);
                    EditorUtility.SetDirty(definition);
                }
                if (createdPrefab && !string.IsNullOrEmpty(prefabPath))
                    AssetDatabase.DeleteAsset(prefabPath);
                if (createdDefinition && !string.IsNullOrEmpty(definitionPath))
                    AssetDatabase.DeleteAsset(definitionPath);
                if (createdAnimatorController && !string.IsNullOrEmpty(animatorControllerPath))
                    AssetDatabase.DeleteAsset(animatorControllerPath);
                throw;
            }
            finally
            {
                if (root != null)
                {
                    if (loadedPrefabContents)
                        PrefabUtility.UnloadPrefabContents(root);
                    else
                        UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static AnimatorController ResolveAnimatorController(
            ObjectDefinitionSO existingDefinition,
            string safeName,
            out string controllerPath,
            out bool created)
        {
            AnimatorController controller = existingDefinition != null
                ? existingDefinition.Data.animatorController as AnimatorController
                : null;
            controllerPath = controller != null
                ? AssetDatabase.GetAssetPath(controller)
                : string.Empty;

            if (controller == null ||
                string.IsNullOrEmpty(controllerPath) ||
                !(controllerPath.Replace('\\', '/').StartsWith(
                    ObjectMakerPaths.GeneratedAnimators + "/",
                    StringComparison.Ordinal) ||
                  controllerPath.Replace('\\', '/').StartsWith(
                    "Assets/2DObjectMaker/",
                    StringComparison.Ordinal)))
            {
                controllerPath = AssetDatabase.GenerateUniqueAssetPath(
                    ObjectMakerPaths.GeneratedAnimators + "/" + safeName + ".controller");
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                if (controller == null)
                    throw new InvalidOperationException(
                        "2DObjectMaker 공용 Animator 생성에 실패했습니다: " + controllerPath);
                created = true;
                return controller;
            }

            created = false;
            return controller;
        }

        private static void EnsureCommonAnimatorLayout(
            AnimatorController controller,
            ObjectDefinitionData data)
        {
            if (controller.layers == null || controller.layers.Length == 0)
                controller.AddLayer("Base Layer");

            Dictionary<string, Motion> preservedMotions = CaptureStateMotions(
                controller.layers[0].stateMachine);
            while (controller.layers.Length > 1)
                controller.RemoveLayer(controller.layers.Length - 1);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            ClearGeneratedGraph(controller, stateMachine);
            EnsureParameter(controller, ObjectAnimatorGraph2D.HitParameter,
                AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, ObjectAnimatorGraph2D.DeadParameter,
                AnimatorControllerParameterType.Trigger);

            AnimatorState idle = AddState(
                stateMachine,
                ObjectCommonAnimator.Idle,
                new Vector3(220f, 40f),
                preservedMotions);
            AnimatorState hit = AddState(
                stateMachine,
                ObjectCommonAnimator.Hit,
                new Vector3(440f, 140f),
                preservedMotions);
            AnimatorState dead = AddState(
                stateMachine,
                ObjectCommonAnimator.Dead,
                new Vector3(440f, 240f),
                preservedMotions);
            stateMachine.defaultState = idle;
            AddAnyTriggerTransition(
                stateMachine,
                dead,
                ObjectAnimatorGraph2D.DeadParameter,
                "Auto_Any_Dead");
            AddAnyTriggerTransition(
                stateMachine,
                hit,
                ObjectAnimatorGraph2D.HitParameter,
                "Auto_Any_Hit");
            AddExitTransition(
                hit,
                idle,
                "Auto_Hit_Idle");

            if (data.kind == ObjectKind.Player)
                BuildPlayerAnimatorLayout(
                    controller,
                    stateMachine,
                    idle,
                    data,
                    preservedMotions);
            else if (data.kind == ObjectKind.Monster)
                BuildEnemyAnimatorLayout(
                    controller,
                    stateMachine,
                    idle,
                    data.ai,
                    preservedMotions);

            EditorUtility.SetDirty(stateMachine);
            EditorUtility.SetDirty(controller);
        }

        private static void BuildPlayerAnimatorLayout(
            AnimatorController controller,
            AnimatorStateMachine stateMachine,
            AnimatorState idle,
            ObjectDefinitionData data,
            Dictionary<string, Motion> preservedMotions)
        {
            ObjectPlayerSettings settings = data.player;
            if (settings == null)
                return;

            bool hasMoveKey = data.core.moveSpeed > 0f &&
                              (settings.moveLeftKey != UnityEngine.InputSystem.Key.None ||
                               settings.moveRightKey != UnityEngine.InputSystem.Key.None);
            if (hasMoveKey)
            {
                EnsureParameter(controller, ObjectAnimatorGraph2D.SpeedParameter,
                    AnimatorControllerParameterType.Float);
                bool hasRunKey = settings.runKey != UnityEngine.InputSystem.Key.None;
                if (hasRunKey)
                {
                    EnsureParameter(controller, ObjectAnimatorGraph2D.RunningParameter,
                        AnimatorControllerParameterType.Bool);
                }

                AnimatorState walk = AddState(
                    stateMachine,
                    ObjectCommonAnimator.Walk,
                    new Vector3(220f, 140f),
                    preservedMotions,
                    ObjectCommonAnimator.Run);

                AnimatorStateTransition idleToWalk = AddTransition(
                    idle, walk, "Auto_Idle_Walk");
                idleToWalk.AddCondition(AnimatorConditionMode.Greater, 0.01f,
                    ObjectAnimatorGraph2D.SpeedParameter);
                if (hasRunKey)
                {
                    idleToWalk.AddCondition(AnimatorConditionMode.IfNot, 0f,
                        ObjectAnimatorGraph2D.RunningParameter);
                }
                AddFloatTransition(walk, idle, ObjectAnimatorGraph2D.SpeedParameter,
                    AnimatorConditionMode.Less, 0.01f, "Auto_Walk_Idle");

                if (hasRunKey)
                {
                    AnimatorState run = AddState(
                        stateMachine,
                        ObjectCommonAnimator.Run,
                        new Vector3(220f, 240f),
                        preservedMotions,
                        ObjectCommonAnimator.Walk);
                    AnimatorStateTransition idleToRun = AddTransition(
                        idle, run, "Auto_Idle_Run");
                    idleToRun.AddCondition(AnimatorConditionMode.Greater, 0.01f,
                        ObjectAnimatorGraph2D.SpeedParameter);
                    idleToRun.AddCondition(AnimatorConditionMode.If, 0f,
                        ObjectAnimatorGraph2D.RunningParameter);
                    AddFloatTransition(run, idle, ObjectAnimatorGraph2D.SpeedParameter,
                        AnimatorConditionMode.Less, 0.01f, "Auto_Run_Idle");
                    AddBoolTransition(walk, run, ObjectAnimatorGraph2D.RunningParameter,
                        true, "Auto_Walk_Run");
                    AddBoolTransition(run, walk, ObjectAnimatorGraph2D.RunningParameter,
                        false, "Auto_Run_Walk");
                }
            }

            bool hasJumpKey = settings.jumpKey != UnityEngine.InputSystem.Key.None &&
                              settings.jumpForce > 0f;
            if (hasJumpKey)
            {
                EnsureParameter(controller, ObjectAnimatorGraph2D.GroundedParameter,
                    AnimatorControllerParameterType.Bool);
                EnsureParameter(controller, ObjectAnimatorGraph2D.JumpParameter,
                    AnimatorControllerParameterType.Trigger);
                AnimatorState jump = AddState(
                    stateMachine,
                    ObjectCommonAnimator.Jump,
                    new Vector3(440f, 40f),
                    preservedMotions);
                AddAnyTriggerTransition(stateMachine, jump,
                    ObjectAnimatorGraph2D.JumpParameter, "Auto_Any_Jump");
                AddBoolTransition(jump, idle, ObjectAnimatorGraph2D.GroundedParameter,
                    true, "Auto_Jump_Idle");
            }

            bool hasMappedAttack = false;
            if (settings.attacks != null)
            {
                for (int i = 0; i < settings.attacks.Count; i++)
                {
                    ObjectPlayerAttack attack = settings.attacks[i];
                    if (attack != null && attack.key != UnityEngine.InputSystem.Key.None)
                    {
                        hasMappedAttack = true;
                        break;
                    }
                }
            }

            if (!hasMappedAttack)
                return;

            EnsureParameter(controller, ObjectAnimatorGraph2D.AttackingParameter,
                AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, ObjectAnimatorGraph2D.PlayerAttackParameter,
                AnimatorControllerParameterType.Int);
            EnsureParameter(controller, ObjectAnimatorGraph2D.PlayerComboParameter,
                AnimatorControllerParameterType.Int);

            for (int attackIndex = 0; attackIndex < settings.attacks.Count; attackIndex++)
            {
                ObjectPlayerAttack attack = settings.attacks[attackIndex];
                if (attack == null || attack.key == UnityEngine.InputSystem.Key.None)
                    continue;

                AnimatorState previous = null;
                for (int comboIndex = 0; comboIndex < attack.comboSteps; comboIndex++)
                {
                    string stateName = ObjectAnimatorGraph2D.PlayerAttackState(
                        attackIndex, comboIndex);
                    AnimatorState state = AddState(
                        stateMachine,
                        stateName,
                        new Vector3(700f + comboIndex * 230f, 40f + attackIndex * 150f),
                        preservedMotions,
                        comboIndex == 0 ? ObjectCommonAnimator.Attack : null);

                    if (comboIndex == 0)
                    {
                        AnimatorStateTransition entry = AddAnyTransition(
                            stateMachine,
                            state,
                            "Auto_Any_" + stateName);
                        entry.AddCondition(AnimatorConditionMode.If, 0f,
                            ObjectAnimatorGraph2D.AttackingParameter);
                        entry.AddCondition(AnimatorConditionMode.Equals, attackIndex + 1,
                            ObjectAnimatorGraph2D.PlayerAttackParameter);
                        entry.AddCondition(AnimatorConditionMode.Equals, 1,
                            ObjectAnimatorGraph2D.PlayerComboParameter);
                    }
                    else
                    {
                        AnimatorStateTransition combo = AddTransition(
                            previous,
                            state,
                            "Auto_Combo_" + stateName);
                        combo.AddCondition(AnimatorConditionMode.If, 0f,
                            ObjectAnimatorGraph2D.AttackingParameter);
                        combo.AddCondition(AnimatorConditionMode.Equals, attackIndex + 1,
                            ObjectAnimatorGraph2D.PlayerAttackParameter);
                        combo.AddCondition(AnimatorConditionMode.Equals, comboIndex + 1,
                            ObjectAnimatorGraph2D.PlayerComboParameter);
                    }
                    AddBoolTransition(state, idle,
                        ObjectAnimatorGraph2D.AttackingParameter,
                        false,
                        "Auto_" + stateName + "_Idle");
                    previous = state;
                }
            }
        }

        private static void BuildEnemyAnimatorLayout(
            AnimatorController controller,
            AnimatorStateMachine stateMachine,
            AnimatorState idle,
            ObjectAIProgram program,
            Dictionary<string, Motion> preservedMotions)
        {
            if (program == null || program.rules == null)
                return;

            bool hasRuleState = false;
            bool hasAttackRule = false;
            bool hasMovementRule = false;
            for (int i = 0; i < program.rules.Count; i++)
            {
                ObjectAIRule rule = program.rules[i];
                if (rule == null || !rule.enabled || rule.action == ObjectAIAction.None)
                    continue;
                hasRuleState = true;
                hasAttackRule |= ObjectBehaviorCatalog.IsAttackAction(rule.action);
                hasMovementRule |= ObjectBehaviorCatalog.IsMovementAction(rule.action);
            }

            if (!hasRuleState)
                return;
            EnsureParameter(controller, ObjectAnimatorGraph2D.EnemyRuleParameter,
                AnimatorControllerParameterType.Int);
            if (hasAttackRule)
            {
                EnsureParameter(controller, ObjectAnimatorGraph2D.EnemyAttackPhaseParameter,
                    AnimatorControllerParameterType.Int);
            }
            if (hasMovementRule)
            {
                EnsureParameter(controller, ObjectAnimatorGraph2D.SpeedParameter,
                    AnimatorControllerParameterType.Float);
            }

            for (int ruleIndex = 0; ruleIndex < program.rules.Count; ruleIndex++)
            {
                ObjectAIRule rule = program.rules[ruleIndex];
                if (rule == null || !rule.enabled || rule.action == ObjectAIAction.None)
                    continue;

                if (ObjectBehaviorCatalog.IsAttackAction(rule.action))
                {
                    AnimatorState prepare = AddState(
                        stateMachine,
                        ObjectAnimatorGraph2D.EnemyRuleAttackState(
                            ruleIndex, rule.action, ObjectMotionCondition.AttackPrepare),
                        new Vector3(700f, 40f + ruleIndex * 150f),
                        preservedMotions,
                        ObjectCommonAnimator.Attack);
                    AnimatorState active = AddState(
                        stateMachine,
                        ObjectAnimatorGraph2D.EnemyRuleAttackState(
                            ruleIndex, rule.action, ObjectMotionCondition.Attack),
                        new Vector3(930f, 40f + ruleIndex * 150f),
                        preservedMotions,
                        ObjectCommonAnimator.Attack);
                    AnimatorState recover = AddState(
                        stateMachine,
                        ObjectAnimatorGraph2D.EnemyRuleAttackState(
                            ruleIndex, rule.action, ObjectMotionCondition.AttackRecover),
                        new Vector3(1160f, 40f + ruleIndex * 150f),
                        preservedMotions,
                        ObjectCommonAnimator.Attack);

                    AnimatorStateTransition entry = AddAnyTransition(
                        stateMachine,
                        prepare,
                        "Auto_Any_Enemy_Rule_" + ruleIndex);
                    entry.AddCondition(AnimatorConditionMode.Equals, ruleIndex + 1,
                        ObjectAnimatorGraph2D.EnemyRuleParameter);
                    entry.AddCondition(AnimatorConditionMode.Equals,
                        (int)ObjectEnemyAnimatorPhase.Prepare,
                        ObjectAnimatorGraph2D.EnemyAttackPhaseParameter);

                    AnimatorStateTransition toActive = AddTransition(
                        prepare, active, "Auto_Enemy_Prepare_" + ruleIndex);
                    toActive.AddCondition(AnimatorConditionMode.Equals,
                        (int)ObjectEnemyAnimatorPhase.Active,
                        ObjectAnimatorGraph2D.EnemyAttackPhaseParameter);
                    AnimatorStateTransition toRecover = AddTransition(
                        active, recover, "Auto_Enemy_Active_" + ruleIndex);
                    toRecover.AddCondition(AnimatorConditionMode.Equals,
                        (int)ObjectEnemyAnimatorPhase.Recover,
                        ObjectAnimatorGraph2D.EnemyAttackPhaseParameter);
                    AddIntTransition(recover, idle,
                        ObjectAnimatorGraph2D.EnemyAttackPhaseParameter,
                        (int)ObjectEnemyAnimatorPhase.None,
                        "Auto_Enemy_Recover_" + ruleIndex);
                    AddIntTransition(prepare, idle,
                        ObjectAnimatorGraph2D.EnemyRuleParameter,
                        0,
                        "Auto_Enemy_Prepare_Cancel_" + ruleIndex);
                    AddIntTransition(active, idle,
                        ObjectAnimatorGraph2D.EnemyRuleParameter,
                        0,
                        "Auto_Enemy_Active_Cancel_" + ruleIndex);
                }
                else
                {
                    AnimatorState state = AddState(
                        stateMachine,
                        ObjectAnimatorGraph2D.EnemyRuleState(ruleIndex, rule.action),
                        new Vector3(700f, 40f + ruleIndex * 115f),
                        preservedMotions,
                        EnemyFallbackState(rule.action));
                    AnimatorStateTransition entry = AddAnyTransition(
                        stateMachine,
                        state,
                        "Auto_Any_Enemy_Rule_" + ruleIndex);
                    entry.AddCondition(AnimatorConditionMode.Equals, ruleIndex + 1,
                        ObjectAnimatorGraph2D.EnemyRuleParameter);
                    AddIntTransition(state, idle,
                        ObjectAnimatorGraph2D.EnemyRuleParameter,
                        0,
                        "Auto_Enemy_Rule_Idle_" + ruleIndex);
                }
            }
        }

        private static string EnemyFallbackState(ObjectAIAction action)
        {
            if (action == ObjectAIAction.Jump)
                return ObjectCommonAnimator.Jump;
            if (action == ObjectAIAction.FollowTarget ||
                action == ObjectAIAction.FollowDamageSource ||
                action == ObjectAIAction.KeepDistance ||
                action == ObjectAIAction.FleeTarget)
                return ObjectCommonAnimator.Run;
            if (ObjectBehaviorCatalog.IsMovementAction(action))
                return ObjectCommonAnimator.Walk;
            return ObjectCommonAnimator.Idle;
        }

        private static Dictionary<string, Motion> CaptureStateMotions(
            AnimatorStateMachine stateMachine)
        {
            var motions = new Dictionary<string, Motion>(StringComparer.Ordinal);
            ChildAnimatorState[] states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState state = states[i].state;
                if (state != null && state.motion != null && !motions.ContainsKey(state.name))
                    motions.Add(state.name, state.motion);
            }
            return motions;
        }

        private static void ClearGeneratedGraph(
            AnimatorController controller,
            AnimatorStateMachine stateMachine)
        {
            AnimatorStateTransition[] anyTransitions = stateMachine.anyStateTransitions;
            for (int i = anyTransitions.Length - 1; i >= 0; i--)
                stateMachine.RemoveAnyStateTransition(anyTransitions[i]);

            ChildAnimatorState[] states = stateMachine.states;
            for (int i = states.Length - 1; i >= 0; i--)
                stateMachine.RemoveState(states[i].state);

            ChildAnimatorStateMachine[] childMachines = stateMachine.stateMachines;
            for (int i = childMachines.Length - 1; i >= 0; i--)
                stateMachine.RemoveStateMachine(childMachines[i].stateMachine);

            for (int i = controller.parameters.Length - 1; i >= 0; i--)
                controller.RemoveParameter(i);
        }

        private static AnimatorState AddState(
            AnimatorStateMachine stateMachine,
            string name,
            Vector3 position,
            Dictionary<string, Motion> preservedMotions,
            string fallbackName = null)
        {
            AnimatorState state = stateMachine.AddState(name, position);
            Motion motion;
            if (preservedMotions.TryGetValue(name, out motion) ||
                (!string.IsNullOrEmpty(fallbackName) &&
                 preservedMotions.TryGetValue(fallbackName, out motion)))
            {
                state.motion = motion;
            }
            return state;
        }

        private static void EnsureParameter(
            AnimatorController controller,
            string name,
            AnimatorControllerParameterType type)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!string.Equals(parameters[i].name, name, StringComparison.Ordinal))
                    continue;
                if (parameters[i].type == type)
                    return;
                controller.RemoveParameter(i);
                break;
            }
            controller.AddParameter(name, type);
        }

        private static AnimatorStateTransition AddAnyTransition(
            AnimatorStateMachine stateMachine,
            AnimatorState destination,
            string transitionName)
        {
            AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(destination);
            ConfigureTransition(transition, destination, transitionName);
            return transition;
        }

        private static void AddAnyTriggerTransition(
            AnimatorStateMachine stateMachine,
            AnimatorState destination,
            string parameter,
            string transitionName)
        {
            AnimatorStateTransition transition = AddAnyTransition(
                stateMachine, destination, transitionName);
            transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
        }

        private static AnimatorStateTransition AddTransition(
            AnimatorState source,
            AnimatorState destination,
            string transitionName)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            ConfigureTransition(transition, destination, transitionName);
            return transition;
        }

        private static void ConfigureTransition(
            AnimatorStateTransition transition,
            AnimatorState destination,
            string transitionName)
        {
            transition.name = transitionName;
            transition.destinationState = destination;
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.05f;
            transition.canTransitionToSelf = false;
            transition.conditions = Array.Empty<AnimatorCondition>();
        }

        private static void AddExitTransition(
            AnimatorState source,
            AnimatorState destination,
            string transitionName)
        {
            AnimatorStateTransition transition = source.AddTransition(destination);
            transition.name = transitionName;
            transition.destinationState = destination;
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.hasFixedDuration = true;
            transition.duration = 0.05f;
            transition.conditions = Array.Empty<AnimatorCondition>();
        }

        private static void AddBoolTransition(
            AnimatorState source,
            AnimatorState destination,
            string parameter,
            bool expected,
            string transitionName)
        {
            AnimatorStateTransition transition = AddTransition(
                source, destination, transitionName);
            transition.AddCondition(
                expected ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                0f,
                parameter);
        }

        private static void AddFloatTransition(
            AnimatorState source,
            AnimatorState destination,
            string parameter,
            AnimatorConditionMode mode,
            float threshold,
            string transitionName)
        {
            AnimatorStateTransition transition = AddTransition(
                source, destination, transitionName);
            transition.AddCondition(mode, threshold, parameter);
        }

        private static void AddIntTransition(
            AnimatorState source,
            AnimatorState destination,
            string parameter,
            int expected,
            string transitionName)
        {
            AnimatorStateTransition transition = AddTransition(
                source, destination, transitionName);
            transition.AddCondition(AnimatorConditionMode.Equals, expected, parameter);
        }

        private static void ConfigureRoot(GameObject root, ObjectDefinitionSO definition)
        {
            ObjectDefinitionData data = definition.Data;
            root.name = data.displayName;

            Rigidbody2D body = root.GetComponent<Rigidbody2D>();
            if (body == null)
                body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = data.kind == ObjectKind.Player || data.movement.groundWalker
                ? 1f
                : 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            Collider2D bodyCollider = root.GetComponent<Collider2D>();
            bool createdCollider = bodyCollider == null;
            if (createdCollider)
                bodyCollider = root.AddComponent<CapsuleCollider2D>();

            ObjectActor2D actor = root.GetComponent<ObjectActor2D>();
            if (actor == null)
                actor = root.AddComponent<ObjectActor2D>();

            ObjectAudioController2D audio = root.GetComponent<ObjectAudioController2D>();
            if (audio == null)
                audio = root.AddComponent<ObjectAudioController2D>();
            audio.Configure();

            SpriteRenderer spriteRenderer = FindPrimarySpriteRenderer(root, actor);
            if (spriteRenderer == null)
            {
                Transform visual = root.transform.Find("Visual");
                if (visual == null)
                {
                    var visualObject = new GameObject("Visual");
                    visualObject.transform.SetParent(root.transform, false);
                    visual = visualObject.transform;
                }
                spriteRenderer = visual.GetComponent<SpriteRenderer>();
                if (spriteRenderer == null)
                    spriteRenderer = visual.gameObject.AddComponent<SpriteRenderer>();
            }

            Animator animator = spriteRenderer.GetComponent<Animator>();
            if (animator == null)
                animator = spriteRenderer.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = data.animatorController;
            animator.applyRootMotion = false;

            Animator previousAnimator = actor.Animator;
            if (previousAnimator != null && previousAnimator != animator &&
                string.Equals(previousAnimator.gameObject.name, "Visual", StringComparison.Ordinal))
            {
                SpriteRenderer previousRenderer = previousAnimator.GetComponent<SpriteRenderer>();
                if (previousRenderer == null || previousRenderer.sprite == null)
                    UnityEngine.Object.DestroyImmediate(previousAnimator);
            }

            Vector2 visualSize = spriteRenderer.sprite != null
                ? spriteRenderer.sprite.bounds.size
                : new Vector2(1f, 1.8f);
            visualSize.x = Mathf.Max(0.1f, visualSize.x);
            visualSize.y = Mathf.Max(0.1f, visualSize.y);

            if (createdCollider)
            {
                var capsule = bodyCollider as CapsuleCollider2D;
                if (capsule != null)
                {
                    capsule.direction = CapsuleDirection2D.Vertical;
                    capsule.size = visualSize;
                }
            }

            Transform groundProbe = root.transform.Find("GroundProbe");
            if (groundProbe == null)
            {
                var groundProbeObject = new GameObject("GroundProbe");
                groundProbeObject.transform.SetParent(root.transform, false);
                groundProbeObject.transform.localPosition =
                    new Vector3(0f, -visualSize.y * 0.5f, 0f);
                groundProbe = groundProbeObject.transform;
            }

            actor.Configure(
                definition,
                body,
                bodyCollider,
                spriteRenderer.transform,
                spriteRenderer,
                animator);

            ObjectFxController2D effects = root.GetComponent<ObjectFxController2D>();
            if (effects == null)
                effects = root.AddComponent<ObjectFxController2D>();
            effects.Configure(actor, spriteRenderer);

            if (data.kind == ObjectKind.Monster)
            {
                ObjectPlayerController2D player = root.GetComponent<ObjectPlayerController2D>();
                if (player != null)
                    UnityEngine.Object.DestroyImmediate(player);
                ObjectMonsterBrain2D brain = root.GetComponent<ObjectMonsterBrain2D>();
                if (brain == null)
                    brain = root.AddComponent<ObjectMonsterBrain2D>();
                brain.Configure(actor, body, bodyCollider, groundProbe);
            }
            else if (data.kind == ObjectKind.Player)
            {
                ObjectMonsterBrain2D brain = root.GetComponent<ObjectMonsterBrain2D>();
                if (brain != null)
                    UnityEngine.Object.DestroyImmediate(brain);
                ObjectPlayerController2D player = root.GetComponent<ObjectPlayerController2D>();
                if (player == null)
                    player = root.AddComponent<ObjectPlayerController2D>();
                player.Configure(actor, body, bodyCollider, groundProbe);
            }
            else
            {
                ObjectMonsterBrain2D brain = root.GetComponent<ObjectMonsterBrain2D>();
                if (brain != null)
                    UnityEngine.Object.DestroyImmediate(brain);
                ObjectPlayerController2D player = root.GetComponent<ObjectPlayerController2D>();
                if (player != null)
                    UnityEngine.Object.DestroyImmediate(player);
            }
        }

        private static SpriteRenderer FindPrimarySpriteRenderer(
            GameObject root,
            ObjectActor2D actor)
        {
            SpriteRenderer configured = actor != null ? actor.SpriteRenderer : null;
            if (configured != null && configured.sprite != null)
                return configured;

            SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].sprite != null)
                    return renderers[i];
            }

            if (configured != null)
                return configured;
            return renderers.Length > 0 ? renderers[0] : null;
        }
    }
}
