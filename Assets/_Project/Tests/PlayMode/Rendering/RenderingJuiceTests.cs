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
    /// Phase 3a readability + game feel on the real Main scene: guest silhouettes, unlucky shake, sticker slap, ending
    /// lighting, pause freezing the juice, title camera. Writes captures/render_guests(.zoom).png and render_ending_4f.png.
    /// </summary>
    public class RenderingJuiceTests
    {
        static readonly FloorIndex Floor4 = FloorIndex.FromPhysicalNumber(4);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RenderingTestDriver.Unfreeze();
            yield return null;
        }

        /// <summary>Kenney model: the tint painted into its atlas tile; blocky fallback: the Body box colour.</summary>
        static Color BaseColorOf(GuestFigure f)
        {
            if (f.IsModel) return f.ModelEntry.Tint;
            var m = f.Visual.Find("Body").GetComponent<MeshRenderer>().sharedMaterial;
            return m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
        }

        static string PartSignature(GuestFigure f)
            => string.Join(",", f.Visual.GetComponentsInChildren<Transform>().Select(t => t.name).OrderBy(n => n));

        [UnityTest]
        public IEnumerator GuestProxy_SizeAndSilhouette_DifferPerArchetype()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var data = GameServices.Get<GameData>();
            var layout = GameServices.Get<IWorldLayout>();
            Assert.GreaterOrEqual(data.Guests.Count, 5, "guests.json archetypes");

            var figs = root.Guests.BuildLineup(new FloorIndex(0), data.Guests, GuestMood.Neutral);
            Assert.AreEqual(data.Guests.Count, figs.Count);
            var signatures = new HashSet<string>();
            var heights = new List<float>();
            for (int i = 0; i < figs.Count; i++)
            {
                var def = data.Guests[i];
                var b = figs[i].WorldBounds();
                float h = b.size.y, w = b.size.x;
                heights.Add(h);
                Assert.GreaterOrEqual(h, 0.4f * layout.FloorHeight, def.id + " tall enough to read (" + h + ")");
                Assert.GreaterOrEqual(w, 0.45f, def.id + " wide enough (" + w + ")");
                Assert.AreEqual(def.accessory, figs[i].Accessory, def.id + " accessory from guests.json");
                var expected = GuestFigure.Vivid(CoreUnityExtensions.ParseColor(def.color));
                var actual = BaseColorOf(figs[i]);
                if (!figs[i].IsFallbackModel) // new archetype not in the Kenney catalog yet: closest-tint stand-in
                {
                    Assert.AreEqual(expected.r, actual.r, 0.02f, def.id + " body colour r");
                    Assert.AreEqual(expected.g, actual.g, 0.02f, def.id + " body colour g");
                    Assert.AreEqual(expected.b, actual.b, 0.02f, def.id + " body colour b");
                }
                string sig = Mathf.RoundToInt(h * 10f) + "x" + Mathf.RoundToInt(w * 10f) + ":" + PartSignature(figs[i]);
                Assert.IsTrue(signatures.Add(sig), def.id + " has a silhouette of its own: " + sig);
            }
            Assert.GreaterOrEqual(heights.Average(), 0.45f * layout.FloorHeight, "average guest ~45% of a floor");
            Assert.GreaterOrEqual(heights.Select(h => Mathf.RoundToInt(h * 10f)).Distinct().Count(), 5, "heights vary per type");

            // capture: neutral / impatient / angry / scared rows (floors 1-4)
            root.Guests.BuildLineup(new FloorIndex(1), data.Guests, GuestMood.Impatient);
            root.Guests.BuildLineup(new FloorIndex(2), data.Guests, GuestMood.Angry);
            root.Guests.BuildLineup(new FloorIndex(3), data.Guests, GuestMood.Scared);
            // the newest archetypes at the front of their own queue (close-up below)
            var newcomers = data.Guests.Where(d => d.accessory == "collar" || d.accessory == "flat_cap").ToList();
            var closeFloor = new FloorIndex(5);
            root.Guests.BuildLineup(closeFloor, newcomers, GuestMood.Neutral);
            Time.captureDeltaTime = 0.07f;
            for (int i = 0; i < 9; i++) yield return null;
            yield return RenderingTestDriver.Freeze();
            // world-only frame: hide the HUD / Day 1 intro card (other modules' canvases) for this capture
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.name != RenderingNames.ScreenCanvas) c.enabled = false;
            var path = FrameCapture.Capture("render_guests");
            Assert.IsTrue(File.Exists(path), path);
            float x0 = layout.ShaftX + 1.2f, x1 = layout.ShaftX + 2f + 0.9f * data.Guests.Count + 0.4f;
            var zoom = RenderingTestDriver.CaptureTargetCrop("render_guests_zoom",
                new Vector3(x0, -0.3f, 0f), new Vector3(x1, layout.FloorHeight * 4f, 0f), 4);
            Assert.IsTrue(File.Exists(zoom), zoom);
            // close-up of the newest archetypes (priest = dark cassock + white collar, nonno = flat cap + white beard)
            if (newcomers.Count > 0)
            {
                var s0 = layout.GetWaitingSlot(closeFloor, 0).ToVector3();
                var s1 = layout.GetWaitingSlot(closeFloor, newcomers.Count - 1).ToVector3();
                float y0 = closeFloor.Value * layout.FloorHeight;
                var close = RenderingTestDriver.CaptureTargetCrop("render_guests_new_zoom",
                    new Vector3(Mathf.Min(s0.x, s1.x) - 0.8f, y0 - 0.1f, 0f), new Vector3(Mathf.Max(s0.x, s1.x) + 0.8f, y0 + layout.FloorHeight - 0.4f, 0f), 8);
                Assert.IsTrue(File.Exists(close), close);
            }
        }

        static void AssertWholePixelOffset(Camera cam, CameraFrame baseFrame, out Vector2Int px)
        {
            var d = cam.transform.position - baseFrame.Position;
            float ppu = baseFrame.PixelsPerUnit;
            float dx = Vector3.Dot(d, baseFrame.Rotation * Vector3.right) * ppu;
            float dy = Vector3.Dot(d, baseFrame.Rotation * Vector3.up) * ppu;
            float dz = Vector3.Dot(d, baseFrame.Rotation * Vector3.forward);
            Assert.AreEqual(Mathf.Round(dx), dx, 0.01f, "x offset in whole pixels");
            Assert.AreEqual(Mathf.Round(dy), dy, 0.01f, "y offset in whole pixels");
            Assert.AreEqual(0f, dz, 1e-3f, "no depth motion");
            Assert.AreEqual(baseFrame.OrthographicSize, cam.orthographicSize, 1e-5f, "zoom untouched");
            px = new Vector2Int(Mathf.RoundToInt(dx), Mathf.RoundToInt(dy));
        }

        static IEnumerator WaitForGuests(int count)
        {
            var guests = GameServices.Get<IGuestQuery>();
            Assert.IsTrue(GameServices.Get<IGameFlow>().BeginDay(), "BeginDay");
            Time.captureDeltaTime = 0.1f;
            for (int i = 0; i < 200 && guests.ActiveGuests.Count < count; i++) yield return null;
            Assert.GreaterOrEqual(guests.ActiveGuests.Count, count, "real guests spawned");
        }

        [UnityTest]
        public IEnumerator UnluckyJudgement_ShakesCamera_AndReturnsToSnap()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            yield return WaitForGuests(1);
            var guests = GameServices.Get<IGuestQuery>();
            var id = guests.ActiveGuests[0];
            Assert.IsTrue(guests.TryGet(id, out var info));
            yield return null;

            var cam = root.Pipeline.WorldCamera;
            var baseFrame = root.Rig.BaseFrame;
            Assert.IsFalse(root.Rig.TitleMode);
            AssertWholePixelOffset(cam, baseFrame, out var rest);
            Assert.AreEqual(Vector2Int.zero, rest, "at rest on the base frame");

            Time.captureDeltaTime = 0.02f;
            GameServices.Events.Publish(new LuckJudged(id, LuckSource.FloorLabel, 4, LuckVerdict.Unlucky, info.Floor));
            Assert.IsTrue(root.Rig.IsShaking, "unlucky starts the shake");
            Assert.IsTrue(root.Guests.IsJumping(id), "the judged guest jumps");
            int moved = 0;
            float maxJump = 0f;
            for (int i = 0; i < 40; i++)
            {
                yield return null;
                AssertWholePixelOffset(cam, baseFrame, out var px);
                if (px != Vector2Int.zero) moved++;
                Assert.LessOrEqual(Mathf.Abs(px.x), 4, "short, small shake");
                var fig = root.Guests.GetFigure(id);
                if (fig != null) maxJump = Mathf.Max(maxJump, fig.Visual.localPosition.y);
            }
            Assert.Greater(moved, 2, "the camera actually shook");
            Assert.Greater(maxJump, 0.4f, "guest leapt");
            Assert.IsFalse(root.Rig.IsShaking);
            Assert.AreEqual(0f, Vector3.Distance(cam.transform.position, baseFrame.Position), 1e-4f, "back exactly on the snapped base frame");
            Assert.IsTrue(root.Lighting.IsFlashing(info.Floor), "red flash still fading");
        }

        [UnityTest]
        public IEnumerator StickerApplied_PlaysStickAnimation()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            RenderingTestDriver.EnterShopWithTips(100);
            yield return null;
            var sign = root.Building.GetSign(Floor4).transform;
            var other = root.Building.GetSign(FloorIndex.FromPhysicalNumber(5)).transform;
            Assert.AreEqual(1f, sign.localScale.x, 1e-4f, "idle before the purchase");

            Time.captureDeltaTime = 0.02f;
            Assert.AreEqual(StickerApplyResult.Applied, GameServices.Get<IShopService>().TryBuyAndApply(new StickerId("four_to_f"), Floor4));
            Assert.IsTrue(root.Building.IsSignAnimating(Floor4), "StickerApplied starts the slap");
            yield return null;
            Assert.Greater(Mathf.Abs(sign.localScale.x - 1f), 0.1f, "sign visibly pops (" + sign.localScale.x + ")");
            Assert.AreEqual(1f, other.localScale.x, 1e-4f, "other signs untouched");
            Assert.Greater(root.Fx.ActiveCount, 0, "dust puff");
            float min = float.MaxValue, max = 0f;
            for (int i = 0; i < 30; i++)
            {
                min = Mathf.Min(min, sign.localScale.x);
                max = Mathf.Max(max, sign.localScale.x);
                yield return null;
            }
            Assert.Less(min, 0.95f, "slams down");
            Assert.Greater(max, 1.2f, "overshoots big first");
            Assert.IsFalse(root.Building.IsSignAnimating(Floor4));
            Assert.AreEqual(1f, sign.localScale.x, 1e-4f, "settles at rest scale");
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, sign.localRotation), 1e-3f, "settles upright");
            Assert.AreEqual("F", root.Building.GetSignText(Floor4));
            Assert.IsTrue(root.Building.GetSignSticker(Floor4));
        }

        [UnityTest]
        public IEnumerator FourthFloorEnding_OnlyFloor4Lit_SignRevealsFour()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            RenderingTestDriver.EnterShopWithTips(100);
            Assert.AreEqual(StickerApplyResult.Applied, GameServices.Get<IShopService>().TryBuyAndApply(new StickerId("four_to_f"), Floor4));
            yield return null;
            Assert.AreEqual("F", root.Building.GetDisplayedText(Floor4));

            Time.captureDeltaTime = 0.05f;
            var day = GameServices.Get<IDayQuery>();
            var score = GameServices.Get<IScoreQuery>();
            // presentation-only reaction: what gameplay publishes when the mystery guest reaches the real 4th floor
            GameServices.Events.Publish(new EndingReached(EndingKind.FourthFloor, day.CurrentDay, score.Luck, score.Tips));
            for (int i = 0; i < 8; i++) yield return null;
            Assert.Less(root.Lighting.GetLevel(Floor4), 0.9f, "floor 4 also goes dark first");
            for (int i = 0; i < 72; i++) yield return null;

            var building = GameServices.Get<IBuildingQuery>();
            for (int i = 0; i < building.FloorCount; i++)
            {
                var f = new FloorIndex(i);
                if (f == Floor4)
                {
                    Assert.IsTrue(root.Lighting.IsLit(f), "physical 4th floor lit");
                    Assert.IsTrue(root.Lighting.IsFlickering(f), "and flickering");
                    var c = root.Lighting.GetBaseColor(f);
                    Assert.Greater(c.b, c.r + 0.2f, "cold light");
                }
                else
                {
                    Assert.IsFalse(root.Lighting.IsLit(f), "floor " + f.PhysicalNumber + " dark");
                    Assert.AreEqual(0f, root.Lighting.GetLightIntensity(f), 1e-4f, "floor " + f.PhysicalNumber + " lights off");
                }
            }
            Assert.AreEqual(0f, root.Elevator.LightLevel, 1e-4f, "car light off");
            Assert.IsTrue(root.Building.IsRevealed(Floor4));
            Assert.IsTrue(root.Building.IsStickerFallen(Floor4), "the F sticker fell off");
            Assert.AreEqual("4", root.Building.GetDisplayedText(Floor4), "sign reveals the real 4");

            // capture on a frame where the broken tube is on
            for (int i = 0; i < 60 && root.Lighting.GetLightIntensity(Floor4) < RenderingStyle.FloorLightIntensity * 0.8f; i++) yield return null;
            yield return RenderingTestDriver.Freeze();
            var path = FrameCapture.Capture("render_ending_4f");
            Assert.IsTrue(File.Exists(path), path);
            RenderingTestDriver.Unfreeze();

            // a real restart brings the normal hotel back
            RenderingTestDriver.Session().RestartGame();
            yield return null;
            yield return null;
            Assert.AreEqual(EndingKind.None, root.Ending.Active);
            for (int i = 0; i < building.FloorCount; i++) Assert.IsTrue(root.Lighting.IsLit(new FloorIndex(i)), "relit " + i);
            Assert.IsFalse(root.Building.IsRevealed(Floor4));
            Assert.AreEqual(1f, root.Elevator.LightLevel, 1e-4f);
        }

        [UnityTest]
        public IEnumerator HauntedAndProsperousEndings_RelightTheWholeBuilding()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            var building = GameServices.Get<IBuildingQuery>();
            var day = GameServices.Get<IDayQuery>();
            Time.captureDeltaTime = 0.05f;

            GameServices.Events.Publish(new EndingReached(EndingKind.Haunted, day.CurrentDay, 0, 0));
            for (int i = 0; i < 30; i++) yield return null;
            for (int i = 0; i < building.FloorCount; i++)
            {
                var f = new FloorIndex(i);
                var c = root.Lighting.GetBaseColor(f);
                Assert.Greater(c.r, c.g + 0.5f, "haunted: red, floor " + f.PhysicalNumber);
                Assert.IsTrue(root.Lighting.IsFlickering(f), "haunted: flicker, floor " + f.PhysicalNumber);
            }
            // capture: the haunted mood (world only)
            yield return RenderingTestDriver.Freeze();
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.name != RenderingNames.ScreenCanvas) c.enabled = false;
            var haunted = FrameCapture.Capture("render_ending_haunted");
            Assert.IsTrue(File.Exists(haunted), haunted);
            RenderingTestDriver.Unfreeze();
            Time.captureDeltaTime = 0.05f;

            GameServices.Events.Publish(new EndingReached(EndingKind.Prosperous, day.CurrentDay, 90, 500));
            for (int i = 0; i < 50; i++) yield return null;
            bool signDimmedOnce = false;
            for (int i = 0; i < building.FloorCount; i++)
            {
                var f = new FloorIndex(i);
                Assert.Greater(root.Lighting.GetLevel(f), 1.1f, "prosperous: brighter than normal, floor " + f.PhysicalNumber);
                Assert.IsFalse(root.Lighting.IsFlickering(f));
                var c = root.Lighting.GetBaseColor(f);
                Assert.Greater(c.r, c.b + 0.3f, "warm gold");
            }
            // slow blink (photosensitivity): warm/dim for a third of every EndingDirector.ProsperousBlinkPeriod
            for (int i = 0; i < 40 && !signDimmedOnce; i++)
            {
                if (root.Building.HotelSignTint.r < 0.9f) signDimmedOnce = true;
                yield return null;
            }
            Assert.IsTrue(signDimmedOnce, "HOTEL sign blinks");
        }

        [UnityTest]
        public IEnumerator Pause_FreezesJuice()
        {
            yield return RenderingTestDriver.LoadMain();
            var root = RenderingTestDriver.Rendering();
            yield return WaitForGuests(1);
            var guests = GameServices.Get<IGuestQuery>();
            var id = guests.ActiveGuests[0];
            Assert.IsTrue(guests.TryGet(id, out var info));

            Time.captureDeltaTime = 0.02f;
            GameServices.Events.Publish(new LuckJudged(id, LuckSource.FloorLabel, 4, LuckVerdict.Unlucky, info.Floor));
            GameServices.Events.Publish(new LuckJudged(id, LuckSource.Occupancy, 3, LuckVerdict.Lucky, info.Floor));
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(root.Rig.IsShaking);
            Assert.Greater(root.Fx.ActiveCount, 0, "lucky sparkles");

            RenderingTestDriver.SetPaused(true);
            yield return null;
            Assert.IsTrue(root.IsPaused);
            var fig = root.Guests.GetFigure(id);
            Assert.IsNotNull(fig);
            float juice = root.JuiceTime, shake = root.Rig.ShakeElapsed;
            var camPos = root.Pipeline.WorldCamera.transform.position;
            Assert.IsTrue(root.Fx.TryGetFirstActive(out var spark));
            int sparks = root.Fx.ActiveCount;
            var pose = fig.Visual.localPosition;
            var poseScale = fig.Visual.localScale;
            var light = root.Lighting.GetCurrentColor(info.Floor);
            for (int i = 0; i < 15; i++)
            {
                yield return null;
                Assert.AreEqual(juice, root.JuiceTime, "juice clock stopped");
                Assert.AreEqual(shake, root.Rig.ShakeElapsed, "shake held");
                Assert.AreEqual(camPos, root.Pipeline.WorldCamera.transform.position, "camera held");
                Assert.IsTrue(root.Fx.TryGetFirstActive(out var s2));
                Assert.AreEqual(spark, s2, "sparks frozen in the air");
                Assert.AreEqual(sparks, root.Fx.ActiveCount);
                Assert.AreEqual(pose, fig.Visual.localPosition, "guest pose frozen");
                Assert.AreEqual(poseScale, fig.Visual.localScale);
                Assert.AreEqual(light, root.Lighting.GetCurrentColor(info.Floor), "flash held");
            }

            RenderingTestDriver.SetPaused(false);
            for (int i = 0; i < 60; i++) yield return null;
            Assert.IsFalse(root.IsPaused);
            Assert.Greater(root.JuiceTime, juice, "juice resumes");
            Assert.IsFalse(root.Rig.IsShaking, "shake finished after resuming");
            Assert.AreEqual(0, root.Fx.ActiveCount, "sparkles finished after resuming");
        }

        [UnityTest]
        public IEnumerator TitleScreen_WiderPanningCamera_ThenDefaultFraming()
        {
            yield return RenderingTestDriver.LoadTitle();
            var root = RenderingTestDriver.Rendering();
            var cam = root.Pipeline.WorldCamera;
            Assert.IsTrue(root.Rig.TitleMode);
            Assert.Greater(cam.orthographicSize, root.Rig.BaseFrame.OrthographicSize * 1.05f, "title frame is wider");
            var title = root.Rig.TitleFrame;
            Assert.AreEqual(Mathf.Round(title.PixelsPerUnit), title.PixelsPerUnit, 1e-3f, "whole pixels per unit");
            Time.captureDeltaTime = 0.25f;
            var seen = new HashSet<int>();
            for (int i = 0; i < 24; i++) { yield return null; seen.Add(root.Rig.OffsetPixels.x); }
            Assert.Greater(seen.Count, 3, "slow pan moves the frame");

            Assert.IsTrue(GameServices.Get<IMenuFlow>().StartGameFromTitle());
            yield return null;
            yield return null;
            Assert.IsFalse(root.Rig.TitleMode);
            Assert.AreEqual(root.Rig.BaseFrame.OrthographicSize, cam.orthographicSize, 1e-5f);
            Assert.AreEqual(0f, Vector3.Distance(root.Rig.BaseFrame.Position, cam.transform.position), 1e-4f, "default framing");
        }
    }
}
