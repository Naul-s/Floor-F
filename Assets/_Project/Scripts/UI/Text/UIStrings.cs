namespace FloorF.UI
{
    /// <summary>One UI string in English + Korean. <see cref="Get"/> falls back to English when the Korean text is empty.</summary>
    readonly struct Loc
    {
        public readonly string En;
        public readonly string Ko;
        public Loc(string en, string ko)
        {
            En = en;
            Ko = ko;
            (all ?? (all = new System.Text.StringBuilder(8192))).Append(en).Append(ko); // font prewarm (UIStrings.Characters)
        }
        public string Get(bool korean) => korean && !string.IsNullOrEmpty(Ko) ? Ko : En;

        static System.Text.StringBuilder all;
        /// <summary>Every Loc text built so far, both languages (no reflection: WebGL/IL2CPP safe).</summary>
        internal static string AllText => all != null ? all.ToString() : "";
    }

    /// <summary>
    /// Every string the UI shows (data text - guest/destination/day/sticker names - comes from GameData *Ko fields).
    /// Korean needs a Hangul-capable TMP font (HANDOFF: orchestrator imports e.g. Galmuri, OFL).
    /// Format placeholders use string.Format ({0}, {1}). A Korean particle after a placeholder is a format specifier
    /// picked by how the value is read ("{0:은}" -> "3은" / "2는", see KoreanParticles) - never "은(는)".
    /// </summary>
    static class UIStrings
    {
        // ---- HUD
        public static readonly Loc DayOf = new Loc("DAY {0}/{1}", "{0}/{1}일차");
        public static readonly Loc Luck = new Loc("LUCK", "운");
        public static readonly Loc Tips = new Loc("TIPS", "팁");
        public static readonly Loc Language = new Loc("EN | ko", "en | 한국어");
        public static readonly Loc RulesTitle = new Loc("HOW LUCK WORKS", "운 판정");
        public static readonly Loc RuleOpen = new Loc("Doors OPEN  >  riders judge the FLOOR number", "문이 열리면  >  탑승객이 층 번호로 판정");
        public static readonly Loc RuleClose = new Loc("Doors CLOSE  >  riders judge the HEAD COUNT", "문이 닫히면  >  탑승객이 인원수로 판정");
        public static readonly Loc RuleDistance = new Loc("Step OUT  >  they judge the FLOORS TRAVELLED", "내릴 때  >  탄 뒤 움직인 층수로 판정");
        public static readonly Loc RuleLegend = new Loc("<color={0}>gold: lucky</color>    <color={1}>red: unlucky</color>", "<color={0}>금색: 행운</color>    <color={1}>빨강: 불길</color>");
        /// <summary>Friday the 13th rule-box line (IDayQuery.IsFriday13).</summary>
        public static readonly Loc RuleFriday13 = new Loc("Friday 13th: travelled 13 = unlucky for ALL", "13일의 금요일: 이동 13 = 모두 불길");
        /// <summary>Friday the 13th HUD badge. {0} = penalty multiplier ("x1.5").</summary>
        public static readonly Loc Friday13Badge = new Loc("FRIDAY 13TH · bad luck {0}", "13일의 금요일 · 불운 {0}");
        /// <summary>Friday the 13th intro card line under the data intro. {0} = the penalty multiplier ("1.5").</summary>
        public static readonly Loc Friday13Intro = new Loc("Today every UNLUCKY moment costs {0}x the Luck - and travelling 13 floors is unlucky for EVERYONE.",
            "오늘은 불길한 판정마다 운이 {0}배로 깎여요 - 그리고 13층을 이동하면 누구에게나 불길해요.");

        /// <summary>Rainy day intro line (IDayQuery.IsRainy, presentation only).</summary>
        public static readonly Loc RainIntro = new Loc("Rain tonight - thunder rolls in the distance.", "오늘 밤은 비 - 멀리서 천둥이 울려요.");

        // ---- star night (IDayQuery.IsStarNight, Day 7): 7 is lucky for everyone, wish stars
        /// <summary>Star-night rule-box line.</summary>
        public static readonly Loc RuleStarNight = new Loc("Star night: 7 = lucky for ALL", "별의 밤: 7 = 모두 행운");
        /// <summary>Star-night HUD badge.</summary>
        public static readonly Loc StarNightBadge = new Loc("STAR NIGHT · 7 lucky for all", "별이 쏟아지는 밤 · 7 = 모두 행운");
        /// <summary>Star-night intro card line under the data intro. {0} = Luck per wish.</summary>
        public static readonly Loc StarNightIntro = new Loc("Tonight 7 is LUCKY for everyone. Shooting stars will cross the sky - click one (or press E) to make a wish: +{0} Luck!",
            "오늘 밤은 7이 누구에게나 행운이에요. 별똥별이 지나가면 클릭(또는 E)해서 소원을 비세요: 운 +{0}!");
        /// <summary>Hint next to a wish star until the first wish of the run.</summary>
        public static readonly Loc WishHint = new Loc("Make a wish! [E]", "소원을 빌어요! [E]");
        /// <summary>Float-up when a wish comes true. {0} = Luck gained.</summary>
        public static readonly Loc WishGranted = new Loc("Wish granted! +{0} Luck", "소원 성취! +{0} 운");
        /// <summary>One-time explain card on star night (tutorial hints on).</summary>
        public static readonly Loc WishCardTitle = new Loc("WISH STARS", "소원의 별");
        /// <summary>{0} = Luck per wish.</summary>
        public static readonly Loc WishCardBody = new Loc("Shooting stars cross the sky tonight - only for a moment! Click one (or press E) to make a wish: +{0} Luck.",
            "오늘 밤엔 별똥별이 하늘을 가로질러요 - 아주 잠깐! 클릭(또는 E)해서 소원을 빌면 운 +{0}.");

        // ---- day-start banner (Running begins)
        public static readonly Loc DayBanner = new Loc("DAY {0}", "{0}일차");
        public static readonly Loc FinalDayBanner = new Loc("FINAL DAY", "마지막 날");

        // ---- 666 curse (an unlucky 6 from a devout guest)
        public const string CurseNumber = "666!";
        public static readonly Loc CurseCaption = new Loc("Devout guest: unlucky 6 counts DOUBLE!", "신앙심 깊은 손님: 불길한 6은 두 배!");

        // ---- elevator panel
        public static readonly Loc Open = new Loc("OPEN", "열기");
        public static readonly Loc Close = new Loc("CLOSE", "닫기");
        public static readonly Loc InCar = new Loc("IN CAR", "탑승");
        public static readonly Loc PanelHint = new Loc("[W/S] move  [Space] doors", "[W/S] 이동  [Space] 문");

        // ---- popups
        public static readonly Loc LuckyWord = new Loc("LUCKY!", "행운!");
        public static readonly Loc UnluckyWord = new Loc("UNLUCKY!", "불길!");
        public static readonly Loc FloorN = new Loc("floor {0}", "{0}층");
        public static readonly Loc PeopleOne = new Loc("{0} person", "{0}명");
        public static readonly Loc PeopleN = new Loc("{0} people", "{0}명");
        public static readonly Loc RodeOne = new Loc("travelled {0} floor", "{0}층 이동");
        public static readonly Loc RodeN = new Loc("travelled {0} floors", "{0}층 이동");

        // ---- flow
        public static readonly Loc Start = new Loc("START  [Enter]", "시작  [Enter]");
        public static readonly Loc Continue = new Loc("CONTINUE  [Enter]", "계속  [Enter]");
        public static readonly Loc NextDay = new Loc("NEXT DAY  [Enter]", "다음 날  [Enter]");
        public static readonly Loc PlayAgain = new Loc("PLAY AGAIN  [Enter]", "다시 하기  [Enter]");
        public static readonly Loc HowToTitle = new Loc("HOW TO PLAY", "플레이 방법");
        public static readonly Loc HowTo = new Loc(
            "• Click the floor buttons to move, OPEN / CLOSE for the doors (or W/S + Space).\n" +
            "• Each guest shows their numbers: <color={0}>gold = lucky</color>, <color={1}>red = unlucky</color>.\n" +
            "• Doors OPEN on a floor: everyone inside judges that floor's NUMBER.\n" +
            "• Doors CLOSE: everyone inside judges HOW MANY are riding.\n" +
            "• Stepping OUT: a guest judges how many floors they travelled (detours count).\n" +
            "• Take guests to their destination for tips. Luck 0 = haunted hotel!",
            "• 층 버튼으로 이동, 열기/닫기로 문 조작 (또는 W/S + Space).\n" +
            "• 손님 머리 위 숫자: <color={0}>금색 = 행운</color>, <color={1}>빨강 = 불길</color>.\n" +
            "• 문이 열리면: 안에 있는 모두가 그 층의 번호로 판정.\n" +
            "• 문이 닫히면: 안에 있는 모두가 탑승 인원수로 판정.\n" +
            "• 내릴 때: 탄 뒤 움직인 층수로 판정 (돌아가도 다 셈).\n" +
            "• 목적지에 내려주면 팁. 운이 0이 되면 흉가 엔딩!");
        public static readonly Loc DayComplete = new Loc("DAY {0} COMPLETE", "{0}일차 종료");
        public static readonly Loc Delivered = new Loc("Guests delivered", "배달한 손님");
        public static readonly Loc Left = new Loc("Guests who left", "떠난 손님");
        public static readonly Loc TipsEarned = new Loc("Tips earned", "번 팁");
        public static readonly Loc LuckLine = new Loc("Luck", "운");
        public static readonly Loc Judgements = new Loc("Lucky / unlucky moments", "행운 / 불길 판정");

        // ---- shop
        public static readonly Loc ShopTitle = new Loc("LUCKY SHOP", "행운 상점");
        public static readonly Loc ShopHint = new Loc("Pick a sticker, then click a glowing floor button.", "스티커를 고르고, 빛나는 층 버튼을 누르세요.");
        public static readonly Loc ShopPickFloor = new Loc("Now click a glowing floor button on the panel.", "이제 패널에서 빛나는 층 버튼을 누르세요.");
        public static readonly Loc PriceStock = new Loc("${0}   {1} left", "${0}   {1}개 남음");
        public static readonly Loc PriceUnlimited = new Loc("${0}", "${0}");
        public static readonly Loc SoldOut = new Loc("SOLD OUT", "품절");
        public static readonly Loc NeedMore = new Loc("need +${0}", "${0} 부족");
        public static readonly Loc ResultApplied = new Loc("Floor {0} is now {1}!", "{0}층이 이제 {1}층!");
        /// <summary>{2} = destination (InlineIcon markup: icon + name).</summary>
        public static readonly Loc ResultAppliedAt = new Loc("Floor {0} ({2}) is now {1}!", "{0}층({2})이 이제 {1}층!");
        public static readonly Loc ResultNotEnough = new Loc("Not enough tips.", "팁이 부족합니다.");
        public static readonly Loc ResultNotApplicable = new Loc("That sticker does not fit that floor.", "그 층에는 붙일 수 없습니다.");
        public static readonly Loc ResultOutOfStock = new Loc("Sold out.", "품절입니다.");
        public static readonly Loc ResultOther = new Loc("Can't do that now.", "지금은 할 수 없습니다.");
        /// <summary>Card kind badges (StickerDef.kind) and the one-day tag on every card.</summary>
        public static readonly Loc KindLabel = new Loc("STICKER", "스티커");
        public static readonly Loc KindCharm = new Loc("CHARM", "부적");
        public static readonly Loc OneDay = new Loc("1 DAY", "하루");
        public static readonly Loc StockLeft = new Loc("{0} left", "{0}개 남음");
        public static readonly Loc Owned = new Loc("BOUGHT", "구입함");
        /// <summary>The staple (4 -> F for the real 4th floor): offered every visit, survives rerolls.</summary>
        public static readonly Loc Staple = new Loc("always here", "항상 있음");
        /// <summary>Shop hint: labels need a floor, charms are bought with one click (the 1 DAY tags + TOMORROW strip say how long).</summary>
        public static readonly Loc ShopHintCharms = new Loc("Sticker: pick it, then a glowing floor. Charm: one click.",
            "스티커: 고른 뒤 빛나는 층 버튼. 부적: 한 번 클릭.");
        /// <summary>{0} = charm name.</summary>
        public static readonly Loc CharmBoughtResult = new Loc("{0} bought - it works all of tomorrow!", "{0} 구입 - 내일 하루 동안 효과!");
        public static readonly Loc ResultAlreadyActive = new Loc("Already active for tomorrow.", "이미 내일 효과가 있어요.");
        public static readonly Loc ResultWrongKind = new Loc("Charms need no floor - just click the card.", "부적은 층이 필요 없어요 - 카드를 누르세요.");
        /// <summary>{0} = cost, {1} = rerolls left.</summary>
        public static readonly Loc Reroll = new Loc("REROLL ${0} ({1})", "다시 뽑기 ${0} ({1})");
        public static readonly Loc RerollNone = new Loc("NO REROLLS LEFT", "다시 뽑기 끝");
        /// <summary>Under the greyed reroll button. {0} = missing tips.</summary>
        public static readonly Loc RerollNeed = new Loc("need +${0}", "${0} 부족");
        public static readonly Loc RerollDone = new Loc("New offers!", "새 상품!");
        public static readonly Loc Tomorrow = new Loc("TOMORROW", "내일");
        public static readonly Loc TomorrowNone = new Loc("nothing yet", "아직 없음");

        // ---- charms during the day (IShopService.ActiveCharms) + CharmTriggered
        public static readonly Loc CharmsToday = new Loc("TODAY'S CHARMS", "오늘의 부적");
        public static readonly Loc CharmUsed = new Loc("used", "사용함");
        /// <summary>Shield pop near the guest. {0} = charm name.</summary>
        public static readonly Loc CharmBlocked = new Loc("{0} blocked it!", "{0:이} 막았다!");
        /// <summary>Any other charm effect that helped. {0} = charm name.</summary>
        public static readonly Loc CharmHelped = new Loc("{0} helped!", "{0} 효과!");
        /// <summary>Verdict word on the luck popup for an Unlucky verdict a shield ate.</summary>
        public static readonly Loc BlockedWord = new Loc("BLOCKED!", "막았다!");
        public static readonly Loc NoLuckLost = new Loc("no luck lost", "운 그대로");
        /// <summary>Charm effect lines (StickerEffects). {0} = percent / count.</summary>
        public static readonly Loc EffectPatience = new Loc("Guests wait {0}% longer", "손님이 {0}% 더 오래 기다림");
        public static readonly Loc EffectSpeed = new Loc("Elevator {0}% faster", "엘리베이터 {0}% 빠르게");
        public static readonly Loc EffectShieldOne = new Loc("Blocks the first UNLUCKY moment", "첫 불길 판정 1번을 막음");
        public static readonly Loc EffectShieldN = new Loc("Blocks the first {0} UNLUCKY moments", "첫 불길 판정 {0}번을 막음");
        public static readonly Loc EffectTips = new Loc("Tips +{0}%", "팁 +{0}%");

        // ---- day end: one-day purchases ran out (StickersExpired)
        public static readonly Loc StickersExpired = new Loc("Stickers and charms wore off - the shop has new ones.", "스티커와 부적은 오늘로 끝 - 상점에 새 상품이 있어요.");
        public static readonly Loc StickersExpiredLabels = new Loc("Stickers wore off: every floor shows its real number again.", "스티커는 오늘로 끝: 모든 층이 원래 번호로 돌아갔어요.");
        public static readonly Loc CharmsExpired = new Loc("Charms wore off.", "부적은 오늘로 끝.");

        // ---- endings
        public static readonly Loc EndProsperousTitle = new Loc("THE LUCKIEST HOTEL IN TOWN", "동네에서 가장 운 좋은 호텔");
        public static readonly Loc EndProsperousText = new Loc(
            "Word spreads about the elevator that never stops on a bad number. Guests book months ahead - and ask for you by name.",
            "나쁜 숫자에는 절대 서지 않는 엘리베이터라는 소문이 퍼졌다. 몇 달 치 예약이 꽉 찼고, 다들 당신을 찾는다.");
        public static readonly Loc EndModestTitle = new Loc("BUSINESS AS USUAL", "평범한 영업");
        public static readonly Loc EndModestText = new Loc(
            "Every day survived. The hotel is fine. Just... fine. Maybe more stickers next season?",
            "모든 날을 버텼다. 호텔은 괜찮다. 그냥... 괜찮다. 다음 시즌엔 스티커를 더?");
        public static readonly Loc EndHauntedTitle = new Loc("THE HAUNTED HOTEL", "흉가 호텔");
        public static readonly Loc EndHauntedText = new Loc(
            "Too many bad numbers, too many screams. Rumour says the elevator is cursed, and nobody checks in anymore.",
            "나쁜 숫자, 비명이 너무 많았다. 엘리베이터가 저주받았다는 소문에 이제 아무도 체크인하지 않는다.");
        public static readonly Loc EndFourthTitle = new Loc("THE FOURTH FLOOR", "4층");
        public static readonly Loc EndFourthText = new Loc(
            "You covered the 4. It was still there. The stranger stepped out onto the floor you hid, smiled... and the lights went out.",
            "4를 가렸지만, 4층은 여전히 거기 있었다. 낯선 손님은 당신이 감춘 그 층에 내려 웃었다... 그리고 불이 꺼졌다.");
        public static readonly Loc FinalStats = new Loc("Final luck {0}   •   Tips ${1}", "최종 운 {0}   •   팁 ${1}");
        public static readonly Loc EndDayReached = new Loc("Days run", "버틴 날");
        public static readonly Loc EndFinalLuck = new Loc("Final luck", "최종 운");
        public static readonly Loc EndTips = new Loc("Tips", "팁");
        public static readonly Loc ToTitle = new Loc("TITLE", "타이틀");

        // ---- endings collection (EndingRecord: title ENDINGS card, ending card badge + unlock line)
        /// <summary>Badge on the 4th-floor ending (it overrides the Luck endings).</summary>
        public static readonly Loc HiddenEnding = new Loc("HIDDEN ENDING", "히든 엔딩");
        /// <summary>Ending card line. {0} = unlocked, {1} = total.</summary>
        public static readonly Loc EndingsUnlocked = new Loc("Endings {0}/{1} unlocked", "엔딩 {0}/{1} 해금");
        public static readonly Loc EndingNew = new Loc("NEW!", "새 엔딩!");
        /// <summary>Title button. {0} = unlocked, {1} = total.</summary>
        public static readonly Loc EndingsButton = new Loc("ENDINGS {0}/{1}", "엔딩 {0}/{1}");
        public static readonly Loc EndingsTitle = new Loc("ENDINGS", "엔딩 모음");
        public static readonly Loc EndingsProgress = new Loc("{0} of {1} found", "{1}개 중 {0}개 발견");
        public const string EndingLocked = "???";
        /// <summary>Times an ending was reached. {0} = count.</summary>
        public static readonly Loc EndingSeen = new Loc("Seen x{0}", "{0}회 달성");
        public static readonly Loc EndNameProsperous = new Loc("PROSPEROUS", "번창");
        public static readonly Loc EndNameModest = new Loc("MODEST", "보통");
        public static readonly Loc EndNameHaunted = new Loc("HAUNTED", "흉가");
        public static readonly Loc EndNameFourth = new Loc("4TH FLOOR", "4층");
        public static readonly Loc EndShortProsperous = new Loc("Guests book months ahead - and ask for you by name.",
            "몇 달 치 예약이 꽉 찼고, 다들 당신을 찾는다.");
        public static readonly Loc EndShortModest = new Loc("The hotel is fine. Just... fine.", "호텔은 괜찮다. 그냥... 괜찮다.");
        public static readonly Loc EndShortHaunted = new Loc("Rumour says the elevator is cursed.", "엘리베이터가 저주받았다는 소문이 돈다.");
        public static readonly Loc EndShortFourth = new Loc("You covered the 4. It was still there.", "4를 가렸지만, 4층은 여전히 거기 있었다.");
        /// <summary>Locked-slot hints. {0} = last day number, {1} = prosperous-ending Luck.</summary>
        public static readonly Loc EndHintProsperous = new Loc("Finish Day {0} with Luck {1} or more.", "{0}일차를 운 {1} 이상으로 마치기.");
        public static readonly Loc EndHintModest = new Loc("Finish Day {0} with Luck under {1}.", "{0}일차를 운 {1} 미만으로 마치기.");
        public static readonly Loc EndHintHaunted = new Loc("Let the Luck fall to 0.", "운이 0까지 떨어지면...");
        public static readonly Loc EndHintFourth = new Loc("Some floors are only hidden, never gone…", "가려진 층은 사라지지 않는다…");

        // ---- title
        public const string GameName = "FLOOR F";
        /// <summary>Brass plate under the title logo.</summary>
        public static readonly Loc Tagline = new Loc("A LUCKY NUMBER HOTEL", "행운의 숫자 호텔");
        public static readonly Loc Credits = new Loc("Made for the 2026 Unity Game Jam   •   music, sound, font & icons: see CREDITS",
            "2026 Unity Game Jam 출품작   •   음악·효과음·폰트·아이콘: 크레딧 참고");
        public static readonly Loc CreditsButton = new Loc("CREDITS", "크레딧");
        public static readonly Loc Back = new Loc("BACK  [Esc]", "뒤로  [Esc]");

        // ---- credits screen (source: Docs/CREDITS.md; the CC BY block is the licensor's required wording, never translated)
        public static readonly Loc CreditsTitle = new Loc("CREDITS", "크레딧");
        public static readonly Loc CreditsMusicHead = new Loc("MUSIC", "음악");
        public const string CreditsMusicCcBy =
            "\"Local Forecast - Elevator\" Kevin MacLeod (incompetech.com)\n" +
            "Licensed under Creative Commons: By Attribution 4.0\n" +
            "https://creativecommons.org/licenses/by/4.0/";
        public static readonly Loc CreditsSoundHead = new Loc("SOUND EFFECTS", "효과음");
        public static readonly Loc CreditsSound = new Loc(
            "Freesound (CC0): LilMati, PagDev, LaurenPonder, egomassive, danlucaz, BlondPanda\n" +
            "Kenney Interface Sounds & Casino Audio (CC0)",
            "Freesound (CC0): LilMati, PagDev, LaurenPonder, egomassive, danlucaz, BlondPanda\n" +
            "Kenney Interface Sounds · Casino Audio (CC0)");
        public static readonly Loc CreditsArtHead = new Loc("ART, FONTS & ICONS", "그래픽 · 폰트 · 아이콘");
        public static readonly Loc CreditsArt = new Loc(
            "Assets by Kenney (www.kenney.nl) - CC0\n" +
            "Fonts: Mulmaru by mushsooni, SIL Open Font License 1.1\n" +
            "Galmuri11 / Galmuri11 Bold by Lee Minseo (quiple), SIL Open Font License 1.1\n" +
            "Icons: Material Symbols by Google, Apache License 2.0",
            "에셋: Kenney (www.kenney.nl) - CC0\n" +
            "폰트: 물마루 (Mulmaru) - mushsooni, SIL Open Font License 1.1\n" +
            "Galmuri11 / Galmuri11 Bold (갈무리) - 이민서 (quiple), SIL Open Font License 1.1\n" +
            "아이콘: Google Material Symbols, Apache License 2.0");
        public static readonly Loc CreditsAi = new Loc(
            "Made with the help of Claude (Anthropic): code, text, balancing and procedural visuals.",
            "Claude(Anthropic)의 도움으로 제작: 코드·문구·밸런싱·절차적 그래픽");
        public static readonly Loc CreditsJam = new Loc("Made for the 2026 Unity Game Jam - theme \"Lucky Number\"",
            "2026 Unity Game Jam 출품작 - 주제 \"Lucky Number\"");

        // ---- pause
        public static readonly Loc Paused = new Loc("PAUSED", "일시정지");
        public static readonly Loc Resume = new Loc("RESUME  [Esc]", "계속하기  [Esc]");
        public static readonly Loc Volume = new Loc("VOLUME", "볼륨");
        public static readonly Loc HintsOn = new Loc("TUTORIAL HINTS: ON", "튜토리얼 힌트: 켜짐");
        public static readonly Loc HintsOff = new Loc("TUTORIAL HINTS: OFF", "튜토리얼 힌트: 꺼짐");
        public static readonly Loc QuitToTitle = new Loc("QUIT TO TITLE", "타이틀로");

        // ---- Day 0 (tutorial day) HUD / flow
        public static readonly Loc DayOnly = new Loc("DAY {0}", "{0}일차");
        public static readonly Loc Practice = new Loc("PRACTICE", "연습");
        public static readonly Loc SkipTutorial = new Loc("SKIP TUTORIAL [Tab]", "튜토리얼 건너뛰기 [Tab]");
        public static readonly Loc TrainingDone = new Loc("TRAINING SHIFT DONE", "수습 근무 끝");
        public static readonly Loc PracticeReset = new Loc("Practice Luck and tips reset when Day 1 starts.",
            "연습으로 얻은 운과 팁은 1일차가 시작될 때 처음으로 돌아가요.");
        /// <summary>The closing lines of the training shift. {0} = last day number, {1} = prosperous-ending Luck.</summary>
        public static readonly Loc TutRules = new Loc(
            "• Luck 0 = the Haunted ending. Game over!\n" +
            "• After Day {0}: Luck {1}+ = the prosperous ending.\n" +
            "• From Day 1 the clock runs - guests who wait too long leave.",
            "• 운이 0이 되면 흉가 엔딩. 게임 오버!\n" +
            "• {0}일차를 마쳤을 때 운 {1} 이상 = 번창 엔딩.\n" +
            "• 1일차부터는 시간이 흐르고, 오래 기다린 손님은 떠나요.");

        // ---- Day 0 lesson prompts (shown while IGameSettings.ShowTutorialHints && IDayQuery.IsTutorial)
        public static readonly Loc TutWave = new Loc("TRAINING {0}/{1}", "수습 {0}/{1}");
        public static readonly Loc TutDoneTitle = new Loc("THAT'S THE JOB!", "수습 완료!");
        /// <summary>{0} = floor label, {1} = how many wait there.</summary>
        public static readonly Loc TutBoardHereOne = new Loc("A guest is waiting here on floor {0}. OPEN the doors to let them in.",
            "{0}층에 손님이 기다려요. 문을 열어(열기) 태우세요.");
        public static readonly Loc TutBoardHereMany = new Loc("{1} guests are waiting here on floor {0}. OPEN the doors to let them in.",
            "{0}층에 손님 {1}명이 기다려요. 문을 열어(열기) 태우세요.");
        public static readonly Loc TutBoardGo = new Loc("Guests are waiting on floor {0}. Press {0} to go there first.",
            "{0}층에 손님이 기다려요. 먼저 {0}층 버튼을 눌러 가세요.");
        /// <summary>{0} = lucky numbers (gold markup), {1} = unlucky numbers (red markup).</summary>
        public static readonly Loc TutChips = new Loc("Read the chip: {0} = their LUCKY number, {1} = their UNLUCKY number, the icon = where they're going.",
            "칩을 보세요: {0} = 행운 숫자, {1} = 불길한 숫자, 아이콘 = 가려는 곳.");
        /// <summary>{0} = head count.</summary>
        public static readonly Loc TutCloseNeutral = new Loc("CLOSE the doors. When they close, everyone inside judges the HEAD COUNT - {0} inside means nothing to them.",
            "문을 닫으세요(닫기). 닫힐 때 안의 모두가 인원수로 판정해요. {0}명은 아무 의미 없는 숫자예요.");
        public static readonly Loc TutCloseLucky = new Loc("Everyone's in! CLOSE the doors: {0} inside is a LUCKY head count for them!",
            "모두 탔어요! 문을 닫으세요(닫기). {0}명은 행운의 인원수!");
        public static readonly Loc TutCloseUnlucky = new Loc("CLOSE the doors - careful: {0} inside is an UNLUCKY head count for someone.",
            "문을 닫으세요(닫기). 조심: {0}명은 누군가에게 불길한 인원수예요.");
        public static readonly Loc TutCloseOn = new Loc("CLOSE the doors and take the others on. The head count is judged again!",
            "문을 닫고(닫기) 나머지 손님을 데려가세요. 인원수 판정이 또 있어요!");
        /// <summary>{0} = group size.</summary>
        public static readonly Loc TutWaitAll = new Loc("{0} guests together this time. Wait until everyone is in, then CLOSE.",
            "이번엔 {0}명이 함께 타요. 모두 탈 때까지 기다린 뒤 닫으세요.");
        public static readonly Loc TutWaitAllLucky = new Loc("{0} guests together this time. Wait until everyone is in, then CLOSE: {0} inside = their lucky number!",
            "이번엔 {0}명이 함께 타요. 모두 탈 때까지 기다린 뒤 닫으세요. {0}명 = 행운의 숫자!");
        /// <summary>{1} = destination (InlineIcon markup: icon + name).</summary>
        public static readonly Loc TutFloor = new Loc("Press floor {0} ({1}) to take them there.",
            "{0}층({1}) 버튼을 눌러 데려다주세요.");
        public static readonly Loc TutFloorTwo = new Loc("Two riders, two stops. Every time the doors open, EVERYONE inside judges that floor. Press floor {0} ({1}).",
            "두 손님, 두 목적지. 문이 열릴 때마다 안의 모두가 그 층 번호로 판정해요. {0}층({1}) 버튼을 누르세요.");
        public static readonly Loc TutOpenThere = new Loc("Arrived! OPEN the doors: everyone inside judges this FLOOR's number!",
            "도착! 문을 여세요(열기). 안에 있는 모두가 이 층의 번호로 판정해요!");
        /// <summary>{0} = floor label.</summary>
        public static readonly Loc TutOpenAll = new Loc("Arrived! OPEN the doors: EVERYONE inside judges floor {0} - not only the one getting off.",
            "도착! 문을 여세요(열기). 내리는 손님만이 아니라 안의 모두가 {0}층으로 판정해요.");
        /// <summary>Doors closed at a floor no rider wants. {0} = a rider's floor label, {1} = its destination (InlineIcon markup).</summary>
        public static readonly Loc TutNotTheirStop = new Loc("This isn't anyone's stop - opening here still counts the floor number! Their floor: {0} ({1}).",
            "여긴 아무도 내리지 않는 층! 그래도 문을 열면 이 층 번호로 판정돼요. 목적지: {0}층({1})");
        public static readonly Loc TutNextWave = new Loc("Nice! More guests are on the way...", "잘했어요! 다음 손님이 와요...");
        /// <summary>{0} = the judged number (floor label or head count).</summary>
        public static readonly Loc TutWhyLuckyFloor = new Loc("LUCKY! {0} is their lucky number: Luck goes UP (meter, top right) and they tip more.",
            "행운! {0:은} 이 손님의 행운 숫자: 운이 올라가고(오른쪽 위 게이지) 팁도 더 줘요.");
        public static readonly Loc TutWhyLuckyCount = new Loc("LUCKY! {0} inside is their lucky number: Luck goes UP (meter, top right).",
            "행운! {0}명은 이 손님의 행운 숫자: 운이 올라가요(오른쪽 위 게이지).");
        public static readonly Loc TutWhyUnluckyFloor = new Loc("UNLUCKY! {0} is their unlucky number: Luck goes DOWN (meter, top right).",
            "불길! {0:은} 이 손님의 불길한 숫자: 운이 내려가요(오른쪽 위 게이지).");
        public static readonly Loc TutWhyUnluckyCount = new Loc("UNLUCKY! {0} inside is their unlucky number: Luck goes DOWN (meter, top right).",
            "불길! {0}명은 이 손님의 불길한 숫자: 운이 내려가요(오른쪽 위 게이지).");
        /// <summary>Rule 3 (LuckSource.RideDistance). {0} = floors travelled.</summary>
        public static readonly Loc TutWhyLuckyDistance = new Loc("LUCKY! They travelled {0} floors since getting in - their lucky number: Luck goes UP.",
            "행운! 탄 뒤 {0}층을 움직였어요 - 이 손님의 행운 숫자: 운이 올라가요.");
        public static readonly Loc TutWhyUnluckyDistance = new Loc("UNLUCKY! They travelled {0} floors since getting in - their unlucky number: Luck goes DOWN.",
            "불길! 탄 뒤 {0}층을 움직였어요 - 이 손님의 불길한 숫자: 운이 내려가요.");
        public static readonly Loc TutWhyDistanceNote = new Loc("Detours count too: the ↕ number on a rider's chip shows it.",
            "돌아간 층도 다 세요: 탑승객 칩의 ↕ 숫자를 보세요.");
        public static readonly Loc TutWhySticker = new Loc("Later, stickers from the shop can relabel a floor (4 → F).",
            "나중에 상점 스티커로 층 번호를 바꿀 수 있어요 (4 → F).");

        // ---- Day 0 coach marks (explain steps wait for a click; action steps for the highlighted button)
        public static readonly Loc TutContinue = new Loc("Click to continue ▶", "클릭해서 계속 ▶");
        /// <summary>{0} = streak, {1} = tip multiplier ("x1.3").</summary>
        public static readonly Loc TutStreak = new Loc("LUCKY STREAK x{0}! Lucky moments in a row raise every tip (now {1}). One UNLUCKY moment breaks it.",
            "연속 행운 x{0}! 행운이 이어질수록 팁이 늘어요(지금 {1}). 불길한 판정 한 번이면 끊겨요.");
        /// <summary>Closing page 0 (points at the rule box): rule 3, ride distance.</summary>
        public static readonly Loc TutEndDistance = new Loc(
            "• One more rule: stepping OUT, a guest judges how many floors they travelled since getting in - detours count!\n" +
            "• The ↕ number on a rider's chip counts them.",
            "• 규칙 하나 더: 내릴 때, 탄 뒤 움직인 층수로 판정해요. 돌아간 층도 다 세요!\n" +
            "• 탑승객 칩의 ↕ 숫자가 그 층수예요.");
        /// <summary>Closing page 1 (points at the Luck meter). {0} = last day number, {1} = prosperous-ending Luck.</summary>
        public static readonly Loc TutEndLuck = new Loc(
            "• Luck 0 = the Haunted ending. Game over!\n" +
            "• After Day {0}: Luck {1}+ = the prosperous ending.",
            "• 운이 0이 되면 흉가 엔딩. 게임 오버!\n" +
            "• {0}일차를 마쳤을 때 운 {1} 이상 = 번창 엔딩.");
        /// <summary>Closing page 2 (points at the clock); the practice-reset line follows it.</summary>
        public static readonly Loc TutEndClock = new Loc(
            "• From Day 1 the clock runs - guests who wait too long leave.",
            "• 1일차부터는 시간이 흐르고, 오래 기다린 손님은 떠나요.");

        // ---- lucky streak (HUD readout under the tips) + day grade
        /// <summary>{0} = streak, {1} = tip multiplier ("x1.3").</summary>
        public static readonly Loc Streak = new Loc("Lucky streak x{0} · tips {1}", "연속 행운 x{0} · 팁 {1}");
        public static readonly Loc StreakBroken = new Loc("Streak broken!", "연속 끊김!");
        public static readonly Loc BestStreak = new Loc("Best lucky streak", "최고 연속 행운");

        /// <summary>Every character of every UI string (en + ko, constants included) - what UIFonts prewarms.</summary>
        public static string Characters
        {
            get
            {
                string touch = Start.En; // a static field read runs the whole initializer: every Loc above is registered
                return touch + Loc.AllText + GameName + CurseNumber + CreditsMusicCcBy + EndingLocked + UIFormat.DistanceGlyph;
            }
        }
    }
}
