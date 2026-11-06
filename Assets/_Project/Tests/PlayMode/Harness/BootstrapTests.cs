using System.Collections;
using System.Linq;
using FloorF.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FloorF.Tests.PlayMode.Harness
{
    /// <summary>The module bootstrap pattern: loading Main gives exactly one root per module, also after a reload.</summary>
    public class BootstrapTests
    {
        static readonly string[] Roots = { "[Gameplay]", "[Rendering]", "[UI]", "[Audio]" };

        static int Count(string name)
        {
            int n = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                n += SceneManager.GetSceneAt(i).GetRootGameObjects().Count(g => g.name == name);
            return n;
        }

        [UnityTest]
        public IEnumerator MainScene_InstallsEachModuleOnce_AlsoAfterReload()
        {
            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            yield return null;
            foreach (var r in Roots) Assert.AreEqual(1, Count(r), r + " after first load");
            Assert.IsTrue(GameServices.Has<GameData>(), "Gameplay registers GameData");
            Assert.IsTrue(GameServices.Has<IWorldLayout>(), "Gameplay registers IWorldLayout");

            yield return SceneManager.LoadSceneAsync(GameScenes.Main);
            yield return null;
            foreach (var r in Roots) Assert.AreEqual(1, Count(r), r + " after reload");
            Assert.IsTrue(GameServices.Has<GameData>(), "services survive a reload (re-registered by the new root)");
        }
    }
}
