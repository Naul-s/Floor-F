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
        public static readonly Loc RuleLegend = new Loc("<color={0}>gold = lucky</color>    <color={1}><s>red</s> = unlucky</color>", "<color={0}>금색 = 행운</color>    <color={1}><s>빨강</s> = 불길</color>");

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
            "• Take guests to their destination for tips. Luck 0 = haunted hotel!",
            "• 층 버튼으로 이동, 열기/닫기로 문 조작 (또는 W/S + Space).\n" +
            "• 손님 머리 위 숫자: <color={0}>금색 = 행운</color>, <color={1}><s>빨강</s> = 불길</color>.\n" +
            "• 문이 열리면: 안에 있는 모두가 그 층의 번호로 판정.\n" +
            "• 문이 닫히면: 안에 있는 모두가 탑승 인원수로 판정.\n" +
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
        public static readonly Loc Subtitle = new Loc("A hotel where the 4th floor is bad luck. Every number counts.", "4층은 불길한 호텔. 모든 숫자가 운을 가른다.");
        public static readonly Loc TitleHint = new Loc("You run the elevator. Keep the guests lucky.", "당신은 엘리베이터 담당. 손님들의 운을 지켜라.");
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

        // ---- day-1 tutorial prompts (shown while IGameSettings.ShowTutorialHints && IDayQuery.IsTutorial)
        public static readonly Loc TutStep = new Loc("TUTORIAL {0}/{1}", "튜토리얼 {0}/{1}");
        public static readonly Loc TutBoardHere = new Loc("Guests are waiting on floor {0}. OPEN the doors to let them in.",
            "{0}층에 손님이 기다려요. 문을 열어(열기) 태우세요.");
        public static readonly Loc TutBoardGo = new Loc("Guests are waiting on floor {0}. Press floor {0} to go there.",
            "{0}층에 손님이 기다려요. {0}층 버튼을 눌러 가세요.");
        public static readonly Loc TutClose = new Loc("Now CLOSE the doors: everyone inside judges the HEAD COUNT!",
            "이제 문을 닫으세요(닫기). 안에 있는 모두가 인원수로 운을 판정!");
        /// <summary>{1} = destination (InlineIcon markup: icon + name).</summary>
        public static readonly Loc TutFloor = new Loc("Press floor {0} ({1}) to take them there.",
            "{0}층({1}) 버튼을 눌러 데려다주세요.");
        public static readonly Loc TutOpenThere = new Loc("Arrived! OPEN the doors: everyone inside judges this FLOOR's number!",
            "도착! 문을 여세요(열기). 안에 있는 모두가 이 층의 번호로 운을 판정!");
        /// <summary>Doors closed at a floor no rider wants. {0} = a rider's floor label, {1} = its destination (InlineIcon markup).</summary>
        public static readonly Loc TutNotTheirStop = new Loc("This isn't anyone's stop - opening here still counts the floor number! Their floor: {0} ({1}).",
            "여긴 아무도 내리지 않는 층! 그래도 문을 열면 이 층 번호로 판정돼요. 목적지: {0}층({1})");
        public static readonly Loc TutDone = new Loc("That's the job! Deliver every guest for tips. Luck 0 = haunted hotel.",
            "이게 전부예요! 모든 손님을 데려다주고 팁을 받으세요. 운이 0이면 흉가!");
        public static readonly Loc TutWhyLucky = new Loc("LUCKY! {0} is a lucky number for that guest: Luck goes UP (meter, top right).",
            "행운! {0:은} 그 손님의 행운 숫자: 운이 올라가요 (오른쪽 위 게이지).");
        public static readonly Loc TutWhyUnlucky = new Loc("UNLUCKY! {0} is a bad number for that guest: Luck goes DOWN (meter, top right).",
            "불길! {0:은} 그 손님의 불길한 숫자: 운이 내려가요 (오른쪽 위 게이지).");
        public static readonly Loc TutWhyLegend = new Loc("Numbers over each head: <color={0}>gold = lucky</color>, <color={1}><s>red</s> = unlucky</color>.",
            "머리 위 숫자: <color={0}>금색 = 행운</color>, <color={1}><s>빨강</s> = 불길</color>.");
    }
}
