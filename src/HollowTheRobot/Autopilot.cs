using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HollowTheRobot
{
    /// <summary>
    /// Self-test for development. Active only when BepInEx/plugins/HollowTheRobot/autotest.txt exists:
    /// loads the save named in that file (default 003.haiku), plays a scripted move list through InputHooks,
    /// takes screenshots into ./shots and quits. No desktop input is needed.
    /// </summary>
    public class Autopilot : MonoBehaviour
    {
        public static string Dir;
        private string shots;

        public static void Install(string pluginDir)
        {
            Dir = pluginDir;
            if (!File.Exists(Path.Combine(pluginDir, "autotest.txt"))) return;
            var go = new GameObject("HTR Autopilot");
            DontDestroyOnLoad(go);
            go.AddComponent<Autopilot>();
            Plugin.Log.LogWarning("AUTOPILOT active (autotest.txt present)");
        }

        private void Start()
        {
            shots = Path.Combine(Dir, "shots");
            Directory.CreateDirectory(shots);
            foreach (var f in Directory.GetFiles(shots)) File.Delete(f);
            StartCoroutine(Run());
        }

        private static readonly System.Collections.Generic.HashSet<string> pendingDown = new System.Collections.Generic.HashSet<string>();
        private static readonly System.Collections.Generic.HashSet<string> pendingUp = new System.Collections.Generic.HashSet<string>();

        private void LateUpdate()
        {
            // Coroutines resume after Update, so a press made there becomes visible for exactly the next frame.
            InputHooks.FakeDown.Clear();
            InputHooks.FakeUp.Clear();
            InputHooks.FakeDown.UnionWith(pendingDown);
            InputHooks.FakeUp.UnionWith(pendingUp);
            pendingDown.Clear();
            pendingUp.Clear();
        }

        private static void Log(string s) => Plugin.Log.LogInfo("[autopilot] " + s);

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            // ReadPixels into raw RGB (Haiku's build strips Unity's PNG encoder); tools/shots_to_png.py converts.
            int w = Screen.width, h = Screen.height;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(shots, $"{name}_{w}x{h}.rgb"), tex.GetRawTextureData());
            Destroy(tex);
            var p = PlayerScript.instance;
            Log($"shot {name} pos={(p ? p.transform.position.ToString() : "-")} clip={KnightControllerDebug.Clip} soul={Soul.Current}");
        }

        private static void Press(string a)
        {
            pendingDown.Add(a);
            InputHooks.FakeHeld.Add(a);
        }

        private static void Release(string a)
        {
            InputHooks.FakeHeld.Remove(a);
            pendingUp.Add(a);
        }

        private IEnumerator Tap(string a, float hold = 0.05f)
        {
            Press(a);
            yield return new WaitForSeconds(hold);
            Release(a);
        }

        private IEnumerator Run()
        {
            string save = File.ReadAllText(Path.Combine(Dir, "autotest.txt")).Trim();
            if (!save.EndsWith(".haiku")) save = "003.haiku";

            // Main menu -> load the save directly.
            float t = 0f;
            while (Object.FindObjectOfType<MainMenuManager>() == null && PlayerScript.instance == null && t < 60f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(4f);
            var menu = Object.FindObjectOfType<MainMenuManager>();
            if (menu != null && PlayerScript.instance == null)
            {
                Log("loading " + save);
                GameManager.instance.saveFilePath = save;
                menu.StartNewGame("normal");
            }
            t = 0f;
            while ((PlayerScript.instance == null || KnightController.Instance == null) && t < 60f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (KnightController.Instance == null)
            {
                Log("no player after 60 s, giving up");
                yield return Quit();
                yield break;
            }
            Log("in game: scene " + SceneManager.GetActiveScene().name);
            yield return new WaitForSeconds(4f);
            InputHooks.FakeAxis["Move Horizontal"] = -1f;
            yield return new WaitForSeconds(0.6f);
            InputHooks.FakeAxis["Move Horizontal"] = 0f;
            yield return new WaitForSeconds(0.3f);
            KnightController.Instance.LogDiagnostics();
            yield return Shot("00_idle");

            // run
            InputHooks.FakeAxis["Move Horizontal"] = 1f;
            yield return new WaitForSeconds(0.35f);
            yield return Shot("01_run");
            yield return new WaitForSeconds(0.2f);
            InputHooks.FakeAxis["Move Horizontal"] = -1f;
            yield return new WaitForSeconds(0.55f);
            InputHooks.FakeAxis["Move Horizontal"] = 0f;
            yield return new WaitForSeconds(0.4f);

            // jump
            Press("Jump");
            yield return new WaitForSeconds(0.18f);
            yield return Shot("02_jump");
            yield return new WaitForSeconds(0.2f);
            Release("Jump");
            yield return new WaitForSeconds(1.0f);

            // nail
            yield return Tap("Attack");
            yield return new WaitForSeconds(0.06f);
            yield return Shot("03_slash");
            yield return new WaitForSeconds(0.5f);
            InputHooks.FakeAxis["Move Vertical"] = 1f;
            yield return Tap("Attack");
            yield return new WaitForSeconds(0.1f);
            yield return Shot("04_upslash");
            InputHooks.FakeAxis["Move Vertical"] = 0f;
            yield return new WaitForSeconds(0.6f);

            // spells
            Soul.Add(Soul.Max);
            yield return Tap("Heal", 0.05f);
            yield return new WaitForSeconds(0.12f);
            yield return Shot("05_fireball_cast");
            yield return new WaitForSeconds(0.15f);
            yield return Shot("06_fireball_fly");
            yield return new WaitForSeconds(0.8f);

            InputHooks.FakeAxis["Move Vertical"] = 1f;
            yield return Tap("Heal", 0.05f);
            InputHooks.FakeAxis["Move Vertical"] = 0f;
            yield return new WaitForSeconds(0.25f);
            yield return Shot("07_wraiths");
            yield return new WaitForSeconds(1.0f);

            Soul.Add(Soul.Max);
            Press("Jump");
            yield return new WaitForSeconds(0.35f);
            Release("Jump");
            InputHooks.FakeAxis["Move Vertical"] = -1f;
            yield return Tap("Heal", 0.05f);
            InputHooks.FakeAxis["Move Vertical"] = 0f;
            yield return new WaitForSeconds(0.25f);
            yield return Shot("08_dive_fall");
            yield return new WaitForSeconds(0.25f);
            yield return Shot("09_dive_land");
            yield return new WaitForSeconds(1.0f);

            // focus (hold heal)
            Press("Heal");
            yield return new WaitForSeconds(0.6f);
            yield return Shot("10_focus");
            Release("Heal");
            yield return new WaitForSeconds(0.6f);

            // Great Slash
            Press("Attack");
            yield return new WaitForSeconds(Plugin.NailArtChargeTime.Value + 0.2f);
            yield return Shot("11_charged");
            Release("Attack");
            yield return new WaitForSeconds(0.12f);
            yield return Shot("12_great_slash");
            yield return new WaitForSeconds(0.8f);

            // Cyclone
            Press("Attack");
            yield return new WaitForSeconds(Plugin.NailArtChargeTime.Value + 0.2f);
            InputHooks.FakeAxis["Move Vertical"] = 1f;
            Release("Attack");
            yield return new WaitForSeconds(0.1f);
            InputHooks.FakeAxis["Move Vertical"] = 0f;
            yield return new WaitForSeconds(0.3f);
            KnightController.Instance.LogDiagnostics();
            yield return Shot("13_cyclone");
            yield return new WaitForSeconds(1.5f);

            // Air dash
            Press("Jump");
            yield return new WaitForSeconds(0.25f);
            yield return Tap("Dash");
            yield return new WaitForSeconds(0.08f);
            yield return Shot("14_air_dash");
            Release("Jump");
            yield return new WaitForSeconds(1.0f);

            // double jump
            Press("Jump");
            yield return new WaitForSeconds(0.25f);
            Release("Jump");
            yield return new WaitForSeconds(0.05f);
            Press("Jump");
            yield return new WaitForSeconds(0.08f);
            yield return Shot("15_double_jump");
            Release("Jump");
            yield return new WaitForSeconds(1.5f);

            Log("done");
            yield return Quit();
        }

        private IEnumerator Quit()
        {
            yield return new WaitForSecondsRealtime(1f);
            if (File.Exists(Path.Combine(Dir, "autotest_keep_open.txt"))) yield break;
            Log("quitting");
            Application.Quit();
        }
    }

    public static class KnightControllerDebug
    {
        public static string Clip;
    }
}
