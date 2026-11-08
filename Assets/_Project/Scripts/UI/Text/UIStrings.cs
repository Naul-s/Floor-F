namespace FloorF.UI
{
    /// <summary>One UI string in English + Korean. <see cref="Get"/> falls back to English when the Korean text is empty.</summary>
    readonly struct Loc
    {
        public readonly string En;
        public readonly string Ko;
        public Loc(string en, string ko) { En = en; Ko = ko; }
        public string Get(bool korean) => korean && !string.IsNullOrEmpty(Ko) ? Ko : En;
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
        public static readonly Loc RuleLegend = new Loc("<color={0}>gold = lucky</color>    <color={1}><s>red</s> = unlucky</color>", "<color={0}>금색 = 행운</color>    <color={1}><s>빨강</s> = 불길</color>");
        /// <summary>Friday the 13th rule-box line (IDayQuery.IsFriday13).</summary>
        public static readonly Loc RuleFriday13 = new Loc("Friday 13th: travelled 13 = unlucky for ALL", "13일의 금요일: 이동 13 = 모두 불길");
        /// <summary>Friday the 13th HUD badge. {0} = penalty multiplier ("x1.5").</summary>
        public static readonly Loc Friday13Badge = new Loc("FRIDAY THE 13TH · bad luck {0}", "13일의 금요일 · 불운 {0}");
        /// <summary>Friday the 13th intro card line under the data intro. {0} = "x1.5", {1} = "1.5".</summary>
        public static readonly Loc Friday13Intro = new Loc("Today every UNLUCKY moment costs {0} Luck - and travelling 13 floors is unlucky for EVERYONE.",
            "오늘은 불길한 판정마다 운이 {1}배로 깎여요 - 그리고 13층을 이동하면 누구에게나 불길해요.");

        // ---- 666 curse (an unlucky 6 from a devout guest)
        public const string CurseNumber = "666!";
        public static readonly Loc CurseCaption = new Loc("A devout guest: an unlucky 6 counts DOUBLE!", "신앙심 깊은 손님: 불길한 6은 두 배!");

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
            "• Each guest shows their numbers: <color={0}>gold = lucky</color>, <color={1}><s>red</s> = unlucky</color>.\n" +
            "• Doors OPEN on a floor: everyone inside judges that floor's NUMBER.\n" +
            "• Doors CLOSE: everyone inside judges HOW MANY are riding.\n" +
            "• Stepping OUT: a guest judges how many floors they travelled (detours count).\n" +
            "• Take guests to their destination for tips. Luck 0 = haunted hotel!",
            "• 층 버튼으로 이동, 열기/닫기로 문 조작 (또는 W/S + Space).\n" +
            "• 손님 머리 위 숫자: <color={0}>금색 = 행운</color>, <color={1}><s>빨강</s> = 불길</color>.\n" +
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
        public static readonly Loc ShopTitle = new Loc("STICKER SHOP", "스티커 상점");
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

        // ---- title
        public const string GameName = "FLOOR F";
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
        public static readonly Loc CreditsArtHead = new Loc("ART, FONT & ICONS", "그래픽 · 폰트 · 아이콘");
        public static readonly Loc CreditsArt = new Loc(
            "Assets by Kenney (www.kenney.nl) - CC0\n" +
            "Font: Galmuri11 by Lee Minseo (quiple), SIL Open Font License 1.1\n" +
            "Icons: Material Symbols by Google, Apache License 2.0",
            "에셋: Kenney (www.kenney.nl) - CC0\n" +
            "폰트: 갈무리11 (Galmuri11) - 이민서 (quiple), SIL Open Font License 1.1\n" +
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
    }
}
