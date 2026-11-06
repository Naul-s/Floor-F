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
    /// Format placeholders use string.Format ({0}, {1}).
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
    }
}
