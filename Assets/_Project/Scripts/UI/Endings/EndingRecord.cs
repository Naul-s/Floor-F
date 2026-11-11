using System;
using System.Globalization;
using System.Text;
using FloorF.Core;
using UnityEngine;

namespace FloorF.UI
{
    /// <summary>
    /// Which endings the player has seen and how often (presentation-only record for the title's ENDINGS card and the
    /// "Endings 3/4 unlocked" line), plus the best run so far (final Luck, tips earned). Serialized as "kind:count" pairs
    /// then the bests ("1:3,4:1,L:72,T:340"); <see cref="Parse"/> never throws and skips anything it does not understand
    /// (unknown kinds, junk, non-positive counts) - an older build reading the bests just skips them.
    /// </summary>
    sealed class EndingRecord
    {
        /// <summary>Every ending, in the order the collection card lists them. <see cref="EndingKind.FourthFloor"/> is the hidden one.</summary>
        public static readonly EndingKind[] All = { EndingKind.Prosperous, EndingKind.Modest, EndingKind.Haunted, EndingKind.FourthFloor };
        public static int Total => All.Length;
        /// <summary>Counts stop here (a corrupt or absurd value never overflows).</summary>
        public const int MaxCount = 99999;

        readonly int[] counts = new int[5]; // index = (int)EndingKind

        /// <summary>Best final Luck / best tips earned in one run; -1 = no run finished yet.</summary>
        public int BestLuck { get; private set; } = -1;
        public int BestTips { get; private set; } = -1;
        public bool HasBest => BestLuck >= 0 || BestTips >= 0;
        const string LuckKey = "L", TipsKey = "T";

        /// <summary>
        /// A finished run's final Luck and tips: kept when better. <paramref name="luckBeaten"/> / <paramref name="tipsBeaten"/>
        /// = an EARLIER best was beaten (the first run ever sets the record without a NEW BEST).
        /// </summary>
        public void OfferBest(int luck, int tips, out bool luckBeaten, out bool tipsBeaten)
        {
            luck = Math.Max(0, Math.Min(luck, MaxCount));
            tips = Math.Max(0, Math.Min(tips, int.MaxValue / 2));
            luckBeaten = BestLuck >= 0 && luck > BestLuck;
            tipsBeaten = BestTips >= 0 && tips > BestTips;
            if (luck > BestLuck) BestLuck = luck;
            if (tips > BestTips) BestTips = tips;
        }

        /// <summary>The secret ending: it overrides the Luck endings and gets a HIDDEN ENDING badge.</summary>
        public static bool IsHidden(EndingKind k) => k == EndingKind.FourthFloor;

        static bool Valid(EndingKind k) => Array.IndexOf(All, k) >= 0;

        public int Times(EndingKind k) => Valid(k) ? counts[(int)k] : 0;
        public bool IsUnlocked(EndingKind k) => Times(k) > 0;

        public int UnlockedCount
        {
            get
            {
                int n = 0;
                foreach (var k in All) if (counts[(int)k] > 0) n++;
                return n;
            }
        }

        /// <summary>Counts one more <paramref name="k"/>. True when it was seen for the first time. None / unknown = ignored.</summary>
        public bool Add(EndingKind k)
        {
            if (!Valid(k)) return false;
            bool first = counts[(int)k] == 0;
            if (counts[(int)k] < MaxCount) counts[(int)k]++;
            return first;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var k in All)
            {
                int c = counts[(int)k];
                if (c <= 0) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(((int)k).ToString(CultureInfo.InvariantCulture)).Append(':').Append(c.ToString(CultureInfo.InvariantCulture));
            }
            if (BestLuck >= 0) Pair(sb, LuckKey, BestLuck);
            if (BestTips >= 0) Pair(sb, TipsKey, BestTips);
            return sb.ToString();
        }

