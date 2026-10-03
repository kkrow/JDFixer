using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.FloatingScreen;
using BeatSaberMarkupLanguage.Parser;
using JDFixer.Interfaces;
using JDFixer.Managers;
using UnityEngine;
using UnityEngine.UI;
using Zenject;
using BSMLFloatingScreen = BeatSaberMarkupLanguage.FloatingScreen.FloatingScreen;

namespace JDFixer.UI
{
    // Small floating window (bottom center, in front of the player) in the menu:
    //  - quick +/- adjust of the reaction time
    //  - saved reaction time presets (click to apply, "+ Save" stores the current value, "Delete" removes presets)
    //  - JDFixer on/off
    // Also hosts the "JDFixer RT Window" page in Mod Settings (window options + Max Reaction Time).
    internal sealed class QuickRTUI : IInitializable, IDisposable, INotifyPropertyChanged, IBeatmapInfoUpdater
    {
        internal static QuickRTUI Instance { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private const int MaxPresets = 8;
        private const float WindowWidth = 64f;
        private const float WindowHeight = 34f;

        private const string WindowResource = "JDFixer.UI.BSML.quickRT.bsml";
        private const string SettingsResource = "JDFixer.UI.BSML.quickRTSettings.bsml";

        private BeatmapInfo _selectedBeatmap = BeatmapInfo.Empty;
        private BSMLFloatingScreen _screen;
        private bool _deleteMode = false;

        // Root panel of the window (id="panel" in quickRT.bsml); gets an invisible raycast blocker so empty areas do not click through
        [UIObject("panel")]
        private GameObject _panel;


        private QuickRTUI()
        {
            Instance = this;
        }


        public void Initialize()
        {
            BeatSaberMarkupLanguage.Settings.BSMLSettings.Instance.AddSettingsMenu("JDFixer RT Window", SettingsResource, this);

            CreateWindow();
        }

        public void Dispose()
        {
            if (BeatSaberMarkupLanguage.Settings.BSMLSettings.Instance != null)
            {
                BeatSaberMarkupLanguage.Settings.BSMLSettings.Instance.RemoveSettingsMenu(this);
            }

            DestroyWindow();
            PluginConfig.Instance.Changed();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void BeatmapInfoUpdated(BeatmapInfo beatmapInfo)
        {
            _selectedBeatmap = beatmapInfo;
            Notify_Window();
        }


        //=============================================================================================
        // Window

        private void CreateWindow()
        {
            if (_screen != null || PluginConfig.Instance.quick_rt_enabled == false)
            {
                return;
            }

            var cfg = PluginConfig.Instance;

            Vector3 position = new Vector3(cfg.quick_rt_pos_x, cfg.quick_rt_pos_y, cfg.quick_rt_pos_z);
            Quaternion rotation = Quaternion.Euler(cfg.quick_rt_rot_x, cfg.quick_rt_rot_y, cfg.quick_rt_rot_z);

            _screen = BSMLFloatingScreen.CreateFloatingScreen(new Vector2(WindowWidth, WindowHeight), cfg.quick_rt_unlocked, position, rotation, 0f, false);
            _screen.gameObject.name = "JDFixerQuickRT";
            _screen.HandleSide = BSMLFloatingScreen.Side.Bottom;
            _screen.HandleReleased += OnHandleReleased;

            Remove_CurvedCanvasSettings();

            float scale = Mathf.Clamp(cfg.quick_rt_scale, 0.3f, 2f);
            _screen.transform.localScale = new Vector3(0.02f * scale, 0.02f * scale, 0.02f * scale);

            BeatSaberMarkupLanguage.BSMLParser.Instance.Parse(BeatSaberMarkupLanguage.Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), WindowResource), _screen.gameObject, this);

            Add_Raycast_Blocker();
        }

