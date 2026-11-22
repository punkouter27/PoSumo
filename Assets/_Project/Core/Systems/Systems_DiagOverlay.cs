using System.Text;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PoSumo
{
    /// The in-game diagnostics overlay every Po game carries: `Tab` — or the 44pt
    /// "DIAG" corner button, the door a phone has no Tab key for — toggles an opaque
    /// panel with
    ///
    ///     FPS ................ frames over the last refresh window
    ///     Episode reward ..... each fighter's cumulative reward this episode
    ///     Inference latency .. rolling mean of the decision steps (Systems_InferenceClock)
    ///     Curriculum stage ... the school the fighters run in and the bracket stage
    ///     Training speed ..... BENCHMARK TRAINING SPEED runs the live arena flat out
    ///                          for a few seconds (Systems_SimBenchmark)
    ///
    /// It lives on one DontDestroyOnLoad object built at startup (with
    /// Systems_DemoMode beside it), draws through its OWN UIDocument on the game's
    /// PanelSettings at a sorting order above every HUD document, and so works the
    /// same on the bracket and in the arena without either scene knowing about it.
    /// Tuning (panel, refresh rate, latency window) is on DemoModeSettings.asset.
    public sealed class Systems_DiagOverlay : MonoBehaviour
    {
        private static readonly Color PanelBg = new Color32(0x12, 0x18, 0x26, 0xFF);
        private static readonly Color PanelLine = new Color32(0x2E, 0x38, 0x52, 0xFF);
        private static readonly Color TextHi = new Color32(0xE6, 0xE9, 0xF2, 0xFF);
        private static readonly Color TextMuted = new Color32(0x9A, 0xA3, 0xB5, 0xFF);
        private static readonly Color Accent = new Color32(0x63, 0x66, 0xF1, 0xFF);

        public static Systems_DiagOverlay Instance { get; private set; }

        /// True while the panel is open.
        public bool Visible { get; private set; }

        private Systems_DemoModeSettings _settings;
        private UIDocument _document;
        private VisualElement _root;
        private Button _button;
        private VisualElement _panel;
        private Label _fps, _reward, _latency, _stage, _demo, _bench;
        private Button _benchButton;
        private Systems_SimBenchmark _benchmark;
        private readonly StringBuilder _sb = new StringBuilder(160);

        private int _frames;
        private float _elapsed;
        private float _refreshTimer;
        private float _shownFps;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null || Application.isBatchMode) return;
            // Training scenes run headless envs for the trainer: no overlay, no demo.
            if (SceneManager.GetActiveScene().name.StartsWith("SCN_TRAIN")) return;
            var go = new GameObject("PoSumoDiagnostics");
            DontDestroyOnLoad(go);
            go.AddComponent<Systems_DiagOverlay>();
            go.AddComponent<Systems_DemoMode>();
            go.AddComponent<Systems_SimBenchmark>();
        }

        private void Awake()
        {
            Instance = this;
            _settings = Resources.Load<Systems_DemoModeSettings>(Systems_DemoModeSettings.RESOURCE_PATH);
            if (_settings == null)
            {
                _settings = ScriptableObject.CreateInstance<Systems_DemoModeSettings>();
            }
            Systems_InferenceClock.Window = _settings.latencyWindow;

            // The document goes on an inactive child so its panel settings are in
            // place before UIDocument.OnEnable builds the tree.
            var host = new GameObject("DiagOverlayDocument");
            host.SetActive(false);
            host.transform.SetParent(transform, false);
            _document = host.AddComponent<UIDocument>();
            _document.panelSettings = _settings.overlayPanelSettings;
            _document.sortingOrder = _settings.overlaySortingOrder;
            host.SetActive(true);
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// The root of this overlay's document, so demo mode can leave it shown
        /// while it hides every other HUD document.
        public UIDocument Document => _document;

        private void Build()
        {
            _root = _document.rootVisualElement;
            if (_root == null) return;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.right = 0;
            _root.style.top = 0;
            _root.style.bottom = 0;

            // TOP-LEFT corner: the title used to sit here; the page draws the name now.
            _button = new Button(Toggle) { text = "DIAG", name = "DiagToggle" };
            _button.style.position = Position.Absolute;
            _button.style.left = Systems_UiKit.SPACE_2;
            _button.style.top = Systems_UiKit.SPACE_2;
            _button.style.width = 72;
            _button.style.height = Systems_UiKit.TOUCH_MIN;
            _button.style.marginLeft = 0;
            _button.style.marginTop = 0;
            _button.style.fontSize = Systems_UiKit.FONT_MICRO;
            _button.style.unityFontStyleAndWeight = FontStyle.Bold;
            _button.style.color = TextHi;
            _button.style.backgroundColor = PanelBg;
            SetBorder(_button, PanelLine, 1, 8);
            _button.focusable = false;
            _root.Add(_button);

            _panel = new VisualElement { name = "DiagPanel" };
            _panel.style.position = Position.Absolute;
            _panel.style.left = Systems_UiKit.SPACE_2;
            _panel.style.top = Systems_UiKit.SPACE_2 + Systems_UiKit.TOUCH_MIN + Systems_UiKit.SPACE_2;
            _panel.style.minWidth = 340;
            _panel.style.maxWidth = Length.Percent(90);
            _panel.style.paddingLeft = 16;
            _panel.style.paddingRight = 16;
            _panel.style.paddingTop = 12;
            _panel.style.paddingBottom = 12;
            _panel.style.backgroundColor = PanelBg;
            SetBorder(_panel, PanelLine, 1, 10);
            _panel.pickingMode = PickingMode.Ignore;

            Label title = Line("DIAGNOSTICS  ·  TAB", Systems_UiKit.FONT_MICRO, Accent, true);
            title.style.marginBottom = 6;
            _panel.Add(title);
            _fps = Row("FPS");
            _reward = Row("Episode reward");
            _latency = Row("Inference latency");
            _stage = Row("Curriculum stage");
            _demo = Row("Demo mode");
            _bench = Row("Training speed");
            _bench.text = "press the button during a bout";
            _benchButton = new Button(RunBenchmark) { text = "BENCHMARK TRAINING SPEED", name = "BenchmarkButton" };
            _benchButton.style.marginTop = 8;
            _benchButton.style.marginLeft = 0;
            _benchButton.style.height = Systems_UiKit.TOUCH_MIN;
            _benchButton.style.fontSize = Systems_UiKit.FONT_MICRO;
            _benchButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _benchButton.style.color = TextHi;
            _benchButton.style.backgroundColor = PanelBg;
            SetBorder(_benchButton, Accent, 1, 8);
            _benchButton.focusable = false;
            _panel.Add(_benchButton);
            _panel.style.display = DisplayStyle.None;
            _root.Add(_panel);
        }

        private Label Row(string caption)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginTop = 4;
            row.pickingMode = PickingMode.Ignore;
            row.Add(Line(caption.ToUpperInvariant(), Systems_UiKit.FONT_MICRO, TextMuted, false));
            Label value = Line("--", Systems_UiKit.FONT_BODY, TextHi, true);
            row.Add(value);
            _panel.Add(row);
            return value;
        }

        private static Label Line(string text, int size, Color color, bool bold)
        {
            Label label = Systems_UiKit.Text(text, size, color, bold);
            label.pickingMode = PickingMode.Ignore;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static void SetBorder(VisualElement e, Color color, float width, float radius)
        {
            e.style.borderLeftColor = color;
            e.style.borderRightColor = color;
            e.style.borderTopColor = color;
            e.style.borderBottomColor = color;
            e.style.borderLeftWidth = width;
            e.style.borderRightWidth = width;
            e.style.borderTopWidth = width;
            e.style.borderBottomWidth = width;
            e.style.borderTopLeftRadius = radius;
            e.style.borderTopRightRadius = radius;
            e.style.borderBottomLeftRadius = radius;
            e.style.borderBottomRightRadius = radius;
        }

        public void Toggle() => SetVisible(!Visible);

        private void RunBenchmark()
        {
            if (_benchmark == null)
            {
                _benchmark = GetComponent<Systems_SimBenchmark>();
            }
            if (_benchmark == null)
            {
                return;
            }
            _benchmark.Begin();
            _refreshTimer = float.MaxValue;
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            if (_panel != null) _panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _refreshTimer = float.MaxValue;   // refresh on the next frame
        }

        /// Demo mode hides the corner button with the rest of the HUD.
        public void SetButtonShown(bool shown)
        {
            if (_button != null) _button.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Update()
        {
            Systems_InferenceClock.EnsureHooked();

            // The panel is rebuilt when a scene change took the document's tree away.
            if (_document != null && _document.rootVisualElement != _root) Build();

#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame) Toggle();
#endif
            _frames++;
            _elapsed += Time.unscaledDeltaTime;
            _refreshTimer += Time.unscaledDeltaTime;
            if (_elapsed >= 0.5f)
            {
                _shownFps = _frames / _elapsed;
                _frames = 0;
                _elapsed = 0f;
            }
            if (!Visible || _refreshTimer < _settings.overlayRefreshSeconds) return;
            _refreshTimer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            _fps.text = _shownFps > 0f ? Mathf.RoundToInt(_shownFps).ToString() : "--";

            _sb.Clear();
            Agent_Biped[] fighters = FindObjectsByType<Agent_Biped>();
            for (int i = 0; i < fighters.Length; i++)
            {
                if (_sb.Length > 0) _sb.Append("   ");
                Agent_Biped f = fighters[i];
                _sb.Append(f.character != null ? f.character.behaviorName : f.name)
                   .Append(' ').Append(f.GetCumulativeReward().ToString("+0.000;-0.000;0.000"));
            }
            _reward.text = _sb.Length > 0 ? _sb.ToString() : "no fighters in this scene";

            _latency.text = Systems_InferenceClock.Samples > 0
                ? $"{Systems_InferenceClock.AverageMs:0.00} ms avg  ({Mathf.Min(Systems_InferenceClock.Samples, Systems_InferenceClock.Window)} decisions)"
                : "-- (no decision yet)";

            _stage.text = StageText(fighters);

            if (_benchmark == null)
            {
                _benchmark = GetComponent<Systems_SimBenchmark>();
            }
            if (_benchmark != null && _benchmark.Result.Length > 0)
            {
                _bench.text = _benchmark.Result;
            }

            Systems_DemoMode demo = Systems_DemoMode.Instance;
            _demo.text = demo == null ? "off"
                : demo.Active ? "ON  (any input exits)"
                : $"in {Mathf.CeilToInt(demo.SecondsUntilDemo)} s idle";
        }

        /// The runtime game has no trainer curriculum (inference only), so the
        /// stage reported is the school the brains were graduated into and run in,
        /// plus where the bracket stands; a trainer-driven env adds its lesson.
        private static string StageText(Agent_Biped[] fighters)
        {
            var sb = new StringBuilder(96);
            string school = fighters.Length > 0 ? fighters[0].mode.ToString().ToUpperInvariant() : "BRACKET";
            sb.Append(school).Append(" school");
            if (Systems_TournamentState.Active && !Systems_TournamentState.IsComplete)
            {
                sb.Append(" · ").Append(Systems_TournamentState.RoundName(Systems_TournamentState.CurrentMatch))
                  .Append(" (bout ").Append(Systems_TournamentState.BoutsPlayed + 1)
                  .Append('/').Append(Systems_TournamentState.BoutCount).Append(')');
            }
            else if (Systems_TournamentState.IsComplete)
            {
                sb.Append(" · tournament complete");
            }
            if (Academy.IsInitialized && Academy.Instance.IsCommunicatorOn)
            {
                // The trainer's curriculum lives in environment parameters whose
                // lessons step down over a run (Training/configs/*Real02.yaml).
                var ep = Academy.Instance.EnvironmentParameters;
                sb.Append(" · stand_assist ").Append(ep.GetWithDefault("stand_assist", 0f).ToString("0.00"))
                  .Append(" · obs_delay_max ").Append(Mathf.RoundToInt(ep.GetWithDefault("obs_delay_max", 0f)));
            }
            else
            {
                sb.Append(" · inference only");
            }
            return sb.ToString();
        }
    }
}
