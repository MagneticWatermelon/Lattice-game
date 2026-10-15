using System;
using Lattice.Net;
using Lattice.UI;
using TMPro;
using UnityEngine;

namespace Lattice.Shell
{
    /// <summary>
    /// The title (GDD §17, U4, before a login service exists): which server to
    /// join, or a playtest's invite file, remembered between games; and why
    /// the last try failed. A command line that names a server or an invite
    /// (a playtest's launcher, a self-check) skips it.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        const string ServerPref = "Lattice.Server", InvitePref = "Lattice.Invite";

        /// <summary>Connect to (server, invite): returns an error, or null if it's under way.</summary>
        public Func<string, string, string> Connect;
        public Action Quit;

        TMP_InputField server, invite;
        TextMeshProUGUI status;

        public static TitleScreen Create(Transform parent)
        {
            var canvas = UiKit.Canvas("Title", 25, parent, clickable: true);
            var t = canvas.gameObject.AddComponent<TitleScreen>();
            t.Build((RectTransform)canvas.transform);
            return t;
        }

        void Build(RectTransform root)
        {
            var back = UiKit.Box("Backdrop", root, new Color(0.03f, 0.04f, 0.045f, 1f));
            back.raycastTarget = true;
            UiKit.Fill(back.rectTransform);
            var mid = UiKit.Rect("Middle", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 640f));
            var title = Widgets.Label(mid, "LATTICE", 0f, 0f, 720f, 96f, 84f, false, UiKit.Ink, TextAlignmentOptions.Center);
            title.characterSpacing = 30f;
            Widgets.Label(mid, "KILN  ·  THREE FACTIONS, ONE CONTINENT", 0f, 100f, 720f, 30f, 20f, false, UiKit.Muted, TextAlignmentOptions.Center).characterSpacing = 6f;
            string remembered = PlayerPrefs.GetString(ServerPref, "");
            server = Widgets.Field(mid, "SERVER", 60f, 180f, 600f, remembered, "this PC's server (WSL), port 40000");
            Widgets.Button(mid, "JOIN", 60f, 268f, 600f, 64f, () => Go(false), true, null, 28f);
            invite = Widgets.Field(mid, "OR A PLAYTEST INVITE", 60f, 370f, 600f, PlayerPrefs.GetString(InvitePref, ""), "path to the invite file");
            Widgets.Button(mid, "JOIN WITH THE INVITE", 60f, 458f, 600f, 56f, () => Go(true));
            status = Widgets.Label(mid, "", 0f, 532f, 720f, 30f, 18f, false, UiKit.Warn, TextAlignmentOptions.Center);
            Widgets.Button(mid, "QUIT", 260f, 584f, 200f, 46f, () => Quit?.Invoke(), false, null, 20f);
        }

        /// <summary>Why the last try failed (shown under the buttons).</summary>
        public void Say(string why) => status.text = why ?? "";

        void Go(bool withInvite)
        {
            string s = server.text.Trim(), i = invite.text.Trim();
            if (withInvite && i.Length == 0)
            {
                Say("name the invite file first");
                return;
            }
            PlayerPrefs.SetString(ServerPref, s);
            PlayerPrefs.SetString(InvitePref, i);
            PlayerPrefs.Save();
            Say(Connect?.Invoke(withInvite ? "" : s, withInvite ? i : ""));
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                Go(invite.isFocused && invite.text.Trim().Length > 0);
        }
    }
}