        static void Pair(StringBuilder sb, string key, int value)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(key).Append(':').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Tolerant parse: null/empty/garbage -> what could be read (possibly nothing). A kind listed twice keeps the larger count.</summary>
        public static EndingRecord Parse(string raw)
        {
            var r = new EndingRecord();
            if (string.IsNullOrEmpty(raw)) return r;
            foreach (var part in raw.Split(','))
            {
                int colon = part.IndexOf(':');
                if (colon <= 0) continue;
                string key = part.Substring(0, colon).Trim();
                if (key == LuckKey || key == TipsKey)
                {
                    if (!long.TryParse(part.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long best) || best < 0) continue;
                    if (key == LuckKey) r.BestLuck = Math.Max(r.BestLuck, (int)Math.Min(best, MaxCount));
                    else r.BestTips = Math.Max(r.BestTips, (int)Math.Min(best, int.MaxValue / 2));
                    continue;
                }
                if (!int.TryParse(part.Substring(0, colon).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int kind)) continue;
                if (!long.TryParse(part.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long count)) continue;
                var k = (EndingKind)kind;
                if (!Valid(k) || count <= 0) continue;
                int c = (int)Math.Min(count, MaxCount);
                if (c > r.counts[kind]) r.counts[kind] = c;
            }
            return r;
        }
    }

    /// <summary>Where the <see cref="EndingRecord"/> string lives between sessions.</summary>
    interface IEndingStore
    {
        /// <summary>The saved string, or null when there is none.</summary>
        string Load();
        void Save(string value);
    }

    /// <summary>PlayerPrefs (WebGL: IndexedDB) under one key; Save flushes at once (an ending is a rare event).</summary>
    sealed class PlayerPrefsEndingStore : IEndingStore
    {
        public const string DefaultKey = "floorf.endings.v1";
        public readonly string Key;
        public PlayerPrefsEndingStore(string key = DefaultKey) { Key = string.IsNullOrEmpty(key) ? DefaultKey : key; }
        public string Load() => PlayerPrefs.HasKey(Key) ? PlayerPrefs.GetString(Key) : null;
        public void Save(string value)
        {
            PlayerPrefs.SetString(Key, value ?? "");
            PlayerPrefs.Save();
        }
        public void Delete()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }

    /// <summary>In-memory store (tests, batch mode). <see cref="Value"/> is what a PlayerPrefs key would hold.</summary>
    sealed class MemoryEndingStore : IEndingStore
    {
        public string Value;
        public int Saves;
        public MemoryEndingStore(string value = null) { Value = value; }
        public string Load() => Value;
        public void Save(string value) { Value = value; Saves++; }
    }

    /// <summary>The record + its store: loads once, saves after every new ending. Storage errors are logged, never thrown.</summary>
    sealed class EndingCollection
    {
        /// <summary>Tests: the store the next UIRoot uses. Null = PlayerPrefs, except in batch mode (CI/agents), which
        /// always uses a fresh in-memory store so test runs never read or write the real player prefs.</summary>
        internal static IEndingStore StoreOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => StoreOverride = null;

        public static IEndingStore CreateStore()
        {
            if (StoreOverride != null) return StoreOverride;
            if (Application.isBatchMode) return new MemoryEndingStore();
            return new PlayerPrefsEndingStore();
        }

        readonly IEndingStore store;
        public EndingRecord Record { get; private set; }
        /// <summary>The ending unlocked for the first time in this run (None after GameStarted / when it was seen before).</summary>
        public EndingKind LastNew;
        /// <summary>This run beat the saved best final Luck / tips (false after GameStarted).</summary>
        public bool NewBestLuck, NewBestTips;

        /// <summary>A new run started: nothing new to show yet.</summary>
        public void ClearNew()
        {
            LastNew = EndingKind.None;
            NewBestLuck = NewBestTips = false;
        }

        public EndingCollection(IEndingStore store)
        {
            this.store = store;
            string raw = null;
            try { raw = store?.Load(); }
            catch (Exception e) { Debug.LogWarning("[UI] endings record unreadable: " + e.Message); }
            Record = EndingRecord.Parse(raw);
        }

        public int Unlocked => Record.UnlockedCount;
        public static int Total => EndingRecord.Total;

        /// <summary>One more <paramref name="k"/> reached: counted and saved. True when it is a first unlock.</summary>
        public bool Add(EndingKind k) => Add(k, -1, -1);

        /// <summary>
        /// One more <paramref name="k"/> reached with this run's final Luck and tips earned (negative = not known: no best
        /// record update): counted, bests kept, saved. True when it is a first unlock.
        /// </summary>
        public bool Add(EndingKind k, int finalLuck, int tipsEarned)
        {
            bool first = Record.Add(k);
            LastNew = first ? k : EndingKind.None;
            NewBestLuck = NewBestTips = false;
            if (finalLuck >= 0 && tipsEarned >= 0 && Array.IndexOf(EndingRecord.All, k) >= 0)
            {
                Record.OfferBest(finalLuck, tipsEarned, out bool luck, out bool tips);
                NewBestLuck = luck;
                NewBestTips = tips;
            }
            try { store?.Save(Record.Serialize()); }
            catch (Exception e) { Debug.LogWarning("[UI] endings record not saved: " + e.Message); }
            return first;
        }
    }
}
