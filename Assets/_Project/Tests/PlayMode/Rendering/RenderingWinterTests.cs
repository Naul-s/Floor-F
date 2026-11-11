using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.Harness;
using FloorF.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Rendering
{
    /// <summary>
    /// Winter season (days 8-10) on the real Main scene. Gameplay owns ISeasonQuery; these tests register a stand-in with
    /// the values of a given day (it replaces gameplay's while the test runs) and publish the season events themselves.
    /// Captures render_winter_christmas(_lobby/_roof/_street), render_winter_lottery(_lobby/_sign), render_winter_guests,
    /// render_ending_winter_miracle(_sky), render_ending_silent_night.
    /// </summary>
    public class RenderingWinterTests
    {
        sealed class FakeSeason : ISeasonQuery
        {
            public bool IsSeasonExtended { get; set; } = true;
            public bool IsExtendedDay { get; set; } = true;
            public bool MysteryTonight { get; set; }
            public bool IsLotteryNight { get; set; }
            public int LotteryNumber { get; set; }
            public bool IsChristmas { get; set; }
            public int LuckyForAll { get; set; }
            public float SnowAmount { get; set; }
        }

        FakeSeason fake;

        void Use(FakeSeason s)
        {
            fake = s;
            GameServices.Register<ISeasonQuery>(s);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (fake != null) GameServices.Unregister<ISeasonQuery>(fake);
            fake = null;
            RenderingTestDriver.Unfreeze();
            yield return null;
        }

        static void HideOtherCanvases(bool hide)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.name != RenderingNames.ScreenCanvas) c.enabled = !hide;
        }

        static float Blueness(Color c) => c.b - c.r;

        [UnityTest]
        public IEnumerator NoSeason_NothingWinterIsBuiltOrDrawn()
        {
            yield return RenderingTestDriver.LoadMain();
            if (GameServices.TryGet<ISeasonQuery>(out var real))
                Assert.IsFalse(real.IsExtendedDay || real.SnowAmount > 0f || real.IsChristmas || real.IsLotteryNight, "day 1 is not a winter day");
            var root = RenderingTestDriver.Rendering();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 10; i++) yield return null;
            var w = root.Winter;
            Assert.IsFalse(w.Snowing);
            Assert.IsFalse(w.SnowRenderer.enabled);
            Assert.IsNull(w.HeavyRenderer, "no snow cover built on an ordinary night");
            Assert.IsNull(w.DustRenderer);
            Assert.IsFalse(root.Christmas.Built, "no Christmas dressing built");
            Assert.IsFalse(root.Lottery.Built, "no lottery drum built");
            Assert.AreEqual(0f, w.Mood);
            Assert.AreEqual(RenderingStyle.Background, root.Pipeline.WorldCamera.backgroundColor, "ordinary sky");
            Assert.AreEqual((Color32)RenderingStyle.Ambient, (Color32)RenderSettings.ambientLight, "ordinary ambient");
            // a stray season event without a lottery night is harmless
            GameServices.Events.Publish(new LotteryDrawn(7, 0, 3));
            GameServices.Events.Publish(new LotteryWon(new GuestId(9999), new FloorIndex(6), 7));
            yield return null;
            Assert.IsFalse(root.Lottery.Built);
        }

        [UnityTest]
        public IEnumerator ChristmasEve_SnowCoverDecorations_ColdMood_ThenCaptures()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            Use(new FakeSeason { SnowAmount = 1f, IsChristmas = true, LuckyForAll = 7 });
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 25; i++) yield return null;
            var w = root.Winter;
            w.SnapMood();
            yield return null;

            Assert.IsTrue(w.Snowing);
            Assert.IsTrue(w.SnowRenderer.enabled, "snow falls");
            Assert.That(w.FlakeQuadCount, Is.InRange(100, WinterSchedule.MaxFlakes), "a heavy snowfall, still cheap");
            Assert.IsTrue(w.HeavyCover, "Christmas Eve: snow lies on the hotel and the street");
            Assert.IsFalse(w.DustCover);
            Assert.IsTrue(w.SignCapRenderer.enabled, "snow on the HOTEL sign");
            Assert.Greater(w.IcicleCount, 20, "icicles");
            Assert.LessOrEqual(w.Root.GetComponentsInChildren<Renderer>(false).Count(r => r.enabled), 5, "few draws");

            var xmas = root.Christmas;
            Assert.IsTrue(xmas.Shown);
            Assert.AreEqual(root.Building.FloorCount, xmas.WreathCount, "a wreath on every floor");
            Assert.Greater(xmas.BulbCount, 40, "bulbs on the facade");
            Assert.IsTrue(xmas.HatRenderer.enabled, "Santa hat on the HOTEL sign");
            var lobbyProps = root.Building.PropZoneMinX;
            Assert.GreaterOrEqual(xmas.TreeBounds.min.x, lobbyProps - 0.05f, "tree inside the lobby");
            Assert.LessOrEqual(xmas.TreeBounds.max.x, root.Building.PropZoneMaxX, "tree clear of the sign and the shaft");
            Assert.Less(xmas.TreeBounds.max.y, GameServices.Get<IWorldLayout>().FloorHeight - 0.3f, "tree under the ceiling");
            Assert.Greater(xmas.SleighCentre.y, root.Facade.RoofY, "sleigh on the roof");

            // bulbs fade, never flash: two frames apart they change only a little
            var a = (Color)root.Winter.Atlas.Get(xmas.BulbTexel(0));
            yield return null;
            var b = (Color)root.Winter.Atlas.Get(xmas.BulbTexel(0));
            Assert.Less(Mathf.Abs(a.maxColorComponent - b.maxColorComponent), 0.1f);

            // cold mood: bluer ambient, moonlight and sky; the floors keep their warm light
            Assert.AreEqual(1f, w.Mood, 1e-3f);
            Assert.Greater(Blueness(RenderSettings.ambientLight), Blueness(RenderingStyle.Ambient));
            Assert.Greater(Blueness(root.Pipeline.WorldCamera.backgroundColor), Blueness(RenderingStyle.Background));
            var lobbyLight = root.Lighting.GetBaseColor(new FloorIndex(0));
            Assert.Greater(lobbyLight.r, lobbyLight.b, "warm lobby against the cold night");

            yield return RenderingTestDriver.Freeze();
            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_winter_christmas")));
            HideOtherCanvases(false);
            var layout = GameServices.Get<IWorldLayout>();
            float sx = layout.ShaftX, hw = layout.FloorWidth * 0.5f, top = layout.FloorCount * layout.FloorHeight;
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_winter_christmas_lobby",
                new Vector3(sx - hw - 1.2f, -0.9f, -layout.Depth * 0.5f), new Vector3(sx + hw + 1.2f, layout.FloorHeight * 2f, -layout.Depth * 0.5f), 4)));
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_winter_christmas_roof",
                new Vector3(sx - hw - 1.6f, top - 1.2f, -layout.Depth * 0.5f), new Vector3(sx + hw + 1.6f, top + 3.2f, -layout.Depth * 0.5f), 4)));
            Vector3 W(float x, float y) => SkyView.ViewportToWorld(root.Rig.Current, new Vector2(x, y), SkyView.FxDepth);
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_winter_christmas_street", W(0.0f, 0.0f), W(0.5f, 0.32f), 3)));
            RenderingTestDriver.Unfreeze();

            // the season ends (restart / another day): everything goes away again
            fake.SnowAmount = 0f;
            fake.IsChristmas = false;
            fake.IsExtendedDay = false;
            yield return null;
            Assert.IsFalse(w.SnowRenderer.enabled || w.HeavyCover || w.SignCapRenderer.enabled || w.IcicleRenderer.enabled);
            Assert.IsFalse(xmas.Shown);
            Assert.IsFalse(xmas.Root.gameObject.activeSelf || xmas.HatRenderer.enabled);
        }

        [UnityTest]
        public IEnumerator LotteryNight_FirstSnow_DrumDraw_GoldHighlight_Coins_ThenCaptures()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var building = root.Building;
            // the number some floor shows right now (floor 7 unless a sticker moved it)
            var floor = new FloorIndex(6);
            int number = int.Parse(building.GetDisplayedText(floor));
            Use(new FakeSeason { SnowAmount = 0.3f, IsLotteryNight = true });
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay());
            Time.captureDeltaTime = 0.05f;
            for (int i = 0; i < 4; i++) yield return null;
            var lot = root.Lottery;
            Assert.IsTrue(lot.Shown, "the lottery machine stands in the lobby");
            Assert.IsFalse(lot.IsHighlighted(floor), "nothing drawn yet");

            // the draw: the drum spins, a ball rolls into the tray
            fake.LotteryNumber = number;
            GameServices.Events.Publish(new LotteryDrawn(number, 0, 3));
            float a0 = lot.DrumAngle;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.IsTrue(lot.Drawing);
            Assert.AreNotEqual(a0, lot.DrumAngle, "the drum spins");
            for (int i = 0; i < 40; i++) yield return null;
            Assert.IsFalse(lot.Drawing);
            Assert.IsTrue(lot.BallInTray, "the drawn ball rests in the tray");

            for (int i = 0; i < building.FloorCount; i++)
            {
                var f = new FloorIndex(i);
                Assert.AreEqual(building.GetDisplayedText(f) == number.ToString(), lot.IsHighlighted(f), "gold highlight on floors showing " + number + ", floor " + f.PhysicalNumber);
            }
            Assert.IsTrue(lot.IsHighlighted(floor));

            // first snow: light flakes, a dusting, no heavy cover, no Christmas
            var w = root.Winter;
            Assert.That(w.FlakeQuadCount, Is.InRange(20, 70));
            Assert.IsTrue(w.DustCover);
            Assert.IsFalse(w.HeavyCover);
            Assert.IsFalse(root.Christmas.Built);

            // jackpot: gold coins on that floor
            GameServices.Events.Publish(new LotteryWon(new GuestId(9999), floor, number));
            yield return null;
            Assert.Greater(root.Fx.ActiveCount, 10, "coin shower");
            for (int i = 0; i < 3; i++) yield return null;

            yield return RenderingTestDriver.Freeze();
            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_winter_lottery")));
            HideOtherCanvases(false);
            var layout = GameServices.Get<IWorldLayout>();
            float sx = layout.ShaftX, hw = layout.FloorWidth * 0.5f;
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_winter_lottery_lobby",
                new Vector3(sx - hw - 0.5f, -0.5f, -layout.Depth * 0.5f), new Vector3(sx + 1f, layout.FloorHeight, -layout.Depth * 0.5f), 6)));
            float y0 = floor.Value * layout.FloorHeight;
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_winter_lottery_sign",
                new Vector3(sx - hw, y0 - 0.3f, -layout.Depth * 0.5f), new Vector3(sx + hw, y0 + layout.FloorHeight, -layout.Depth * 0.5f), 5)));
            RenderingTestDriver.Unfreeze();

            // a sticker that moves the number moves the highlight (presentation follows the label)
            fake.LotteryNumber = 0;
            yield return null;
            Assert.IsFalse(lot.IsHighlighted(floor), "no number, no highlight");
        }

        [UnityTest]
        public IEnumerator WinterGuests_Santa_Carolers_SoloTraveller_ThenCaptures()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var data = GameServices.Get<GameData>();
            var defs = new List<GuestDef>();
            GuestDef Find(string accessory) => data.Guests.FirstOrDefault(d => d.accessory == accessory);
            var santa = Find("santa_hat");
            var carolers = Find("scarf");
            var solo = Find("backpack");
            Assert.IsNotNull(santa, "guests.json: santa");
            Assert.IsNotNull(carolers, "guests.json: carolers");
            Assert.IsNotNull(solo, "guests.json: solo_traveler");
            defs.Add(santa); defs.Add(carolers); defs.Add(carolers); defs.Add(carolers); defs.Add(solo);
            var floor = new FloorIndex(5); // guest rooms: warm light, true colours
            var figs = root.Guests.BuildLineup(floor, defs, GuestMood.Neutral);
            string Parts(GuestFigure f) => string.Join(",", f.Visual.GetComponentsInChildren<Transform>().Select(t => t.name));
            foreach (var part in new[] { "Hat", "Beard", "Pompom", "Belt" }) StringAssert.Contains(part, Parts(figs[0]), "santa: " + part);
            foreach (var part in new[] { "Scarf", "Songbook", "Woolly Hat" }) StringAssert.Contains(part, Parts(figs[1]), "caroler: " + part);
            foreach (var part in new[] { "Backpack", "Bedroll", "Beanie" }) StringAssert.Contains(part, Parts(figs[4]), "solo traveller: " + part);
            Color ScarfOf(GuestFigure f) => f.Visual.GetComponentsInChildren<Transform>().First(t => t.name == "Scarf").GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
            Assert.AreNotEqual(ScarfOf(figs[1]), ScarfOf(figs[2]), "carolers wear different scarves");
            Assert.AreNotEqual(ScarfOf(figs[2]), ScarfOf(figs[3]));
            if (root.Assets.Kenney != null && root.Assets.Kenney.FindGuest("santa_hat", 0) != null)
                Assert.IsFalse(figs[0].IsFallbackModel, "santa has a model of its own (red suit, white beard)");

            Time.captureDeltaTime = 0.07f;
            for (int i = 0; i < 6; i++) yield return null;
            yield return RenderingTestDriver.Freeze();
            HideOtherCanvases(true);
            FrameCapture.Capture("render_winter_guests_frame");
            HideOtherCanvases(false);
            var layout = GameServices.Get<IWorldLayout>();
            var s0 = layout.GetWaitingSlot(floor, 0).ToVector3();
            var s1 = layout.GetWaitingSlot(floor, defs.Count - 1).ToVector3();
            float y0 = floor.Value * layout.FloorHeight;
            var path = RenderingTestDriver.CaptureTargetCrop("render_winter_guests",
                new Vector3(Mathf.Min(s0.x, s1.x) - 0.8f, y0 - 0.1f, 0f), new Vector3(Mathf.Max(s0.x, s1.x) + 0.8f, y0 + layout.FloorHeight - 0.3f, 0f), 8);
            Assert.IsTrue(File.Exists(path), path);
        }

        [UnityTest]
        public IEnumerator WinterEndings_MiracleSleighAcrossTheMoon_SilentNightLightsOut_ThenCaptures()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            Use(new FakeSeason { SnowAmount = 1f, IsChristmas = true });
            var day = GameServices.Get<IDayQuery>();
            var layout = GameServices.Get<IWorldLayout>();
            int n = layout.FloorCount;
            Time.captureDeltaTime = 0.05f;
            for (int i = 0; i < 3; i++) yield return null;

            GameServices.Events.Publish(new EndingReached(EndingKind.WinterMiracle, day.CurrentDay, 80, 900));
            yield return null;
            Assert.AreEqual(EndingKind.WinterMiracle, root.Ending.Active);
            Assert.IsTrue(root.Winter.SleighFlight);
            Assert.IsTrue(root.Christmas.Shown, "Christmas dressing in the ending");
            // wait for the sleigh in front of the moon
            var moon = root.Sky.MoonCentre;
            bool crossed = false;
            for (int i = 0; i < 400 && !crossed; i++)
            {
                yield return null;
                crossed = root.Winter.SleighVisible && Mathf.Abs(root.Winter.SleighPixels.x + 8f - moon.x) < 3f;
            }
            Assert.IsTrue(crossed, "Santa's sleigh crosses the moon");
            Assert.Greater(root.Winter.SleighQuadCount, 20);
            for (int i = 0; i < n; i++)
            {
                var f = new FloorIndex(i);
                var c = root.Lighting.GetBaseColor(f);
                Assert.Greater(c.r, c.b + 0.3f, "warm windows, floor " + f.PhysicalNumber);
                Assert.IsTrue(root.Lighting.IsLit(f));
                Assert.IsFalse(root.Lighting.IsFlickering(f));
            }
            Assert.IsTrue(root.Winter.Snowing);
            yield return RenderingTestDriver.Freeze();
            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_ending_winter_miracle")));
            HideOtherCanvases(false);
            Vector3 W(float x, float y) => SkyView.ViewportToWorld(root.Rig.Current, new Vector2(x, y), SkyView.StarDepth);
            Assert.IsTrue(File.Exists(RenderingTestDriver.CaptureTargetCrop("render_ending_winter_miracle_sky", W(0.12f, 0.78f), W(0.42f, 1f), 4)));
            RenderingTestDriver.Unfreeze();

            Time.captureDeltaTime = 0.1f;
            GameServices.Events.Publish(new EndingReached(EndingKind.SilentNight, day.CurrentDay, 40, 500));
            yield return null;
            Assert.IsFalse(root.Winter.SleighFlight, "no sleigh on the silent night");
            for (int i = 0; i < 160; i++) yield return null; // 16 s: every floor but the lobby faded out
            Assert.IsTrue(root.Lighting.IsLit(new FloorIndex(0)), "the lobby (and its tree) stays lit");
            for (int i = 1; i < n; i++) Assert.IsFalse(root.Lighting.IsLit(new FloorIndex(i)), "floor " + (i + 1) + " dark");
            Assert.IsTrue(root.Winter.Snowing, "quiet snow");
            yield return RenderingTestDriver.Freeze();
            HideOtherCanvases(true);
            Assert.IsTrue(File.Exists(FrameCapture.Capture("render_ending_silent_night")));
            HideOtherCanvases(false);
            RenderingTestDriver.Unfreeze();

            // restart: the ending's snow / sleigh / dressing go with it (the fake season still says Christmas)
            fake.SnowAmount = 0f; fake.IsChristmas = false; fake.IsExtendedDay = false;
            RenderingTestDriver.Session().RestartGame();
            yield return null;
            yield return null;
            Assert.AreEqual(EndingKind.None, root.Ending.Active);
            Assert.IsFalse(root.Winter.Snowing);
            Assert.IsFalse(root.Christmas.Shown);
            for (int i = 0; i < n; i++) Assert.IsTrue(root.Lighting.IsLit(new FloorIndex(i)), "relit " + (i + 1));
        }
    }
}
