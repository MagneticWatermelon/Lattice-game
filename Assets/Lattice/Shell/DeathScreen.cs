using System;
using Lattice.Native;
using Lattice.Net;
using Lattice.UI;
using TMPro;
using UnityEngine;

namespace Lattice.Shell
{
    /// <summary>
    /// Dead, with a body (GDD §4.4): the recap, and what comes next. Where
    /// players deploy, the deploy screen (it opens by itself after a moment,
    /// or at once with Space); otherwise the respawn's countdown.
    /// </summary>
    public sealed class DeathScreen : MonoBehaviour
    {
        /// <summary>Asked to deploy (Space, or the button).</summary>
        public Action Release;

        TextMeshProUGUI next;
        Clickable button;
        float diedAt = -1f;
        int shownTenths = -2;
        bool shownDeploys;

        public static DeathScreen Create(Transform parent)
        {
            var root = UiKit.Rect("Death", parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(760f, RecapCard.Height + 90f));
            var d = root.gameObject.AddComponent<DeathScreen>();
            d.Build(root);
            return d;
        }

        void Build(RectTransform root)
        {
            RecapCard.Create(root, 0f, 0f, 760f);
            button = Widgets.Button(root, "CHOOSE A SPAWN  ·  SPACE", 0f, RecapCard.Height + 16f, 760f, 60f, () => Release?.Invoke(), true, null, 24f);
            next = Widgets.Label(root, "", 0f, RecapCard.Height + 16f, 760f, 60f, 26f, true, UiKit.Ink, TextAlignmentOptions.Center);
        }

        void OnEnable() => diedAt = Time.unscaledTime;

        void Update()
        {
            var f = NetDriver.Frame;
            bool deploys = f.Shell.deploys != 0;
            if (deploys != shownDeploys || shownTenths == -2)
            {
                shownDeploys = deploys;
                button.gameObject.SetActive(deploys);
                next.gameObject.SetActive(!deploys);
                shownTenths = -1;
            }
            if (deploys)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.spaceKey.wasPressedThisFrame)
                    Release?.Invoke();
                button.SetColors(Names.Ink(f.Own.faction), Color.Lerp(Names.Ink(f.Own.faction), Color.white, 0.25f));
                return;
            }
            float total = LtConst.RespawnSteps / (float)LtConst.TickHz;
            float left = Mathf.Max(0f, total - (Time.unscaledTime - diedAt));
            int tenths = Mathf.CeilToInt(left * 10f);
            if (tenths != shownTenths)
            {
                shownTenths = tenths;
                next.text = $"RESPAWN  {tenths / 10}.{tenths % 10} s";
            }
        }
    }
}
