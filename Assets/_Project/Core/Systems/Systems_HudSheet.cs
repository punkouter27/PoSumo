using UnityEngine;
using UnityEngine.UIElements;

namespace PoSumo
{
    /// Turns the match screen's Dock into a one-thumb bottom sheet and puts the
    /// game's state flow on it: MENU › TACHIAI › FIGHT › RESULT.
    ///
    /// Presentation only — nothing here touches a fighter, a referee rule, the
    /// observation vector or the action count, so no brain is affected.
    ///
    /// What it does to the Dock:
    ///   - a raised surface with a rounded top edge and a grabber, so the band the
    ///     live strip already lives in reads as ONE sheet rather than a card
    ///     floating over the crowd;
    ///   - the stage strip as its first row;
    ///   - the chrome's bottom corners (telemetry stream, build date) already sit
    ///     inside the Dock's reserved bottom padding (Systems_HudRoot.ReserveChrome),
    ///     so they become the sheet's footer with no layout change.
    ///
    /// Spawned with the screen chrome (Systems_GameMatchManager.SpawnScreenChrome)
    /// and gated on the same `enableScreenChrome` flag, because the sheet exists
    /// to carry the chrome's bottom corners — one switch, not two that can disagree.
    /// The bracket screen draws the same strip with MENU lit (`BuildStrip`).
    ///
    /// The stage is POLLED, not evented: TACHIAI covers the intro, the walk-in,
    /// the countdown and the grace between rounds — four internal phases and no
    /// single event — and the four match events are the referee's whole coupling
    /// surface. Two bool reads a frame, and the strip is repainted only on change.
    public sealed class Systems_HudSheet : MonoBehaviour
    {
        public const int STAGE_MENU = 0;
        public const int STAGE_TACHIAI = 1;
        public const int STAGE_FIGHT = 2;
        public const int STAGE_RESULT = 3;
        private const int STAGE_COUNT = 4;

        private static readonly string[] STAGE_NAMES = { "MENU", "TACHIAI", "FIGHT", "RESULT" };

        private const int GRABBER_WIDTH = 40;
        private const int GRABBER_HEIGHT = 4;
        private const int STRIP_RULE = 2;

        private Systems_GameMatchManager _manager;
        private readonly Label[] _cells = new Label[STAGE_COUNT];
        private int _shownStage = -1;

        /// Styles the dock and adds the strip. Returns null when there is no HUD.
        public static Systems_HudSheet Attach(Transform owner, Systems_HudRoot hud,
                                              Systems_GameMatchManager manager)
        {
            if (hud == null || hud.Dock == null)
            {
                return null;
            }
            var go = new GameObject("HudSheet");
            if (owner != null)
            {
                go.transform.SetParent(owner, false);
            }
            Systems_HudSheet sheet = go.AddComponent<Systems_HudSheet>();
            sheet._manager = manager;
            sheet.Build(hud.Dock);
            return sheet;
        }

        /// The four-cell stage strip, shared with the bracket screen. `cells` must
        /// hold four slots; they are filled for `Paint`.
        public static VisualElement BuildStrip(Label[] cells)
        {
            VisualElement strip = Systems_UiKit.Row().NoPick();
            strip.name = "StageStrip";
            strip.style.flexShrink = 0;
            strip.style.marginBottom = Systems_UiKit.SPACE_2;
            for (int stageIndex = 0; stageIndex < STAGE_COUNT; stageIndex++)
            {
                Label cell = Systems_UiKit.Caption(STAGE_NAMES[stageIndex], Systems_UiKit.FONT_MICRO,
                                                   Systems_UiKit.TextLow, true);
                cell.style.flexGrow = 1;
                cell.style.flexBasis = 0;
                cell.style.unityTextAlign = TextAnchor.MiddleCenter;
                cell.style.paddingTop = Systems_UiKit.SPACE_1;
                cell.style.paddingBottom = Systems_UiKit.SPACE_1;
                cell.style.marginLeft = stageIndex == 0 ? 0 : Systems_UiKit.SPACE_1;
                cell.style.borderBottomWidth = STRIP_RULE;
                cell.style.borderBottomColor = Systems_UiKit.Track;
                strip.Add(cell.NoPick());
                cells[stageIndex] = cell;
            }
            return strip;
        }

        /// Lights the current stage gold, dims the rest. Stages already passed
        /// keep a mid tone so the strip reads as a path, not four buttons.
        public static void Paint(Label[] cells, int stage)
        {
            for (int stageIndex = 0; stageIndex < cells.Length; stageIndex++)
            {
                Label cell = cells[stageIndex];
                if (cell == null)
                {
                    continue;
                }
                bool current = stageIndex == stage;
                cell.style.color = current ? Systems_UiKit.Gold
                                 : stageIndex < stage ? Systems_UiKit.TextMid
                                 : Systems_UiKit.TextLow;
                cell.style.borderBottomColor = current ? Systems_UiKit.Gold : Systems_UiKit.Track;
            }
        }

        private void Build(VisualElement dock)
        {
            dock.name = "HudSheet";
            dock.style.backgroundColor = Systems_UiKit.Surface;
            dock.style.borderTopLeftRadius = Systems_UiKit.RADIUS_LG + Systems_UiKit.SPACE_1;
            dock.style.borderTopRightRadius = Systems_UiKit.RADIUS_LG + Systems_UiKit.SPACE_1;
            dock.style.borderTopWidth = 1;
            dock.style.borderTopColor = Systems_UiKit.Line;
            dock.style.paddingTop = Systems_UiKit.SPACE_2;

            VisualElement grabber = new VisualElement().NoPick();
            grabber.style.width = GRABBER_WIDTH;
            grabber.style.height = GRABBER_HEIGHT;
            grabber.style.alignSelf = Align.Center;
            grabber.style.flexShrink = 0;
            grabber.style.marginBottom = Systems_UiKit.SPACE_2;
            grabber.style.backgroundColor = Systems_UiKit.Track;
            grabber.Round(GRABBER_HEIGHT / 2);

            VisualElement strip = BuildStrip(_cells);

            // First children, above the live strip and whatever else the
            // companions dock — Insert, because some of them added theirs already.
            dock.Insert(0, strip);
            dock.Insert(0, grabber);
            Repaint();
        }

        private void Update()
        {
            Repaint();
        }

        private void Repaint()
        {
            int stage = CurrentStage();
            if (stage == _shownStage)
            {
                return;
            }
            _shownStage = stage;
            Paint(_cells, stage);
        }

        private int CurrentStage()
        {
            if (_manager == null)
            {
                return STAGE_TACHIAI;
            }
            if (_manager.ResultShowing)
            {
                return STAGE_RESULT;
            }
            return _manager.ScoringLive ? STAGE_FIGHT : STAGE_TACHIAI;
        }
    }
}
