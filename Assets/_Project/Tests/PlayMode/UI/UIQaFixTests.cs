using System.Collections;
using System.Collections.Generic;
using FloorF.Core;
using FloorF.Gameplay;
using FloorF.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.UI
{
    /// <summary>Phase 3c QA fixes: tutorial CLOSE/OPEN timing, bubble de-overlap, shop readability, curated badge names.</summary>
    public class UIQaFixTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UITestDriver.Reset();
            yield return null;
        }

        static IEnumerator StartDay1(bool hints = true)
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            GameServices.Get<IGameSettings>().ShowTutorialHints = hints;
            yield return UITestDriver.ClickStart();
            var guests = GameServices.Get<IGuestQuery>();
            var waiting = new List<GuestId>();
            yield return UITestDriver.Until(() => guests.GetWaiting(FloorIndex.Ground, waiting) >= 3);
            Assert.AreEqual(3, guests.GetWaiting(FloorIndex.Ground, waiting), "day 1: three guests wait in the lobby");
            yield return null;
        }

        static bool AnyBoarding(IGuestQuery guests)
        {
            var active = guests.ActiveGuests;
            for (int i = 0; i < active.Count; i++)
                if (guests.TryGet(active[i], out var g) && g.State == GuestState.Boarding) return true;
            return false;
        }

        [UnityTest]
        public IEnumerator TutorialClose_WaitsForAllBoardable()
        {
            Assert.Ignore("ui-dev rewrites for Day 0"); // the Day 0 training shift replaced the old day-1 tutorial flow
            yield return StartDay1();
            var ui = UITestDriver.UI();
            var elevator = GameServices.Get<IElevatorQuery>();
            var guests = GameServices.Get<IGuestQuery>();
            var waiting = new List<GuestId>();
            ui.ClickOpen();
            int halfBoarded = 0;
            Time.captureDeltaTime = 0.05f;
            for (int i = 0; i < 300 && elevator.Occupancy < 3; i++)
            {
                yield return null;
                bool someoneLeft = guests.GetWaiting(FloorIndex.Ground, waiting) > 0 || AnyBoarding(guests);
                if (!someoneLeft) continue;
                if (elevator.Occupancy > 0) halfBoarded++;
                Assert.AreNotEqual("Close", ui.TutorialTarget, "frame " + i + ": CLOSE while guests are still getting in (occupancy " + elevator.Occupancy + ")");
                if (ui.TutorialPrompt != null) StringAssert.DoesNotContain("CLOSE", ui.TutorialPrompt, "frame " + i);
            }
            Time.captureDeltaTime = 0f;
            Assert.Greater(halfBoarded, 0, "the car was seen half-boarded");
            Assert.AreEqual(3, elevator.Occupancy, "all three got in");
            yield return null;
            Assert.AreEqual(TutorialView.Step.Close, ui.TutorialStep);
            Assert.AreEqual("Close", ui.TutorialTarget, "everyone in -> CLOSE");
            StringAssert.Contains("HEAD COUNT", ui.TutorialPrompt);
        }

        [UnityTest]
        public IEnumerator TutorialOpen_OnlyAtRiderDestination()
        {
            Assert.Ignore("ui-dev rewrites for Day 0"); // the Day 0 training shift replaced the old day-1 tutorial flow
            yield return StartDay1();
            var ui = UITestDriver.UI();
            var elevator = GameServices.Get<IElevatorQuery>();
            var guests = GameServices.Get<IGuestQuery>();
            var building = GameServices.Get<IBuildingQuery>();
            ui.ClickOpen();
            yield return UITestDriver.Until(() => elevator.Occupancy >= 3);
            yield return null;
            ui.ClickClose();
            yield return UITestDriver.Until(() => elevator.Doors == DoorState.Closed);
            yield return null;
            Assert.AreEqual(TutorialView.Step.Floor, ui.TutorialStep);

            // a floor nobody wants and nobody minds (no lucky/unlucky number for any rider)
            var dests = new HashSet<int>();
            var riders = new List<GuestInfo>();
            foreach (var r in elevator.Riders) { Assert.IsTrue(guests.TryGet(r, out var info)); riders.Add(info); dests.Add(info.DestinationFloor.Value); }
            int wrong = -1;
            for (int f = 1; f < building.FloorCount && wrong < 0; f++)
            {
                if (dests.Contains(f)) continue;
                bool neutral = true;
                foreach (var r in riders) if (r.Luck.Judge(new FloorIndex(f).PhysicalNumber) != LuckVerdict.Neutral) neutral = false;
                if (neutral) wrong = f;
            }
            Assert.GreaterOrEqual(wrong, 1, "found a floor that is nobody's stop");
            var wrongFloor = new FloorIndex(wrong);

            ui.ClickFloor(wrong);
            yield return UITestDriver.Until(() => elevator.Motion == ElevatorMotion.Idle && elevator.CurrentFloor == wrongFloor && !elevator.HasTarget);
            Assert.AreEqual(wrongFloor, elevator.CurrentFloor, "stopped at the wrong floor");
            yield return null;
            Assert.AreEqual(TutorialView.Step.OpenThere, ui.TutorialStep);
            Assert.IsNotNull(ui.TutorialPrompt, "a gentle hint, not silence");
            StringAssert.DoesNotContain("Arrived", ui.TutorialPrompt, "not anyone's stop");
            StringAssert.Contains("anyone's stop", ui.TutorialPrompt);
            Assert.AreNotEqual("Open", ui.TutorialTarget, "no arrow on OPEN at the wrong floor");
            StringAssert.StartsWith("Floor ", ui.TutorialTarget, "arrow on a rider's floor button instead");
            int pointed = int.Parse(ui.TutorialTarget.Substring(6));
            Assert.IsTrue(dests.Contains(pointed - 1), "points at a rider's destination, got " + ui.TutorialTarget);

            // opening here (counts the floor, but it is not their stop) does not finish the step
            int opened = 0;
            using (GameServices.Events.Subscribe<DoorsOpened>(e => opened++))
            {
                ui.ClickOpen();
                yield return UITestDriver.Until(() => opened > 0);
            }
            yield return null;
            Assert.AreEqual(TutorialView.Step.OpenThere, ui.TutorialStep, "wrong-floor OPEN is not the lesson");
            ui.ClickClose();
            yield return UITestDriver.Until(() => elevator.Doors == DoorState.Closed);
            yield return null;

            var dest = new FloorIndex(pointed - 1);
            ui.ClickFloor(dest.Value);
            yield return UITestDriver.Until(() => elevator.Motion == ElevatorMotion.Idle && elevator.CurrentFloor == dest && !elevator.HasTarget);
            yield return null;
            Assert.AreEqual(TutorialView.Step.OpenThere, ui.TutorialStep);
            StringAssert.Contains("Arrived", ui.TutorialPrompt, "at a rider's stop");
            Assert.AreEqual("Open", ui.TutorialTarget);
            opened = 0;
            using (GameServices.Events.Subscribe<DoorsOpened>(e => opened++))
            {
                ui.ClickOpen();
                yield return UITestDriver.Until(() => opened > 0);
            }
            yield return null;
            Assert.AreEqual(TutorialView.Step.Done, ui.TutorialStep);
        }

        /// <summary>Canvas x-range covered by Rendering's floor-sign plates ("Sign n" objects).</summary>
        static bool SignColumn(UIRoot ui, out float minX, out float maxX)
        {
            minX = float.MaxValue; maxX = float.MinValue;
            int n = GameServices.Get<IBuildingQuery>().FloorCount, found = 0;
            for (int f = 1; f <= n; f++)
            {
                var sign = GameObject.Find("Sign " + f);
                if (sign == null) continue;
                foreach (var r in sign.GetComponentsInChildren<Renderer>())
                {
                    var b = r.bounds;
                    if (!ui.WorldToCanvas(new Vector3(b.min.x, b.center.y, b.min.z), out var a)) continue;
                    if (!ui.WorldToCanvas(new Vector3(b.max.x, b.center.y, b.min.z), out var c)) continue;
                    minX = Mathf.Min(minX, a.x, c.x);
                    maxX = Mathf.Max(maxX, a.x, c.x);
                    found++;
                }
            }
            return found > 0;
        }

        static void AssertClean(UIRoot ui, string when)
        {
            Assert.IsNull(ui.SpeechSpeechOverlap(), when + ": bubbles stacked");
            Assert.IsNull(ui.SpeechPopupOverlap(), when + ": bubble under a luck popup");
            Assert.IsNull(ui.SpeechShaftOverlap(), when + ": bubble over the shaft");
            Assert.IsTrue(SignColumn(ui, out float sMin, out float sMax), "floor signs rendered");
            Assert.IsNull(ui.PopupOverlapX(sMin, sMax), when + ": luck popup over the floor signs");
        }

        [UnityTest]
        public IEnumerator Bubbles_NoOverlapAfterPopupPush()
        {
            yield return StartDay1(hints: false);
            var ui = UITestDriver.UI();
            var bus = GameServices.Events;
            var elevator = GameServices.Get<IElevatorQuery>();
            var guests = GameServices.Get<IGuestQuery>();
            var waiting = new List<GuestId>();

            // the ui_running case: waiting guests talk while two luck popups sit over the car
            int n = guests.GetWaiting(FloorIndex.Ground, waiting);
            var lines = new[] { "Whoa, an elevator operator!", "Three of us!", "I have been waiting for ages, honestly" };
            for (int i = 0; i < n; i++) bus.Publish(new GuestSpoke(waiting[i], GuestLineTriggers.Spawn, lines[i % lines.Length]));
            bus.Publish(new LuckJudged(waiting[0], LuckSource.Occupancy, 3, LuckVerdict.Lucky, FloorIndex.Ground));
            bus.Publish(new LuckJudged(waiting[1], LuckSource.FloorLabel, -1, LuckVerdict.Unlucky, FloorIndex.Ground));
            Assert.AreEqual(2, ui.ActivePopupCount);
            Time.captureDeltaTime = 0.05f;
            for (int f = 0; f < 12; f++)
            {
                yield return null;
                AssertClean(ui, "waiting frame " + f);
            }

            // riders: board, close (real head-count popups), talk while the car moves
            Time.captureDeltaTime = 0f;
            ui.ClickOpen();
            yield return UITestDriver.Until(() => elevator.Occupancy >= 3);
            ui.ClickClose();
            yield return UITestDriver.Until(() => ui.ActivePopupCount > 0, 40);
            ui.ClickFloor(4);
            var riders = elevator.Riders;
            for (int i = 0; i < riders.Count; i++) bus.Publish(new GuestSpoke(riders[i], GuestLineTriggers.Board, lines[i % lines.Length]));
            Time.captureDeltaTime = 0.05f;
            for (int f = 0; f < 12; f++)
            {
                yield return null;
                AssertClean(ui, "riding frame " + f);
            }
            Time.captureDeltaTime = 0f;
        }

        [UnityTest]
        public IEnumerator ShopDescription_IsReadable()
        {
            GameplayRoot.SeedOverride = 7;
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var settings = GameServices.Get<IGameSettings>();
            yield return UITestDriver.ClickStart();
            var s = UITestDriver.Session();
            s.Score.AddTips(60, earned: true);
            s.EndDayNow();
            yield return null;
            ui.ClickFlowButton();
            yield return UITestDriver.Frames(2);
            Assert.AreEqual(DayPhase.Shop, ui.VisibleFlowScreen);
            Assert.Greater(ui.ShopOfferCount, 0);

            foreach (bool ko in new[] { false, true })
            {
                settings.UseKorean = ko;
                yield return UITestDriver.Frames(2);
                for (int i = 0; i < ui.ShopOfferCount; i++)
                {
                    Assert.GreaterOrEqual(ui.ShopDescriptionFontSize(i), 24f, "offer " + i + " description at 24px (12px grid)");
                    Assert.GreaterOrEqual(ui.ShopDescriptionContrast(i), 7f, "offer " + i + " description contrast (unselected)");
                }
                Assert.IsNull(ui.ShopDescriptionProblem(), (ko ? "ko" : "en") + " unselected");
                ui.ClickShopOffer(0);
                yield return UITestDriver.Frames(2);
                Assert.GreaterOrEqual(ui.ShopDescriptionContrast(0), 7f, "selected (gold) card: black ink");
                Assert.IsNull(ui.ShopDescriptionProblem(), (ko ? "ko" : "en") + " selected");
                ui.ClickShopOffer(0); // un-pick
                yield return null;
            }
            settings.UseKorean = false;
            Assert.IsNull(ui.FirstOffGridOrForeignText(), "still on the 12px grid");
        }

        [UnityTest]
        public IEnumerator ShortNames_Curated()
        {
            GameplayRoot.SettingsStoreOverride = new MemorySettingsStore();
            yield return UITestDriver.LoadMain();
            var ui = UITestDriver.UI();
            var settings = GameServices.Get<IGameSettings>();
            var building = GameServices.Get<IBuildingQuery>();
            var data = GameServices.Get<GameData>();
            foreach (bool ko in new[] { false, true })
            {
                settings.UseKorean = ko;
                yield return UITestDriver.Frames(2);
                for (int f = 0; f < ui.FloorButtonCount; f++)
                {
                    Assert.IsTrue(data.TryGetDestination(building.GetDestination(new FloorIndex(f)), out var d));
                    string full = ko && !string.IsNullOrEmpty(d.nameKo) ? d.nameKo : d.name;
                    string curated = UIFormat.DestinationShortName(d.Id.Value, ko);
                    string shown = ui.BadgeText(f);
                    Assert.IsTrue(shown == full || shown == curated, (ko ? "ko" : "en") + " floor " + f + ": '" + shown + "' is neither '" + full + "' nor '" + curated + "'");
                    StringAssert.DoesNotEndWith(".", shown, "never a cut-off name");
                    Assert.GreaterOrEqual(ui.BadgeFontSize(f), 24f);
                }
            }
            settings.UseKorean = false;
        }
    }
}
