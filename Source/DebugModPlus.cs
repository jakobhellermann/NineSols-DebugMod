using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using DebugModPlus.Modules;
using DebugModPlus.Modules.Hitbox;
using DebugModPlus.Savestates;
using HarmonyLib;
using MonsterLove.StateMachine;
using NineSolsAPI;
using UnityEngine;

namespace DebugModPlus;

[BepInDependency(NineSolsAPICore.PluginGUID)]
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class DebugModPlus : BaseUnityPlugin {
    public static DebugModPlus Instance = null!;

    private DebugUI debugUI = null!;
    private QuantumConsoleModule quantumConsoleModule = new();

    private Harmony harmony = null!;

    private InfotextModule infotextModule = new();
    public HitboxModule HitboxModule = new();
    public SavestateModule SavestateModule = null!;

    public SpeedrunTimerModule SpeedrunTimerModule = null!;

    private FsmInspectorModule fsmInspectorModule = new();
    public GhostModule GhostModule = null!;

    private ConfigEntry<KeyboardShortcut> configShortcutFsmPickerModifier = null!;
    private Dictionary<KeyboardShortcut, string> configSavestateShortcutsCreate = null!;
    private Dictionary<KeyboardShortcut, string> configSavestateShortcutsLoad = null!;

    private void Awake() {
        Instance = this;
        Log.Init(Logger);
        Log.Info($"Plugin {MyPluginInfo.PLUGIN_GUID} started loading...");

        try {
            harmony = Harmony.CreateAndPatchAll(typeof(DebugModPlus).Assembly);

            var versionPatches = GameVersions.Select(GameVersions.SpeedrunPatch,
                typeof(PatchesSpeedrunPatch),
                typeof(PatchesCurrentPatch));
            harmony.PatchAll(versionPatches);


            Log.Info($"Patched {harmony.GetPatchedMethods().Count()} methods...");

            // config
            var configTimerMode = Config.Bind("SpeedrunTimer", "Timer Mode", TimerMode.Triggers);
            var changeModeShortcut = Config.Bind("SpeedrunTimer Shortcuts", "Cycle Timer Mode", new KeyboardShortcut());
            var resetTimerShortcut = Config.Bind("SpeedrunTimer Shortcuts", "Reset Timer", new KeyboardShortcut());
            var pauseTimerShortcut = Config.Bind("SpeedrunTimer Shortcuts", "Pause Timer", new KeyboardShortcut());
            var setStartpointShortcut =
                Config.Bind("SpeedrunTimer Shortcuts", "Set Startpoint", new KeyboardShortcut());
            var setEndpointShortcut = Config.Bind("SpeedrunTimer Shortcuts", "Set Endpoint", new KeyboardShortcut());
            var clearCheckpointsShortcut =
                Config.Bind("SpeedrunTimer Shortcuts", "Clear Checkpoints", new KeyboardShortcut());

            configShortcutFsmPickerModifier = Config.Bind("Shortcuts",
                "FSM Picker Modifier",
                new KeyboardShortcut(),
                new ConfigDescription(
                    "When this key is pressed and you click on a sprite, it will try to open the FSM inspector for that object"));

            var configTimerRecordGhost = Config.Bind("SpeedrunTimer", "Record Ghost", false);
            var configGhostColorPb = Config.Bind("SpeedrunTimer", "PB Ghost Color", new Color(1f, 0.8f, 0f, 0.5f));
            var configPauseStopsTimer = Config.Bind("SpeedrunTimer", "Pause Timer Stops Speedrun Timer", false);

            var configSavestateFilter = Config.Bind("Savestates",
                "Savestate filter",
                SavestateFilter.Flags | SavestateFilter.Player);

            configSavestateShortcutsCreate = new Dictionary<KeyboardShortcut, string> {
                //{ new KeyboardShortcut(KeyCode.Keypad1, KeyCode.LeftControl), "1" },
                //{ new KeyboardShortcut(KeyCode.Keypad2, KeyCode.LeftControl), "2" },
                //{ new KeyboardShortcut(KeyCode.Keypad3, KeyCode.LeftControl), "3" },
            };
            configSavestateShortcutsLoad = new Dictionary<KeyboardShortcut, string> {
                //{ new KeyboardShortcut(KeyCode.Keypad1), "1" },
                //{ new KeyboardShortcut(KeyCode.Keypad2), "2" },
                //{ new KeyboardShortcut(KeyCode.Keypad3), "3" },
            };

            // module initialization

            SavestateModule = new SavestateModule(
                configSavestateFilter,
                Config.Bind("Savestates",
                    "Save",
                    new KeyboardShortcut(KeyCode.KeypadPlus)
                ),
                Config.Bind("Savestates",
                    "Load",
                    new KeyboardShortcut(KeyCode.KeypadEnter)
                ),
                Config.Bind("Savestates",
                    "Delete",
                    new KeyboardShortcut(KeyCode.KeypadMinus)
                ),
                Config.Bind("Savestates",
                    "Page next",
                    new KeyboardShortcut(KeyCode.RightArrow)
                ),
                Config.Bind("Savestates",
                    "Page prev",
                    new KeyboardShortcut(KeyCode.LeftArrow)
                )
            );

            SpeedrunTimerModule =
                new SpeedrunTimerModule(configTimerMode, configTimerRecordGhost, configPauseStopsTimer);
            GhostModule = new GhostModule(configGhostColorPb);

            SavestateModule.SavestateLoaded += (_, _) => SpeedrunTimerModule.OnSavestateLoaded();
            SavestateModule.SavestateCreated += (_, _) => SpeedrunTimerModule.OnSavestateCreated();

            KeybindManager.Add(this, quantumConsoleModule.ToggleConsole, KeyCode.LeftControl, KeyCode.Period);
            KeybindManager.Add(this, ToggleSettings, KeyCode.LeftControl, KeyCode.Comma);
            KeybindManager.Add(this, () => SpeedrunTimerModule.CycleTimerMode(), () => changeModeShortcut.Value);
            KeybindManager.Add(this, () => SpeedrunTimerModule.ResetTimerUser(), () => resetTimerShortcut.Value);
            KeybindManager.Add(this, () => SpeedrunTimerModule.PauseTimer(), () => pauseTimerShortcut.Value);
            KeybindManager.Add(this, () => SpeedrunTimerModule.SetStartpoint(), () => setStartpointShortcut.Value);
            KeybindManager.Add(this, () => SpeedrunTimerModule.SetEndpoint(), () => setEndpointShortcut.Value);
            KeybindManager.Add(this,
                () => SpeedrunTimerModule.ClearCheckpoints(),
                () => clearCheckpointsShortcut.Value);

            // var recordGhost = Config.Bind("SpeedrunTimer", "Record Ghost", false);
            // KeybindManager.Add(this, () => GhostModule.ToggleRecording(), KeyCode.P);
            // KeybindManager.Add(this, () => GhostModule.Playback(GhostModule.CurrentRecording), KeyCode.O);

            debugUI = gameObject.AddComponent<DebugUI>();
            debugUI.AddBindableMethods(Config, typeof(FreecamModule));
            debugUI.AddBindableMethods(Config, typeof(TimeModule));
            debugUI.AddBindableMethods(Config, typeof(InfotextModule));
            debugUI.AddBindableMethods(Config, typeof(HitboxModule));
            debugUI.AddBindableMethods(Config, typeof(SavestateModule));
            debugUI.AddBindableMethods(Config, typeof(CheatModule));
            // debugUI.AddBindableMethods(Config, typeof(FlagLoggerModule));

            FlagLoggerModule.Awake();

            RCGLifeCycle.DontDestroyForever(gameObject);

            Log.Info($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        } catch (Exception e) {
            Log.Error(e);
        }
    }


    private void ToggleSettings() {
        debugUI.settingsOpen = !debugUI.settingsOpen;
        if (Player.i is not null) {
            // if (settingsOpen) {
            // stateBefore = Player.i.playerInput.fsm.State;
            // Player.i.playerInput.fsm.ChangeState(PlayerInputStateType.Console);
            // } else
            // Player.i.playerInput.fsm.ChangeState(stateBefore);
        }
    }

    private void Update() {
        FreecamModule.Update();
        MapTeleportModule.Update();
        infotextModule.Update();
        SavestateModule.Update();

        var didCreate = false;
        foreach (var binding in configSavestateShortcutsCreate) {
            if (KeybindManager.CheckShortcutOnly(binding.Key)) {
                SavestateModule.TryCreateSavestate(binding.Value);
                didCreate = true;
            }
        }

        if (!didCreate) {
            foreach (var binding in configSavestateShortcutsLoad) {
                if (KeybindManager.CheckShortcutOnly(binding.Key)) {
                    SavestateModule.TryLoadSavestate(binding.Value);
                }
            }
        }


        var canUseFsmPicker = Player.i?.playerInput.fsm.State is not PlayerInputStateType.UI;

        if (canUseFsmPicker && configShortcutFsmPickerModifier.Value.IsPressed()) {
            Cursor.visible = true;
            if (Input.GetMouseButtonDown(0)) {
                TryPickFsm();
            }
        }
    }

    private void TryPickFsm() {
        fsmInspectorModule.Objects.Clear();

        try {
            var mainCamera = CameraManager.Instance.cameraCore.theRealSceneCamera;
            var worldPosition =
                mainCamera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x,
                    Input.mousePosition.y,
                    -mainCamera.transform.position.z));
            worldPosition.z = 0; // Set z to 0 to match the 2D plane
            var visible = PickVisible(worldPosition);

            var stateMachine = visible.Select(sprite =>
                    sprite.GetComponentInParent<StateMachineOwner>()?.gameObject ??
                    sprite.GetComponentInParent<FSMStateMachineRunner>()?.gameObject)
                .Where(x => x)
                .Distinct()
                .FirstOrDefault();
            if (stateMachine)
                fsmInspectorModule.Objects.Add(stateMachine!);
            else
                ToastManager.Toast($"No state machine found at cursor");
        } catch (Exception e) {
            ToastManager.Toast(e);
        }
    }

    // ReSharper disable Unity.PerformanceAnalysis
    private List<GameObject> PickVisible(Vector3 worldPosition) {
        return FindObjectsOfType<SpriteRenderer>().Select(renderer => (renderer.gameObject, renderer.bounds))
            .Concat(FindObjectsOfType<ParticleSystemRenderer>()
                .Select(renderer => (renderer.gameObject, renderer.bounds)))
            .Where(t => t.bounds.Contains(worldPosition))
            .Where(t => {
                var goName = t.gameObject.gameObject.name.ToLower();
                var parentName = t.gameObject.gameObject.transform.parent?.name ?? "";
                return !goName.Contains("light") && !goName.Contains("fade") &&
                       !goName.Contains("glow") && !goName.Contains("attack") &&
                       !parentName.Contains("Vibe") && !parentName.Contains("Skin");
            })
            .Select(x => x.gameObject)
            .ToList();
    }

    private void LateUpdate() {
        try {
            // GhostModule.LateUpdate();
            // SpeedrunTimerModule.LateUpdate();
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    private void OnGUI() {
        try {
            SpeedrunTimerModule.OnGui();
            fsmInspectorModule.OnGui();
            SavestateModule.OnGui();
        } catch (Exception e) {
            Log.Error(e);
        }
    }


    private void OnDestroy() {
        harmony.UnpatchSelf();
        HitboxModule.Unload();
        GhostModule.Unload();
        SpeedrunTimerModule.Destroy();
        infotextModule.Destroy();

        Log.Info($"Plugin {MyPluginInfo.PLUGIN_GUID} unloaded\n\n");
    }
}