        // BSML puts an HMUI.CurvedCanvasSettings on the floating screen and calls SetRadius(0) because we ask for
        // a flat screen. With a radius of 0, CurvedCanvasSettings.TransformPointFromCanvasTo3D divides x by that
        // radius, so the panel's vertices turn into NaN/Inf and the broken mesh bleeds into the shared UI canvas:
        // elements of other mods end up displaced and tilted. The component has no sprite here, so it draws nothing
        // anyway - removing it is safe and keeps our NaN out of the geometry.
        private void Remove_CurvedCanvasSettings()
        {
            var curved = _screen.GetComponent<HMUI.CurvedCanvasSettings>();

            if (curved != null)
            {
                UnityEngine.Object.Destroy(curved);
            }
        }

        private void DestroyWindow()
        {
            if (_screen == null)
            {
                return;
            }

            _screen.HandleReleased -= OnHandleReleased;
            UnityEngine.Object.Destroy(_screen.gameObject);
            _screen = null;
            _panel = null;
        }

        // The panel background is not a raycast target, so the VR pointer passes through the gaps between buttons
        // and clicks whatever is behind the window. An invisible full-size Image under the buttons (first sibling,
        // so every button still wins the raycast) makes the whole panel solid for the pointer.
        private void Add_Raycast_Blocker()
        {
            if (_panel == null)
            {
                return;
            }

            GameObject blocker = new GameObject("JDFixerRaycastBlocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement))
            {
                layer = 5
            };

            blocker.transform.SetParent(_panel.transform, false);
            blocker.transform.SetAsFirstSibling();

            RectTransform rect = (RectTransform)blocker.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            blocker.GetComponent<LayoutElement>().ignoreLayout = true;

            Image image = blocker.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;

            // Also make the panel background itself solid
            Image background = _panel.GetComponent<Image>();
            if (background != null)
            {
                background.raycastTarget = true;
            }
        }

        private void OnHandleReleased(object sender, FloatingScreenHandleEventArgs e)
        {
            var cfg = PluginConfig.Instance;
            Vector3 euler = e.Rotation.eulerAngles;

            cfg.quick_rt_pos_x = e.Position.x;
            cfg.quick_rt_pos_y = e.Position.y;
            cfg.quick_rt_pos_z = e.Position.z;

            cfg.quick_rt_rot_x = euler.x;
            cfg.quick_rt_rot_y = euler.y;
            cfg.quick_rt_rot_z = euler.z;

            cfg.Changed();
        }

        private void Reset_Window_Position()
        {
            var cfg = PluginConfig.Instance;

            cfg.quick_rt_pos_x = 0f;
            cfg.quick_rt_pos_y = 0.3f;
            cfg.quick_rt_pos_z = 1.8f;
            cfg.quick_rt_rot_x = 23f;
            cfg.quick_rt_rot_y = 0f;
            cfg.quick_rt_rot_z = 0f;
            cfg.Changed();

            if (_screen != null)
            {
                _screen.ScreenPosition = new Vector3(cfg.quick_rt_pos_x, cfg.quick_rt_pos_y, cfg.quick_rt_pos_z);
                _screen.ScreenRotation = Quaternion.Euler(cfg.quick_rt_rot_x, cfg.quick_rt_rot_y, cfg.quick_rt_rot_z);
            }
        }


        //=============================================================================================
        // Reaction time logic

        private static float Clamp_RT(float rt)
        {
            float min = PluginConfig.Instance.minReactionTime;
            float max = PluginConfig.Instance.maxReactionTime;

            if (max < min)
            {
                max = min;
            }

            return Mathf.Clamp(rt, min, max);
        }

        // Current reaction time in ms. In JD mode it is derived from the selected map's NJS.
        private float Get_Current_RT()
        {
            var cfg = PluginConfig.Instance;

            if (cfg.slider_setting == 1)
            {
                return cfg.reactionTime;
            }

            float rt = BeatmapUtils.Calculate_ReactionTime_Setpoint_Float(cfg.jumpDistance, _selectedBeatmap.NJS);

            // No map selected (NJS unknown): fall back to the last reaction time
            return rt > 0f ? rt : cfg.reactionTime;
        }

        // Switches JDFixer to the reaction time slider and sets the value
        private void Set_RT(float rt)
        {
            var cfg = PluginConfig.Instance;

            cfg.slider_setting = 1;
            cfg.reactionTime = Clamp_RT(Mathf.Round(rt));

            Notify_Other_UIs();
            Notify_Window();
        }

        private void Nudge_RT(float delta)
        {
            Set_RT(Get_Current_RT() + delta);
        }

        // Called by the mod's own Enabled checkbox so the ON / OFF text follows it
        internal static void ExternalEnabledRefresh()
        {
            if (Instance != null)
            {
                Instance.Notify_Window();
            }
        }


        private static void Notify_Other_Enabled()
        {
            if (ModifierUI.Instance != null)
            {
                ModifierUI.Instance.ExternalEnabledRefresh();
            }

            if (LegacyModifierUI.Instance != null)
            {
                LegacyModifierUI.Instance.ExternalEnabledRefresh();
            }

            if (CustomOnlineUI.Instance != null)
            {
                CustomOnlineUI.Instance.ExternalEnabledRefresh();
            }
        }


        private static void Notify_Other_UIs()
        {
            if (ModifierUI.Instance != null)
            {
                ModifierUI.Instance.ExternalRefresh();
            }

            if (LegacyModifierUI.Instance != null)
            {
                LegacyModifierUI.Instance.ExternalRefresh();
            }

            if (CustomOnlineUI.Instance != null)
            {
                CustomOnlineUI.Instance.ExternalRefresh();
            }
        }

        private void Apply_Max_RT(float value)
        {
            var cfg = PluginConfig.Instance;

            int new_max = (int)Math.Round(value);
            if (new_max <= cfg.minReactionTime)
            {
                new_max = cfg.minReactionTime + 1;
            }

            cfg.maxReactionTime = new_max;

            if (cfg.reactionTime > new_max)
            {
                cfg.reactionTime = new_max;
            }

            // Rebuild slider ranges from the new limit
            if (JDFixerUIManager.Instance != null)
            {
                JDFixerUIManager.Instance.RefreshCurrent();
            }

            Notify_Other_UIs();
            Notify_Window();
        }


        //=============================================================================================
        // Presets

        private static System.Collections.Generic.List<RTQuickPreset> Presets => PluginConfig.Instance.rt_quick_presets;

        private static int Preset_Count => Presets == null ? 0 : Presets.Count;

        private string Slot_Text(int index)
        {
            if (index >= Preset_Count)
            {
                return "";
            }

            float value = Presets[index].reactionTime;

            if (_deleteMode)
            {
                return "<#ff5555>x " + value.ToString("0");
            }

            bool selected = PluginConfig.Instance.slider_setting == 1 && Mathf.Abs(PluginConfig.Instance.reactionTime - value) < 0.5f;

            return (selected ? "<#00ff99>" : "") + value.ToString("0");
        }

        private bool Slot_Active(int index)
        {
            return index < Preset_Count;
        }

        private void Slot_Click(int index)
        {
            if (index >= Preset_Count)
            {
                return;
            }

            if (_deleteMode)
            {
                Presets.RemoveAt(index);
                PluginConfig.Instance.Changed();

                if (Presets.Count == 0)
                {
                    _deleteMode = false;
                }

                Notify_Window();
                return;
            }

            Set_RT(Presets[index].reactionTime);
        }

        private void Save_Current_As_Preset()
        {
            var list = Presets;
            if (list == null || list.Count >= MaxPresets)
            {
                return;
            }

            float rt = Clamp_RT(Mathf.Round(Get_Current_RT()));

            if (list.Any(x => Mathf.Abs(x.reactionTime - rt) < 0.5f))
            {
                return;
            }

            list.Add(new RTQuickPreset(rt));
            list.Sort((a, b) => a.reactionTime.CompareTo(b.reactionTime));
            PluginConfig.Instance.Changed();

            Notify_Window();
        }


        //=============================================================================================
        // Bindings: Window

        [UIValue("title_text")]
        private string Title_Text => (PluginConfig.Instance.use_jd_pref || PluginConfig.Instance.use_rt_pref) ? "JDFixer <#ff9900><size=75%>Prefs override" : "JDFixer";

        [UIValue("rt_text")]
        private string RT_Text => PluginConfig.Instance.slider_setting == 1
            ? "<#cc99ff>RT  <#ffffff>" + PluginConfig.Instance.reactionTime.ToString("0") + " ms"
            : "<#ffff00>JD  <#ffffff>" + PluginConfig.Instance.jumpDistance.ToString("0.##");

        [UIValue("enabled_text")]
        private string Enabled_Text => PluginConfig.Instance.enabled ? "<#00ff99>ON" : "<#ff5555>OFF";

        [UIValue("save_text")]
        private string Save_Text => Preset_Count >= MaxPresets ? "Full" : "+ Save";

        [UIValue("delete_text")]
        private string Delete_Text => _deleteMode ? "<#ff5555>Done" : "Delete";

        [UIValue("row1_active")]
        private bool Row1_Active => Preset_Count > 0;

        [UIValue("row2_active")]
        private bool Row2_Active => Preset_Count > 4;

        [UIValue("empty_active")]
        private bool Empty_Active => Preset_Count == 0;


        [UIAction("toggle_enabled")]
        private void Toggle_Enabled()
        {
            PluginConfig.Instance.enabled = !PluginConfig.Instance.enabled;
            PluginConfig.Instance.Changed();

            Notify_Other_Enabled();
            Notify_Window();
        }

        [UIAction("rt_minus_big")]
        private void RT_Minus_Big() => Nudge_RT(-25f);

        [UIAction("rt_minus_small")]
        private void RT_Minus_Small() => Nudge_RT(-5f);

        [UIAction("rt_plus_small")]
        private void RT_Plus_Small() => Nudge_RT(5f);

        [UIAction("rt_plus_big")]
        private void RT_Plus_Big() => Nudge_RT(25f);

        [UIAction("save_clicked")]
        private void Save_Clicked() => Save_Current_As_Preset();

        [UIAction("delete_clicked")]
        private void Delete_Clicked()
        {
            if (Preset_Count == 0)
            {
                _deleteMode = false;
            }
            else
            {
                _deleteMode = !_deleteMode;
            }

            Notify_Window();
        }


        [UIValue("s0_text")]
        private string S0_Text => Slot_Text(0);
        [UIValue("s0_active")]
        private bool S0_Active => Slot_Active(0);
        [UIAction("s0_click")]
        private void S0_Click() => Slot_Click(0);

        [UIValue("s1_text")]
        private string S1_Text => Slot_Text(1);
        [UIValue("s1_active")]
        private bool S1_Active => Slot_Active(1);
        [UIAction("s1_click")]
        private void S1_Click() => Slot_Click(1);

        [UIValue("s2_text")]
        private string S2_Text => Slot_Text(2);
        [UIValue("s2_active")]
        private bool S2_Active => Slot_Active(2);
        [UIAction("s2_click")]
        private void S2_Click() => Slot_Click(2);

        [UIValue("s3_text")]
        private string S3_Text => Slot_Text(3);
        [UIValue("s3_active")]
        private bool S3_Active => Slot_Active(3);
        [UIAction("s3_click")]
        private void S3_Click() => Slot_Click(3);

        [UIValue("s4_text")]
        private string S4_Text => Slot_Text(4);
        [UIValue("s4_active")]
        private bool S4_Active => Slot_Active(4);
        [UIAction("s4_click")]
        private void S4_Click() => Slot_Click(4);

        [UIValue("s5_text")]
        private string S5_Text => Slot_Text(5);
        [UIValue("s5_active")]
        private bool S5_Active => Slot_Active(5);
        [UIAction("s5_click")]
        private void S5_Click() => Slot_Click(5);

        [UIValue("s6_text")]
        private string S6_Text => Slot_Text(6);
        [UIValue("s6_active")]
        private bool S6_Active => Slot_Active(6);
        [UIAction("s6_click")]
        private void S6_Click() => Slot_Click(6);

        [UIValue("s7_text")]
        private string S7_Text => Slot_Text(7);
        [UIValue("s7_active")]
        private bool S7_Active => Slot_Active(7);
        [UIAction("s7_click")]
        private void S7_Click() => Slot_Click(7);


        private void Notify_Window()
        {
            string[] names = new string[]
            {
                nameof(Title_Text), nameof(RT_Text), nameof(Enabled_Text), nameof(Save_Text), nameof(Delete_Text),
                nameof(Row1_Active), nameof(Row2_Active), nameof(Empty_Active),
                nameof(S0_Text), nameof(S0_Active),
                nameof(S1_Text), nameof(S1_Active),
                nameof(S2_Text), nameof(S2_Active),
                nameof(S3_Text), nameof(S3_Active),
                nameof(S4_Text), nameof(S4_Active),
                nameof(S5_Text), nameof(S5_Active),
                nameof(S6_Text), nameof(S6_Active),
                nameof(S7_Text), nameof(S7_Active)
            };

            foreach (string name in names)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }


        //=============================================================================================
        // Bindings: Mod Settings page "JDFixer RT Window"

        [UIValue("quick_enabled_value")]
        private bool Quick_Enabled_Value
        {
            get => PluginConfig.Instance.quick_rt_enabled;
            set
            {
                PluginConfig.Instance.quick_rt_enabled = value;

                if (value)
                {
                    CreateWindow();
                }
                else
                {
                    DestroyWindow();
                }
            }
        }
        [UIAction("set_quick_enabled")]
        private void Set_Quick_Enabled(bool value)
        {
            Quick_Enabled_Value = value;
        }


        [UIValue("quick_unlocked_value")]
        private bool Quick_Unlocked_Value
        {
            get => PluginConfig.Instance.quick_rt_unlocked;
            set
            {
                PluginConfig.Instance.quick_rt_unlocked = value;

                if (_screen != null)
                {
                    _screen.ShowHandle = value;
                }
            }
        }
        [UIAction("set_quick_unlocked")]
        private void Set_Quick_Unlocked(bool value)
        {
            Quick_Unlocked_Value = value;
        }


        [UIValue("max_rt_value")]
        private float Max_RT_Value
        {
            get => PluginConfig.Instance.maxReactionTime;
            set
            {
                Apply_Max_RT(value);
            }
        }
        [UIAction("set_max_rt")]
        private void Set_Max_RT(float value)
        {
            Max_RT_Value = value;
        }
        [UIAction("max_rt_formatter")]
        private string Max_RT_Formatter(float value) => value.ToString("0") + " ms";


        [UIValue("quick_scale_value")]
        private float Quick_Scale_Value
        {
            get => PluginConfig.Instance.quick_rt_scale;
            set
            {
                PluginConfig.Instance.quick_rt_scale = value;

                if (_screen != null)
                {
                    float scale = Mathf.Clamp(value, 0.3f, 2f);
                    _screen.transform.localScale = new Vector3(0.02f * scale, 0.02f * scale, 0.02f * scale);
                }
            }
        }
        [UIAction("set_quick_scale")]
        private void Set_Quick_Scale(float value)
        {
            Quick_Scale_Value = value;
        }
        [UIAction("quick_scale_formatter")]
        private string Quick_Scale_Formatter(float value) => (value * 100f).ToString("0") + " %";


        [UIAction("reset_position_clicked")]
        private void Reset_Position_Clicked()
        {
            Reset_Window_Position();
        }
    }
}
