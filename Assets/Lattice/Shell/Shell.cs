using System;
using Lattice.Native;
using Lattice.Net;
using Lattice.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Lattice.Shell
{
    /// <summary>
    /// The game's shell (U4, GDD §9.2, §17): which screen is up, and the mouse.
    /// The title (before joining, unless the command line joined), the deploy
    /// screen (with no body: joining, or released from our corpse), the death
    /// screen (dead, with a body), the map (M), the menu and its settings
    /// (Esc), and the reward ticker. While a screen is up, the mouse is the
    /// screen's and the player stands still; deploying hands it back.
    /// </summary>
    public sealed class Shell : MonoBehaviour
    {
        /// <summary>Joins (server, invite): an error, or null if under way (the game's).</summary>
        public Func<string, string, string> Connect;
        public Action Quit;
        /// <summary>A self-check drives the player: deploy by itself, no death screen, no title.</summary>
        public bool Auto;
        /// <summary>Open this screen once the world is up (screenshots): map, menu, settings.</summary>
        public string ShowAtStart;
        /// <summary>A self-check's class (--kit N; -1: the one chosen last).</summary>
        public int Kit = -1;
        /// <summary>A self-check's weapon (--weapon N, <c>LT_WEAPON_*</c>; -1: the class's last).</summary>
        public int Weapon = -1;

        Lattice.World.WorldView world;
        DeployScreen deploy;
        MapScreen map;
        MenuScreen menu;
        DeathScreen death;
        TitleScreen title;
        bool mapOpen, menuOpen, release, wasUp, everWelcomed, shownAtStart;
        float deadFor;

        public static bool AnyOpen { get; private set; }

        public static Shell Create(Transform parent, Lattice.World.WorldView world, bool titleFirst)
        {
            var canvas = UiKit.Canvas("Shell", 15, parent);
            var s = canvas.gameObject.AddComponent<Shell>();
            s.world = world;
            s.Build(canvas.transform, titleFirst, parent);
            return s;
        }

        void Build(Transform root, bool titleFirst, Transform parent)
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("Event system", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                es.transform.SetParent(parent, false);
            }
            Settings.Load();
            RewardTicker.Create(root);
            death = DeathScreen.Create(root);
            death.Release = () => release = true;
            deploy = DeployScreen.Create(root);
            map = MapScreen.Create(root);
            menu = MenuScreen.Create(root);
            menu.Resume = () => menuOpen = false;
            menu.OpenMap = () => (menuOpen, mapOpen) = (false, true);
            menu.Controls = () =>
            {
                menuOpen = false;
                var p = Lattice.LocalPlayer.LocalPlayer.Instance;
                if (p != null)
                    p.Help = true;
            };
            menu.Quit = () => Quit?.Invoke();
            death.gameObject.SetActive(false);
            deploy.gameObject.SetActive(false);
            map.gameObject.SetActive(false);
            menu.gameObject.SetActive(false);
            if (titleFirst)
            {
                title = TitleScreen.Create(parent);
                title.Connect = (s, i) => Connect?.Invoke(s, i);
                title.Quit = () => Quit?.Invoke();
            }
        }

        /// <summary>Applies the saved settings, but what a self-check's command line set.</summary>
        public void ApplySettings(bool keepVSync, bool keepScale, bool keepVolume)
        {
            (menu.KeepVSync, menu.KeepScale, menu.KeepVolume) = (keepVSync, keepScale, keepVolume);
            Settings.Apply(keepVSync, keepScale, keepVolume);
        }

        void Update()
        {
            var f = NetDriver.Frame;
            var p = Lattice.LocalPlayer.LocalPlayer.Instance;
            bool welcomed = NetDriver.Running && f.Welcomed;
            everWelcomed |= welcomed;
            bool ready = welcomed && (world == null || world.Ready);
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            // The title, until we're in (and back with the reason if joining fails first).
            if (title != null)
            {
                bool failed = NetDriver.Running && !everWelcomed && (f.State == LtCode.StateDenied || f.State == LtCode.StateTimedOut || f.State == LtCode.StateDisconnected);
                if (failed)
                {
                    title.Say(f.State == LtCode.StateDenied ? "the server refused us (its key or id differs)" : "no answer from the server: is it running?");
                    NetDriver.Stop();
                }
                bool up = !NetDriver.Running && !everWelcomed;
                if (title.gameObject.activeSelf != up)
                    title.gameObject.SetActive(up);
            }

            bool deploys = f.Shell.deploys != 0;
            bool away = f.Shell.away != 0;
            bool dead = welcomed && f.Own.dead != 0;
            deadFor = dead && !away ? deadFor + dt : 0f;
            // After a moment dead (or at once, asked), the deploy screen: deploying from it releases the body.
            if (deploys && dead && !away && deadFor > 2.5f)
                release = true;
            if (!dead)
                release = false;
            bool showDeploy = ready && deploys && dead && (away || release);
            bool showDeath = ready && dead && !away && !showDeploy;

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && ready)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (mapOpen)
                        mapOpen = false;
                    else if (menuOpen && menu.InSettings)
                        menu.Show(false);
                    else if (p != null && p.Help)
                        p.Help = false;
                    else
                        menuOpen = !menuOpen;
                }
                if (kb.mKey.wasPressedThisFrame && !menuOpen && !showDeploy)
                    mapOpen = !mapOpen;
            }
            // (Once deployed, if we deploy: the deploy screen comes first.)
            if (ready && !showDeploy && !dead && !shownAtStart && !string.IsNullOrEmpty(ShowAtStart))
            {
                shownAtStart = true;
                switch (ShowAtStart)
                {
                    case "map": mapOpen = true; break;
                    case "menu": menuOpen = true; break;
                    case "settings":
                        menuOpen = true;
                        Show(menu, true);
                        menu.Show(true);
                        break;
                }
            }
            mapOpen &= ready && !showDeploy;
            menuOpen &= welcomed;

            Show(deploy, showDeploy);
            deploy.Auto = Auto;
            if (Kit >= 0)
                deploy.Kit = Kit;
            if (Weapon >= 0)
                deploy.Weapon = Weapon;
            else if (Kit >= 0)
                deploy.Weapon = Weapons.Own(Kit);
            Show(death, showDeath && !menuOpen && !mapOpen);
            Show(map, mapOpen && !menuOpen);
            if (menuOpen && !menu.gameObject.activeSelf)
                menu.Show(false);
            Show(menu, menuOpen);

            bool up2 = showDeploy || mapOpen || menuOpen || (title != null && title.gameObject.activeSelf);
            AnyOpen = up2;
            Lattice.LocalPlayer.LocalPlayer.UiOpen = up2;
            if (p != null)
            {
                if (up2 && p.Grabbed)
                    p.Release();
                // Deployed from the deploy screen: the mouse is the game's again.
                if (wasUp && !up2 && !dead && !Auto && Application.isFocused && !menuOpen)
                    p.Grab();
            }
            wasUp = showDeploy;
        }

        static void Show(Component c, bool on)
        {
            if (c.gameObject.activeSelf != on)
                c.gameObject.SetActive(on);
        }
    }
}
