using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace MirrorpondHUD.Core
{
    /// <summary>
    /// Lucide 图标字体（ISC License，见 assets/icons/LICENSE.txt）。
    /// 码位来自 lucide-static 1.66.0 的 lucide.css；字体文件 assets/icons/Lucide.ttf。
    /// 用法：<c>Text = LucideIcons.Cpu</c>，或 <c>LucideIcons.Get("cpu")</c>；
    /// 字体族用 <see cref="FontFamily"/>，UiKit 的 <c>CardIcon</c> 已封装。
    /// </summary>
    public static class LucideIcons
    {
        /// <summary>字体族名（WPF FontFamily 名称）。</summary>
        public const string FamilyName = "lucide";

        static readonly FontFamily LucideFamily = new FontFamily(
            new Uri("pack://application:,,,/MirrorpondHUD;component/"), "./assets/icons/#lucide");

        /// <summary>宿主内嵌的 Lucide 字体族；可直接赋给 TextBlock.FontFamily。</summary>
        public static FontFamily FontFamily { get { return LucideFamily; } }

        public const string AArrowDown = "\uE585";
        public const string AArrowUp = "\uE586";
        public const string ALargeSmall = "\uE587";
        public const string Accessibility = "\uE297";
        public const string Activity = "\uE038";
        public const string ActivitySquare = "\uE4B4";
        public const string Ad = "\uE700";
        public const string AirVent = "\uE34D";
        public const string Airplay = "\uE039";
        public const string AlarmCheck = "\uE1EC";
        public const string AlarmClock = "\uE03A";
        public const string AlarmClockCheck = "\uE1EC";
        public const string AlarmClockMinus = "\uE1ED";
        public const string AlarmClockOff = "\uE23B";
        public const string AlarmClockPlus = "\uE1EE";
        public const string AlarmMinus = "\uE1ED";
        public const string AlarmPlus = "\uE1EE";
        public const string AlarmSmoke = "\uE57B";
        public const string Album = "\uE03B";
        public const string AlertCircle = "\uE077";
        public const string AlertOctagon = "\uE127";
        public const string AlertTriangle = "\uE193";
        public const string AlignCenter = "\uE182";
        public const string AlignCenterHorizontal = "\uE26C";
        public const string AlignCenterVertical = "\uE26D";
        public const string AlignEndHorizontal = "\uE26E";
        public const string AlignEndVertical = "\uE26F";
        public const string AlignHorizontalDistributeCenter = "\uE03C";
        public const string AlignHorizontalDistributeEnd = "\uE03D";
        public const string AlignHorizontalDistributeStart = "\uE03E";
        public const string AlignHorizontalJustifyCenter = "\uE272";
        public const string AlignHorizontalJustifyEnd = "\uE273";
        public const string AlignHorizontalJustifyStart = "\uE274";
        public const string AlignHorizontalSpaceAround = "\uE275";
        public const string AlignHorizontalSpaceBetween = "\uE276";
        public const string AlignJustify = "\uE184";
        public const string AlignLeft = "\uE185";
        public const string AlignRight = "\uE183";
        public const string AlignStartHorizontal = "\uE270";
        public const string AlignStartVertical = "\uE271";
        public const string AlignVerticalDistributeCenter = "\uE27E";
        public const string AlignVerticalDistributeEnd = "\uE27F";
        public const string AlignVerticalDistributeStart = "\uE280";
        public const string AlignVerticalJustifyCenter = "\uE277";
        public const string AlignVerticalJustifyEnd = "\uE278";
        public const string AlignVerticalJustifyStart = "\uE279";
        public const string AlignVerticalSpaceAround = "\uE27A";
        public const string AlignVerticalSpaceBetween = "\uE27B";
        public const string Ambulance = "\uE5BB";
        public const string Ampersand = "\uE49C";
        public const string Ampersands = "\uE49D";
        public const string Amphora = "\uE61B";
        public const string Anchor = "\uE03F";
        public const string Angle = "\uE732";
        public const string Angry = "\uE2FC";
        public const string Annoyed = "\uE2FD";
        public const string Antenna = "\uE4E2";
        public const string Anvil = "\uE580";
        public const string Aperture = "\uE040";
        public const string AppWindow = "\uE426";
        public const string AppWindowMac = "\uE5D2";
        public const string Apple = "\uE34E";
        public const string Archive = "\uE041";
        public const string ArchiveArrowDown = "\uE7C3";
        public const string ArchiveArrowUp = "\uE2CD";
        public const string ArchiveRestore = "\uE2CD";
        public const string ArchiveX = "\uE50C";
        public const string AreaChart = "\uE4D3";
        public const string Armchair = "\uE2C0";
        public const string ArmenianDram = "\uE791";
        public const string ArrowBigDown = "\uE1E1";
        public const string ArrowBigDownDash = "\uE41D";
        public const string ArrowBigLeft = "\uE1E2";
        public const string ArrowBigLeftDash = "\uE41E";
        public const string ArrowBigRight = "\uE1E3";
        public const string ArrowBigRightDash = "\uE41F";
        public const string ArrowBigUp = "\uE1E4";
        public const string ArrowBigUpDash = "\uE420";
        public const string ArrowDown = "\uE042";
        public const string ArrowDown01 = "\uE413";
        public const string ArrowDown01_2 = "\uE413";
        public const string ArrowDown10 = "\uE414";
        public const string ArrowDown10_2 = "\uE414";
        public const string ArrowDownAZ = "\uE415";
        public const string ArrowDownAz = "\uE415";
        public const string ArrowDownCircle = "\uE078";
        public const string ArrowDownFromLine = "\uE454";
        public const string ArrowDownLeft = "\uE043";
        public const string ArrowDownLeftFromCircle = "\uE3F7";
        public const string ArrowDownLeftFromSquare = "\uE5A1";
        public const string ArrowDownLeftSquare = "\uE4B5";
        public const string ArrowDownNarrowWide = "\uE044";
        public const string ArrowDownRight = "\uE045";
        public const string ArrowDownRightFromCircle = "\uE3F8";
        public const string ArrowDownRightFromSquare = "\uE5A2";
        public const string ArrowDownRightSquare = "\uE4B6";
        public const string ArrowDownSquare = "\uE427";
        public const string ArrowDownToDot = "\uE44D";
        public const string ArrowDownToLine = "\uE455";
        public const string ArrowDownUp = "\uE046";
        public const string ArrowDownWideNarrow = "\uE047";
        public const string ArrowDownZA = "\uE416";
        public const string ArrowDownZa = "\uE416";
        public const string ArrowLeft = "\uE048";
        public const string ArrowLeftCircle = "\uE079";
        public const string ArrowLeftFromLine = "\uE456";
        public const string ArrowLeftRight = "\uE24A";
        public const string ArrowLeftSquare = "\uE428";
        public const string ArrowLeftToLine = "\uE457";
        public const string ArrowRight = "\uE049";
        public const string ArrowRightCircle = "\uE07A";
        public const string ArrowRightFromLine = "\uE458";
        public const string ArrowRightLeft = "\uE417";
        public const string ArrowRightSquare = "\uE429";
        public const string ArrowRightToLine = "\uE459";
        public const string ArrowUp = "\uE04A";
        public const string ArrowUp01 = "\uE418";
        public const string ArrowUp01_2 = "\uE418";
        public const string ArrowUp10 = "\uE419";
        public const string ArrowUp10_2 = "\uE419";
        public const string ArrowUpAZ = "\uE41A";
        public const string ArrowUpAz = "\uE41A";
        public const string ArrowUpCircle = "\uE07B";
        public const string ArrowUpDown = "\uE37D";
        public const string ArrowUpFromDot = "\uE44E";
        public const string ArrowUpFromLine = "\uE45A";
        public const string ArrowUpLeft = "\uE04B";
        public const string ArrowUpLeftFromCircle = "\uE3F9";
        public const string ArrowUpLeftFromSquare = "\uE5A3";
        public const string ArrowUpLeftSquare = "\uE4B7";
        public const string ArrowUpNarrowWide = "\uE04C";
        public const string ArrowUpRight = "\uE04D";
        public const string ArrowUpRightFromCircle = "\uE3FA";
        public const string ArrowUpRightFromSquare = "\uE5A4";
        public const string ArrowUpRightSquare = "\uE4B8";
        public const string ArrowUpSquare = "\uE42A";
        public const string ArrowUpToLine = "\uE45B";
        public const string ArrowUpWideNarrow = "\uE41B";
        public const string ArrowUpZA = "\uE41C";
        public const string ArrowUpZa = "\uE41C";
        public const string ArrowsUpFromLine = "\uE4D4";
        public const string Asterisk = "\uE1EF";
        public const string AsteriskSquare = "\uE168";
        public const string Astroid = "\uE6F0";
        public const string AtSign = "\uE04E";
        public const string Atom = "\uE3D7";
        public const string AudioLines = "\uE55A";
        public const string AudioLinesOff = "\uE737";
        public const string AudioLinesX = "\uE72E";
        public const string AudioWaveform = "\uE55B";
        public const string Avocado = "\uE7B2";
        public const string Award = "\uE04F";
        public const string Axe = "\uE050";
        public const string Axis3D = "\uE2FE";
        public const string Axis3d = "\uE2FE";
        public const string AzerbaijaniManat = "\uE7BE";
        public const string Baby = "\uE2CE";
        public const string Backpack = "\uE2C8";
        public const string Bacon = "\uE7B3";
        public const string Badge = "\uE474";
        public const string BadgeAlert = "\uE475";
        public const string BadgeCent = "\uE50F";
        public const string BadgeCheck = "\uE241";
        public const string BadgeDollarSign = "\uE476";
        public const string BadgeEuro = "\uE510";
        public const string BadgeHelp = "\uE47B";
        public const string BadgeIndianRupee = "\uE511";
        public const string BadgeInfo = "\uE477";
        public const string BadgeJapaneseYen = "\uE512";
        public const string BadgeMinus = "\uE478";
        public const string BadgePercent = "\uE479";
        public const string BadgePlus = "\uE47A";
        public const string BadgePoundSterling = "\uE513";
        public const string BadgeQuestionMark = "\uE47B";
        public const string BadgeRussianRuble = "\uE514";
        public const string BadgeSwissFranc = "\uE515";
        public const string BadgeTurkishLira = "\uE67E";
        public const string BadgeX = "\uE47C";
        public const string BaggageClaim = "\uE2C9";
        public const string BallWave = "\uE7BF";
        public const string Balloon = "\uE6AF";
        public const string Ban = "\uE051";
        public const string Banana = "\uE34F";
        public const string Bandage = "\uE61D";
        public const string BangladeshiTaka = "\uE78D";
        public const string Banknote = "\uE052";
        public const string BanknoteArrowDown = "\uE64C";
        public const string BanknoteArrowUp = "\uE64D";
        public const string BanknoteCheck = "\uE70C";
        public const string BanknoteX = "\uE64E";
        public const string BarChart = "\uE06A";
        public const string BarChart2 = "\uE068";
        public const string BarChart3 = "\uE2A3";
        public const string BarChart4 = "\uE2A4";
        public const string BarChartBig = "\uE4A9";
        public const string BarChartHorizontal = "\uE2A2";
        public const string BarChartHorizontalBig = "\uE4A7";
        public const string Barcode = "\uE533";
        public const string Barrel = "\uE675";
        public const string Baseline = "\uE285";
        public const string Bath = "\uE2AB";
        public const string Battery = "\uE053";
        public const string BatteryCharging = "\uE054";
        public const string BatteryFull = "\uE055";
        public const string BatteryLow = "\uE056";
        public const string BatteryMedium = "\uE057";
        public const string BatteryPlus = "\uE63E";
        public const string BatteryWarning = "\uE3AC";
        public const string Beaker = "\uE058";
        public const string Bean = "\uE38F";
        public const string BeanOff = "\uE390";
        public const string Bed = "\uE2C1";
        public const string BedDouble = "\uE2C2";
        public const string BedSingle = "\uE2C3";
        public const string Beef = "\uE3A5";
        public const string BeefOff = "\uE6DC";
        public const string Beer = "\uE2CF";
        public const string BeerOff = "\uE5D9";
        public const string BelarusianRuble = "\uE7A6";
        public const string Bell = "\uE059";
        public const string BellCheck = "\uE6EF";
        public const string BellDot = "\uE42B";
        public const string BellElectric = "\uE57C";
        public const string BellMinus = "\uE1F0";
        public const string BellOff = "\uE05A";
        public const string BellPlus = "\uE1F1";
        public const string BellRing = "\uE224";
        public const string BetweenHorizonalEnd = "\uE591";
        public const string BetweenHorizonalStart = "\uE592";
        public const string BetweenHorizontalEnd = "\uE591";
        public const string BetweenHorizontalStart = "\uE592";
        public const string BetweenVerticalEnd = "\uE593";
        public const string BetweenVerticalStart = "\uE594";
        public const string BicepsFlexed = "\uE5EB";
        public const string Bike = "\uE1D2";
        public const string Binary = "\uE1F2";
        public const string Binoculars = "\uE621";
        public const string Biohazard = "\uE441";
        public const string Bird = "\uE3C5";
        public const string Birdhouse = "\uE69A";
        public const string Bitcoin = "\uE05B";
        public const string Blend = "\uE59C";
        public const string Blender = "\uE6F4";
        public const string Blinds = "\uE3C0";
        public const string Blocks = "\uE4FA";
        public const string Bluetooth = "\uE05C";
        public const string BluetoothConnected = "\uE1B8";
        public const string BluetoothOff = "\uE1B9";
        public const string BluetoothSearching = "\uE1BA";
        public const string Bold = "\uE05D";
        public const string Bolt = "\uE58C";
        public const string Bomb = "\uE2FF";
        public const string Bone = "\uE358";
        public const string BoneFracture = "\uE715";
        public const string Book = "\uE05E";
        public const string BookA = "\uE544";
        public const string BookAlert = "\uE672";
        public const string BookAudio = "\uE545";
        public const string BookBookmark = "\uE3F1";
        public const string BookCheck = "\uE546";
        public const string BookCopy = "\uE3EC";
        public const string BookDashed = "\uE3ED";
        public const string BookDown = "\uE3EE";
        public const string BookHeadphones = "\uE547";
        public const string BookHeart = "\uE548";
        public const string BookImage = "\uE549";
        public const string BookKey = "\uE3EF";
        public const string BookLock = "\uE3F0";
        public const string BookMarked = "\uE3F1";
        public const string BookMinus = "\uE3F2";
        public const string BookOpen = "\uE05F";
        public const string BookOpenCheck = "\uE381";
        public const string BookOpenText = "\uE54A";
        public const string BookPlus = "\uE3F3";
        public const string BookSearch = "\uE6AB";
        public const string BookTemplate = "\uE3ED";
        public const string BookText = "\uE54B";
        public const string BookType = "\uE54C";
        public const string BookUp = "\uE3F4";
        public const string BookUp2 = "\uE4A6";
        public const string BookUser = "\uE54D";
        public const string BookX = "\uE3F5";
        public const string Bookmark = "\uE060";
        public const string BookmarkCheck = "\uE51F";
        public const string BookmarkMinus = "\uE23C";
        public const string BookmarkOff = "\uE6DF";
        public const string BookmarkPlus = "\uE23D";
        public const string BookmarkX = "\uE520";
        public const string BoomBox = "\uE4EE";
        public const string Bot = "\uE1BB";
        public const string BotMessageSquare = "\uE5CE";
        public const string BotOff = "\uE5E0";
        public const string BottleWine = "\uE67B";
        public const string BowArrow = "\uE65E";
        public const string Box = "\uE061";
        public const string BoxSelect = "\uE1CB";
        public const string Boxes = "\uE2D0";
        public const string BoxesLinked = "\uE7C4";
        public const string Braces = "\uE36A";
        public const string Brackets = "\uE443";
        public const string Brain = "\uE3C6";
        public const string BrainCircuit = "\uE3C7";
        public const string BrainCog = "\uE3C8";
        public const string BrainOff = "\uE7B4";
        public const string BrickWall = "\uE581";
        public const string BrickWallFire = "\uE653";
        public const string BrickWallShield = "\uE690";
        public const string Bridge = "\uE74F";
        public const string Briefcase = "\uE062";
        public const string BriefcaseBusiness = "\uE5D5";
        public const string BriefcaseConveyorBelt = "\uE62B";
        public const string BriefcaseMedical = "\uE5D6";
        public const string BriefcasePlus = "\uE787";
        public const string BringToFront = "\uE4EF";
        public const string Broccoli = "\uE6F5";
        public const string Broom = "\uE730";
        public const string BroomSparkles = "\uE72F";
        public const string Brush = "\uE1D3";
        public const string BrushCleaning = "\uE666";
        public const string Bubbles = "\uE654";
        public const string Bug = "\uE20C";
        public const string BugOff = "\uE50D";
        public const string BugPlay = "\uE50E";
        public const string Building = "\uE1CC";
        public const string Building2 = "\uE290";
        public const string BuildingComplex = "\uE290";
        public const string BuildingComplexPlus = "\uE76B";
        public const string Bus = "\uE1D4";
        public const string BusFront = "\uE4FB";
        public const string Cable = "\uE4E3";
        public const string CableCar = "\uE4FC";
        public const string Cake = "\uE344";
        public const string CakeSlice = "\uE4B9";
        public const string Calculator = "\uE1BC";
        public const string Calendar = "\uE063";
        public const string Calendar1 = "\uE630";
        public const string CalendarArrowDown = "\uE5FE";
        public const string CalendarArrowLeft = "\uE7C5";
        public const string CalendarArrowRight = "\uE7C6";
        public const string CalendarArrowUp = "\uE5FF";
        public const string CalendarCheck = "\uE2B7";
        public const string CalendarCheck2 = "\uE2B8";
        public const string CalendarChevronsLeft = "\uE7A7";
        public const string CalendarChevronsRight = "\uE76C";
        public const string CalendarClock = "\uE304";
        public const string CalendarCog = "\uE5ED";
        public const string CalendarDays = "\uE2B9";
        public const string CalendarFold = "\uE5B4";
        public const string CalendarHeart = "\uE305";
        public const string CalendarMinus = "\uE2BA";
        public const string CalendarMinus2 = "\uE5B5";
        public const string CalendarOff = "\uE2BB";
        public const string CalendarPlus = "\uE2BC";
        public const string CalendarPlus2 = "\uE5B6";
        public const string CalendarRange = "\uE2BD";
        public const string CalendarSearch = "\uE306";
        public const string CalendarSync = "\uE636";
        public const string CalendarX = "\uE2BE";
        public const string CalendarX2 = "\uE2BF";
        public const string Calendars = "\uE6A7";
        public const string Camera = "\uE064";
        public const string CameraOff = "\uE065";
        public const string Can = "\uE750";
        public const string CanSoda = "\uE755";
        public const string CandlestickChart = "\uE4A8";
        public const string Candy = "\uE391";
        public const string CandyCane = "\uE4BA";
        public const string CandyOff = "\uE392";
        public const string Cannabis = "\uE5D4";
        public const string CannabisOff = "\uE6E0";
        public const string Captions = "\uE3A4";
        public const string CaptionsOff = "\uE5C1";
        public const string Car = "\uE1D5";
        public const string CarBattery = "\uE735";
        public const string CarFront = "\uE4FD";
        public const string CarTaxiFront = "\uE4FE";
        public const string Caravan = "\uE539";
        public const string CardSim = "\uE671";
        public const string Carrot = "\uE25A";
        public const string Carton = "\uE767";
        public const string CartonOff = "\uE766";
        public const string CaseLower = "\uE3D8";
        public const string CaseSensitive = "\uE3D9";
        public const string CaseUpper = "\uE3DA";
        public const string CassetteTape = "\uE4CA";
        public const string Cast = "\uE066";
        public const string Castle = "\uE3E0";
        public const string Cat = "\uE38C";
        public const string Cctv = "\uE57D";
        public const string CctvOff = "\uE6D8";
        public const string Certificate = "\uE7B1";
        public const string ChartArea = "\uE4D3";
        public const string ChartBar = "\uE2A2";
        public const string ChartBarBig = "\uE4A7";
        public const string ChartBarDecreasing = "\uE607";
        public const string ChartBarIncreasing = "\uE608";
        public const string ChartBarStacked = "\uE609";
        public const string ChartBellCurve = "\uE81E";
        public const string ChartCandlestick = "\uE4A8";
        public const string ChartColumn = "\uE2A3";
        public const string ChartColumnBig = "\uE4A9";
        public const string ChartColumnDecreasing = "\uE067";
        public const string ChartColumnIncreasing = "\uE2A4";
        public const string ChartColumnStacked = "\uE60A";
        public const string ChartGantt = "\uE624";
        public const string ChartLine = "\uE2A5";
        public const string ChartNetwork = "\uE60B";
        public const string ChartNoAxesColumn = "\uE068";
        public const string ChartNoAxesColumnDecreasing = "\uE069";
        public const string ChartNoAxesColumnIncreasing = "\uE06A";
        public const string ChartNoAxesCombined = "\uE60C";
        public const string ChartNoAxesGantt = "\uE4C4";
        public const string ChartPie = "\uE06B";
        public const string ChartQuadrant = "\uE7C7";
        public const string ChartRadar = "\uE7C8";
        public const string ChartScatter = "\uE48A";
        public const string ChartSpline = "\uE60D";
        public const string Check = "\uE06C";
        public const string CheckCheck = "\uE38E";
        public const string CheckCircle = "\uE07C";
        public const string CheckCircle2 = "\uE226";
        public const string CheckLine = "\uE66B";
        public const string CheckSquare = "\uE16A";
        public const string CheckSquare2 = "\uE559";
        public const string ChefHat = "\uE2AC";
        public const string Cherry = "\uE350";
        public const string ChessBishop = "\uE6A0";
        public const string ChessKing = "\uE6A1";
        public const string ChessKnight = "\uE6A2";
        public const string ChessPawn = "\uE6A3";
        public const string ChessQueen = "\uE6A4";
        public const string ChessRook = "\uE6A5";
        public const string ChevronDown = "\uE06D";
        public const string ChevronDownCircle = "\uE4DD";
        public const string ChevronDownSquare = "\uE3CF";
        public const string ChevronFirst = "\uE243";
        public const string ChevronLast = "\uE244";
        public const string ChevronLeft = "\uE06E";
        public const string ChevronLeftCircle = "\uE4DE";
        public const string ChevronLeftSquare = "\uE3D0";
        public const string ChevronRight = "\uE06F";
        public const string ChevronRightCircle = "\uE4DF";
        public const string ChevronRightSquare = "\uE3D1";
        public const string ChevronUp = "\uE070";
        public const string ChevronUpCircle = "\uE4E0";
        public const string ChevronUpSquare = "\uE3D2";
        public const string ChevronsDown = "\uE071";
        public const string ChevronsDownUp = "\uE228";
        public const string ChevronsLeft = "\uE072";
        public const string ChevronsLeftRight = "\uE293";
        public const string ChevronsLeftRightEllipsis = "\uE61F";
        public const string ChevronsRight = "\uE073";
        public const string ChevronsRightLeft = "\uE294";
        public const string ChevronsUp = "\uE074";
        public const string ChevronsUpDown = "\uE211";
        public const string Church = "\uE3E1";
        public const string Cigarette = "\uE2C6";
        public const string CigaretteOff = "\uE2C7";
        public const string Circle = "\uE076";
        public const string CircleAlert = "\uE077";
        public const string CircleArrowDown = "\uE078";
        public const string CircleArrowLeft = "\uE079";
        public const string CircleArrowOutDownLeft = "\uE3F7";
        public const string CircleArrowOutDownRight = "\uE3F8";
        public const string CircleArrowOutUpLeft = "\uE3F9";
        public const string CircleArrowOutUpRight = "\uE3FA";
        public const string CircleArrowRight = "\uE07A";
        public const string CircleArrowUp = "\uE07B";
        public const string CircleCheck = "\uE226";
        public const string CircleCheckBig = "\uE07C";
        public const string CircleChevronDown = "\uE4DD";
        public const string CircleChevronLeft = "\uE4DE";
        public const string CircleChevronRight = "\uE4DF";
        public const string CircleChevronUp = "\uE4E0";
        public const string CircleChevronsDown = "\uE7BA";
        public const string CircleChevronsLeft = "\uE7BB";
        public const string CircleChevronsRight = "\uE7BC";
        public const string CircleChevronsUp = "\uE7BD";
        public const string CircleDashed = "\uE4B0";
        public const string CircleDashedCheck = "\uE75E";
        public const string CircleDivide = "\uE07D";
        public const string CircleDollarSign = "\uE47D";
        public const string CircleDot = "\uE345";
        public const string CircleDotDashed = "\uE4B1";
        public const string CircleEllipsis = "\uE346";
        public const string CircleEqual = "\uE400";
        public const string CircleEuro = "\uE71F";
        public const string CircleFadingArrowUp = "\uE618";
        public const string CircleFadingPlay = "\uE7A8";
        public const string CircleFadingPlus = "\uE5BC";
        public const string CircleGauge = "\uE4E1";
        public const string CircleHelp = "\uE082";
        public const string CircleLetterA = "\uE7D3";
        public const string CircleLetterB = "\uE7D4";
        public const string CircleLetterC = "\uE7D5";
        public const string CircleLetterD = "\uE7D6";
        public const string CircleLetterE = "\uE7D7";
        public const string CircleLetterF = "\uE7D8";
        public const string CircleLetterG = "\uE7D9";
        public const string CircleLetterH = "\uE7DA";
        public const string CircleLetterI = "\uE7DB";
        public const string CircleLetterJ = "\uE7DC";
        public const string CircleLetterK = "\uE7DD";
        public const string CircleLetterL = "\uE7DE";
        public const string CircleLetterM = "\uE7DF";
        public const string CircleLetterN = "\uE7E0";
        public const string CircleLetterO = "\uE7E1";
        public const string CircleLetterP = "\uE3C9";
        public const string CircleLetterPOff = "\uE3CA";
        public const string CircleLetterQ = "\uE7E2";
        public const string CircleLetterR = "\uE7E3";
        public const string CircleLetterS = "\uE7E4";
        public const string CircleLetterT = "\uE7E5";
        public const string CircleLetterU = "\uE7E6";
        public const string CircleLetterV = "\uE7E7";
        public const string CircleLetterW = "\uE7E8";
        public const string CircleLetterX = "\uE7E9";
        public const string CircleLetterY = "\uE7EA";
        public const string CircleLetterZ = "\uE7EB";
        public const string CircleMinus = "\uE07E";
        public const string CircleOff = "\uE401";
        public const string CircleParking = "\uE3C9";
        public const string CircleParkingOff = "\uE3CA";
        public const string CirclePause = "\uE07F";
        public const string CirclePercent = "\uE51A";
        public const string CirclePile = "\uE6E1";
        public const string CirclePlay = "\uE080";
        public const string CirclePlus = "\uE081";
        public const string CirclePoundSterling = "\uE66D";
        public const string CirclePower = "\uE550";
        public const string CircleQuestionMark = "\uE082";
        public const string CircleSlash = "\uE402";
        public const string CircleSlash2 = "\uE213";
        public const string CircleSlashed = "\uE213";
        public const string CircleSmall = "\uE640";
        public const string CircleStar = "\uE68D";
        public const string CircleStop = "\uE083";
        public const string CircleUser = "\uE461";
        public const string CircleUserRound = "\uE462";
        public const string CircleX = "\uE084";
        public const string CircuitBoard = "\uE403";
        public const string Citrus = "\uE375";
        public const string Clapperboard = "\uE29B";
        public const string ClefAlto = "\uE779";
        public const string ClefBass = "\uE77A";
        public const string ClefTreble = "\uE77B";
        public const string Clipboard = "\uE085";
        public const string ClipboardCheck = "\uE219";
        public const string ClipboardClock = "\uE688";
        public const string ClipboardCopy = "\uE225";
        public const string ClipboardEdit = "\uE307";
        public const string ClipboardList = "\uE086";
        public const string ClipboardMinus = "\uE5BE";
        public const string ClipboardPaste = "\uE3E8";
        public const string ClipboardPen = "\uE307";
        public const string ClipboardPenLine = "\uE308";
        public const string ClipboardPlus = "\uE5BF";
        public const string ClipboardSignature = "\uE308";
        public const string ClipboardType = "\uE309";
        public const string ClipboardX = "\uE222";
        public const string Clock = "\uE087";
        public const string Clock1 = "\uE24B";
        public const string Clock10 = "\uE24C";
        public const string Clock11 = "\uE24D";
        public const string Clock12 = "\uE24E";
        public const string Clock2 = "\uE24F";
        public const string Clock3 = "\uE250";
        public const string Clock4 = "\uE251";
        public const string Clock5 = "\uE252";
        public const string Clock6 = "\uE253";
        public const string Clock7 = "\uE254";
        public const string Clock8 = "\uE255";
        public const string Clock9 = "\uE256";
        public const string ClockAlert = "\uE62A";
        public const string ClockArrowDown = "\uE600";
        public const string ClockArrowLeft = "\uE70D";
        public const string ClockArrowRight = "\uE70E";
        public const string ClockArrowUp = "\uE601";
        public const string ClockCheck = "\uE69E";
        public const string ClockFading = "\uE64A";
        public const string ClockPlus = "\uE667";
        public const string ClosedCaption = "\uE68A";
        public const string Cloud = "\uE088";
        public const string CloudAlert = "\uE633";
        public const string CloudBackup = "\uE6E2";
        public const string CloudCheck = "\uE66E";
        public const string CloudCog = "\uE30A";
        public const string CloudDownload = "\uE089";
        public const string CloudDrizzle = "\uE08A";
        public const string CloudFog = "\uE214";
        public const string CloudHail = "\uE08B";
        public const string CloudLightning = "\uE08C";
        public const string CloudMoon = "\uE215";
        public const string CloudMoonRain = "\uE2FA";
        public const string CloudOff = "\uE08D";
        public const string CloudRain = "\uE08E";
        public const string CloudRainWind = "\uE08F";
        public const string CloudSnow = "\uE090";
        public const string CloudSun = "\uE216";
        public const string CloudSunRain = "\uE2FB";
        public const string CloudSync = "\uE6E3";
        public const string CloudUpload = "\uE091";
        public const string Cloudy = "\uE217";
        public const string Clover = "\uE092";
        public const string Club = "\uE496";
        public const string Code = "\uE093";
        public const string Code2 = "\uE206";
        public const string CodeSquare = "\uE16B";
        public const string CodeXml = "\uE206";
        public const string Coffee = "\uE096";
        public const string Cog = "\uE30B";
        public const string Coins = "\uE097";
        public const string Columns = "\uE098";
        public const string Columns2 = "\uE098";
        public const string Columns3 = "\uE099";
        public const string Columns3Cog = "\uE661";
        public const string Columns4 = "\uE589";
        public const string ColumnsSettings = "\uE661";
        public const string Combine = "\uE44C";
        public const string Command = "\uE09A";
        public const string Compass = "\uE09B";
        public const string Component = "\uE2AD";
        public const string Computer = "\uE4E4";
        public const string ConciergeBell = "\uE378";
        public const string Cone = "\uE523";
        public const string Construction = "\uE3B4";
        public const string Contact = "\uE09C";
        public const string Contact2 = "\uE463";
        public const string ContactRound = "\uE463";
        public const string Container = "\uE4D5";
        public const string Contrast = "\uE09D";
        public const string Cookie = "\uE26B";
        public const string CookingPot = "\uE584";
        public const string Copy = "\uE09E";
        public const string CopyCheck = "\uE3FB";
        public const string CopyMinus = "\uE3FC";
        public const string CopyPlus = "\uE3FD";
        public const string CopySlash = "\uE3FE";
        public const string CopyX = "\uE3FF";
        public const string Copyleft = "\uE09F";
        public const string Copyright = "\uE0A0";
        public const string CornerDownLeft = "\uE0A1";
        public const string CornerDownRight = "\uE0A2";
        public const string CornerLeftDown = "\uE0A3";
        public const string CornerLeftUp = "\uE0A4";
        public const string CornerRightDown = "\uE0A5";
        public const string CornerRightUp = "\uE0A6";
        public const string CornerUpLeft = "\uE0A7";
        public const string CornerUpRight = "\uE0A8";
        public const string Cpu = "\uE0A9";
        public const string CpuAi = "\uE7A9";
        public const string CreativeCommons = "\uE3B2";
        public const string CreditCard = "\uE0AA";
        public const string CreditCardCheck = "\uE743";
        public const string CreditCardMinus = "\uE744";
        public const string CreditCardPlus = "\uE74B";
        public const string CreditCardReader = "\uE756";
        public const string CreditCardX = "\uE745";
        public const string Croissant = "\uE2AE";
        public const string Crop = "\uE0AB";
        public const string Cross = "\uE1E5";
        public const string Crosshair = "\uE0AC";
        public const string Crown = "\uE1D6";
        public const string Cuboid = "\uE524";
        public const string CupSoda = "\uE2D1";
        public const string Cupcake = "\uE77D";
        public const string CurlyBraces = "\uE36A";
        public const string Currency = "\uE230";
        public const string Cylinder = "\uE525";
        public const string Dam = "\uE606";
        public const string Database = "\uE0AD";
        public const string DatabaseArrowDown = "\uE716";
        public const string DatabaseArrowUp = "\uE717";
        public const string DatabaseBackup = "\uE3AB";
        public const string DatabaseCheck = "\uE718";
        public const string DatabaseCog = "\uE7C9";
        public const string DatabaseMinus = "\uE719";
        public const string DatabasePlus = "\uE71A";
        public const string DatabaseSearch = "\uE6B1";
        public const string DatabaseX = "\uE71B";
        public const string DatabaseZap = "\uE50B";
        public const string DecimalsArrowLeft = "\uE65C";
        public const string DecimalsArrowRight = "\uE65D";
        public const string Delete = "\uE0AE";
        public const string Dessert = "\uE4BB";
        public const string Diameter = "\uE526";
        public const string Diamond = "\uE2D2";
        public const string DiamondMinus = "\uE5E1";
        public const string DiamondPercent = "\uE51B";
        public const string DiamondPlus = "\uE5E2";
        public const string Dice1 = "\uE287";
        public const string Dice2 = "\uE288";
        public const string Dice3 = "\uE289";
        public const string Dice4 = "\uE28A";
        public const string Dice5 = "\uE28B";
        public const string Dice6 = "\uE28C";
        public const string Dices = "\uE2C5";
        public const string Diff = "\uE30C";
        public const string Disc = "\uE0AF";
        public const string Disc2 = "\uE3F6";
        public const string Disc3 = "\uE494";
        public const string DiscAlbum = "\uE55C";
        public const string Divide = "\uE0B0";
        public const string DivideCircle = "\uE07D";
        public const string DivideSquare = "\uE16D";
        public const string Dna = "\uE393";
        public const string DnaOff = "\uE394";
        public const string Dock = "\uE5D3";
        public const string Dog = "\uE38D";
        public const string DollarSign = "\uE0B1";
        public const string Dome = "\uE75F";
        public const string Donut = "\uE4BC";
        public const string DoorClosed = "\uE3D5";
        public const string DoorClosedCog = "\uE792";
        public const string DoorClosedLocked = "\uE664";
        public const string DoorClosedPackage = "\uE77E";
        public const string DoorOpen = "\uE3D6";
        public const string DoorStairwell = "\uE757";
        public const string Dot = "\uE44F";
        public const string DotSquare = "\uE16E";
        public const string Download = "\uE0B2";
        public const string DownloadCloud = "\uE089";
        public const string DraftingCompass = "\uE527";
        public const string Drama = "\uE521";
        public const string Drill = "\uE58D";
        public const string Drone = "\uE676";
        public const string Droplet = "\uE0B4";
        public const string DropletOff = "\uE638";
        public const string Droplets = "\uE0B5";
        public const string Drum = "\uE55D";
        public const string Drumstick = "\uE25B";
        public const string Dumbbell = "\uE3A1";
        public const string Ear = "\uE382";
        public const string EarOff = "\uE383";
        public const string Earbuds = "\uE7CB";
        public const string EarbudsOff = "\uE7CA";
        public const string Earth = "\uE1F3";
        public const string EarthLock = "\uE5CC";
        public const string Eclipse = "\uE59D";
        public const string Edit = "\uE172";
        public const string Edit2 = "\uE12F";
        public const string Edit3 = "\uE130";
        public const string Egg = "\uE25D";
        public const string EggFried = "\uE351";
        public const string EggOff = "\uE395";
        public const string EighthNote = "\uE34A";
        public const string EighthNotes = "\uE122";
        public const string Eject = "\uE733";
        public const string Ellipse = "\uE6B2";
        public const string Ellipsis = "\uE0B6";
        public const string EllipsisVertical = "\uE0B7";
        public const string EmiratiDirham = "\uE7C0";
        public const string Engine = "\uE758";
        public const string Equal = "\uE1BD";
        public const string EqualApproximately = "\uE634";
        public const string EqualApproximatelyNot = "\uE760";
        public const string EqualNot = "\uE1BE";
        public const string EqualSquare = "\uE16F";
        public const string Eraser = "\uE28F";
        public const string EthernetPort = "\uE620";
        public const string Euro = "\uE0B8";
        public const string EvCharger = "\uE697";
        public const string Expand = "\uE21A";
        public const string ExternalLink = "\uE0B9";
        public const string Eye = "\uE0BA";
        public const string EyeClosed = "\uE62E";
        public const string EyeDashed = "\uE701";
        public const string EyeOff = "\uE0BB";
        public const string FaceAngry = "\uE2FC";
        public const string FaceExpressionless = "\uE2FD";
        public const string FaceGrinning = "\uE300";
        public const string FaceNeutral = "\uE114";
        public const string FaceSlightlyFrowning = "\uE0DB";
        public const string FaceSlightlySmiling = "\uE164";
        public const string FaceSlightlySmilingPlus = "\uE301";
        public const string Factory = "\uE29F";
        public const string Fan = "\uE379";
        public const string FastForward = "\uE0BD";
        public const string Faucet = "\uE77F";
        public const string Feather = "\uE0BE";
        public const string Fence = "\uE582";
        public const string FerrisWheel = "\uE47F";
        public const string File = "\uE0C0";
        public const string FileArchive = "\uE30D";
        public const string FileAudio = "\uE31A";
        public const string FileAudio2 = "\uE31A";
        public const string FileAxis3D = "\uE30E";
        public const string FileAxis3d = "\uE30E";
        public const string FileBadge = "\uE30F";
        public const string FileBadge2 = "\uE30F";
        public const string FileBarChart = "\uE312";
        public const string FileBarChart2 = "\uE311";
        public const string FileBox = "\uE310";
        public const string FileBraces = "\uE36B";
        public const string FileBracesCorner = "\uE36C";
        public const string FileChartColumn = "\uE311";
        public const string FileChartColumnIncreasing = "\uE312";
        public const string FileChartLine = "\uE313";
        public const string FileChartPie = "\uE314";
        public const string FileCheck = "\uE0C1";
        public const string FileCheck2 = "\uE0C2";
        public const string FileCheckCorner = "\uE0C2";
        public const string FileClock = "\uE315";
        public const string FileCode = "\uE0C3";
        public const string FileCode2 = "\uE45E";
        public const string FileCodeCorner = "\uE45E";
        public const string FileCog = "\uE316";
        public const string FileCog2 = "\uE316";
        public const string FileDatabase = "\uE81F";
        public const string FileDiff = "\uE317";
        public const string FileDigit = "\uE0C4";
        public const string FileDown = "\uE318";
        public const string FileEdit = "\uE31F";
        public const string FileExclamationPoint = "\uE319";
        public const string FileHeadphone = "\uE31A";
        public const string FileHeart = "\uE31B";
        public const string FileImage = "\uE31C";
        public const string FileInput = "\uE0C5";
        public const string FileJson = "\uE36B";
        public const string FileJson2 = "\uE36C";
        public const string FileKey = "\uE31D";
        public const string FileKey2 = "\uE31D";
        public const string FileLineChart = "\uE313";
        public const string FileLock = "\uE31E";
        public const string FileLock2 = "\uE31E";
        public const string FileMinus = "\uE0C6";
        public const string FileMinus2 = "\uE0C7";
        public const string FileMinusCorner = "\uE0C7";
        public const string FileMusic = "\uE55E";
        public const string FileOutput = "\uE0C8";
        public const string FilePen = "\uE31F";
        public const string FilePenLine = "\uE320";
        public const string FilePieChart = "\uE314";
        public const string FilePlay = "\uE321";
        public const string FilePlus = "\uE0C9";
        public const string FilePlus2 = "\uE0CA";
        public const string FilePlusCorner = "\uE0CA";
        public const string FileQuestion = "\uE322";
        public const string FileQuestionMark = "\uE322";
        public const string FileScan = "\uE323";
        public const string FileSearch = "\uE0CB";
        public const string FileSearch2 = "\uE324";
        public const string FileSearchCorner = "\uE324";
        public const string FileSignal = "\uE325";
        public const string FileSignature = "\uE320";
        public const string FileSliders = "\uE5A0";
        public const string FileSpreadsheet = "\uE326";
        public const string FileStack = "\uE4A1";
        public const string FileSymlink = "\uE327";
        public const string FileTerminal = "\uE328";
        public const string FileText = "\uE0CC";
        public const string FileType = "\uE329";
        public const string FileType2 = "\uE36D";
        public const string FileTypeCorner = "\uE36D";
        public const string FileUp = "\uE32A";
        public const string FileUser = "\uE62D";
        public const string FileVideo = "\uE321";
        public const string FileVideo2 = "\uE32B";
        public const string FileVideoCamera = "\uE32B";
        public const string FileVolume = "\uE32C";
        public const string FileVolume2 = "\uE325";
        public const string FileWarning = "\uE319";
        public const string FileX = "\uE0CD";
        public const string FileX2 = "\uE0CE";
        public const string FileXCorner = "\uE0CE";
        public const string Files = "\uE0CF";
        public const string Film = "\uE0D0";
        public const string Filter = "\uE0DC";
        public const string FilterX = "\uE3B5";
        public const string Fingerprint = "\uE2CB";
        public const string FingerprintPattern = "\uE2CB";
        public const string FireExtinguisher = "\uE57E";
        public const string Fish = "\uE3A6";
        public const string FishOff = "\uE3B0";
        public const string FishSymbol = "\uE4F4";
        public const string FishingHook = "\uE6E4";
        public const string FishingRod = "\uE6B3";
        public const string Flag = "\uE0D1";
        public const string FlagOff = "\uE292";
        public const string FlagTriangleLeft = "\uE237";
        public const string FlagTriangleRight = "\uE238";
        public const string Flame = "\uE0D2";
        public const string FlameKindling = "\uE53A";
        public const string Flashlight = "\uE0D3";
        public const string FlashlightOff = "\uE0D4";
        public const string FlaskConical = "\uE0D5";
        public const string FlaskConicalOff = "\uE396";
        public const string FlaskRound = "\uE0D6";
        public const string FlipHorizontal = "\uE35D";
        public const string FlipHorizontal2 = "\uE35E";
        public const string FlipVertical = "\uE35F";
        public const string FlipVertical2 = "\uE360";
        public const string Flower = "\uE2D3";
        public const string Flower2 = "\uE2D4";
        public const string Focus = "\uE29E";
        public const string FoldHorizontal = "\uE43B";
        public const string FoldVertical = "\uE43C";
        public const string Folder = "\uE0D7";
        public const string FolderArchive = "\uE32D";
        public const string FolderBookmark = "\uE6F1";
        public const string FolderCheck = "\uE32E";
        public const string FolderClock = "\uE32F";
        public const string FolderClosed = "\uE330";
        public const string FolderCode = "\uE5FB";
        public const string FolderCog = "\uE331";
        public const string FolderCog2 = "\uE331";
        public const string FolderDot = "\uE4C5";
        public const string FolderDown = "\uE332";
        public const string FolderEdit = "\uE338";
        public const string FolderGit = "\uE409";
        public const string FolderGit2 = "\uE40A";
        public const string FolderHeart = "\uE333";
        public const string FolderInput = "\uE334";
        public const string FolderKanban = "\uE4C6";
        public const string FolderKey = "\uE335";
        public const string FolderLock = "\uE336";
        public const string FolderMinus = "\uE0D8";
        public const string FolderOpen = "\uE247";
        public const string FolderOpenDot = "\uE4C7";
        public const string FolderOutput = "\uE337";
        public const string FolderPen = "\uE338";
        public const string FolderPlus = "\uE0D9";
        public const string FolderRoot = "\uE4C8";
        public const string FolderSearch = "\uE339";
        public const string FolderSearch2 = "\uE33A";
        public const string FolderSymlink = "\uE33B";
        public const string FolderSync = "\uE4C9";
        public const string FolderTree = "\uE33C";
        public const string FolderUp = "\uE33D";
        public const string FolderUser = "\uE7AA";
        public const string FolderUsers = "\uE7AB";
        public const string FolderX = "\uE33E";
        public const string Folders = "\uE33F";
        public const string Footprints = "\uE3B9";
        public const string ForkKnife = "\uE2F6";
        public const string ForkKnifeCrossed = "\uE2F7";
        public const string Forklift = "\uE3C1";
        public const string Form = "\uE6A8";
        public const string FormInput = "\uE21F";
        public const string Forward = "\uE229";
        public const string Frame = "\uE291";
        public const string Frown = "\uE0DB";
        public const string Fuel = "\uE2AF";
        public const string Fullscreen = "\uE534";
        public const string FunctionSquare = "\uE22D";
        public const string Funnel = "\uE0DC";
        public const string FunnelPlus = "\uE0DD";
        public const string FunnelX = "\uE3B5";
        public const string Galaxy = "\uE73F";
        public const string GalleryHorizontal = "\uE4CE";
        public const string GalleryHorizontalEnd = "\uE4CF";
        public const string GalleryThumbnails = "\uE4D0";
        public const string GalleryVertical = "\uE4D1";
        public const string GalleryVerticalEnd = "\uE4D2";
        public const string Gamepad = "\uE0DE";
        public const string Gamepad2 = "\uE0DF";
        public const string GamepadDirectional = "\uE69B";
        public const string GanttChart = "\uE4C4";
        public const string GanttChartSquare = "\uE169";
        public const string GapHorizontal = "\uE761";
        public const string GapVertical = "\uE762";
        public const string Gauge = "\uE1BF";
        public const string GaugeCircle = "\uE4E1";
        public const string Gavel = "\uE0E0";
        public const string Gem = "\uE242";
        public const string GeorgianLari = "\uE678";
        public const string Germ = "\uE75A";
        public const string GermOff = "\uE759";
        public const string Ghost = "\uE20E";
        public const string Gift = "\uE0E1";
        public const string GitBranch = "\uE0E2";
        public const string GitBranchMinus = "\uE69C";
        public const string GitBranchPlus = "\uE1F4";
        public const string GitCommit = "\uE0E3";
        public const string GitCommitHorizontal = "\uE0E3";
        public const string GitCommitVertical = "\uE552";
        public const string GitCompare = "\uE359";
        public const string GitCompareArrows = "\uE553";
        public const string GitFork = "\uE28D";
        public const string GitGraph = "\uE554";
        public const string GitMerge = "\uE0E4";
        public const string GitMergeConflict = "\uE6B4";
        public const string GitPullRequest = "\uE0E5";
        public const string GitPullRequestArrow = "\uE555";
        public const string GitPullRequestClosed = "\uE35A";
        public const string GitPullRequestCreate = "\uE556";
        public const string GitPullRequestCreateArrow = "\uE557";
        public const string GitPullRequestDraft = "\uE35B";
        public const string GlassWater = "\uE2D5";
        public const string Glasses = "\uE20D";
        public const string Globe = "\uE0E8";
        public const string Globe2 = "\uE1F3";
        public const string GlobeCheck = "\uE6FC";
        public const string GlobeCode = "\uE76D";
        public const string GlobeLock = "\uE5CD";
        public const string GlobeOff = "\uE6B5";
        public const string GlobeX = "\uE6B6";
        public const string Goal = "\uE4A5";
        public const string Gpu = "\uE66A";
        public const string Grab = "\uE1E6";
        public const string GraduationCap = "\uE234";
        public const string Grape = "\uE352";
        public const string GraphHub = "\uE7B5";
        public const string GraphStar = "\uE7AC";
        public const string Grid = "\uE0E9";
        public const string Grid2X2 = "\uE4FF";
        public const string Grid2X2Check = "\uE5E4";
        public const string Grid2X2Plus = "\uE628";
        public const string Grid2X2X = "\uE5E5";
        public const string Grid2x2 = "\uE4FF";
        public const string Grid2x2Check = "\uE5E4";
        public const string Grid2x2Plus = "\uE628";
        public const string Grid2x2X = "\uE5E5";
        public const string Grid3X3 = "\uE0E9";
        public const string Grid3x2 = "\uE66F";
        public const string Grid3x3 = "\uE0E9";
        public const string Grip = "\uE3B1";
        public const string GripHorizontal = "\uE0EA";
        public const string GripVertical = "\uE0EB";
        public const string Groceries = "\uE799";
        public const string Group = "\uE464";
        public const string Guitar = "\uE55F";
        public const string GuitarElectric = "\uE7AD";
        public const string HalfNote = "\uE34B";
        public const string Ham = "\uE5D7";
        public const string Hamburger = "\uE665";
        public const string Hammer = "\uE0EC";
        public const string Hand = "\uE1D7";
        public const string HandCoins = "\uE5B8";
        public const string HandFist = "\uE68B";
        public const string HandGrab = "\uE1E6";
        public const string HandHeart = "\uE5B9";
        public const string HandHelping = "\uE3B8";
        public const string HandMetal = "\uE22C";
        public const string HandPlatter = "\uE5BA";
        public const string Handbag = "\uE689";
        public const string HandleBottomRight = "\uE79D";
        public const string Handshake = "\uE5C0";
        public const string HardDrive = "\uE0ED";
        public const string HardDriveDownload = "\uE4E5";
        public const string HardDriveUpload = "\uE4E6";
        public const string HardHat = "\uE0EE";
        public const string Hash = "\uE0EF";
        public const string HatGlasses = "\uE683";
        public const string Haze = "\uE0F0";
        public const string Hd = "\uE6E5";
        public const string HdmiPort = "\uE4E7";
        public const string Heading = "\uE384";
        public const string Heading1 = "\uE385";
        public const string Heading2 = "\uE386";
        public const string Heading3 = "\uE387";
        public const string Heading4 = "\uE388";
        public const string Heading5 = "\uE389";
        public const string Heading6 = "\uE38A";
        public const string HeadphoneOff = "\uE629";
        public const string Headphones = "\uE0F1";
        public const string Headset = "\uE5BD";
        public const string Heart = "\uE0F2";
        public const string HeartCrack = "\uE2D6";
        public const string HeartHandshake = "\uE2D7";
        public const string HeartMinus = "\uE651";
        public const string HeartOff = "\uE295";
        public const string HeartPlus = "\uE652";
        public const string HeartPulse = "\uE36E";
        public const string HeartX = "\uE6ED";
        public const string Heater = "\uE58E";
        public const string Helicopter = "\uE69D";
        public const string HelpCircle = "\uE082";
        public const string HelpingHand = "\uE3B8";
        public const string Hexagon = "\uE0F3";
        public const string Highlighter = "\uE0F4";
        public const string HikingStick = "\uE79A";
        public const string History = "\uE1F5";
        public const string HockeyStick = "\uE7C1";
        public const string Home = "\uE0F5";
        public const string Hop = "\uE397";
        public const string HopOff = "\uE398";
        public const string Hospital = "\uE5D8";
        public const string HotAirBalloon = "\uE7C2";
        public const string Hotel = "\uE3E2";
        public const string Hourglass = "\uE296";
        public const string HourglassCog = "\uE76E";
        public const string House = "\uE0F5";
        public const string HouseCog = "\uE788";
        public const string HouseHeart = "\uE695";
        public const string HousePlug = "\uE5F0";
        public const string HousePlus = "\uE5F1";
        public const string HouseWifi = "\uE63C";
        public const string Houses = "\uE76F";
        public const string IceCream = "\uE353";
        public const string IceCream2 = "\uE3A7";
        public const string IceCreamBowl = "\uE3A7";
        public const string IceCreamCone = "\uE353";
        public const string IdCard = "\uE617";
        public const string IdCardLanyard = "\uE670";
        public const string Image = "\uE0F6";
        public const string ImageDown = "\uE53C";
        public const string ImageMinus = "\uE1F6";
        public const string ImageOff = "\uE1C0";
        public const string ImagePlay = "\uE5DF";
        public const string ImagePlus = "\uE1F7";
        public const string ImageUp = "\uE5CB";
        public const string ImageUpscale = "\uE637";
        public const string Images = "\uE5C4";
        public const string Import = "\uE22F";
        public const string Inbox = "\uE0F7";
        public const string Indent = "\uE108";
        public const string IndentDecrease = "\uE107";
        public const string IndentIncrease = "\uE108";
        public const string IndianRupee = "\uE0F8";
        public const string Infinity = "\uE1E7";
        public const string Info = "\uE0F9";
        public const string Inspect = "\uE202";
        public const string InspectionPanel = "\uE583";
        public const string Italic = "\uE0FB";
        public const string IterationCcw = "\uE423";
        public const string IterationCw = "\uE424";
        public const string IvBag = "\uE770";
        public const string JapaneseYen = "\uE0FC";
        public const string Joystick = "\uE355";
        public const string Kanban = "\uE4DC";
        public const string KanbanSquare = "\uE170";
        public const string KanbanSquareDashed = "\uE16C";
        public const string Kayak = "\uE68F";
        public const string KazakhTenge = "\uE793";
        public const string Key = "\uE0FD";
        public const string KeyRound = "\uE4A3";
        public const string KeySquare = "\uE4A4";
        public const string Keyboard = "\uE284";
        public const string KeyboardMusic = "\uE560";
        public const string KeyboardOff = "\uE5DE";
        public const string Lambda = "\uE780";
        public const string Lamp = "\uE2D8";
        public const string LampCeiling = "\uE2D9";
        public const string LampDesk = "\uE2DA";
        public const string LampFloor = "\uE2DB";
        public const string LampWallDown = "\uE2DC";
        public const string LampWallUp = "\uE2DD";
        public const string LandPlot = "\uE528";
        public const string Landmark = "\uE23A";
        public const string Languages = "\uE0FE";
        public const string Laptop = "\uE1CD";
        public const string Laptop2 = "\uE1D8";
        public const string LaptopMinimal = "\uE1D8";
        public const string LaptopMinimalCheck = "\uE632";
        public const string Laser = "\uE7CC";
        public const string Lasso = "\uE1CE";
        public const string LassoSelect = "\uE1CF";
        public const string Laugh = "\uE300";
        public const string LayerArrowDown = "\uE72A";
        public const string LayerArrowUp = "\uE72B";
        public const string Layers = "\uE529";
        public const string Layers2 = "\uE52A";
        public const string Layers3 = "\uE529";
        public const string LayersArrowDown = "\uE72C";
        public const string LayersArrowUp = "\uE72D";
        public const string LayersMinus = "\uE6EE";
        public const string LayersPlus = "\uE6E6";
        public const string Layout = "\uE12C";
        public const string LayoutArrowDown = "\uE771";
        public const string LayoutArrowRight = "\uE772";
        public const string LayoutDashboard = "\uE1C1";
        public const string LayoutFreeform = "\uE723";
        public const string LayoutGrid = "\uE0FF";
        public const string LayoutGridCircles = "\uE790";
        public const string LayoutList = "\uE1D9";
        public const string LayoutPanelLeft = "\uE470";
        public const string LayoutPanelTop = "\uE471";
        public const string LayoutTemplate = "\uE207";
        public const string Leaf = "\uE2DE";
        public const string LeafyGreen = "\uE46F";
        public const string Lectern = "\uE5E9";
        public const string Lens = "\uE79C";
        public const string LensConcave = "\uE6B7";
        public const string LensConvex = "\uE6B8";
        public const string LetterA = "\uE7EC";
        public const string LetterB = "\uE05D";
        public const string LetterC = "\uE7ED";
        public const string LetterD = "\uE7EE";
        public const string LetterE = "\uE7EF";
        public const string LetterF = "\uE7F0";
        public const string LetterG = "\uE7F1";
        public const string LetterH = "\uE384";
        public const string LetterI = "\uE7F2";
        public const string LetterJ = "\uE7F3";
        public const string LetterK = "\uE7F4";
        public const string LetterL = "\uE7F5";
        public const string LetterM = "\uE7F6";
        public const string LetterN = "\uE7F7";
        public const string LetterO = "\uE7F8";
        public const string LetterP = "\uE7F9";
        public const string LetterQ = "\uE7FA";
        public const string LetterR = "\uE7FB";
        public const string LetterS = "\uE7FC";
        public const string LetterSpacing = "\uE7B6";
        public const string LetterT = "\uE7FD";
        public const string LetterText = "\uE605";
        public const string LetterU = "\uE7FE";
        public const string LetterV = "\uE7FF";
        public const string LetterW = "\uE800";
        public const string LetterX = "\uE801";
        public const string LetterY = "\uE802";
        public const string LetterZ = "\uE803";
        public const string Letters = "\uE78E";
        public const string Library = "\uE100";
        public const string LibraryBig = "\uE54E";
        public const string LibrarySquare = "\uE54F";
        public const string LifeBuoy = "\uE101";
        public const string Ligature = "\uE43A";
        public const string Lightbulb = "\uE1C2";
        public const string LightbulbOff = "\uE208";
        public const string Lighthouse = "\uE751";
        public const string LineChart = "\uE2A5";
        public const string LineDotBottomVertical = "\uE789";
        public const string LineDotLeftHorizontal = "\uE78A";
        public const string LineDotRightHorizontal = "\uE6B9";
        public const string LineDotTopVertical = "\uE78B";
        public const string LineHeight = "\uE7B7";
        public const string LineSquiggle = "\uE67A";
        public const string LineStyle = "\uE6DA";
        public const string Link = "\uE102";
        public const string Link2 = "\uE103";
        public const string Link2Off = "\uE104";
        public const string List = "\uE106";
        public const string ListCheck = "\uE5FA";
        public const string ListChecks = "\uE1D0";
        public const string ListChevronsDownUp = "\uE694";
        public const string ListChevronsUpDown = "\uE696";
        public const string ListClock = "\uE738";
        public const string ListCollapse = "\uE59B";
        public const string ListEnd = "\uE2DF";
        public const string ListFilter = "\uE460";
        public const string ListFilterPlus = "\uE639";
        public const string ListIndentDecrease = "\uE107";
        public const string ListIndentIncrease = "\uE108";
        public const string ListMinus = "\uE23E";
        public const string ListMusic = "\uE2E0";
        public const string ListOrdered = "\uE1D1";
        public const string ListPlus = "\uE23F";
        public const string ListRestart = "\uE452";
        public const string ListSortAscending = "\uE702";
        public const string ListSortDescending = "\uE703";
        public const string ListStart = "\uE2E1";
        public const string ListTodo = "\uE4C3";
        public const string ListTree = "\uE408";
        public const string ListVideo = "\uE2E2";
        public const string ListX = "\uE240";
        public const string Loader = "\uE109";
        public const string Loader2 = "\uE10A";
        public const string LoaderCircle = "\uE10A";
        public const string LoaderPinwheel = "\uE5E6";
        public const string Locate = "\uE1DA";
        public const string LocateFixed = "\uE1DB";
        public const string LocateOff = "\uE282";
        public const string LocationEdit = "\uE655";
        public const string Lock = "\uE10B";
        public const string LockKeyhole = "\uE531";
        public const string LockKeyholeOpen = "\uE532";
        public const string LockOpen = "\uE10C";
        public const string LogIn = "\uE10D";
        public const string LogOut = "\uE10E";
        public const string Logs = "\uE5F4";
        public const string Lollipop = "\uE4BD";
        public const string Luggage = "\uE2CA";
        public const string MSquare = "\uE503";
        public const string Magnet = "\uE2B5";
        public const string Mail = "\uE10F";
        public const string MailBadge = "\uE734";
        public const string MailCheck = "\uE361";
        public const string MailClock = "\uE73E";
        public const string MailDot = "\uE79E";
        public const string MailMinus = "\uE362";
        public const string MailOpen = "\uE363";
        public const string MailPen = "\uE74E";
        public const string MailPlus = "\uE364";
        public const string MailQuestion = "\uE365";
        public const string MailQuestionMark = "\uE365";
        public const string MailSearch = "\uE366";
        public const string MailWarning = "\uE367";
        public const string MailX = "\uE368";
        public const string Mailbox = "\uE3D4";
        public const string Mails = "\uE369";
        public const string Map = "\uE110";
        public const string MapMinus = "\uE686";
        public const string MapPin = "\uE111";
        public const string MapPinCheck = "\uE60F";
        public const string MapPinCheckInside = "\uE610";
        public const string MapPinHouse = "\uE61C";
        public const string MapPinMinus = "\uE611";
        public const string MapPinMinusInside = "\uE612";
        public const string MapPinOff = "\uE2A6";
        public const string MapPinPen = "\uE655";
        public const string MapPinPlus = "\uE613";
        public const string MapPinPlusInside = "\uE614";
        public const string MapPinSearch = "\uE6DE";
        public const string MapPinX = "\uE615";
        public const string MapPinXInside = "\uE616";
        public const string MapPinned = "\uE53D";
        public const string MapPlus = "\uE63F";
        public const string Margin = "\uE826";
        public const string MarginBottom = "\uE820";
        public const string MarginHorizontal = "\uE821";
        public const string MarginLeft = "\uE822";
        public const string MarginRight = "\uE823";
        public const string MarginTop = "\uE824";
        public const string MarginVertical = "\uE825";
        public const string Mars = "\uE641";
        public const string MarsStroke = "\uE642";
        public const string Martini = "\uE2E3";
        public const string Maximize = "\uE112";
        public const string Maximize2 = "\uE113";
        public const string Medal = "\uE36F";
        public const string Megaphone = "\uE235";
        public const string MegaphoneOff = "\uE370";
        public const string Meh = "\uE114";
        public const string MemoryStick = "\uE445";
        public const string Menu = "\uE115";
        public const string MenuSquare = "\uE453";
        public const string Merge = "\uE43F";
        public const string MessageCircle = "\uE116";
        public const string MessageCircleCheck = "\uE6BA";
        public const string MessageCircleCode = "\uE562";
        public const string MessageCircleDashed = "\uE563";
        public const string MessageCircleDashedCheck = "\uE746";
        public const string MessageCircleHeart = "\uE564";
        public const string MessageCircleMore = "\uE565";
        public const string MessageCircleOff = "\uE566";
        public const string MessageCirclePlus = "\uE567";
        public const string MessageCircleQuestion = "\uE568";
        public const string MessageCircleQuestionMark = "\uE568";
        public const string MessageCircleReply = "\uE569";
        public const string MessageCircleText = "\uE7A4";
        public const string MessageCircleWarning = "\uE56A";
        public const string MessageCircleX = "\uE56B";
        public const string MessageSquare = "\uE117";
        public const string MessageSquareCheck = "\uE6BB";
        public const string MessageSquareCode = "\uE56C";
        public const string MessageSquareDashed = "\uE40B";
        public const string MessageSquareDiff = "\uE56D";
        public const string MessageSquareDot = "\uE56E";
        public const string MessageSquareHeart = "\uE56F";
        public const string MessageSquareLock = "\uE62C";
        public const string MessageSquareMore = "\uE570";
        public const string MessageSquareOff = "\uE571";
        public const string MessageSquarePlus = "\uE40C";
        public const string MessageSquareQuote = "\uE572";
        public const string MessageSquareReply = "\uE573";
        public const string MessageSquareShare = "\uE574";
        public const string MessageSquareText = "\uE575";
        public const string MessageSquareWarning = "\uE576";
        public const string MessageSquareX = "\uE577";
        public const string MessagesCircle = "\uE773";
        public const string MessagesSquare = "\uE40D";
        public const string Metronome = "\uE6BC";
        public const string Mic = "\uE118";
        public const string Mic2 = "\uE349";
        public const string MicAudioLines = "\uE724";
        public const string MicOff = "\uE119";
        public const string MicSignal = "\uE1FA";
        public const string MicVocal = "\uE349";
        public const string Microchip = "\uE61A";
        public const string Microscope = "\uE2E4";
        public const string Microwave = "\uE37A";
        public const string MidiPort = "\uE739";
        public const string Milestone = "\uE298";
        public const string Milk = "\uE399";
        public const string MilkOff = "\uE39A";
        public const string Minimize = "\uE11A";
        public const string Minimize2 = "\uE11B";
        public const string Minus = "\uE11C";
        public const string MinusCircle = "\uE07E";
        public const string MinusSquare = "\uE171";
        public const string MirrorRectangular = "\uE6BD";
        public const string MirrorRound = "\uE6BE";
        public const string Monitor = "\uE11D";
        public const string MonitorActivity = "\uE827";
        public const string MonitorCheck = "\uE482";
        public const string MonitorCloud = "\uE699";
        public const string MonitorCog = "\uE603";
        public const string MonitorDot = "\uE483";
        public const string MonitorDown = "\uE421";
        public const string MonitorOff = "\uE1DC";
        public const string MonitorPause = "\uE484";
        public const string MonitorPc = "\uE77C";
        public const string MonitorPlay = "\uE485";
        public const string MonitorSmartphone = "\uE3A2";
        public const string MonitorSpeaker = "\uE210";
        public const string MonitorStop = "\uE486";
        public const string MonitorUp = "\uE422";
        public const string MonitorX = "\uE487";
        public const string Moon = "\uE11E";
        public const string MoonStar = "\uE410";
        public const string Mop = "\uE73B";
        public const string MopSparkles = "\uE73A";
        public const string MoreHorizontal = "\uE0B6";
        public const string MoreVertical = "\uE0B7";
        public const string Mosque = "\uE725";
        public const string Motorbike = "\uE698";
        public const string Mountain = "\uE231";
        public const string MountainSnow = "\uE232";
        public const string Mouse = "\uE28E";
        public const string MouseLeft = "\uE6BF";
        public const string MouseOff = "\uE5DB";
        public const string MousePointer = "\uE11F";
        public const string MousePointer2 = "\uE1C3";
        public const string MousePointer2Off = "\uE6A6";
        public const string MousePointerBan = "\uE5E7";
        public const string MousePointerClick = "\uE120";
        public const string MousePointerSquareDashed = "\uE509";
        public const string MouseRight = "\uE6C0";
        public const string Mouth = "\uE775";
        public const string MouthOff = "\uE774";
        public const string Move = "\uE121";
        public const string Move3D = "\uE2E5";
        public const string Move3d = "\uE2E5";
        public const string MoveDiagonal = "\uE1C4";
        public const string MoveDiagonal2 = "\uE1C5";
        public const string MoveDown = "\uE48C";
        public const string MoveDownLeft = "\uE48D";
        public const string MoveDownRight = "\uE48E";
        public const string MoveHorizontal = "\uE1C6";
        public const string MoveLeft = "\uE48F";
        public const string MoveRight = "\uE490";
        public const string MoveUp = "\uE491";
        public const string MoveUpLeft = "\uE492";
        public const string MoveUpRight = "\uE493";
        public const string MoveVertical = "\uE1C7";
        public const string Music = "\uE122";
        public const string Music2 = "\uE34A";
        public const string Music3 = "\uE34B";
        public const string Music4 = "\uE34C";
        public const string Navigation = "\uE123";
        public const string Navigation2 = "\uE124";
        public const string Navigation2Off = "\uE2A7";
        public const string NavigationOff = "\uE2A8";
        public const string NepaliRupee = "\uE781";
        public const string Network = "\uE125";
        public const string Newspaper = "\uE348";
        public const string Nfc = "\uE3C3";
        public const string NonBinary = "\uE643";
        public const string Notebook = "\uE595";
        public const string NotebookDot = "\uE776";
        public const string NotebookPen = "\uE596";
        public const string NotebookTabs = "\uE597";
        public const string NotebookText = "\uE598";
        public const string NotepadText = "\uE599";
        public const string NotepadTextDashed = "\uE59A";
        public const string Numbers = "\uE804";
        public const string Nut = "\uE39B";
        public const string NutOff = "\uE39C";
        public const string Octagon = "\uE126";
        public const string OctagonAlert = "\uE127";
        public const string OctagonMinus = "\uE627";
        public const string OctagonPause = "\uE21B";
        public const string OctagonX = "\uE128";
        public const string Omega = "\uE619";
        public const string Option = "\uE1F8";
        public const string Orbit = "\uE3E7";
        public const string Origami = "\uE5E3";
        public const string Outdent = "\uE107";
        public const string Package = "\uE129";
        public const string Package2 = "\uE340";
        public const string PackageCheck = "\uE266";
        public const string PackageMinus = "\uE267";
        public const string PackageOpen = "\uE2CC";
        public const string PackagePlus = "\uE268";
        public const string PackageSearch = "\uE269";
        public const string PackageX = "\uE26A";
        public const string PaintBucket = "\uE2E6";
        public const string PaintRoller = "\uE59E";
        public const string Paintbrush = "\uE2E7";
        public const string Paintbrush2 = "\uE2E8";
        public const string PaintbrushVertical = "\uE2E8";
        public const string Palette = "\uE1DD";
        public const string Palmtree = "\uE281";
        public const string Panda = "\uE668";
        public const string PanelBottom = "\uE42C";
        public const string PanelBottomClose = "\uE42D";
        public const string PanelBottomDashed = "\uE42E";
        public const string PanelBottomInactive = "\uE42E";
        public const string PanelBottomOpen = "\uE42F";
        public const string PanelLeft = "\uE12A";
        public const string PanelLeftClose = "\uE21C";
        public const string PanelLeftDashed = "\uE430";
        public const string PanelLeftInactive = "\uE430";
        public const string PanelLeftOpen = "\uE21D";
        public const string PanelLeftRightDashed = "\uE692";
        public const string PanelRight = "\uE431";
        public const string PanelRightClose = "\uE432";
        public const string PanelRightDashed = "\uE433";
        public const string PanelRightInactive = "\uE433";
        public const string PanelRightOpen = "\uE434";
        public const string PanelTop = "\uE435";
        public const string PanelTopBottomDashed = "\uE693";
        public const string PanelTopClose = "\uE436";
        public const string PanelTopDashed = "\uE437";
        public const string PanelTopInactive = "\uE437";
        public const string PanelTopOpen = "\uE438";
        public const string PanelsLeftBottom = "\uE12B";
        public const string PanelsLeftRight = "\uE099";
        public const string PanelsRightBottom = "\uE588";
        public const string PanelsTopBottom = "\uE58A";
        public const string PanelsTopLeft = "\uE12C";
        public const string PaperBag = "\uE71D";
        public const string Paperclip = "\uE12D";
        public const string Parasol = "\uE6FD";
        public const string Parentheses = "\uE444";
        public const string Park = "\uE777";
        public const string ParkingCircle = "\uE3C9";
        public const string ParkingCircleOff = "\uE3CA";
        public const string ParkingMeter = "\uE500";
        public const string ParkingSquare = "\uE3CB";
        public const string ParkingSquareOff = "\uE3CC";
        public const string PartyPopper = "\uE343";
        public const string Pause = "\uE12E";
        public const string PauseCircle = "\uE07F";
        public const string PauseOctagon = "\uE21B";
        public const string PawPrint = "\uE4F5";
        public const string PcCase = "\uE446";
        public const string Pen = "\uE12F";
        public const string PenBox = "\uE172";
        public const string PenLine = "\uE130";
        public const string PenOff = "\uE5EE";
        public const string PenSquare = "\uE172";
        public const string PenTool = "\uE131";
        public const string Pencil = "\uE1F9";
        public const string PencilLine = "\uE4F0";
        public const string PencilOff = "\uE5EF";
        public const string PencilRuler = "\uE4F1";
        public const string PencilSparkles = "\uE70F";
        public const string Pentagon = "\uE52B";
        public const string Percent = "\uE132";
        public const string PercentCircle = "\uE51A";
        public const string PercentDiamond = "\uE51B";
        public const string PercentSquare = "\uE51C";
        public const string PerfumeBottle = "\uE7AE";
        public const string PersonStanding = "\uE21E";
        public const string Phi = "\uE71C";
        public const string PhilippinePeso = "\uE604";
        public const string Phone = "\uE133";
        public const string PhoneCall = "\uE134";
        public const string PhoneForwarded = "\uE135";
        public const string PhoneIncoming = "\uE136";
        public const string PhoneLog = "\uE79F";
        public const string PhoneMissed = "\uE137";
        public const string PhoneOff = "\uE138";
        public const string PhoneOutgoing = "\uE139";
        public const string Pi = "\uE472";
        public const string PiSquare = "\uE488";
        public const string Piano = "\uE561";
        public const string Pickaxe = "\uE5C6";
        public const string PictureInPicture = "\uE3AE";
        public const string PictureInPicture2 = "\uE3AF";
        public const string PieChart = "\uE06B";
        public const string PiggyBank = "\uE13A";
        public const string Pilcrow = "\uE3A3";
        public const string PilcrowLeft = "\uE5DC";
        public const string PilcrowRight = "\uE5DD";
        public const string PilcrowSquare = "\uE48B";
        public const string Pill = "\uE3BD";
        public const string PillBottle = "\uE5EA";
        public const string Pin = "\uE259";
        public const string PinOff = "\uE2B6";
        public const string Pipette = "\uE13B";
        public const string Pizza = "\uE354";
        public const string Plane = "\uE1DE";
        public const string PlaneLanding = "\uE3CD";
        public const string PlaneTakeoff = "\uE3CE";
        public const string PlantPot = "\uE778";
        public const string Play = "\uE13C";
        public const string PlayCircle = "\uE080";
        public const string PlayOff = "\uE6FE";
        public const string PlaySquare = "\uE481";
        public const string PlayingCard = "\uE747";
        public const string PlayingCards = "\uE749";
        public const string PlayingCardsFan = "\uE748";
        public const string Plug = "\uE37F";
        public const string Plug2 = "\uE380";
        public const string PlugZap = "\uE45C";
        public const string PlugZap2 = "\uE45C";
        public const string Plus = "\uE13D";
        public const string PlusCircle = "\uE081";
        public const string PlusSquare = "\uE173";
        public const string PocketKnife = "\uE4A0";
        public const string Podcast = "\uE1FA";
        public const string Podium = "\uE704";
        public const string Pointer = "\uE1E8";
        public const string PointerOff = "\uE57F";
        public const string Popcorn = "\uE4BE";
        public const string Popsicle = "\uE4BF";
        public const string PoundSterling = "\uE13F";
        public const string Power = "\uE140";
        public const string PowerCircle = "\uE550";
        public const string PowerOff = "\uE209";
        public const string PowerSquare = "\uE551";
        public const string Presentation = "\uE4AE";
        public const string Printer = "\uE141";
        public const string Printer3d = "\uE78F";
        public const string PrinterCheck = "\uE5F5";
        public const string PrinterX = "\uE6C1";
        public const string Projector = "\uE4AF";
        public const string Proportions = "\uE5CF";
        public const string Puzzle = "\uE29C";
        public const string Pyramid = "\uE52C";
        public const string QrCode = "\uE1DF";
        public const string Quote = "\uE239";
        public const string Rabbit = "\uE4F6";
        public const string Radar = "\uE497";
        public const string Radiation = "\uE442";
        public const string Radical = "\uE5C2";
        public const string Radio = "\uE142";
        public const string RadioOff = "\uE6DD";
        public const string RadioReceiver = "\uE1FB";
        public const string RadioTower = "\uE404";
        public const string Radius = "\uE52D";
        public const string Rainbow = "\uE4C2";
        public const string Rat = "\uE3EB";
        public const string Ratio = "\uE4E8";
        public const string Receipt = "\uE3D3";
        public const string ReceiptCent = "\uE5A5";
        public const string ReceiptEuro = "\uE5A6";
        public const string ReceiptIndianRupee = "\uE5A7";
        public const string ReceiptJapaneseYen = "\uE5A8";
        public const string ReceiptPoundSterling = "\uE5A9";
        public const string ReceiptRussianRuble = "\uE5AA";
        public const string ReceiptSwissFranc = "\uE5AB";
        public const string ReceiptText = "\uE5AC";
        public const string ReceiptTurkishLira = "\uE67F";
        public const string RectangleCircle = "\uE673";
        public const string RectangleEllipsis = "\uE21F";
        public const string RectangleGoggles = "\uE656";
        public const string RectangleHorizontal = "\uE376";
        public const string RectangleVertical = "\uE377";
        public const string Recycle = "\uE2E9";
        public const string Redo = "\uE143";
        public const string Redo2 = "\uE2A0";
        public const string RedoDot = "\uE450";
        public const string RefreshCcw = "\uE144";
        public const string RefreshCcwDot = "\uE4B2";
        public const string RefreshCw = "\uE145";
        public const string RefreshCwOff = "\uE498";
        public const string Refrigerator = "\uE37B";
        public const string Regex = "\uE1FC";
        public const string RemoveFormatting = "\uE3B3";
        public const string Repeat = "\uE146";
        public const string Repeat1 = "\uE1FD";
        public const string Repeat2 = "\uE411";
        public const string RepeatOff = "\uE6F3";
        public const string Replace = "\uE3DB";
        public const string ReplaceAll = "\uE3DC";
        public const string Reply = "\uE22A";
        public const string ReplyAll = "\uE22B";
        public const string Rewind = "\uE147";
        public const string Ribbon = "\uE558";
        public const string Road = "\uE6D9";
        public const string RobotArm = "\uE740";
        public const string RobotVacuum = "\uE74A";
        public const string Rocket = "\uE286";
        public const string RockingChair = "\uE233";
        public const string RollerCoaster = "\uE480";
        public const string Rose = "\uE691";
        public const string Rotate3D = "\uE2EA";
        public const string Rotate3d = "\uE2EA";
        public const string RotateCcw = "\uE148";
        public const string RotateCcwClock = "\uE1F5";
        public const string RotateCcwKey = "\uE650";
        public const string RotateCcwSquare = "\uE5D0";
        public const string RotateCw = "\uE149";
        public const string RotateCwClock = "\uE782";
        public const string RotateCwFadingClock = "\uE726";
        public const string RotateCwSquare = "\uE5D1";
        public const string Route = "\uE53E";
        public const string RouteOff = "\uE53F";
        public const string Router = "\uE3BF";
        public const string Rows = "\uE439";
        public const string Rows2 = "\uE439";
        public const string Rows3 = "\uE58A";
        public const string Rows4 = "\uE58B";
        public const string Rss = "\uE14A";
        public const string RugbyBall = "\uE794";
        public const string Ruler = "\uE14B";
        public const string RulerDimensionLine = "\uE662";
        public const string RussianRuble = "\uE14C";
        public const string Sailboat = "\uE37E";
        public const string Salad = "\uE3A8";
        public const string Sandwich = "\uE3A9";
        public const string Satellite = "\uE447";
        public const string SatelliteDish = "\uE448";
        public const string SaudiRiyal = "\uE64B";
        public const string Save = "\uE14D";
        public const string SaveAll = "\uE40F";
        public const string SaveCheck = "\uE710";
        public const string SaveOff = "\uE5F3";
        public const string SavePen = "\uE705";
        public const string SavePlus = "\uE706";
        public const string Scale = "\uE212";
        public const string Scale3D = "\uE2EB";
        public const string Scale3d = "\uE2EB";
        public const string Scaling = "\uE2EC";
        public const string Scan = "\uE257";
        public const string ScanBarcode = "\uE535";
        public const string ScanBox = "\uE720";
        public const string ScanEye = "\uE536";
        public const string ScanFace = "\uE371";
        public const string ScanHeart = "\uE63A";
        public const string ScanLine = "\uE258";
        public const string ScanQrCode = "\uE5F6";
        public const string ScanSearch = "\uE537";
        public const string ScanSquare = "\uE727";
        public const string ScanText = "\uE538";
        public const string ScatterChart = "\uE48A";
        public const string School = "\uE3E3";
        public const string School2 = "\uE3E5";
        public const string Scissors = "\uE14E";
        public const string ScissorsLineDashed = "\uE4E9";
        public const string ScissorsSquare = "\uE4EC";
        public const string ScissorsSquareDashedBottom = "\uE4EB";
        public const string Scooter = "\uE6AC";
        public const string ScratchBlocks = "\uE79B";
        public const string ScreenShare = "\uE14F";
        public const string ScreenShareOff = "\uE150";
        public const string Screw = "\uE7A0";
        public const string Scroll = "\uE2ED";
        public const string ScrollText = "\uE45F";
        public const string Search = "\uE151";
        public const string SearchAlert = "\uE6E7";
        public const string SearchCheck = "\uE4AA";
        public const string SearchCode = "\uE4AB";
        public const string SearchSlash = "\uE4AC";
        public const string SearchX = "\uE4AD";
        public const string Section = "\uE5E8";
        public const string Send = "\uE152";
        public const string SendHorizonal = "\uE4F2";
        public const string SendHorizontal = "\uE4F2";
        public const string SendToBack = "\uE4F3";
        public const string SeparatorHorizontal = "\uE1C8";
        public const string SeparatorVertical = "\uE1C9";
        public const string Server = "\uE153";
        public const string ServerCog = "\uE341";
        public const string ServerCrash = "\uE1E9";
        public const string ServerNas = "\uE7A5";
        public const string ServerOff = "\uE1EA";
        public const string ServerPlus = "\uE721";
        public const string Settings = "\uE154";
        public const string Settings2 = "\uE245";
        public const string Shapes = "\uE4B3";
        public const string Share = "\uE155";
        public const string Share2 = "\uE156";
        public const string Sheet = "\uE157";
        public const string Shell = "\uE4F7";
        public const string ShelvingUnit = "\uE6C2";
        public const string Shield = "\uE158";
        public const string ShieldAlert = "\uE1FE";
        public const string ShieldBan = "\uE159";
        public const string ShieldCheck = "\uE1FF";
        public const string ShieldClose = "\uE200";
        public const string ShieldCog = "\uE6E9";
        public const string ShieldCogCorner = "\uE6E8";
        public const string ShieldEllipsis = "\uE516";
        public const string ShieldHalf = "\uE517";
        public const string ShieldHouse = "\uE7A1";
        public const string ShieldKeyhole = "\uE722";
        public const string ShieldLock = "\uE731";
        public const string ShieldMinus = "\uE518";
        public const string ShieldOff = "\uE15A";
        public const string ShieldPlus = "\uE519";
        public const string ShieldQuestion = "\uE40E";
        public const string ShieldQuestionMark = "\uE40E";
        public const string ShieldUser = "\uE647";
        public const string ShieldX = "\uE200";
        public const string Ship = "\uE3BA";
        public const string ShipCargo = "\uE741";
        public const string ShipWheel = "\uE502";
        public const string Shirt = "\uE1CA";
        public const string ShoppingBag = "\uE15B";
        public const string ShoppingBasket = "\uE4EA";
        public const string ShoppingCart = "\uE15C";
        public const string ShoppingCartMinus = "\uE752";
        public const string ShoppingCartPlus = "\uE753";
        public const string Shovel = "\uE15D";
        public const string ShowerHead = "\uE37C";
        public const string Shredder = "\uE65B";
        public const string Shrimp = "\uE649";
        public const string ShrimpOff = "\uE754";
        public const string Shrink = "\uE220";
        public const string Shrub = "\uE2EE";
        public const string Shuffle = "\uE15E";
        public const string Sidebar = "\uE12A";
        public const string SidebarClose = "\uE21C";
        public const string SidebarOpen = "\uE21D";
        public const string Sigma = "\uE201";
        public const string SigmaSquare = "\uE489";
        public const string Signal = "\uE25F";
        public const string SignalHigh = "\uE260";
        public const string SignalLow = "\uE261";
        public const string SignalMedium = "\uE262";
        public const string SignalZero = "\uE263";
        public const string Signature = "\uE5F2";
        public const string Signpost = "\uE540";
        public const string SignpostBig = "\uE541";
        public const string Siren = "\uE2EF";
        public const string SirenOff = "\uE7CD";
        public const string SixteenthNotes = "\uE34C";
        public const string SkipBack = "\uE15F";
        public const string SkipForward = "\uE160";
        public const string Skull = "\uE221";
        public const string Slash = "\uE51D";
        public const string SlashSquare = "\uE174";
        public const string Slice = "\uE2F0";
        public const string Sliders = "\uE162";
        public const string SlidersHorizontal = "\uE29A";
        public const string SlidersVertical = "\uE162";
        public const string Smartphone = "\uE163";
        public const string SmartphoneCharging = "\uE22E";
        public const string SmartphoneNfc = "\uE3C4";
        public const string SmartphoneUserRound = "\uE7B8";
        public const string Smile = "\uE164";
        public const string SmilePlus = "\uE301";
        public const string Snail = "\uE4F8";
        public const string Snowflake = "\uE165";
        public const string SoapDispenserDroplet = "\uE669";
        public const string Sofa = "\uE2C4";
        public const string SolarPanel = "\uE69F";
        public const string SortAsc = "\uE04C";
        public const string SortDesc = "\uE047";
        public const string Soup = "\uE3AA";
        public const string Space = "\uE3DD";
        public const string Spade = "\uE499";
        public const string Sparkle = "\uE47E";
        public const string Sparkles = "\uE412";
        public const string Speaker = "\uE166";
        public const string Speech = "\uE51E";
        public const string SpellCheck = "\uE49A";
        public const string SpellCheck2 = "\uE49B";
        public const string Spline = "\uE38B";
        public const string SplinePointer = "\uE64F";
        public const string Split = "\uE440";
        public const string SplitSquareHorizontal = "\uE3B6";
        public const string SplitSquareVertical = "\uE3B7";
        public const string Spool = "\uE677";
        public const string SportShoe = "\uE6DB";
        public const string Spotlight = "\uE682";
        public const string SprayCan = "\uE495";
        public const string Sprout = "\uE1EB";
        public const string Square = "\uE167";
        public const string SquareActivity = "\uE4B4";
        public const string SquareArrowDown = "\uE427";
        public const string SquareArrowDownLeft = "\uE4B5";
        public const string SquareArrowDownRight = "\uE4B6";
        public const string SquareArrowLeft = "\uE428";
        public const string SquareArrowOutDownLeft = "\uE5A1";
        public const string SquareArrowOutDownRight = "\uE5A2";
        public const string SquareArrowOutUpLeft = "\uE5A3";
        public const string SquareArrowOutUpRight = "\uE5A4";
        public const string SquareArrowRight = "\uE429";
        public const string SquareArrowRightEnter = "\uE6C3";
        public const string SquareArrowRightExit = "\uE6C4";
        public const string SquareArrowUp = "\uE42A";
        public const string SquareArrowUpLeft = "\uE4B7";
        public const string SquareArrowUpRight = "\uE4B8";
        public const string SquareAsterisk = "\uE168";
        public const string SquareBookmark = "\uE03B";
        public const string SquareBottomDashedScissors = "\uE4EB";
        public const string SquareCenterlineDashedHorizontal = "\uE6C5";
        public const string SquareCenterlineDashedVertical = "\uE6C6";
        public const string SquareChartGantt = "\uE169";
        public const string SquareCheck = "\uE559";
        public const string SquareCheckBig = "\uE16A";
        public const string SquareChevronDown = "\uE3CF";
        public const string SquareChevronLeft = "\uE3D0";
        public const string SquareChevronRight = "\uE3D1";
        public const string SquareChevronUp = "\uE3D2";
        public const string SquareChevronsDown = "\uE7CE";
        public const string SquareChevronsLeft = "\uE7CF";
        public const string SquareChevronsRight = "\uE7D0";
        public const string SquareChevronsUp = "\uE7D1";
        public const string SquareCode = "\uE16B";
        public const string SquareDashed = "\uE1CB";
        public const string SquareDashedBottom = "\uE4C0";
        public const string SquareDashedBottomCode = "\uE4C1";
        public const string SquareDashedKanban = "\uE16C";
        public const string SquareDashedMousePointer = "\uE509";
        public const string SquareDashedPlus = "\uE783";
        public const string SquareDashedText = "\uE6EA";
        public const string SquareDashedTopSolid = "\uE66C";
        public const string SquareDashedX = "\uE785";
        public const string SquareDashedXCorner = "\uE784";
        public const string SquareDimensions = "\uE73C";
        public const string SquareDivide = "\uE16D";
        public const string SquareDot = "\uE16E";
        public const string SquareEqual = "\uE16F";
        public const string SquareExclamationPoint = "\uE75B";
        public const string SquareFunction = "\uE22D";
        public const string SquareGanttChart = "\uE169";
        public const string SquareKanban = "\uE170";
        public const string SquareKanbanPlus = "\uE805";
        public const string SquareLetterA = "\uE806";
        public const string SquareLetterB = "\uE807";
        public const string SquareLetterC = "\uE808";
        public const string SquareLetterD = "\uE809";
        public const string SquareLetterE = "\uE80A";
        public const string SquareLetterF = "\uE80B";
        public const string SquareLetterG = "\uE80C";
        public const string SquareLetterH = "\uE80D";
        public const string SquareLetterI = "\uE80E";
        public const string SquareLetterJ = "\uE80F";
        public const string SquareLetterK = "\uE810";
        public const string SquareLetterL = "\uE811";
        public const string SquareLetterM = "\uE503";
        public const string SquareLetterN = "\uE812";
        public const string SquareLetterO = "\uE813";
        public const string SquareLetterP = "\uE3CB";
        public const string SquareLetterPOff = "\uE3CC";
        public const string SquareLetterQ = "\uE814";
        public const string SquareLetterR = "\uE815";
        public const string SquareLetterS = "\uE816";
        public const string SquareLetterT = "\uE817";
        public const string SquareLetterU = "\uE818";
        public const string SquareLetterV = "\uE819";
        public const string SquareLetterW = "\uE81A";
        public const string SquareLetterX = "\uE81B";
        public const string SquareLetterY = "\uE81C";
        public const string SquareLetterZ = "\uE81D";
        public const string SquareLibrary = "\uE54F";
        public const string SquareM = "\uE503";
        public const string SquareMenu = "\uE453";
        public const string SquareMinus = "\uE171";
        public const string SquareMousePointer = "\uE202";
        public const string SquareOff = "\uE728";
        public const string SquarePadding = "\uE82E";
        public const string SquarePaddingBottom = "\uE828";
        public const string SquarePaddingHorizontal = "\uE829";
        public const string SquarePaddingLeft = "\uE82A";
        public const string SquarePaddingRight = "\uE82B";
        public const string SquarePaddingTop = "\uE82C";
        public const string SquarePaddingVertical = "\uE82D";
        public const string SquareParking = "\uE3CB";
        public const string SquareParkingOff = "\uE3CC";
        public const string SquarePause = "\uE684";
        public const string SquarePen = "\uE172";
        public const string SquarePercent = "\uE51C";
        public const string SquarePi = "\uE488";
        public const string SquarePilcrow = "\uE48B";
        public const string SquarePlay = "\uE481";
        public const string SquarePlus = "\uE173";
        public const string SquarePower = "\uE551";
        public const string SquareRadical = "\uE5C3";
        public const string SquareRoundCorner = "\uE648";
        public const string SquareScissors = "\uE4EC";
        public const string SquareSigma = "\uE489";
        public const string SquareSlash = "\uE174";
        public const string SquareSparkles = "\uE78C";
        public const string SquareSplitHorizontal = "\uE3B6";
        public const string SquareSplitVertical = "\uE3B7";
        public const string SquareSquare = "\uE60E";
        public const string SquareStack = "\uE4A2";
        public const string SquareStar = "\uE68E";
        public const string SquareStop = "\uE685";
        public const string SquareTerminal = "\uE20A";
        public const string SquareText = "\uE736";
        public const string SquareUser = "\uE465";
        public const string SquareUserRound = "\uE466";
        public const string SquareX = "\uE175";
        public const string SquaresExclude = "\uE657";
        public const string SquaresIntersect = "\uE658";
        public const string SquaresSubtract = "\uE659";
        public const string SquaresUnite = "\uE65A";
        public const string Squircle = "\uE57A";
        public const string SquircleDashed = "\uE679";
        public const string Squirrel = "\uE49F";
        public const string Stamp = "\uE3BB";
        public const string Star = "\uE176";
        public const string StarCheck = "\uE707";
        public const string StarHalf = "\uE20B";
        public const string StarMinus = "\uE708";
        public const string StarOff = "\uE2B0";
        public const string StarPlus = "\uE709";
        public const string StarX = "\uE70A";
        public const string Stars = "\uE412";
        public const string StepBack = "\uE3E9";
        public const string StepForward = "\uE3EA";
        public const string Stethoscope = "\uE2F1";
        public const string Sticker = "\uE302";
        public const string StickyNote = "\uE303";
        public const string StickyNoteCheck = "\uE6F6";
        public const string StickyNoteMinus = "\uE6F7";
        public const string StickyNoteOff = "\uE6F8";
        public const string StickyNotePlus = "\uE6F9";
        public const string StickyNoteX = "\uE6FA";
        public const string StickyNotes = "\uE6FB";
        public const string Stone = "\uE6EB";
        public const string StopCircle = "\uE083";
        public const string Store = "\uE3E4";
        public const string StretchHorizontal = "\uE27C";
        public const string StretchVertical = "\uE27D";
        public const string Strikethrough = "\uE177";
        public const string Subscript = "\uE25C";
        public const string Subtitles = "\uE3A4";
        public const string Summary = "\uE711";
        public const string Sun = "\uE178";
        public const string SunDim = "\uE299";
        public const string SunMedium = "\uE2B1";
        public const string SunMoon = "\uE2B2";
        public const string SunSnow = "\uE372";
        public const string Sunrise = "\uE179";
        public const string Sunset = "\uE17A";
        public const string Superscript = "\uE25E";
        public const string SwatchBook = "\uE59F";
        public const string SwissFranc = "\uE17B";
        public const string SwitchCamera = "\uE17C";
        public const string Sword = "\uE2B3";
        public const string Swords = "\uE2B4";
        public const string Syringe = "\uE2F2";
        public const string Table = "\uE17D";
        public const string Table2 = "\uE2F9";
        public const string TableCellsMerge = "\uE5C7";
        public const string TableCellsSplit = "\uE5C8";
        public const string TableColumnsSplit = "\uE5C9";
        public const string TableConfig = "\uE661";
        public const string TableOfContents = "\uE61E";
        public const string TableProperties = "\uE4DB";
        public const string TableRowsSplit = "\uE5CA";
        public const string Tablet = "\uE17E";
        public const string TabletSmartphone = "\uE50A";
        public const string Tablets = "\uE3BE";
        public const string Tag = "\uE17F";
        public const string TagPlus = "\uE712";
        public const string TagX = "\uE713";
        public const string Tags = "\uE35C";
        public const string Tally1 = "\uE4D6";
        public const string Tally2 = "\uE4D7";
        public const string Tally3 = "\uE4D8";
        public const string Tally4 = "\uE4D9";
        public const string Tally5 = "\uE4DA";
        public const string Tangent = "\uE52E";
        public const string Target = "\uE180";
        public const string Telescope = "\uE5C5";
        public const string Tent = "\uE227";
        public const string TentTree = "\uE53B";
        public const string Terminal = "\uE181";
        public const string TerminalSquare = "\uE20A";
        public const string TestTube = "\uE405";
        public const string TestTube2 = "\uE406";
        public const string TestTubeDiagonal = "\uE406";
        public const string TestTubes = "\uE407";
        public const string Text = "\uE185";
        public const string TextAlignCenter = "\uE182";
        public const string TextAlignEnd = "\uE183";
        public const string TextAlignJustify = "\uE184";
        public const string TextAlignJustifyCenter = "\uE795";
        public const string TextAlignJustifyEnd = "\uE796";
        public const string TextAlignJustifyStart = "\uE797";
        public const string TextAlignStart = "\uE185";
        public const string TextCursor = "\uE264";
        public const string TextCursorInput = "\uE265";
        public const string TextInitial = "\uE605";
        public const string TextQuote = "\uE49E";
        public const string TextSearch = "\uE5AD";
        public const string TextSelect = "\uE6EA";
        public const string TextSelection = "\uE3DE";
        public const string TextWrap = "\uE248";
        public const string Theater = "\uE522";
        public const string Thermometer = "\uE186";
        public const string ThermometerSnowflake = "\uE187";
        public const string ThermometerSun = "\uE188";
        public const string ThumbsDown = "\uE189";
        public const string ThumbsUp = "\uE18A";
        public const string TicTacToe = "\uE768";
        public const string Ticket = "\uE20F";
        public const string TicketCheck = "\uE5AE";
        public const string TicketMinus = "\uE5AF";
        public const string TicketPercent = "\uE5B0";
        public const string TicketPlus = "\uE5B1";
        public const string TicketSlash = "\uE5B2";
        public const string TicketX = "\uE5B3";
        public const string Tickets = "\uE622";
        public const string TicketsPlane = "\uE623";
        public const string Timeline = "\uE6EC";
        public const string Timer = "\uE1E0";
        public const string TimerOff = "\uE249";
        public const string TimerReset = "\uE236";
        public const string ToggleLeft = "\uE18B";
        public const string ToggleRight = "\uE18C";
        public const string Toilet = "\uE635";
        public const string Tombstone = "\uE7B9";
        public const string ToolCase = "\uE67D";
        public const string Toolbox = "\uE6B0";
        public const string Toothbrush = "\uE76A";
        public const string ToothbrushSparkles = "\uE769";
        public const string Tornado = "\uE218";
        public const string Torus = "\uE52F";
        public const string Touchpad = "\uE449";
        public const string TouchpadOff = "\uE44A";
        public const string TowelRack = "\uE6C7";
        public const string TowerControl = "\uE3BC";
        public const string ToyBrick = "\uE347";
        public const string Tractor = "\uE504";
        public const string TrafficCone = "\uE505";
        public const string Trailer = "\uE742";
        public const string Train = "\uE2A9";
        public const string TrainFront = "\uE506";
        public const string TrainFrontTunnel = "\uE507";
        public const string TrainTrack = "\uE508";
        public const string TramFront = "\uE2A9";
        public const string Transgender = "\uE644";
        public const string Trash = "\uE18D";
        public const string Trash2 = "\uE18E";
        public const string TrashOff = "\uE763";
        public const string TreeDeciduous = "\uE2F3";
        public const string TreePalm = "\uE281";
        public const string TreePine = "\uE2F4";
        public const string Trees = "\uE2F5";
        public const string TrendingDown = "\uE190";
        public const string TrendingUp = "\uE191";
        public const string TrendingUpDown = "\uE625";
        public const string Triangle = "\uE192";
        public const string TriangleAlert = "\uE193";
        public const string TriangleDashed = "\uE63D";
        public const string TriangleRight = "\uE4ED";
        public const string TrianglesCenterlineDashedHorizontal = "\uE35E";
        public const string TrianglesCenterlineDashedVertical = "\uE360";
        public const string Trophy = "\uE373";
        public const string Truck = "\uE194";
        public const string TruckElectric = "\uE65F";
        public const string TubeLotion = "\uE786";
        public const string TurkishLira = "\uE680";
        public const string Turntable = "\uE68C";
        public const string Turtle = "\uE4F9";
        public const string Tv = "\uE195";
        public const string Tv2 = "\uE203";
        public const string TvMinimal = "\uE203";
        public const string TvMinimalPlay = "\uE5EC";
        public const string Type = "\uE198";
        public const string TypeOutline = "\uE602";
        public const string Umbrella = "\uE199";
        public const string UmbrellaOff = "\uE543";
        public const string Underline = "\uE19A";
        public const string Undo = "\uE19B";
        public const string Undo2 = "\uE2A1";
        public const string UndoDot = "\uE451";
        public const string UnfoldHorizontal = "\uE43D";
        public const string UnfoldVertical = "\uE43E";
        public const string Ungroup = "\uE467";
        public const string University = "\uE3E5";
        public const string Unlink = "\uE19C";
        public const string Unlink2 = "\uE19D";
        public const string Unlock = "\uE10C";
        public const string UnlockKeyhole = "\uE532";
        public const string Unplug = "\uE45D";
        public const string Upload = "\uE19E";
        public const string UploadCloud = "\uE091";
        public const string Usb = "\uE356";
        public const string UsbCPort = "\uE73D";
        public const string User = "\uE19F";
        public const string User2 = "\uE468";
        public const string UserCheck = "\uE1A0";
        public const string UserCheck2 = "\uE469";
        public const string UserCircle = "\uE461";
        public const string UserCircle2 = "\uE462";
        public const string UserCog = "\uE342";
        public const string UserCog2 = "\uE46A";
        public const string UserGroup = "\uE764";
        public const string UserKey = "\uE6C8";
        public const string UserLock = "\uE660";
        public const string UserMinus = "\uE1A1";
        public const string UserMinus2 = "\uE46B";
        public const string UserPen = "\uE5FC";
        public const string UserPlus = "\uE1A2";
        public const string UserPlus2 = "\uE46C";
        public const string UserRound = "\uE468";
        public const string UserRoundArrowLeft = "\uE714";
        public const string UserRoundCheck = "\uE469";
        public const string UserRoundCog = "\uE46A";
        public const string UserRoundGroup = "\uE765";
        public const string UserRoundKey = "\uE6C9";
        public const string UserRoundMinus = "\uE46B";
        public const string UserRoundPen = "\uE5FD";
        public const string UserRoundPlus = "\uE46C";
        public const string UserRoundSearch = "\uE578";
        public const string UserRoundShield = "\uE7D2";
        public const string UserRoundStar = "\uE7A2";
        public const string UserRoundX = "\uE46D";
        public const string UserSearch = "\uE579";
        public const string UserShield = "\uE729";
        public const string UserSquare = "\uE465";
        public const string UserSquare2 = "\uE466";
        public const string UserStar = "\uE687";
        public const string UserX = "\uE1A3";
        public const string UserX2 = "\uE46D";
        public const string Users = "\uE1A4";
        public const string Users2 = "\uE46E";
        public const string UsersCollaboration = "\uE7AF";
        public const string UsersRound = "\uE46E";
        public const string UsersRoundCollaboration = "\uE7B0";
        public const string Utensils = "\uE2F6";
        public const string UtensilsCrossed = "\uE2F7";
        public const string UtilityPole = "\uE3C2";
        public const string Van = "\uE6AD";
        public const string Variable = "\uE473";
        public const string Vault = "\uE58F";
        public const string VectorPolygon = "\uE74C";
        public const string VectorSquare = "\uE67C";
        public const string Vegan = "\uE39D";
        public const string VenetianMask = "\uE2AA";
        public const string Venus = "\uE645";
        public const string VenusAndMars = "\uE646";
        public const string Verified = "\uE241";
        public const string Vibrate = "\uE223";
        public const string VibrateOff = "\uE29D";
        public const string Video = "\uE1A5";
        public const string VideoOff = "\uE1A6";
        public const string Videotape = "\uE4CB";
        public const string View = "\uE1A7";
        public const string Virus = "\uE75D";
        public const string VirusOff = "\uE75C";
        public const string Voicemail = "\uE1A8";
        public const string Volleyball = "\uE62F";
        public const string Volume = "\uE1A9";
        public const string Volume1 = "\uE1AA";
        public const string Volume2 = "\uE1AB";
        public const string VolumeOff = "\uE626";
        public const string VolumeX = "\uE1AC";
        public const string Vote = "\uE3AD";
        public const string Wallet = "\uE204";
        public const string Wallet2 = "\uE4CD";
        public const string WalletCards = "\uE4CC";
        public const string WalletMinimal = "\uE4CD";
        public const string Wallpaper = "\uE44B";
        public const string Wand = "\uE246";
        public const string Wand2 = "\uE357";
        public const string WandSparkles = "\uE357";
        public const string Warehouse = "\uE3E6";
        public const string WashingMachine = "\uE590";
        public const string Watch = "\uE1AD";
        public const string Waves = "\uE283";
        public const string WavesArrowDown = "\uE6A9";
        public const string WavesArrowUp = "\uE6AA";
        public const string WavesHorizontal = "\uE283";
        public const string WavesLadder = "\uE63B";
        public const string WavesVertical = "\uE6F2";
        public const string Waypoints = "\uE542";
        public const string Webcam = "\uE205";
        public const string WebcamOff = "\uE6FF";
        public const string Webhook = "\uE374";
        public const string WebhookOff = "\uE5B7";
        public const string Weight = "\uE530";
        public const string WeightTilde = "\uE6AE";
        public const string Wheat = "\uE39E";
        public const string WheatOff = "\uE39F";
        public const string Whistle = "\uE74D";
        public const string WholeWord = "\uE3DF";
        public const string Wifi = "\uE1AE";
        public const string WifiCog = "\uE674";
        public const string WifiHigh = "\uE5F7";
        public const string WifiLock = "\uE7A3";
        public const string WifiLow = "\uE5F8";
        public const string WifiOff = "\uE1AF";
        public const string WifiPen = "\uE663";
        public const string WifiSync = "\uE681";
        public const string WifiZero = "\uE5F9";
        public const string Wind = "\uE1B0";
        public const string WindArrowDown = "\uE631";
        public const string WindArrowUp = "\uE798";
        public const string Wine = "\uE2F8";
        public const string WineOff = "\uE3A0";
        public const string Workflow = "\uE425";
        public const string Worm = "\uE5DA";
        public const string WrapText = "\uE248";
        public const string Wrench = "\uE1B1";
        public const string WrenchOff = "\uE70B";
        public const string X = "\uE1B2";
        public const string XCircle = "\uE084";
        public const string XLineTop = "\uE6CA";
        public const string XOctagon = "\uE128";
        public const string XSquare = "\uE175";
        public const string Zap = "\uE1B4";
        public const string ZapOff = "\uE1B5";
        public const string ZodiacAquarius = "\uE6CB";
        public const string ZodiacAries = "\uE6CC";
        public const string ZodiacCancer = "\uE6CD";
        public const string ZodiacCapricorn = "\uE6CE";
        public const string ZodiacGemini = "\uE6CF";
        public const string ZodiacLeo = "\uE6D0";
        public const string ZodiacLibra = "\uE6D1";
        public const string ZodiacOphiuchus = "\uE6D2";
        public const string ZodiacPisces = "\uE6D3";
        public const string ZodiacSagittarius = "\uE6D4";
        public const string ZodiacScorpio = "\uE6D5";
        public const string ZodiacTaurus = "\uE6D6";
        public const string ZodiacVirgo = "\uE6D7";
        public const string ZoomIn = "\uE1B6";
        public const string ZoomOut = "\uE1B7";

        static readonly Dictionary<string, string> Glyphs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "a-arrow-down", AArrowDown },
            { "a-arrow-up", AArrowUp },
            { "a-large-small", ALargeSmall },
            { "accessibility", Accessibility },
            { "activity", Activity },
            { "activity-square", ActivitySquare },
            { "ad", Ad },
            { "air-vent", AirVent },
            { "airplay", Airplay },
            { "alarm-check", AlarmCheck },
            { "alarm-clock", AlarmClock },
            { "alarm-clock-check", AlarmClockCheck },
            { "alarm-clock-minus", AlarmClockMinus },
            { "alarm-clock-off", AlarmClockOff },
            { "alarm-clock-plus", AlarmClockPlus },
            { "alarm-minus", AlarmMinus },
            { "alarm-plus", AlarmPlus },
            { "alarm-smoke", AlarmSmoke },
            { "album", Album },
            { "alert-circle", AlertCircle },
            { "alert-octagon", AlertOctagon },
            { "alert-triangle", AlertTriangle },
            { "align-center", AlignCenter },
            { "align-center-horizontal", AlignCenterHorizontal },
            { "align-center-vertical", AlignCenterVertical },
            { "align-end-horizontal", AlignEndHorizontal },
            { "align-end-vertical", AlignEndVertical },
            { "align-horizontal-distribute-center", AlignHorizontalDistributeCenter },
            { "align-horizontal-distribute-end", AlignHorizontalDistributeEnd },
            { "align-horizontal-distribute-start", AlignHorizontalDistributeStart },
            { "align-horizontal-justify-center", AlignHorizontalJustifyCenter },
            { "align-horizontal-justify-end", AlignHorizontalJustifyEnd },
            { "align-horizontal-justify-start", AlignHorizontalJustifyStart },
            { "align-horizontal-space-around", AlignHorizontalSpaceAround },
            { "align-horizontal-space-between", AlignHorizontalSpaceBetween },
            { "align-justify", AlignJustify },
            { "align-left", AlignLeft },
            { "align-right", AlignRight },
            { "align-start-horizontal", AlignStartHorizontal },
            { "align-start-vertical", AlignStartVertical },
            { "align-vertical-distribute-center", AlignVerticalDistributeCenter },
            { "align-vertical-distribute-end", AlignVerticalDistributeEnd },
            { "align-vertical-distribute-start", AlignVerticalDistributeStart },
            { "align-vertical-justify-center", AlignVerticalJustifyCenter },
            { "align-vertical-justify-end", AlignVerticalJustifyEnd },
            { "align-vertical-justify-start", AlignVerticalJustifyStart },
            { "align-vertical-space-around", AlignVerticalSpaceAround },
            { "align-vertical-space-between", AlignVerticalSpaceBetween },
            { "ambulance", Ambulance },
            { "ampersand", Ampersand },
            { "ampersands", Ampersands },
            { "amphora", Amphora },
            { "anchor", Anchor },
            { "angle", Angle },
            { "angry", Angry },
            { "annoyed", Annoyed },
            { "antenna", Antenna },
            { "anvil", Anvil },
            { "aperture", Aperture },
            { "app-window", AppWindow },
            { "app-window-mac", AppWindowMac },
            { "apple", Apple },
            { "archive", Archive },
            { "archive-arrow-down", ArchiveArrowDown },
            { "archive-arrow-up", ArchiveArrowUp },
            { "archive-restore", ArchiveRestore },
            { "archive-x", ArchiveX },
            { "area-chart", AreaChart },
            { "armchair", Armchair },
            { "armenian-dram", ArmenianDram },
            { "arrow-big-down", ArrowBigDown },
            { "arrow-big-down-dash", ArrowBigDownDash },
            { "arrow-big-left", ArrowBigLeft },
            { "arrow-big-left-dash", ArrowBigLeftDash },
            { "arrow-big-right", ArrowBigRight },
            { "arrow-big-right-dash", ArrowBigRightDash },
            { "arrow-big-up", ArrowBigUp },
            { "arrow-big-up-dash", ArrowBigUpDash },
            { "arrow-down", ArrowDown },
            { "arrow-down-0-1", ArrowDown01 },
            { "arrow-down-01", ArrowDown01_2 },
            { "arrow-down-1-0", ArrowDown10 },
            { "arrow-down-10", ArrowDown10_2 },
            { "arrow-down-a-z", ArrowDownAZ },
            { "arrow-down-az", ArrowDownAz },
            { "arrow-down-circle", ArrowDownCircle },
            { "arrow-down-from-line", ArrowDownFromLine },
            { "arrow-down-left", ArrowDownLeft },
            { "arrow-down-left-from-circle", ArrowDownLeftFromCircle },
            { "arrow-down-left-from-square", ArrowDownLeftFromSquare },
            { "arrow-down-left-square", ArrowDownLeftSquare },
            { "arrow-down-narrow-wide", ArrowDownNarrowWide },
            { "arrow-down-right", ArrowDownRight },
            { "arrow-down-right-from-circle", ArrowDownRightFromCircle },
            { "arrow-down-right-from-square", ArrowDownRightFromSquare },
            { "arrow-down-right-square", ArrowDownRightSquare },
            { "arrow-down-square", ArrowDownSquare },
            { "arrow-down-to-dot", ArrowDownToDot },
            { "arrow-down-to-line", ArrowDownToLine },
            { "arrow-down-up", ArrowDownUp },
            { "arrow-down-wide-narrow", ArrowDownWideNarrow },
            { "arrow-down-z-a", ArrowDownZA },
            { "arrow-down-za", ArrowDownZa },
            { "arrow-left", ArrowLeft },
            { "arrow-left-circle", ArrowLeftCircle },
            { "arrow-left-from-line", ArrowLeftFromLine },
            { "arrow-left-right", ArrowLeftRight },
            { "arrow-left-square", ArrowLeftSquare },
            { "arrow-left-to-line", ArrowLeftToLine },
            { "arrow-right", ArrowRight },
            { "arrow-right-circle", ArrowRightCircle },
            { "arrow-right-from-line", ArrowRightFromLine },
            { "arrow-right-left", ArrowRightLeft },
            { "arrow-right-square", ArrowRightSquare },
            { "arrow-right-to-line", ArrowRightToLine },
            { "arrow-up", ArrowUp },
            { "arrow-up-0-1", ArrowUp01 },
            { "arrow-up-01", ArrowUp01_2 },
            { "arrow-up-1-0", ArrowUp10 },
            { "arrow-up-10", ArrowUp10_2 },
            { "arrow-up-a-z", ArrowUpAZ },
            { "arrow-up-az", ArrowUpAz },
            { "arrow-up-circle", ArrowUpCircle },
            { "arrow-up-down", ArrowUpDown },
            { "arrow-up-from-dot", ArrowUpFromDot },
            { "arrow-up-from-line", ArrowUpFromLine },
            { "arrow-up-left", ArrowUpLeft },
            { "arrow-up-left-from-circle", ArrowUpLeftFromCircle },
            { "arrow-up-left-from-square", ArrowUpLeftFromSquare },
            { "arrow-up-left-square", ArrowUpLeftSquare },
            { "arrow-up-narrow-wide", ArrowUpNarrowWide },
            { "arrow-up-right", ArrowUpRight },
            { "arrow-up-right-from-circle", ArrowUpRightFromCircle },
            { "arrow-up-right-from-square", ArrowUpRightFromSquare },
            { "arrow-up-right-square", ArrowUpRightSquare },
            { "arrow-up-square", ArrowUpSquare },
            { "arrow-up-to-line", ArrowUpToLine },
            { "arrow-up-wide-narrow", ArrowUpWideNarrow },
            { "arrow-up-z-a", ArrowUpZA },
            { "arrow-up-za", ArrowUpZa },
            { "arrows-up-from-line", ArrowsUpFromLine },
            { "asterisk", Asterisk },
            { "asterisk-square", AsteriskSquare },
            { "astroid", Astroid },
            { "at-sign", AtSign },
            { "atom", Atom },
            { "audio-lines", AudioLines },
            { "audio-lines-off", AudioLinesOff },
            { "audio-lines-x", AudioLinesX },
            { "audio-waveform", AudioWaveform },
            { "avocado", Avocado },
            { "award", Award },
            { "axe", Axe },
            { "axis-3-d", Axis3D },
            { "axis-3d", Axis3d },
            { "azerbaijani-manat", AzerbaijaniManat },
            { "baby", Baby },
            { "backpack", Backpack },
            { "bacon", Bacon },
            { "badge", Badge },
            { "badge-alert", BadgeAlert },
            { "badge-cent", BadgeCent },
            { "badge-check", BadgeCheck },
            { "badge-dollar-sign", BadgeDollarSign },
            { "badge-euro", BadgeEuro },
            { "badge-help", BadgeHelp },
            { "badge-indian-rupee", BadgeIndianRupee },
            { "badge-info", BadgeInfo },
            { "badge-japanese-yen", BadgeJapaneseYen },
            { "badge-minus", BadgeMinus },
            { "badge-percent", BadgePercent },
            { "badge-plus", BadgePlus },
            { "badge-pound-sterling", BadgePoundSterling },
            { "badge-question-mark", BadgeQuestionMark },
            { "badge-russian-ruble", BadgeRussianRuble },
            { "badge-swiss-franc", BadgeSwissFranc },
            { "badge-turkish-lira", BadgeTurkishLira },
            { "badge-x", BadgeX },
            { "baggage-claim", BaggageClaim },
            { "ball-wave", BallWave },
            { "balloon", Balloon },
            { "ban", Ban },
            { "banana", Banana },
            { "bandage", Bandage },
            { "bangladeshi-taka", BangladeshiTaka },
            { "banknote", Banknote },
            { "banknote-arrow-down", BanknoteArrowDown },
            { "banknote-arrow-up", BanknoteArrowUp },
            { "banknote-check", BanknoteCheck },
            { "banknote-x", BanknoteX },
            { "bar-chart", BarChart },
            { "bar-chart-2", BarChart2 },
            { "bar-chart-3", BarChart3 },
            { "bar-chart-4", BarChart4 },
            { "bar-chart-big", BarChartBig },
            { "bar-chart-horizontal", BarChartHorizontal },
            { "bar-chart-horizontal-big", BarChartHorizontalBig },
            { "barcode", Barcode },
            { "barrel", Barrel },
            { "baseline", Baseline },
            { "bath", Bath },
            { "battery", Battery },
            { "battery-charging", BatteryCharging },
            { "battery-full", BatteryFull },
            { "battery-low", BatteryLow },
            { "battery-medium", BatteryMedium },
            { "battery-plus", BatteryPlus },
            { "battery-warning", BatteryWarning },
            { "beaker", Beaker },
            { "bean", Bean },
            { "bean-off", BeanOff },
            { "bed", Bed },
            { "bed-double", BedDouble },
            { "bed-single", BedSingle },
            { "beef", Beef },
            { "beef-off", BeefOff },
            { "beer", Beer },
            { "beer-off", BeerOff },
            { "belarusian-ruble", BelarusianRuble },
            { "bell", Bell },
            { "bell-check", BellCheck },
            { "bell-dot", BellDot },
            { "bell-electric", BellElectric },
            { "bell-minus", BellMinus },
            { "bell-off", BellOff },
            { "bell-plus", BellPlus },
            { "bell-ring", BellRing },
            { "between-horizonal-end", BetweenHorizonalEnd },
            { "between-horizonal-start", BetweenHorizonalStart },
            { "between-horizontal-end", BetweenHorizontalEnd },
            { "between-horizontal-start", BetweenHorizontalStart },
            { "between-vertical-end", BetweenVerticalEnd },
            { "between-vertical-start", BetweenVerticalStart },
            { "biceps-flexed", BicepsFlexed },
            { "bike", Bike },
            { "binary", Binary },
            { "binoculars", Binoculars },
            { "biohazard", Biohazard },
            { "bird", Bird },
            { "birdhouse", Birdhouse },
            { "bitcoin", Bitcoin },
            { "blend", Blend },
            { "blender", Blender },
            { "blinds", Blinds },
            { "blocks", Blocks },
            { "bluetooth", Bluetooth },
            { "bluetooth-connected", BluetoothConnected },
            { "bluetooth-off", BluetoothOff },
            { "bluetooth-searching", BluetoothSearching },
            { "bold", Bold },
            { "bolt", Bolt },
            { "bomb", Bomb },
            { "bone", Bone },
            { "bone-fracture", BoneFracture },
            { "book", Book },
            { "book-a", BookA },
            { "book-alert", BookAlert },
            { "book-audio", BookAudio },
            { "book-bookmark", BookBookmark },
            { "book-check", BookCheck },
            { "book-copy", BookCopy },
            { "book-dashed", BookDashed },
            { "book-down", BookDown },
            { "book-headphones", BookHeadphones },
            { "book-heart", BookHeart },
            { "book-image", BookImage },
            { "book-key", BookKey },
            { "book-lock", BookLock },
            { "book-marked", BookMarked },
            { "book-minus", BookMinus },
            { "book-open", BookOpen },
            { "book-open-check", BookOpenCheck },
            { "book-open-text", BookOpenText },
            { "book-plus", BookPlus },
            { "book-search", BookSearch },
            { "book-template", BookTemplate },
            { "book-text", BookText },
            { "book-type", BookType },
            { "book-up", BookUp },
            { "book-up-2", BookUp2 },
            { "book-user", BookUser },
            { "book-x", BookX },
            { "bookmark", Bookmark },
            { "bookmark-check", BookmarkCheck },
            { "bookmark-minus", BookmarkMinus },
            { "bookmark-off", BookmarkOff },
            { "bookmark-plus", BookmarkPlus },
            { "bookmark-x", BookmarkX },
            { "boom-box", BoomBox },
            { "bot", Bot },
            { "bot-message-square", BotMessageSquare },
            { "bot-off", BotOff },
            { "bottle-wine", BottleWine },
            { "bow-arrow", BowArrow },
            { "box", Box },
            { "box-select", BoxSelect },
            { "boxes", Boxes },
            { "boxes-linked", BoxesLinked },
            { "braces", Braces },
            { "brackets", Brackets },
            { "brain", Brain },
            { "brain-circuit", BrainCircuit },
            { "brain-cog", BrainCog },
            { "brain-off", BrainOff },
            { "brick-wall", BrickWall },
            { "brick-wall-fire", BrickWallFire },
            { "brick-wall-shield", BrickWallShield },
            { "bridge", Bridge },
            { "briefcase", Briefcase },
            { "briefcase-business", BriefcaseBusiness },
            { "briefcase-conveyor-belt", BriefcaseConveyorBelt },
            { "briefcase-medical", BriefcaseMedical },
            { "briefcase-plus", BriefcasePlus },
            { "bring-to-front", BringToFront },
            { "broccoli", Broccoli },
            { "broom", Broom },
            { "broom-sparkles", BroomSparkles },
            { "brush", Brush },
            { "brush-cleaning", BrushCleaning },
            { "bubbles", Bubbles },
            { "bug", Bug },
            { "bug-off", BugOff },
            { "bug-play", BugPlay },
            { "building", Building },
            { "building-2", Building2 },
            { "building-complex", BuildingComplex },
            { "building-complex-plus", BuildingComplexPlus },
            { "bus", Bus },
            { "bus-front", BusFront },
            { "cable", Cable },
            { "cable-car", CableCar },
            { "cake", Cake },
            { "cake-slice", CakeSlice },
            { "calculator", Calculator },
            { "calendar", Calendar },
            { "calendar-1", Calendar1 },
            { "calendar-arrow-down", CalendarArrowDown },
            { "calendar-arrow-left", CalendarArrowLeft },
            { "calendar-arrow-right", CalendarArrowRight },
            { "calendar-arrow-up", CalendarArrowUp },
            { "calendar-check", CalendarCheck },
            { "calendar-check-2", CalendarCheck2 },
            { "calendar-chevrons-left", CalendarChevronsLeft },
            { "calendar-chevrons-right", CalendarChevronsRight },
            { "calendar-clock", CalendarClock },
            { "calendar-cog", CalendarCog },
            { "calendar-days", CalendarDays },
            { "calendar-fold", CalendarFold },
            { "calendar-heart", CalendarHeart },
            { "calendar-minus", CalendarMinus },
            { "calendar-minus-2", CalendarMinus2 },
            { "calendar-off", CalendarOff },
            { "calendar-plus", CalendarPlus },
            { "calendar-plus-2", CalendarPlus2 },
            { "calendar-range", CalendarRange },
            { "calendar-search", CalendarSearch },
            { "calendar-sync", CalendarSync },
            { "calendar-x", CalendarX },
            { "calendar-x-2", CalendarX2 },
            { "calendars", Calendars },
            { "camera", Camera },
            { "camera-off", CameraOff },
            { "can", Can },
            { "can-soda", CanSoda },
            { "candlestick-chart", CandlestickChart },
            { "candy", Candy },
            { "candy-cane", CandyCane },
            { "candy-off", CandyOff },
            { "cannabis", Cannabis },
            { "cannabis-off", CannabisOff },
            { "captions", Captions },
            { "captions-off", CaptionsOff },
            { "car", Car },
            { "car-battery", CarBattery },
            { "car-front", CarFront },
            { "car-taxi-front", CarTaxiFront },
            { "caravan", Caravan },
            { "card-sim", CardSim },
            { "carrot", Carrot },
            { "carton", Carton },
            { "carton-off", CartonOff },
            { "case-lower", CaseLower },
            { "case-sensitive", CaseSensitive },
            { "case-upper", CaseUpper },
            { "cassette-tape", CassetteTape },
            { "cast", Cast },
            { "castle", Castle },
            { "cat", Cat },
            { "cctv", Cctv },
            { "cctv-off", CctvOff },
            { "certificate", Certificate },
            { "chart-area", ChartArea },
            { "chart-bar", ChartBar },
            { "chart-bar-big", ChartBarBig },
            { "chart-bar-decreasing", ChartBarDecreasing },
            { "chart-bar-increasing", ChartBarIncreasing },
            { "chart-bar-stacked", ChartBarStacked },
            { "chart-bell-curve", ChartBellCurve },
            { "chart-candlestick", ChartCandlestick },
            { "chart-column", ChartColumn },
            { "chart-column-big", ChartColumnBig },
            { "chart-column-decreasing", ChartColumnDecreasing },
            { "chart-column-increasing", ChartColumnIncreasing },
            { "chart-column-stacked", ChartColumnStacked },
            { "chart-gantt", ChartGantt },
            { "chart-line", ChartLine },
            { "chart-network", ChartNetwork },
            { "chart-no-axes-column", ChartNoAxesColumn },
            { "chart-no-axes-column-decreasing", ChartNoAxesColumnDecreasing },
            { "chart-no-axes-column-increasing", ChartNoAxesColumnIncreasing },
            { "chart-no-axes-combined", ChartNoAxesCombined },
            { "chart-no-axes-gantt", ChartNoAxesGantt },
            { "chart-pie", ChartPie },
            { "chart-quadrant", ChartQuadrant },
            { "chart-radar", ChartRadar },
            { "chart-scatter", ChartScatter },
            { "chart-spline", ChartSpline },
            { "check", Check },
            { "check-check", CheckCheck },
            { "check-circle", CheckCircle },
            { "check-circle-2", CheckCircle2 },
            { "check-line", CheckLine },
            { "check-square", CheckSquare },
            { "check-square-2", CheckSquare2 },
            { "chef-hat", ChefHat },
            { "cherry", Cherry },
            { "chess-bishop", ChessBishop },
            { "chess-king", ChessKing },
            { "chess-knight", ChessKnight },
            { "chess-pawn", ChessPawn },
            { "chess-queen", ChessQueen },
            { "chess-rook", ChessRook },
            { "chevron-down", ChevronDown },
            { "chevron-down-circle", ChevronDownCircle },
            { "chevron-down-square", ChevronDownSquare },
            { "chevron-first", ChevronFirst },
            { "chevron-last", ChevronLast },
            { "chevron-left", ChevronLeft },
            { "chevron-left-circle", ChevronLeftCircle },
            { "chevron-left-square", ChevronLeftSquare },
            { "chevron-right", ChevronRight },
            { "chevron-right-circle", ChevronRightCircle },
            { "chevron-right-square", ChevronRightSquare },
            { "chevron-up", ChevronUp },
            { "chevron-up-circle", ChevronUpCircle },
            { "chevron-up-square", ChevronUpSquare },
            { "chevrons-down", ChevronsDown },
            { "chevrons-down-up", ChevronsDownUp },
            { "chevrons-left", ChevronsLeft },
            { "chevrons-left-right", ChevronsLeftRight },
            { "chevrons-left-right-ellipsis", ChevronsLeftRightEllipsis },
            { "chevrons-right", ChevronsRight },
            { "chevrons-right-left", ChevronsRightLeft },
            { "chevrons-up", ChevronsUp },
            { "chevrons-up-down", ChevronsUpDown },
            { "church", Church },
            { "cigarette", Cigarette },
            { "cigarette-off", CigaretteOff },
            { "circle", Circle },
            { "circle-alert", CircleAlert },
            { "circle-arrow-down", CircleArrowDown },
            { "circle-arrow-left", CircleArrowLeft },
            { "circle-arrow-out-down-left", CircleArrowOutDownLeft },
            { "circle-arrow-out-down-right", CircleArrowOutDownRight },
            { "circle-arrow-out-up-left", CircleArrowOutUpLeft },
            { "circle-arrow-out-up-right", CircleArrowOutUpRight },
            { "circle-arrow-right", CircleArrowRight },
            { "circle-arrow-up", CircleArrowUp },
            { "circle-check", CircleCheck },
            { "circle-check-big", CircleCheckBig },
            { "circle-chevron-down", CircleChevronDown },
            { "circle-chevron-left", CircleChevronLeft },
            { "circle-chevron-right", CircleChevronRight },
            { "circle-chevron-up", CircleChevronUp },
            { "circle-chevrons-down", CircleChevronsDown },
            { "circle-chevrons-left", CircleChevronsLeft },
            { "circle-chevrons-right", CircleChevronsRight },
            { "circle-chevrons-up", CircleChevronsUp },
            { "circle-dashed", CircleDashed },
            { "circle-dashed-check", CircleDashedCheck },
            { "circle-divide", CircleDivide },
            { "circle-dollar-sign", CircleDollarSign },
            { "circle-dot", CircleDot },
            { "circle-dot-dashed", CircleDotDashed },
            { "circle-ellipsis", CircleEllipsis },
            { "circle-equal", CircleEqual },
            { "circle-euro", CircleEuro },
            { "circle-fading-arrow-up", CircleFadingArrowUp },
            { "circle-fading-play", CircleFadingPlay },
            { "circle-fading-plus", CircleFadingPlus },
            { "circle-gauge", CircleGauge },
            { "circle-help", CircleHelp },
            { "circle-letter-a", CircleLetterA },
            { "circle-letter-b", CircleLetterB },
            { "circle-letter-c", CircleLetterC },
            { "circle-letter-d", CircleLetterD },
            { "circle-letter-e", CircleLetterE },
            { "circle-letter-f", CircleLetterF },
            { "circle-letter-g", CircleLetterG },
            { "circle-letter-h", CircleLetterH },
            { "circle-letter-i", CircleLetterI },
            { "circle-letter-j", CircleLetterJ },
            { "circle-letter-k", CircleLetterK },
            { "circle-letter-l", CircleLetterL },
            { "circle-letter-m", CircleLetterM },
            { "circle-letter-n", CircleLetterN },
            { "circle-letter-o", CircleLetterO },
            { "circle-letter-p", CircleLetterP },
            { "circle-letter-p-off", CircleLetterPOff },
            { "circle-letter-q", CircleLetterQ },
            { "circle-letter-r", CircleLetterR },
            { "circle-letter-s", CircleLetterS },
            { "circle-letter-t", CircleLetterT },
            { "circle-letter-u", CircleLetterU },
            { "circle-letter-v", CircleLetterV },
            { "circle-letter-w", CircleLetterW },
            { "circle-letter-x", CircleLetterX },
            { "circle-letter-y", CircleLetterY },
            { "circle-letter-z", CircleLetterZ },
            { "circle-minus", CircleMinus },
            { "circle-off", CircleOff },
            { "circle-parking", CircleParking },
            { "circle-parking-off", CircleParkingOff },
            { "circle-pause", CirclePause },
            { "circle-percent", CirclePercent },
            { "circle-pile", CirclePile },
            { "circle-play", CirclePlay },
            { "circle-plus", CirclePlus },
            { "circle-pound-sterling", CirclePoundSterling },
            { "circle-power", CirclePower },
            { "circle-question-mark", CircleQuestionMark },
            { "circle-slash", CircleSlash },
            { "circle-slash-2", CircleSlash2 },
            { "circle-slashed", CircleSlashed },
            { "circle-small", CircleSmall },
            { "circle-star", CircleStar },
            { "circle-stop", CircleStop },
            { "circle-user", CircleUser },
            { "circle-user-round", CircleUserRound },
            { "circle-x", CircleX },
            { "circuit-board", CircuitBoard },
            { "citrus", Citrus },
            { "clapperboard", Clapperboard },
            { "clef-alto", ClefAlto },
            { "clef-bass", ClefBass },
            { "clef-treble", ClefTreble },
            { "clipboard", Clipboard },
            { "clipboard-check", ClipboardCheck },
            { "clipboard-clock", ClipboardClock },
            { "clipboard-copy", ClipboardCopy },
            { "clipboard-edit", ClipboardEdit },
            { "clipboard-list", ClipboardList },
            { "clipboard-minus", ClipboardMinus },
            { "clipboard-paste", ClipboardPaste },
            { "clipboard-pen", ClipboardPen },
            { "clipboard-pen-line", ClipboardPenLine },
            { "clipboard-plus", ClipboardPlus },
            { "clipboard-signature", ClipboardSignature },
            { "clipboard-type", ClipboardType },
            { "clipboard-x", ClipboardX },
            { "clock", Clock },
            { "clock-1", Clock1 },
            { "clock-10", Clock10 },
            { "clock-11", Clock11 },
            { "clock-12", Clock12 },
            { "clock-2", Clock2 },
            { "clock-3", Clock3 },
            { "clock-4", Clock4 },
            { "clock-5", Clock5 },
            { "clock-6", Clock6 },
            { "clock-7", Clock7 },
            { "clock-8", Clock8 },
            { "clock-9", Clock9 },
            { "clock-alert", ClockAlert },
            { "clock-arrow-down", ClockArrowDown },
            { "clock-arrow-left", ClockArrowLeft },
            { "clock-arrow-right", ClockArrowRight },
            { "clock-arrow-up", ClockArrowUp },
            { "clock-check", ClockCheck },
            { "clock-fading", ClockFading },
            { "clock-plus", ClockPlus },
            { "closed-caption", ClosedCaption },
            { "cloud", Cloud },
            { "cloud-alert", CloudAlert },
            { "cloud-backup", CloudBackup },
            { "cloud-check", CloudCheck },
            { "cloud-cog", CloudCog },
            { "cloud-download", CloudDownload },
            { "cloud-drizzle", CloudDrizzle },
            { "cloud-fog", CloudFog },
            { "cloud-hail", CloudHail },
            { "cloud-lightning", CloudLightning },
            { "cloud-moon", CloudMoon },
            { "cloud-moon-rain", CloudMoonRain },
            { "cloud-off", CloudOff },
            { "cloud-rain", CloudRain },
            { "cloud-rain-wind", CloudRainWind },
            { "cloud-snow", CloudSnow },
            { "cloud-sun", CloudSun },
            { "cloud-sun-rain", CloudSunRain },
            { "cloud-sync", CloudSync },
            { "cloud-upload", CloudUpload },
            { "cloudy", Cloudy },
            { "clover", Clover },
            { "club", Club },
            { "code", Code },
            { "code-2", Code2 },
            { "code-square", CodeSquare },
            { "code-xml", CodeXml },
            { "coffee", Coffee },
            { "cog", Cog },
            { "coins", Coins },
            { "columns", Columns },
            { "columns-2", Columns2 },
            { "columns-3", Columns3 },
            { "columns-3-cog", Columns3Cog },
            { "columns-4", Columns4 },
            { "columns-settings", ColumnsSettings },
            { "combine", Combine },
            { "command", Command },
            { "compass", Compass },
            { "component", Component },
            { "computer", Computer },
            { "concierge-bell", ConciergeBell },
            { "cone", Cone },
            { "construction", Construction },
            { "contact", Contact },
            { "contact-2", Contact2 },
            { "contact-round", ContactRound },
            { "container", Container },
            { "contrast", Contrast },
            { "cookie", Cookie },
            { "cooking-pot", CookingPot },
            { "copy", Copy },
            { "copy-check", CopyCheck },
            { "copy-minus", CopyMinus },
            { "copy-plus", CopyPlus },
            { "copy-slash", CopySlash },
            { "copy-x", CopyX },
            { "copyleft", Copyleft },
            { "copyright", Copyright },
            { "corner-down-left", CornerDownLeft },
            { "corner-down-right", CornerDownRight },
            { "corner-left-down", CornerLeftDown },
            { "corner-left-up", CornerLeftUp },
            { "corner-right-down", CornerRightDown },
            { "corner-right-up", CornerRightUp },
            { "corner-up-left", CornerUpLeft },
            { "corner-up-right", CornerUpRight },
            { "cpu", Cpu },
            { "cpu-ai", CpuAi },
            { "creative-commons", CreativeCommons },
            { "credit-card", CreditCard },
            { "credit-card-check", CreditCardCheck },
            { "credit-card-minus", CreditCardMinus },
            { "credit-card-plus", CreditCardPlus },
            { "credit-card-reader", CreditCardReader },
            { "credit-card-x", CreditCardX },
            { "croissant", Croissant },
            { "crop", Crop },
            { "cross", Cross },
            { "crosshair", Crosshair },
            { "crown", Crown },
            { "cuboid", Cuboid },
            { "cup-soda", CupSoda },
            { "cupcake", Cupcake },
            { "curly-braces", CurlyBraces },
            { "currency", Currency },
            { "cylinder", Cylinder },
            { "dam", Dam },
            { "database", Database },
            { "database-arrow-down", DatabaseArrowDown },
            { "database-arrow-up", DatabaseArrowUp },
            { "database-backup", DatabaseBackup },
            { "database-check", DatabaseCheck },
            { "database-cog", DatabaseCog },
            { "database-minus", DatabaseMinus },
            { "database-plus", DatabasePlus },
            { "database-search", DatabaseSearch },
            { "database-x", DatabaseX },
            { "database-zap", DatabaseZap },
            { "decimals-arrow-left", DecimalsArrowLeft },
            { "decimals-arrow-right", DecimalsArrowRight },
            { "delete", Delete },
            { "dessert", Dessert },
            { "diameter", Diameter },
            { "diamond", Diamond },
            { "diamond-minus", DiamondMinus },
            { "diamond-percent", DiamondPercent },
            { "diamond-plus", DiamondPlus },
            { "dice-1", Dice1 },
            { "dice-2", Dice2 },
            { "dice-3", Dice3 },
            { "dice-4", Dice4 },
            { "dice-5", Dice5 },
            { "dice-6", Dice6 },
            { "dices", Dices },
            { "diff", Diff },
            { "disc", Disc },
            { "disc-2", Disc2 },
            { "disc-3", Disc3 },
            { "disc-album", DiscAlbum },
            { "divide", Divide },
            { "divide-circle", DivideCircle },
            { "divide-square", DivideSquare },
            { "dna", Dna },
            { "dna-off", DnaOff },
            { "dock", Dock },
            { "dog", Dog },
            { "dollar-sign", DollarSign },
            { "dome", Dome },
            { "donut", Donut },
            { "door-closed", DoorClosed },
            { "door-closed-cog", DoorClosedCog },
            { "door-closed-locked", DoorClosedLocked },
            { "door-closed-package", DoorClosedPackage },
            { "door-open", DoorOpen },
            { "door-stairwell", DoorStairwell },
            { "dot", Dot },
            { "dot-square", DotSquare },
            { "download", Download },
            { "download-cloud", DownloadCloud },
            { "drafting-compass", DraftingCompass },
            { "drama", Drama },
            { "drill", Drill },
            { "drone", Drone },
            { "droplet", Droplet },
            { "droplet-off", DropletOff },
            { "droplets", Droplets },
            { "drum", Drum },
            { "drumstick", Drumstick },
            { "dumbbell", Dumbbell },
            { "ear", Ear },
            { "ear-off", EarOff },
            { "earbuds", Earbuds },
            { "earbuds-off", EarbudsOff },
            { "earth", Earth },
            { "earth-lock", EarthLock },
            { "eclipse", Eclipse },
            { "edit", Edit },
            { "edit-2", Edit2 },
            { "edit-3", Edit3 },
            { "egg", Egg },
            { "egg-fried", EggFried },
            { "egg-off", EggOff },
            { "eighth-note", EighthNote },
            { "eighth-notes", EighthNotes },
            { "eject", Eject },
            { "ellipse", Ellipse },
            { "ellipsis", Ellipsis },
            { "ellipsis-vertical", EllipsisVertical },
            { "emirati-dirham", EmiratiDirham },
            { "engine", Engine },
            { "equal", Equal },
            { "equal-approximately", EqualApproximately },
            { "equal-approximately-not", EqualApproximatelyNot },
            { "equal-not", EqualNot },
            { "equal-square", EqualSquare },
            { "eraser", Eraser },
            { "ethernet-port", EthernetPort },
            { "euro", Euro },
            { "ev-charger", EvCharger },
            { "expand", Expand },
            { "external-link", ExternalLink },
            { "eye", Eye },
            { "eye-closed", EyeClosed },
            { "eye-dashed", EyeDashed },
            { "eye-off", EyeOff },
            { "face-angry", FaceAngry },
            { "face-expressionless", FaceExpressionless },
            { "face-grinning", FaceGrinning },
            { "face-neutral", FaceNeutral },
            { "face-slightly-frowning", FaceSlightlyFrowning },
            { "face-slightly-smiling", FaceSlightlySmiling },
            { "face-slightly-smiling-plus", FaceSlightlySmilingPlus },
            { "factory", Factory },
            { "fan", Fan },
            { "fast-forward", FastForward },
            { "faucet", Faucet },
            { "feather", Feather },
            { "fence", Fence },
            { "ferris-wheel", FerrisWheel },
            { "file", File },
            { "file-archive", FileArchive },
            { "file-audio", FileAudio },
            { "file-audio-2", FileAudio2 },
            { "file-axis-3-d", FileAxis3D },
            { "file-axis-3d", FileAxis3d },
            { "file-badge", FileBadge },
            { "file-badge-2", FileBadge2 },
            { "file-bar-chart", FileBarChart },
            { "file-bar-chart-2", FileBarChart2 },
            { "file-box", FileBox },
            { "file-braces", FileBraces },
            { "file-braces-corner", FileBracesCorner },
            { "file-chart-column", FileChartColumn },
            { "file-chart-column-increasing", FileChartColumnIncreasing },
            { "file-chart-line", FileChartLine },
            { "file-chart-pie", FileChartPie },
            { "file-check", FileCheck },
            { "file-check-2", FileCheck2 },
            { "file-check-corner", FileCheckCorner },
            { "file-clock", FileClock },
            { "file-code", FileCode },
            { "file-code-2", FileCode2 },
            { "file-code-corner", FileCodeCorner },
            { "file-cog", FileCog },
            { "file-cog-2", FileCog2 },
            { "file-database", FileDatabase },
            { "file-diff", FileDiff },
            { "file-digit", FileDigit },
            { "file-down", FileDown },
            { "file-edit", FileEdit },
            { "file-exclamation-point", FileExclamationPoint },
            { "file-headphone", FileHeadphone },
            { "file-heart", FileHeart },
            { "file-image", FileImage },
            { "file-input", FileInput },
            { "file-json", FileJson },
            { "file-json-2", FileJson2 },
            { "file-key", FileKey },
            { "file-key-2", FileKey2 },
            { "file-line-chart", FileLineChart },
            { "file-lock", FileLock },
            { "file-lock-2", FileLock2 },
            { "file-minus", FileMinus },
            { "file-minus-2", FileMinus2 },
            { "file-minus-corner", FileMinusCorner },
            { "file-music", FileMusic },
            { "file-output", FileOutput },
            { "file-pen", FilePen },
            { "file-pen-line", FilePenLine },
            { "file-pie-chart", FilePieChart },
            { "file-play", FilePlay },
            { "file-plus", FilePlus },
            { "file-plus-2", FilePlus2 },
            { "file-plus-corner", FilePlusCorner },
            { "file-question", FileQuestion },
            { "file-question-mark", FileQuestionMark },
            { "file-scan", FileScan },
            { "file-search", FileSearch },
            { "file-search-2", FileSearch2 },
            { "file-search-corner", FileSearchCorner },
            { "file-signal", FileSignal },
            { "file-signature", FileSignature },
            { "file-sliders", FileSliders },
            { "file-spreadsheet", FileSpreadsheet },
            { "file-stack", FileStack },
            { "file-symlink", FileSymlink },
            { "file-terminal", FileTerminal },
            { "file-text", FileText },
            { "file-type", FileType },
            { "file-type-2", FileType2 },
            { "file-type-corner", FileTypeCorner },
            { "file-up", FileUp },
            { "file-user", FileUser },
            { "file-video", FileVideo },
            { "file-video-2", FileVideo2 },
            { "file-video-camera", FileVideoCamera },
            { "file-volume", FileVolume },
            { "file-volume-2", FileVolume2 },
            { "file-warning", FileWarning },
            { "file-x", FileX },
            { "file-x-2", FileX2 },
            { "file-x-corner", FileXCorner },
            { "files", Files },
            { "film", Film },
            { "filter", Filter },
            { "filter-x", FilterX },
            { "fingerprint", Fingerprint },
            { "fingerprint-pattern", FingerprintPattern },
            { "fire-extinguisher", FireExtinguisher },
            { "fish", Fish },
            { "fish-off", FishOff },
            { "fish-symbol", FishSymbol },
            { "fishing-hook", FishingHook },
            { "fishing-rod", FishingRod },
            { "flag", Flag },
            { "flag-off", FlagOff },
            { "flag-triangle-left", FlagTriangleLeft },
            { "flag-triangle-right", FlagTriangleRight },
            { "flame", Flame },
            { "flame-kindling", FlameKindling },
            { "flashlight", Flashlight },
            { "flashlight-off", FlashlightOff },
            { "flask-conical", FlaskConical },
            { "flask-conical-off", FlaskConicalOff },
            { "flask-round", FlaskRound },
            { "flip-horizontal", FlipHorizontal },
            { "flip-horizontal-2", FlipHorizontal2 },
            { "flip-vertical", FlipVertical },
            { "flip-vertical-2", FlipVertical2 },
            { "flower", Flower },
            { "flower-2", Flower2 },
            { "focus", Focus },
            { "fold-horizontal", FoldHorizontal },
            { "fold-vertical", FoldVertical },
            { "folder", Folder },
            { "folder-archive", FolderArchive },
            { "folder-bookmark", FolderBookmark },
            { "folder-check", FolderCheck },
            { "folder-clock", FolderClock },
            { "folder-closed", FolderClosed },
            { "folder-code", FolderCode },
            { "folder-cog", FolderCog },
            { "folder-cog-2", FolderCog2 },
            { "folder-dot", FolderDot },
            { "folder-down", FolderDown },
            { "folder-edit", FolderEdit },
            { "folder-git", FolderGit },
            { "folder-git-2", FolderGit2 },
            { "folder-heart", FolderHeart },
            { "folder-input", FolderInput },
            { "folder-kanban", FolderKanban },
            { "folder-key", FolderKey },
            { "folder-lock", FolderLock },
            { "folder-minus", FolderMinus },
            { "folder-open", FolderOpen },
            { "folder-open-dot", FolderOpenDot },
            { "folder-output", FolderOutput },
            { "folder-pen", FolderPen },
            { "folder-plus", FolderPlus },
            { "folder-root", FolderRoot },
            { "folder-search", FolderSearch },
            { "folder-search-2", FolderSearch2 },
            { "folder-symlink", FolderSymlink },
            { "folder-sync", FolderSync },
            { "folder-tree", FolderTree },
            { "folder-up", FolderUp },
            { "folder-user", FolderUser },
            { "folder-users", FolderUsers },
            { "folder-x", FolderX },
            { "folders", Folders },
            { "footprints", Footprints },
            { "fork-knife", ForkKnife },
            { "fork-knife-crossed", ForkKnifeCrossed },
            { "forklift", Forklift },
            { "form", Form },
            { "form-input", FormInput },
            { "forward", Forward },
            { "frame", Frame },
            { "frown", Frown },
            { "fuel", Fuel },
            { "fullscreen", Fullscreen },
            { "function-square", FunctionSquare },
            { "funnel", Funnel },
            { "funnel-plus", FunnelPlus },
            { "funnel-x", FunnelX },
            { "galaxy", Galaxy },
            { "gallery-horizontal", GalleryHorizontal },
            { "gallery-horizontal-end", GalleryHorizontalEnd },
            { "gallery-thumbnails", GalleryThumbnails },
            { "gallery-vertical", GalleryVertical },
            { "gallery-vertical-end", GalleryVerticalEnd },
            { "gamepad", Gamepad },
            { "gamepad-2", Gamepad2 },
            { "gamepad-directional", GamepadDirectional },
            { "gantt-chart", GanttChart },
            { "gantt-chart-square", GanttChartSquare },
            { "gap-horizontal", GapHorizontal },
            { "gap-vertical", GapVertical },
            { "gauge", Gauge },
            { "gauge-circle", GaugeCircle },
            { "gavel", Gavel },
            { "gem", Gem },
            { "georgian-lari", GeorgianLari },
            { "germ", Germ },
            { "germ-off", GermOff },
            { "ghost", Ghost },
            { "gift", Gift },
            { "git-branch", GitBranch },
            { "git-branch-minus", GitBranchMinus },
            { "git-branch-plus", GitBranchPlus },
            { "git-commit", GitCommit },
            { "git-commit-horizontal", GitCommitHorizontal },
            { "git-commit-vertical", GitCommitVertical },
            { "git-compare", GitCompare },
            { "git-compare-arrows", GitCompareArrows },
            { "git-fork", GitFork },
            { "git-graph", GitGraph },
            { "git-merge", GitMerge },
            { "git-merge-conflict", GitMergeConflict },
            { "git-pull-request", GitPullRequest },
            { "git-pull-request-arrow", GitPullRequestArrow },
            { "git-pull-request-closed", GitPullRequestClosed },
            { "git-pull-request-create", GitPullRequestCreate },
            { "git-pull-request-create-arrow", GitPullRequestCreateArrow },
            { "git-pull-request-draft", GitPullRequestDraft },
            { "glass-water", GlassWater },
            { "glasses", Glasses },
            { "globe", Globe },
            { "globe-2", Globe2 },
            { "globe-check", GlobeCheck },
            { "globe-code", GlobeCode },
            { "globe-lock", GlobeLock },
            { "globe-off", GlobeOff },
            { "globe-x", GlobeX },
            { "goal", Goal },
            { "gpu", Gpu },
            { "grab", Grab },
            { "graduation-cap", GraduationCap },
            { "grape", Grape },
            { "graph-hub", GraphHub },
            { "graph-star", GraphStar },
            { "grid", Grid },
            { "grid-2-x-2", Grid2X2 },
            { "grid-2-x-2-check", Grid2X2Check },
            { "grid-2-x-2-plus", Grid2X2Plus },
            { "grid-2-x-2-x", Grid2X2X },
            { "grid-2x2", Grid2x2 },
            { "grid-2x2-check", Grid2x2Check },
            { "grid-2x2-plus", Grid2x2Plus },
            { "grid-2x2-x", Grid2x2X },
            { "grid-3-x-3", Grid3X3 },
            { "grid-3x2", Grid3x2 },
            { "grid-3x3", Grid3x3 },
            { "grip", Grip },
            { "grip-horizontal", GripHorizontal },
            { "grip-vertical", GripVertical },
            { "groceries", Groceries },
            { "group", Group },
            { "guitar", Guitar },
            { "guitar-electric", GuitarElectric },
            { "half-note", HalfNote },
            { "ham", Ham },
            { "hamburger", Hamburger },
            { "hammer", Hammer },
            { "hand", Hand },
            { "hand-coins", HandCoins },
            { "hand-fist", HandFist },
            { "hand-grab", HandGrab },
            { "hand-heart", HandHeart },
            { "hand-helping", HandHelping },
            { "hand-metal", HandMetal },
            { "hand-platter", HandPlatter },
            { "handbag", Handbag },
            { "handle-bottom-right", HandleBottomRight },
            { "handshake", Handshake },
            { "hard-drive", HardDrive },
            { "hard-drive-download", HardDriveDownload },
            { "hard-drive-upload", HardDriveUpload },
            { "hard-hat", HardHat },
            { "hash", Hash },
            { "hat-glasses", HatGlasses },
            { "haze", Haze },
            { "hd", Hd },
            { "hdmi-port", HdmiPort },
            { "heading", Heading },
            { "heading-1", Heading1 },
            { "heading-2", Heading2 },
            { "heading-3", Heading3 },
            { "heading-4", Heading4 },
            { "heading-5", Heading5 },
            { "heading-6", Heading6 },
            { "headphone-off", HeadphoneOff },
            { "headphones", Headphones },
            { "headset", Headset },
            { "heart", Heart },
            { "heart-crack", HeartCrack },
            { "heart-handshake", HeartHandshake },
            { "heart-minus", HeartMinus },
            { "heart-off", HeartOff },
            { "heart-plus", HeartPlus },
            { "heart-pulse", HeartPulse },
            { "heart-x", HeartX },
            { "heater", Heater },
            { "helicopter", Helicopter },
            { "help-circle", HelpCircle },
            { "helping-hand", HelpingHand },
            { "hexagon", Hexagon },
            { "highlighter", Highlighter },
            { "hiking-stick", HikingStick },
            { "history", History },
            { "hockey-stick", HockeyStick },
            { "home", Home },
            { "hop", Hop },
            { "hop-off", HopOff },
            { "hospital", Hospital },
            { "hot-air-balloon", HotAirBalloon },
            { "hotel", Hotel },
            { "hourglass", Hourglass },
            { "hourglass-cog", HourglassCog },
            { "house", House },
            { "house-cog", HouseCog },
            { "house-heart", HouseHeart },
            { "house-plug", HousePlug },
            { "house-plus", HousePlus },
            { "house-wifi", HouseWifi },
            { "houses", Houses },
            { "ice-cream", IceCream },
            { "ice-cream-2", IceCream2 },
            { "ice-cream-bowl", IceCreamBowl },
            { "ice-cream-cone", IceCreamCone },
            { "id-card", IdCard },
            { "id-card-lanyard", IdCardLanyard },
            { "image", Image },
            { "image-down", ImageDown },
            { "image-minus", ImageMinus },
            { "image-off", ImageOff },
            { "image-play", ImagePlay },
            { "image-plus", ImagePlus },
            { "image-up", ImageUp },
            { "image-upscale", ImageUpscale },
            { "images", Images },
            { "import", Import },
            { "inbox", Inbox },
            { "indent", Indent },
            { "indent-decrease", IndentDecrease },
            { "indent-increase", IndentIncrease },
            { "indian-rupee", IndianRupee },
            { "infinity", Infinity },
            { "info", Info },
            { "inspect", Inspect },
            { "inspection-panel", InspectionPanel },
            { "italic", Italic },
            { "iteration-ccw", IterationCcw },
            { "iteration-cw", IterationCw },
            { "iv-bag", IvBag },
            { "japanese-yen", JapaneseYen },
            { "joystick", Joystick },
            { "kanban", Kanban },
            { "kanban-square", KanbanSquare },
            { "kanban-square-dashed", KanbanSquareDashed },
            { "kayak", Kayak },
            { "kazakh-tenge", KazakhTenge },
            { "key", Key },
            { "key-round", KeyRound },
            { "key-square", KeySquare },
            { "keyboard", Keyboard },
            { "keyboard-music", KeyboardMusic },
            { "keyboard-off", KeyboardOff },
            { "lambda", Lambda },
            { "lamp", Lamp },
            { "lamp-ceiling", LampCeiling },
            { "lamp-desk", LampDesk },
            { "lamp-floor", LampFloor },
            { "lamp-wall-down", LampWallDown },
            { "lamp-wall-up", LampWallUp },
            { "land-plot", LandPlot },
            { "landmark", Landmark },
            { "languages", Languages },
            { "laptop", Laptop },
            { "laptop-2", Laptop2 },
            { "laptop-minimal", LaptopMinimal },
            { "laptop-minimal-check", LaptopMinimalCheck },
            { "laser", Laser },
            { "lasso", Lasso },
            { "lasso-select", LassoSelect },
            { "laugh", Laugh },
            { "layer-arrow-down", LayerArrowDown },
            { "layer-arrow-up", LayerArrowUp },
            { "layers", Layers },
            { "layers-2", Layers2 },
            { "layers-3", Layers3 },
            { "layers-arrow-down", LayersArrowDown },
            { "layers-arrow-up", LayersArrowUp },
            { "layers-minus", LayersMinus },
            { "layers-plus", LayersPlus },
            { "layout", Layout },
            { "layout-arrow-down", LayoutArrowDown },
            { "layout-arrow-right", LayoutArrowRight },
            { "layout-dashboard", LayoutDashboard },
            { "layout-freeform", LayoutFreeform },
            { "layout-grid", LayoutGrid },
            { "layout-grid-circles", LayoutGridCircles },
            { "layout-list", LayoutList },
            { "layout-panel-left", LayoutPanelLeft },
            { "layout-panel-top", LayoutPanelTop },
            { "layout-template", LayoutTemplate },
            { "leaf", Leaf },
            { "leafy-green", LeafyGreen },
            { "lectern", Lectern },
            { "lens", Lens },
            { "lens-concave", LensConcave },
            { "lens-convex", LensConvex },
            { "letter-a", LetterA },
            { "letter-b", LetterB },
            { "letter-c", LetterC },
            { "letter-d", LetterD },
            { "letter-e", LetterE },
            { "letter-f", LetterF },
            { "letter-g", LetterG },
            { "letter-h", LetterH },
            { "letter-i", LetterI },
            { "letter-j", LetterJ },
            { "letter-k", LetterK },
            { "letter-l", LetterL },
            { "letter-m", LetterM },
            { "letter-n", LetterN },
            { "letter-o", LetterO },
            { "letter-p", LetterP },
            { "letter-q", LetterQ },
            { "letter-r", LetterR },
            { "letter-s", LetterS },
            { "letter-spacing", LetterSpacing },
            { "letter-t", LetterT },
            { "letter-text", LetterText },
            { "letter-u", LetterU },
            { "letter-v", LetterV },
            { "letter-w", LetterW },
            { "letter-x", LetterX },
            { "letter-y", LetterY },
            { "letter-z", LetterZ },
            { "letters", Letters },
            { "library", Library },
            { "library-big", LibraryBig },
            { "library-square", LibrarySquare },
            { "life-buoy", LifeBuoy },
            { "ligature", Ligature },
            { "lightbulb", Lightbulb },
            { "lightbulb-off", LightbulbOff },
            { "lighthouse", Lighthouse },
            { "line-chart", LineChart },
            { "line-dot-bottom-vertical", LineDotBottomVertical },
            { "line-dot-left-horizontal", LineDotLeftHorizontal },
            { "line-dot-right-horizontal", LineDotRightHorizontal },
            { "line-dot-top-vertical", LineDotTopVertical },
            { "line-height", LineHeight },
            { "line-squiggle", LineSquiggle },
            { "line-style", LineStyle },
            { "link", Link },
            { "link-2", Link2 },
            { "link-2-off", Link2Off },
            { "list", List },
            { "list-check", ListCheck },
            { "list-checks", ListChecks },
            { "list-chevrons-down-up", ListChevronsDownUp },
            { "list-chevrons-up-down", ListChevronsUpDown },
            { "list-clock", ListClock },
            { "list-collapse", ListCollapse },
            { "list-end", ListEnd },
            { "list-filter", ListFilter },
            { "list-filter-plus", ListFilterPlus },
            { "list-indent-decrease", ListIndentDecrease },
            { "list-indent-increase", ListIndentIncrease },
            { "list-minus", ListMinus },
            { "list-music", ListMusic },
            { "list-ordered", ListOrdered },
            { "list-plus", ListPlus },
            { "list-restart", ListRestart },
            { "list-sort-ascending", ListSortAscending },
            { "list-sort-descending", ListSortDescending },
            { "list-start", ListStart },
            { "list-todo", ListTodo },
            { "list-tree", ListTree },
            { "list-video", ListVideo },
            { "list-x", ListX },
            { "loader", Loader },
            { "loader-2", Loader2 },
            { "loader-circle", LoaderCircle },
            { "loader-pinwheel", LoaderPinwheel },
            { "locate", Locate },
            { "locate-fixed", LocateFixed },
            { "locate-off", LocateOff },
            { "location-edit", LocationEdit },
            { "lock", Lock },
            { "lock-keyhole", LockKeyhole },
            { "lock-keyhole-open", LockKeyholeOpen },
            { "lock-open", LockOpen },
            { "log-in", LogIn },
            { "log-out", LogOut },
            { "logs", Logs },
            { "lollipop", Lollipop },
            { "luggage", Luggage },
            { "m-square", MSquare },
            { "magnet", Magnet },
            { "mail", Mail },
            { "mail-badge", MailBadge },
            { "mail-check", MailCheck },
            { "mail-clock", MailClock },
            { "mail-dot", MailDot },
            { "mail-minus", MailMinus },
            { "mail-open", MailOpen },
            { "mail-pen", MailPen },
            { "mail-plus", MailPlus },
            { "mail-question", MailQuestion },
            { "mail-question-mark", MailQuestionMark },
            { "mail-search", MailSearch },
            { "mail-warning", MailWarning },
            { "mail-x", MailX },
            { "mailbox", Mailbox },
            { "mails", Mails },
            { "map", Map },
            { "map-minus", MapMinus },
            { "map-pin", MapPin },
            { "map-pin-check", MapPinCheck },
            { "map-pin-check-inside", MapPinCheckInside },
            { "map-pin-house", MapPinHouse },
            { "map-pin-minus", MapPinMinus },
            { "map-pin-minus-inside", MapPinMinusInside },
            { "map-pin-off", MapPinOff },
            { "map-pin-pen", MapPinPen },
            { "map-pin-plus", MapPinPlus },
            { "map-pin-plus-inside", MapPinPlusInside },
            { "map-pin-search", MapPinSearch },
            { "map-pin-x", MapPinX },
            { "map-pin-x-inside", MapPinXInside },
            { "map-pinned", MapPinned },
            { "map-plus", MapPlus },
            { "margin", Margin },
            { "margin-bottom", MarginBottom },
            { "margin-horizontal", MarginHorizontal },
            { "margin-left", MarginLeft },
            { "margin-right", MarginRight },
            { "margin-top", MarginTop },
            { "margin-vertical", MarginVertical },
            { "mars", Mars },
            { "mars-stroke", MarsStroke },
            { "martini", Martini },
            { "maximize", Maximize },
            { "maximize-2", Maximize2 },
            { "medal", Medal },
            { "megaphone", Megaphone },
            { "megaphone-off", MegaphoneOff },
            { "meh", Meh },
            { "memory-stick", MemoryStick },
            { "menu", Menu },
            { "menu-square", MenuSquare },
            { "merge", Merge },
            { "message-circle", MessageCircle },
            { "message-circle-check", MessageCircleCheck },
            { "message-circle-code", MessageCircleCode },
            { "message-circle-dashed", MessageCircleDashed },
            { "message-circle-dashed-check", MessageCircleDashedCheck },
            { "message-circle-heart", MessageCircleHeart },
            { "message-circle-more", MessageCircleMore },
            { "message-circle-off", MessageCircleOff },
            { "message-circle-plus", MessageCirclePlus },
            { "message-circle-question", MessageCircleQuestion },
            { "message-circle-question-mark", MessageCircleQuestionMark },
            { "message-circle-reply", MessageCircleReply },
            { "message-circle-text", MessageCircleText },
            { "message-circle-warning", MessageCircleWarning },
            { "message-circle-x", MessageCircleX },
            { "message-square", MessageSquare },
            { "message-square-check", MessageSquareCheck },
            { "message-square-code", MessageSquareCode },
            { "message-square-dashed", MessageSquareDashed },
            { "message-square-diff", MessageSquareDiff },
            { "message-square-dot", MessageSquareDot },
            { "message-square-heart", MessageSquareHeart },
            { "message-square-lock", MessageSquareLock },
            { "message-square-more", MessageSquareMore },
            { "message-square-off", MessageSquareOff },
            { "message-square-plus", MessageSquarePlus },
            { "message-square-quote", MessageSquareQuote },
            { "message-square-reply", MessageSquareReply },
            { "message-square-share", MessageSquareShare },
            { "message-square-text", MessageSquareText },
            { "message-square-warning", MessageSquareWarning },
            { "message-square-x", MessageSquareX },
            { "messages-circle", MessagesCircle },
            { "messages-square", MessagesSquare },
            { "metronome", Metronome },
            { "mic", Mic },
            { "mic-2", Mic2 },
            { "mic-audio-lines", MicAudioLines },
            { "mic-off", MicOff },
            { "mic-signal", MicSignal },
            { "mic-vocal", MicVocal },
            { "microchip", Microchip },
            { "microscope", Microscope },
            { "microwave", Microwave },
            { "midi-port", MidiPort },
            { "milestone", Milestone },
            { "milk", Milk },
            { "milk-off", MilkOff },
            { "minimize", Minimize },
            { "minimize-2", Minimize2 },
            { "minus", Minus },
            { "minus-circle", MinusCircle },
            { "minus-square", MinusSquare },
            { "mirror-rectangular", MirrorRectangular },
            { "mirror-round", MirrorRound },
            { "monitor", Monitor },
            { "monitor-activity", MonitorActivity },
            { "monitor-check", MonitorCheck },
            { "monitor-cloud", MonitorCloud },
            { "monitor-cog", MonitorCog },
            { "monitor-dot", MonitorDot },
            { "monitor-down", MonitorDown },
            { "monitor-off", MonitorOff },
            { "monitor-pause", MonitorPause },
            { "monitor-pc", MonitorPc },
            { "monitor-play", MonitorPlay },
            { "monitor-smartphone", MonitorSmartphone },
            { "monitor-speaker", MonitorSpeaker },
            { "monitor-stop", MonitorStop },
            { "monitor-up", MonitorUp },
            { "monitor-x", MonitorX },
            { "moon", Moon },
            { "moon-star", MoonStar },
            { "mop", Mop },
            { "mop-sparkles", MopSparkles },
            { "more-horizontal", MoreHorizontal },
            { "more-vertical", MoreVertical },
            { "mosque", Mosque },
            { "motorbike", Motorbike },
            { "mountain", Mountain },
            { "mountain-snow", MountainSnow },
            { "mouse", Mouse },
            { "mouse-left", MouseLeft },
            { "mouse-off", MouseOff },
            { "mouse-pointer", MousePointer },
            { "mouse-pointer-2", MousePointer2 },
            { "mouse-pointer-2-off", MousePointer2Off },
            { "mouse-pointer-ban", MousePointerBan },
            { "mouse-pointer-click", MousePointerClick },
            { "mouse-pointer-square-dashed", MousePointerSquareDashed },
            { "mouse-right", MouseRight },
            { "mouth", Mouth },
            { "mouth-off", MouthOff },
            { "move", Move },
            { "move-3-d", Move3D },
            { "move-3d", Move3d },
            { "move-diagonal", MoveDiagonal },
            { "move-diagonal-2", MoveDiagonal2 },
            { "move-down", MoveDown },
            { "move-down-left", MoveDownLeft },
            { "move-down-right", MoveDownRight },
            { "move-horizontal", MoveHorizontal },
            { "move-left", MoveLeft },
            { "move-right", MoveRight },
            { "move-up", MoveUp },
            { "move-up-left", MoveUpLeft },
            { "move-up-right", MoveUpRight },
            { "move-vertical", MoveVertical },
            { "music", Music },
            { "music-2", Music2 },
            { "music-3", Music3 },
            { "music-4", Music4 },
            { "navigation", Navigation },
            { "navigation-2", Navigation2 },
            { "navigation-2-off", Navigation2Off },
            { "navigation-off", NavigationOff },
            { "nepali-rupee", NepaliRupee },
            { "network", Network },
            { "newspaper", Newspaper },
            { "nfc", Nfc },
            { "non-binary", NonBinary },
            { "notebook", Notebook },
            { "notebook-dot", NotebookDot },
            { "notebook-pen", NotebookPen },
            { "notebook-tabs", NotebookTabs },
            { "notebook-text", NotebookText },
            { "notepad-text", NotepadText },
            { "notepad-text-dashed", NotepadTextDashed },
            { "numbers", Numbers },
            { "nut", Nut },
            { "nut-off", NutOff },
            { "octagon", Octagon },
            { "octagon-alert", OctagonAlert },
            { "octagon-minus", OctagonMinus },
            { "octagon-pause", OctagonPause },
            { "octagon-x", OctagonX },
            { "omega", Omega },
            { "option", Option },
            { "orbit", Orbit },
            { "origami", Origami },
            { "outdent", Outdent },
            { "package", Package },
            { "package-2", Package2 },
            { "package-check", PackageCheck },
            { "package-minus", PackageMinus },
            { "package-open", PackageOpen },
            { "package-plus", PackagePlus },
            { "package-search", PackageSearch },
            { "package-x", PackageX },
            { "paint-bucket", PaintBucket },
            { "paint-roller", PaintRoller },
            { "paintbrush", Paintbrush },
            { "paintbrush-2", Paintbrush2 },
            { "paintbrush-vertical", PaintbrushVertical },
            { "palette", Palette },
            { "palmtree", Palmtree },
            { "panda", Panda },
            { "panel-bottom", PanelBottom },
            { "panel-bottom-close", PanelBottomClose },
            { "panel-bottom-dashed", PanelBottomDashed },
            { "panel-bottom-inactive", PanelBottomInactive },
            { "panel-bottom-open", PanelBottomOpen },
            { "panel-left", PanelLeft },
            { "panel-left-close", PanelLeftClose },
            { "panel-left-dashed", PanelLeftDashed },
            { "panel-left-inactive", PanelLeftInactive },
            { "panel-left-open", PanelLeftOpen },
            { "panel-left-right-dashed", PanelLeftRightDashed },
            { "panel-right", PanelRight },
            { "panel-right-close", PanelRightClose },
            { "panel-right-dashed", PanelRightDashed },
            { "panel-right-inactive", PanelRightInactive },
            { "panel-right-open", PanelRightOpen },
            { "panel-top", PanelTop },
            { "panel-top-bottom-dashed", PanelTopBottomDashed },
            { "panel-top-close", PanelTopClose },
            { "panel-top-dashed", PanelTopDashed },
            { "panel-top-inactive", PanelTopInactive },
            { "panel-top-open", PanelTopOpen },
            { "panels-left-bottom", PanelsLeftBottom },
            { "panels-left-right", PanelsLeftRight },
            { "panels-right-bottom", PanelsRightBottom },
            { "panels-top-bottom", PanelsTopBottom },
            { "panels-top-left", PanelsTopLeft },
            { "paper-bag", PaperBag },
            { "paperclip", Paperclip },
            { "parasol", Parasol },
            { "parentheses", Parentheses },
            { "park", Park },
            { "parking-circle", ParkingCircle },
            { "parking-circle-off", ParkingCircleOff },
            { "parking-meter", ParkingMeter },
            { "parking-square", ParkingSquare },
            { "parking-square-off", ParkingSquareOff },
            { "party-popper", PartyPopper },
            { "pause", Pause },
            { "pause-circle", PauseCircle },
            { "pause-octagon", PauseOctagon },
            { "paw-print", PawPrint },
            { "pc-case", PcCase },
            { "pen", Pen },
            { "pen-box", PenBox },
            { "pen-line", PenLine },
            { "pen-off", PenOff },
            { "pen-square", PenSquare },
            { "pen-tool", PenTool },
            { "pencil", Pencil },
            { "pencil-line", PencilLine },
            { "pencil-off", PencilOff },
            { "pencil-ruler", PencilRuler },
            { "pencil-sparkles", PencilSparkles },
            { "pentagon", Pentagon },
            { "percent", Percent },
            { "percent-circle", PercentCircle },
            { "percent-diamond", PercentDiamond },
            { "percent-square", PercentSquare },
            { "perfume-bottle", PerfumeBottle },
            { "person-standing", PersonStanding },
            { "phi", Phi },
            { "philippine-peso", PhilippinePeso },
            { "phone", Phone },
            { "phone-call", PhoneCall },
            { "phone-forwarded", PhoneForwarded },
            { "phone-incoming", PhoneIncoming },
            { "phone-log", PhoneLog },
            { "phone-missed", PhoneMissed },
            { "phone-off", PhoneOff },
            { "phone-outgoing", PhoneOutgoing },
            { "pi", Pi },
            { "pi-square", PiSquare },
            { "piano", Piano },
            { "pickaxe", Pickaxe },
            { "picture-in-picture", PictureInPicture },
            { "picture-in-picture-2", PictureInPicture2 },
            { "pie-chart", PieChart },
            { "piggy-bank", PiggyBank },
            { "pilcrow", Pilcrow },
            { "pilcrow-left", PilcrowLeft },
            { "pilcrow-right", PilcrowRight },
            { "pilcrow-square", PilcrowSquare },
            { "pill", Pill },
            { "pill-bottle", PillBottle },
            { "pin", Pin },
            { "pin-off", PinOff },
            { "pipette", Pipette },
            { "pizza", Pizza },
            { "plane", Plane },
            { "plane-landing", PlaneLanding },
            { "plane-takeoff", PlaneTakeoff },
            { "plant-pot", PlantPot },
            { "play", Play },
            { "play-circle", PlayCircle },
            { "play-off", PlayOff },
            { "play-square", PlaySquare },
            { "playing-card", PlayingCard },
            { "playing-cards", PlayingCards },
            { "playing-cards-fan", PlayingCardsFan },
            { "plug", Plug },
            { "plug-2", Plug2 },
            { "plug-zap", PlugZap },
            { "plug-zap-2", PlugZap2 },
            { "plus", Plus },
            { "plus-circle", PlusCircle },
            { "plus-square", PlusSquare },
            { "pocket-knife", PocketKnife },
            { "podcast", Podcast },
            { "podium", Podium },
            { "pointer", Pointer },
            { "pointer-off", PointerOff },
            { "popcorn", Popcorn },
            { "popsicle", Popsicle },
            { "pound-sterling", PoundSterling },
            { "power", Power },
            { "power-circle", PowerCircle },
            { "power-off", PowerOff },
            { "power-square", PowerSquare },
            { "presentation", Presentation },
            { "printer", Printer },
            { "printer-3d", Printer3d },
            { "printer-check", PrinterCheck },
            { "printer-x", PrinterX },
            { "projector", Projector },
            { "proportions", Proportions },
            { "puzzle", Puzzle },
            { "pyramid", Pyramid },
            { "qr-code", QrCode },
            { "quote", Quote },
            { "rabbit", Rabbit },
            { "radar", Radar },
            { "radiation", Radiation },
            { "radical", Radical },
            { "radio", Radio },
            { "radio-off", RadioOff },
            { "radio-receiver", RadioReceiver },
            { "radio-tower", RadioTower },
            { "radius", Radius },
            { "rainbow", Rainbow },
            { "rat", Rat },
            { "ratio", Ratio },
            { "receipt", Receipt },
            { "receipt-cent", ReceiptCent },
            { "receipt-euro", ReceiptEuro },
            { "receipt-indian-rupee", ReceiptIndianRupee },
            { "receipt-japanese-yen", ReceiptJapaneseYen },
            { "receipt-pound-sterling", ReceiptPoundSterling },
            { "receipt-russian-ruble", ReceiptRussianRuble },
            { "receipt-swiss-franc", ReceiptSwissFranc },
            { "receipt-text", ReceiptText },
            { "receipt-turkish-lira", ReceiptTurkishLira },
            { "rectangle-circle", RectangleCircle },
            { "rectangle-ellipsis", RectangleEllipsis },
            { "rectangle-goggles", RectangleGoggles },
            { "rectangle-horizontal", RectangleHorizontal },
            { "rectangle-vertical", RectangleVertical },
            { "recycle", Recycle },
            { "redo", Redo },
            { "redo-2", Redo2 },
            { "redo-dot", RedoDot },
            { "refresh-ccw", RefreshCcw },
            { "refresh-ccw-dot", RefreshCcwDot },
            { "refresh-cw", RefreshCw },
            { "refresh-cw-off", RefreshCwOff },
            { "refrigerator", Refrigerator },
            { "regex", Regex },
            { "remove-formatting", RemoveFormatting },
            { "repeat", Repeat },
            { "repeat-1", Repeat1 },
            { "repeat-2", Repeat2 },
            { "repeat-off", RepeatOff },
            { "replace", Replace },
            { "replace-all", ReplaceAll },
            { "reply", Reply },
            { "reply-all", ReplyAll },
            { "rewind", Rewind },
            { "ribbon", Ribbon },
            { "road", Road },
            { "robot-arm", RobotArm },
            { "robot-vacuum", RobotVacuum },
            { "rocket", Rocket },
            { "rocking-chair", RockingChair },
            { "roller-coaster", RollerCoaster },
            { "rose", Rose },
            { "rotate-3-d", Rotate3D },
            { "rotate-3d", Rotate3d },
            { "rotate-ccw", RotateCcw },
            { "rotate-ccw-clock", RotateCcwClock },
            { "rotate-ccw-key", RotateCcwKey },
            { "rotate-ccw-square", RotateCcwSquare },
            { "rotate-cw", RotateCw },
            { "rotate-cw-clock", RotateCwClock },
            { "rotate-cw-fading-clock", RotateCwFadingClock },
            { "rotate-cw-square", RotateCwSquare },
            { "route", Route },
            { "route-off", RouteOff },
            { "router", Router },
            { "rows", Rows },
            { "rows-2", Rows2 },
            { "rows-3", Rows3 },
            { "rows-4", Rows4 },
            { "rss", Rss },
            { "rugby-ball", RugbyBall },
            { "ruler", Ruler },
            { "ruler-dimension-line", RulerDimensionLine },
            { "russian-ruble", RussianRuble },
            { "sailboat", Sailboat },
            { "salad", Salad },
            { "sandwich", Sandwich },
            { "satellite", Satellite },
            { "satellite-dish", SatelliteDish },
            { "saudi-riyal", SaudiRiyal },
            { "save", Save },
            { "save-all", SaveAll },
            { "save-check", SaveCheck },
            { "save-off", SaveOff },
            { "save-pen", SavePen },
            { "save-plus", SavePlus },
            { "scale", Scale },
            { "scale-3-d", Scale3D },
            { "scale-3d", Scale3d },
            { "scaling", Scaling },
            { "scan", Scan },
            { "scan-barcode", ScanBarcode },
            { "scan-box", ScanBox },
            { "scan-eye", ScanEye },
            { "scan-face", ScanFace },
            { "scan-heart", ScanHeart },
            { "scan-line", ScanLine },
            { "scan-qr-code", ScanQrCode },
            { "scan-search", ScanSearch },
            { "scan-square", ScanSquare },
            { "scan-text", ScanText },
            { "scatter-chart", ScatterChart },
            { "school", School },
            { "school-2", School2 },
            { "scissors", Scissors },
            { "scissors-line-dashed", ScissorsLineDashed },
            { "scissors-square", ScissorsSquare },
            { "scissors-square-dashed-bottom", ScissorsSquareDashedBottom },
            { "scooter", Scooter },
            { "scratch-blocks", ScratchBlocks },
            { "screen-share", ScreenShare },
            { "screen-share-off", ScreenShareOff },
            { "screw", Screw },
            { "scroll", Scroll },
            { "scroll-text", ScrollText },
            { "search", Search },
            { "search-alert", SearchAlert },
            { "search-check", SearchCheck },
            { "search-code", SearchCode },
            { "search-slash", SearchSlash },
            { "search-x", SearchX },
            { "section", Section },
            { "send", Send },
            { "send-horizonal", SendHorizonal },
            { "send-horizontal", SendHorizontal },
            { "send-to-back", SendToBack },
            { "separator-horizontal", SeparatorHorizontal },
            { "separator-vertical", SeparatorVertical },
            { "server", Server },
            { "server-cog", ServerCog },
            { "server-crash", ServerCrash },
            { "server-nas", ServerNas },
            { "server-off", ServerOff },
            { "server-plus", ServerPlus },
            { "settings", Settings },
            { "settings-2", Settings2 },
            { "shapes", Shapes },
            { "share", Share },
            { "share-2", Share2 },
            { "sheet", Sheet },
            { "shell", Shell },
            { "shelving-unit", ShelvingUnit },
            { "shield", Shield },
            { "shield-alert", ShieldAlert },
            { "shield-ban", ShieldBan },
            { "shield-check", ShieldCheck },
            { "shield-close", ShieldClose },
            { "shield-cog", ShieldCog },
            { "shield-cog-corner", ShieldCogCorner },
            { "shield-ellipsis", ShieldEllipsis },
            { "shield-half", ShieldHalf },
            { "shield-house", ShieldHouse },
            { "shield-keyhole", ShieldKeyhole },
            { "shield-lock", ShieldLock },
            { "shield-minus", ShieldMinus },
            { "shield-off", ShieldOff },
            { "shield-plus", ShieldPlus },
            { "shield-question", ShieldQuestion },
            { "shield-question-mark", ShieldQuestionMark },
            { "shield-user", ShieldUser },
            { "shield-x", ShieldX },
            { "ship", Ship },
            { "ship-cargo", ShipCargo },
            { "ship-wheel", ShipWheel },
            { "shirt", Shirt },
            { "shopping-bag", ShoppingBag },
            { "shopping-basket", ShoppingBasket },
            { "shopping-cart", ShoppingCart },
            { "shopping-cart-minus", ShoppingCartMinus },
            { "shopping-cart-plus", ShoppingCartPlus },
            { "shovel", Shovel },
            { "shower-head", ShowerHead },
            { "shredder", Shredder },
            { "shrimp", Shrimp },
            { "shrimp-off", ShrimpOff },
            { "shrink", Shrink },
            { "shrub", Shrub },
            { "shuffle", Shuffle },
            { "sidebar", Sidebar },
            { "sidebar-close", SidebarClose },
            { "sidebar-open", SidebarOpen },
            { "sigma", Sigma },
            { "sigma-square", SigmaSquare },
            { "signal", Signal },
            { "signal-high", SignalHigh },
            { "signal-low", SignalLow },
            { "signal-medium", SignalMedium },
            { "signal-zero", SignalZero },
            { "signature", Signature },
            { "signpost", Signpost },
            { "signpost-big", SignpostBig },
            { "siren", Siren },
            { "siren-off", SirenOff },
            { "sixteenth-notes", SixteenthNotes },
            { "skip-back", SkipBack },
            { "skip-forward", SkipForward },
            { "skull", Skull },
            { "slash", Slash },
            { "slash-square", SlashSquare },
            { "slice", Slice },
            { "sliders", Sliders },
            { "sliders-horizontal", SlidersHorizontal },
            { "sliders-vertical", SlidersVertical },
            { "smartphone", Smartphone },
            { "smartphone-charging", SmartphoneCharging },
            { "smartphone-nfc", SmartphoneNfc },
            { "smartphone-user-round", SmartphoneUserRound },
            { "smile", Smile },
            { "smile-plus", SmilePlus },
            { "snail", Snail },
            { "snowflake", Snowflake },
            { "soap-dispenser-droplet", SoapDispenserDroplet },
            { "sofa", Sofa },
            { "solar-panel", SolarPanel },
            { "sort-asc", SortAsc },
            { "sort-desc", SortDesc },
            { "soup", Soup },
            { "space", Space },
            { "spade", Spade },
            { "sparkle", Sparkle },
            { "sparkles", Sparkles },
            { "speaker", Speaker },
            { "speech", Speech },
            { "spell-check", SpellCheck },
            { "spell-check-2", SpellCheck2 },
            { "spline", Spline },
            { "spline-pointer", SplinePointer },
            { "split", Split },
            { "split-square-horizontal", SplitSquareHorizontal },
            { "split-square-vertical", SplitSquareVertical },
            { "spool", Spool },
            { "sport-shoe", SportShoe },
            { "spotlight", Spotlight },
            { "spray-can", SprayCan },
            { "sprout", Sprout },
            { "square", Square },
            { "square-activity", SquareActivity },
            { "square-arrow-down", SquareArrowDown },
            { "square-arrow-down-left", SquareArrowDownLeft },
            { "square-arrow-down-right", SquareArrowDownRight },
            { "square-arrow-left", SquareArrowLeft },
            { "square-arrow-out-down-left", SquareArrowOutDownLeft },
            { "square-arrow-out-down-right", SquareArrowOutDownRight },
            { "square-arrow-out-up-left", SquareArrowOutUpLeft },
            { "square-arrow-out-up-right", SquareArrowOutUpRight },
            { "square-arrow-right", SquareArrowRight },
            { "square-arrow-right-enter", SquareArrowRightEnter },
            { "square-arrow-right-exit", SquareArrowRightExit },
            { "square-arrow-up", SquareArrowUp },
            { "square-arrow-up-left", SquareArrowUpLeft },
            { "square-arrow-up-right", SquareArrowUpRight },
            { "square-asterisk", SquareAsterisk },
            { "square-bookmark", SquareBookmark },
            { "square-bottom-dashed-scissors", SquareBottomDashedScissors },
            { "square-centerline-dashed-horizontal", SquareCenterlineDashedHorizontal },
            { "square-centerline-dashed-vertical", SquareCenterlineDashedVertical },
            { "square-chart-gantt", SquareChartGantt },
            { "square-check", SquareCheck },
            { "square-check-big", SquareCheckBig },
            { "square-chevron-down", SquareChevronDown },
            { "square-chevron-left", SquareChevronLeft },
            { "square-chevron-right", SquareChevronRight },
            { "square-chevron-up", SquareChevronUp },
            { "square-chevrons-down", SquareChevronsDown },
            { "square-chevrons-left", SquareChevronsLeft },
            { "square-chevrons-right", SquareChevronsRight },
            { "square-chevrons-up", SquareChevronsUp },
            { "square-code", SquareCode },
            { "square-dashed", SquareDashed },
            { "square-dashed-bottom", SquareDashedBottom },
            { "square-dashed-bottom-code", SquareDashedBottomCode },
            { "square-dashed-kanban", SquareDashedKanban },
            { "square-dashed-mouse-pointer", SquareDashedMousePointer },
            { "square-dashed-plus", SquareDashedPlus },
            { "square-dashed-text", SquareDashedText },
            { "square-dashed-top-solid", SquareDashedTopSolid },
            { "square-dashed-x", SquareDashedX },
            { "square-dashed-x-corner", SquareDashedXCorner },
            { "square-dimensions", SquareDimensions },
            { "square-divide", SquareDivide },
            { "square-dot", SquareDot },
            { "square-equal", SquareEqual },
            { "square-exclamation-point", SquareExclamationPoint },
            { "square-function", SquareFunction },
            { "square-gantt-chart", SquareGanttChart },
            { "square-kanban", SquareKanban },
            { "square-kanban-plus", SquareKanbanPlus },
            { "square-letter-a", SquareLetterA },
            { "square-letter-b", SquareLetterB },
            { "square-letter-c", SquareLetterC },
            { "square-letter-d", SquareLetterD },
            { "square-letter-e", SquareLetterE },
            { "square-letter-f", SquareLetterF },
            { "square-letter-g", SquareLetterG },
            { "square-letter-h", SquareLetterH },
            { "square-letter-i", SquareLetterI },
            { "square-letter-j", SquareLetterJ },
            { "square-letter-k", SquareLetterK },
            { "square-letter-l", SquareLetterL },
            { "square-letter-m", SquareLetterM },
            { "square-letter-n", SquareLetterN },
            { "square-letter-o", SquareLetterO },
            { "square-letter-p", SquareLetterP },
            { "square-letter-p-off", SquareLetterPOff },
            { "square-letter-q", SquareLetterQ },
            { "square-letter-r", SquareLetterR },
            { "square-letter-s", SquareLetterS },
            { "square-letter-t", SquareLetterT },
            { "square-letter-u", SquareLetterU },
            { "square-letter-v", SquareLetterV },
            { "square-letter-w", SquareLetterW },
            { "square-letter-x", SquareLetterX },
            { "square-letter-y", SquareLetterY },
            { "square-letter-z", SquareLetterZ },
            { "square-library", SquareLibrary },
            { "square-m", SquareM },
            { "square-menu", SquareMenu },
            { "square-minus", SquareMinus },
            { "square-mouse-pointer", SquareMousePointer },
            { "square-off", SquareOff },
            { "square-padding", SquarePadding },
            { "square-padding-bottom", SquarePaddingBottom },
            { "square-padding-horizontal", SquarePaddingHorizontal },
            { "square-padding-left", SquarePaddingLeft },
            { "square-padding-right", SquarePaddingRight },
            { "square-padding-top", SquarePaddingTop },
            { "square-padding-vertical", SquarePaddingVertical },
            { "square-parking", SquareParking },
            { "square-parking-off", SquareParkingOff },
            { "square-pause", SquarePause },
            { "square-pen", SquarePen },
            { "square-percent", SquarePercent },
            { "square-pi", SquarePi },
            { "square-pilcrow", SquarePilcrow },
            { "square-play", SquarePlay },
            { "square-plus", SquarePlus },
            { "square-power", SquarePower },
            { "square-radical", SquareRadical },
            { "square-round-corner", SquareRoundCorner },
            { "square-scissors", SquareScissors },
            { "square-sigma", SquareSigma },
            { "square-slash", SquareSlash },
            { "square-sparkles", SquareSparkles },
            { "square-split-horizontal", SquareSplitHorizontal },
            { "square-split-vertical", SquareSplitVertical },
            { "square-square", SquareSquare },
            { "square-stack", SquareStack },
            { "square-star", SquareStar },
            { "square-stop", SquareStop },
            { "square-terminal", SquareTerminal },
            { "square-text", SquareText },
            { "square-user", SquareUser },
            { "square-user-round", SquareUserRound },
            { "square-x", SquareX },
            { "squares-exclude", SquaresExclude },
            { "squares-intersect", SquaresIntersect },
            { "squares-subtract", SquaresSubtract },
            { "squares-unite", SquaresUnite },
            { "squircle", Squircle },
            { "squircle-dashed", SquircleDashed },
            { "squirrel", Squirrel },
            { "stamp", Stamp },
            { "star", Star },
            { "star-check", StarCheck },
            { "star-half", StarHalf },
            { "star-minus", StarMinus },
            { "star-off", StarOff },
            { "star-plus", StarPlus },
            { "star-x", StarX },
            { "stars", Stars },
            { "step-back", StepBack },
            { "step-forward", StepForward },
            { "stethoscope", Stethoscope },
            { "sticker", Sticker },
            { "sticky-note", StickyNote },
            { "sticky-note-check", StickyNoteCheck },
            { "sticky-note-minus", StickyNoteMinus },
            { "sticky-note-off", StickyNoteOff },
            { "sticky-note-plus", StickyNotePlus },
            { "sticky-note-x", StickyNoteX },
            { "sticky-notes", StickyNotes },
            { "stone", Stone },
            { "stop-circle", StopCircle },
            { "store", Store },
            { "stretch-horizontal", StretchHorizontal },
            { "stretch-vertical", StretchVertical },
            { "strikethrough", Strikethrough },
            { "subscript", Subscript },
            { "subtitles", Subtitles },
            { "summary", Summary },
            { "sun", Sun },
            { "sun-dim", SunDim },
            { "sun-medium", SunMedium },
            { "sun-moon", SunMoon },
            { "sun-snow", SunSnow },
            { "sunrise", Sunrise },
            { "sunset", Sunset },
            { "superscript", Superscript },
            { "swatch-book", SwatchBook },
            { "swiss-franc", SwissFranc },
            { "switch-camera", SwitchCamera },
            { "sword", Sword },
            { "swords", Swords },
            { "syringe", Syringe },
            { "table", Table },
            { "table-2", Table2 },
            { "table-cells-merge", TableCellsMerge },
            { "table-cells-split", TableCellsSplit },
            { "table-columns-split", TableColumnsSplit },
            { "table-config", TableConfig },
            { "table-of-contents", TableOfContents },
            { "table-properties", TableProperties },
            { "table-rows-split", TableRowsSplit },
            { "tablet", Tablet },
            { "tablet-smartphone", TabletSmartphone },
            { "tablets", Tablets },
            { "tag", Tag },
            { "tag-plus", TagPlus },
            { "tag-x", TagX },
            { "tags", Tags },
            { "tally-1", Tally1 },
            { "tally-2", Tally2 },
            { "tally-3", Tally3 },
            { "tally-4", Tally4 },
            { "tally-5", Tally5 },
            { "tangent", Tangent },
            { "target", Target },
            { "telescope", Telescope },
            { "tent", Tent },
            { "tent-tree", TentTree },
            { "terminal", Terminal },
            { "terminal-square", TerminalSquare },
            { "test-tube", TestTube },
            { "test-tube-2", TestTube2 },
            { "test-tube-diagonal", TestTubeDiagonal },
            { "test-tubes", TestTubes },
            { "text", Text },
            { "text-align-center", TextAlignCenter },
            { "text-align-end", TextAlignEnd },
            { "text-align-justify", TextAlignJustify },
            { "text-align-justify-center", TextAlignJustifyCenter },
            { "text-align-justify-end", TextAlignJustifyEnd },
            { "text-align-justify-start", TextAlignJustifyStart },
            { "text-align-start", TextAlignStart },
            { "text-cursor", TextCursor },
            { "text-cursor-input", TextCursorInput },
            { "text-initial", TextInitial },
            { "text-quote", TextQuote },
            { "text-search", TextSearch },
            { "text-select", TextSelect },
            { "text-selection", TextSelection },
            { "text-wrap", TextWrap },
            { "theater", Theater },
            { "thermometer", Thermometer },
            { "thermometer-snowflake", ThermometerSnowflake },
            { "thermometer-sun", ThermometerSun },
            { "thumbs-down", ThumbsDown },
            { "thumbs-up", ThumbsUp },
            { "tic-tac-toe", TicTacToe },
            { "ticket", Ticket },
            { "ticket-check", TicketCheck },
            { "ticket-minus", TicketMinus },
            { "ticket-percent", TicketPercent },
            { "ticket-plus", TicketPlus },
            { "ticket-slash", TicketSlash },
            { "ticket-x", TicketX },
            { "tickets", Tickets },
            { "tickets-plane", TicketsPlane },
            { "timeline", Timeline },
            { "timer", Timer },
            { "timer-off", TimerOff },
            { "timer-reset", TimerReset },
            { "toggle-left", ToggleLeft },
            { "toggle-right", ToggleRight },
            { "toilet", Toilet },
            { "tombstone", Tombstone },
            { "tool-case", ToolCase },
            { "toolbox", Toolbox },
            { "toothbrush", Toothbrush },
            { "toothbrush-sparkles", ToothbrushSparkles },
            { "tornado", Tornado },
            { "torus", Torus },
            { "touchpad", Touchpad },
            { "touchpad-off", TouchpadOff },
            { "towel-rack", TowelRack },
            { "tower-control", TowerControl },
            { "toy-brick", ToyBrick },
            { "tractor", Tractor },
            { "traffic-cone", TrafficCone },
            { "trailer", Trailer },
            { "train", Train },
            { "train-front", TrainFront },
            { "train-front-tunnel", TrainFrontTunnel },
            { "train-track", TrainTrack },
            { "tram-front", TramFront },
            { "transgender", Transgender },
            { "trash", Trash },
            { "trash-2", Trash2 },
            { "trash-off", TrashOff },
            { "tree-deciduous", TreeDeciduous },
            { "tree-palm", TreePalm },
            { "tree-pine", TreePine },
            { "trees", Trees },
            { "trending-down", TrendingDown },
            { "trending-up", TrendingUp },
            { "trending-up-down", TrendingUpDown },
            { "triangle", Triangle },
            { "triangle-alert", TriangleAlert },
            { "triangle-dashed", TriangleDashed },
            { "triangle-right", TriangleRight },
            { "triangles-centerline-dashed-horizontal", TrianglesCenterlineDashedHorizontal },
            { "triangles-centerline-dashed-vertical", TrianglesCenterlineDashedVertical },
            { "trophy", Trophy },
            { "truck", Truck },
            { "truck-electric", TruckElectric },
            { "tube-lotion", TubeLotion },
            { "turkish-lira", TurkishLira },
            { "turntable", Turntable },
            { "turtle", Turtle },
            { "tv", Tv },
            { "tv-2", Tv2 },
            { "tv-minimal", TvMinimal },
            { "tv-minimal-play", TvMinimalPlay },
            { "type", Type },
            { "type-outline", TypeOutline },
            { "umbrella", Umbrella },
            { "umbrella-off", UmbrellaOff },
            { "underline", Underline },
            { "undo", Undo },
            { "undo-2", Undo2 },
            { "undo-dot", UndoDot },
            { "unfold-horizontal", UnfoldHorizontal },
            { "unfold-vertical", UnfoldVertical },
            { "ungroup", Ungroup },
            { "university", University },
            { "unlink", Unlink },
            { "unlink-2", Unlink2 },
            { "unlock", Unlock },
            { "unlock-keyhole", UnlockKeyhole },
            { "unplug", Unplug },
            { "upload", Upload },
            { "upload-cloud", UploadCloud },
            { "usb", Usb },
            { "usb-c-port", UsbCPort },
            { "user", User },
            { "user-2", User2 },
            { "user-check", UserCheck },
            { "user-check-2", UserCheck2 },
            { "user-circle", UserCircle },
            { "user-circle-2", UserCircle2 },
            { "user-cog", UserCog },
            { "user-cog-2", UserCog2 },
            { "user-group", UserGroup },
            { "user-key", UserKey },
            { "user-lock", UserLock },
            { "user-minus", UserMinus },
            { "user-minus-2", UserMinus2 },
            { "user-pen", UserPen },
            { "user-plus", UserPlus },
            { "user-plus-2", UserPlus2 },
            { "user-round", UserRound },
            { "user-round-arrow-left", UserRoundArrowLeft },
            { "user-round-check", UserRoundCheck },
            { "user-round-cog", UserRoundCog },
            { "user-round-group", UserRoundGroup },
            { "user-round-key", UserRoundKey },
            { "user-round-minus", UserRoundMinus },
            { "user-round-pen", UserRoundPen },
            { "user-round-plus", UserRoundPlus },
            { "user-round-search", UserRoundSearch },
            { "user-round-shield", UserRoundShield },
            { "user-round-star", UserRoundStar },
            { "user-round-x", UserRoundX },
            { "user-search", UserSearch },
            { "user-shield", UserShield },
            { "user-square", UserSquare },
            { "user-square-2", UserSquare2 },
            { "user-star", UserStar },
            { "user-x", UserX },
            { "user-x-2", UserX2 },
            { "users", Users },
            { "users-2", Users2 },
            { "users-collaboration", UsersCollaboration },
            { "users-round", UsersRound },
            { "users-round-collaboration", UsersRoundCollaboration },
            { "utensils", Utensils },
            { "utensils-crossed", UtensilsCrossed },
            { "utility-pole", UtilityPole },
            { "van", Van },
            { "variable", Variable },
            { "vault", Vault },
            { "vector-polygon", VectorPolygon },
            { "vector-square", VectorSquare },
            { "vegan", Vegan },
            { "venetian-mask", VenetianMask },
            { "venus", Venus },
            { "venus-and-mars", VenusAndMars },
            { "verified", Verified },
            { "vibrate", Vibrate },
            { "vibrate-off", VibrateOff },
            { "video", Video },
            { "video-off", VideoOff },
            { "videotape", Videotape },
            { "view", View },
            { "virus", Virus },
            { "virus-off", VirusOff },
            { "voicemail", Voicemail },
            { "volleyball", Volleyball },
            { "volume", Volume },
            { "volume-1", Volume1 },
            { "volume-2", Volume2 },
            { "volume-off", VolumeOff },
            { "volume-x", VolumeX },
            { "vote", Vote },
            { "wallet", Wallet },
            { "wallet-2", Wallet2 },
            { "wallet-cards", WalletCards },
            { "wallet-minimal", WalletMinimal },
            { "wallpaper", Wallpaper },
            { "wand", Wand },
            { "wand-2", Wand2 },
            { "wand-sparkles", WandSparkles },
            { "warehouse", Warehouse },
            { "washing-machine", WashingMachine },
            { "watch", Watch },
            { "waves", Waves },
            { "waves-arrow-down", WavesArrowDown },
            { "waves-arrow-up", WavesArrowUp },
            { "waves-horizontal", WavesHorizontal },
            { "waves-ladder", WavesLadder },
            { "waves-vertical", WavesVertical },
            { "waypoints", Waypoints },
            { "webcam", Webcam },
            { "webcam-off", WebcamOff },
            { "webhook", Webhook },
            { "webhook-off", WebhookOff },
            { "weight", Weight },
            { "weight-tilde", WeightTilde },
            { "wheat", Wheat },
            { "wheat-off", WheatOff },
            { "whistle", Whistle },
            { "whole-word", WholeWord },
            { "wifi", Wifi },
            { "wifi-cog", WifiCog },
            { "wifi-high", WifiHigh },
            { "wifi-lock", WifiLock },
            { "wifi-low", WifiLow },
            { "wifi-off", WifiOff },
            { "wifi-pen", WifiPen },
            { "wifi-sync", WifiSync },
            { "wifi-zero", WifiZero },
            { "wind", Wind },
            { "wind-arrow-down", WindArrowDown },
            { "wind-arrow-up", WindArrowUp },
            { "wine", Wine },
            { "wine-off", WineOff },
            { "workflow", Workflow },
            { "worm", Worm },
            { "wrap-text", WrapText },
            { "wrench", Wrench },
            { "wrench-off", WrenchOff },
            { "x", X },
            { "x-circle", XCircle },
            { "x-line-top", XLineTop },
            { "x-octagon", XOctagon },
            { "x-square", XSquare },
            { "zap", Zap },
            { "zap-off", ZapOff },
            { "zodiac-aquarius", ZodiacAquarius },
            { "zodiac-aries", ZodiacAries },
            { "zodiac-cancer", ZodiacCancer },
            { "zodiac-capricorn", ZodiacCapricorn },
            { "zodiac-gemini", ZodiacGemini },
            { "zodiac-leo", ZodiacLeo },
            { "zodiac-libra", ZodiacLibra },
            { "zodiac-ophiuchus", ZodiacOphiuchus },
            { "zodiac-pisces", ZodiacPisces },
            { "zodiac-sagittarius", ZodiacSagittarius },
            { "zodiac-scorpio", ZodiacScorpio },
            { "zodiac-taurus", ZodiacTaurus },
            { "zodiac-virgo", ZodiacVirgo },
            { "zoom-in", ZoomIn },
            { "zoom-out", ZoomOut },
        };

        static readonly string[] AllNames = new string[]
        {
            "a-arrow-down",
            "a-arrow-up",
            "a-large-small",
            "accessibility",
            "activity",
            "activity-square",
            "ad",
            "air-vent",
            "airplay",
            "alarm-check",
            "alarm-clock",
            "alarm-clock-check",
            "alarm-clock-minus",
            "alarm-clock-off",
            "alarm-clock-plus",
            "alarm-minus",
            "alarm-plus",
            "alarm-smoke",
            "album",
            "alert-circle",
            "alert-octagon",
            "alert-triangle",
            "align-center",
            "align-center-horizontal",
            "align-center-vertical",
            "align-end-horizontal",
            "align-end-vertical",
            "align-horizontal-distribute-center",
            "align-horizontal-distribute-end",
            "align-horizontal-distribute-start",
            "align-horizontal-justify-center",
            "align-horizontal-justify-end",
            "align-horizontal-justify-start",
            "align-horizontal-space-around",
            "align-horizontal-space-between",
            "align-justify",
            "align-left",
            "align-right",
            "align-start-horizontal",
            "align-start-vertical",
            "align-vertical-distribute-center",
            "align-vertical-distribute-end",
            "align-vertical-distribute-start",
            "align-vertical-justify-center",
            "align-vertical-justify-end",
            "align-vertical-justify-start",
            "align-vertical-space-around",
            "align-vertical-space-between",
            "ambulance",
            "ampersand",
            "ampersands",
            "amphora",
            "anchor",
            "angle",
            "angry",
            "annoyed",
            "antenna",
            "anvil",
            "aperture",
            "app-window",
            "app-window-mac",
            "apple",
            "archive",
            "archive-arrow-down",
            "archive-arrow-up",
            "archive-restore",
            "archive-x",
            "area-chart",
            "armchair",
            "armenian-dram",
            "arrow-big-down",
            "arrow-big-down-dash",
            "arrow-big-left",
            "arrow-big-left-dash",
            "arrow-big-right",
            "arrow-big-right-dash",
            "arrow-big-up",
            "arrow-big-up-dash",
            "arrow-down",
            "arrow-down-0-1",
            "arrow-down-01",
            "arrow-down-1-0",
            "arrow-down-10",
            "arrow-down-a-z",
            "arrow-down-az",
            "arrow-down-circle",
            "arrow-down-from-line",
            "arrow-down-left",
            "arrow-down-left-from-circle",
            "arrow-down-left-from-square",
            "arrow-down-left-square",
            "arrow-down-narrow-wide",
            "arrow-down-right",
            "arrow-down-right-from-circle",
            "arrow-down-right-from-square",
            "arrow-down-right-square",
            "arrow-down-square",
            "arrow-down-to-dot",
            "arrow-down-to-line",
            "arrow-down-up",
            "arrow-down-wide-narrow",
            "arrow-down-z-a",
            "arrow-down-za",
            "arrow-left",
            "arrow-left-circle",
            "arrow-left-from-line",
            "arrow-left-right",
            "arrow-left-square",
            "arrow-left-to-line",
            "arrow-right",
            "arrow-right-circle",
            "arrow-right-from-line",
            "arrow-right-left",
            "arrow-right-square",
            "arrow-right-to-line",
            "arrow-up",
            "arrow-up-0-1",
            "arrow-up-01",
            "arrow-up-1-0",
            "arrow-up-10",
            "arrow-up-a-z",
            "arrow-up-az",
            "arrow-up-circle",
            "arrow-up-down",
            "arrow-up-from-dot",
            "arrow-up-from-line",
            "arrow-up-left",
            "arrow-up-left-from-circle",
            "arrow-up-left-from-square",
            "arrow-up-left-square",
            "arrow-up-narrow-wide",
            "arrow-up-right",
            "arrow-up-right-from-circle",
            "arrow-up-right-from-square",
            "arrow-up-right-square",
            "arrow-up-square",
            "arrow-up-to-line",
            "arrow-up-wide-narrow",
            "arrow-up-z-a",
            "arrow-up-za",
            "arrows-up-from-line",
            "asterisk",
            "asterisk-square",
            "astroid",
            "at-sign",
            "atom",
            "audio-lines",
            "audio-lines-off",
            "audio-lines-x",
            "audio-waveform",
            "avocado",
            "award",
            "axe",
            "axis-3-d",
            "axis-3d",
            "azerbaijani-manat",
            "baby",
            "backpack",
            "bacon",
            "badge",
            "badge-alert",
            "badge-cent",
            "badge-check",
            "badge-dollar-sign",
            "badge-euro",
            "badge-help",
            "badge-indian-rupee",
            "badge-info",
            "badge-japanese-yen",
            "badge-minus",
            "badge-percent",
            "badge-plus",
            "badge-pound-sterling",
            "badge-question-mark",
            "badge-russian-ruble",
            "badge-swiss-franc",
            "badge-turkish-lira",
            "badge-x",
            "baggage-claim",
            "ball-wave",
            "balloon",
            "ban",
            "banana",
            "bandage",
            "bangladeshi-taka",
            "banknote",
            "banknote-arrow-down",
            "banknote-arrow-up",
            "banknote-check",
            "banknote-x",
            "bar-chart",
            "bar-chart-2",
            "bar-chart-3",
            "bar-chart-4",
            "bar-chart-big",
            "bar-chart-horizontal",
            "bar-chart-horizontal-big",
            "barcode",
            "barrel",
            "baseline",
            "bath",
            "battery",
            "battery-charging",
            "battery-full",
            "battery-low",
            "battery-medium",
            "battery-plus",
            "battery-warning",
            "beaker",
            "bean",
            "bean-off",
            "bed",
            "bed-double",
            "bed-single",
            "beef",
            "beef-off",
            "beer",
            "beer-off",
            "belarusian-ruble",
            "bell",
            "bell-check",
            "bell-dot",
            "bell-electric",
            "bell-minus",
            "bell-off",
            "bell-plus",
            "bell-ring",
            "between-horizonal-end",
            "between-horizonal-start",
            "between-horizontal-end",
            "between-horizontal-start",
            "between-vertical-end",
            "between-vertical-start",
            "biceps-flexed",
            "bike",
            "binary",
            "binoculars",
            "biohazard",
            "bird",
            "birdhouse",
            "bitcoin",
            "blend",
            "blender",
            "blinds",
            "blocks",
            "bluetooth",
            "bluetooth-connected",
            "bluetooth-off",
            "bluetooth-searching",
            "bold",
            "bolt",
            "bomb",
            "bone",
            "bone-fracture",
            "book",
            "book-a",
            "book-alert",
            "book-audio",
            "book-bookmark",
            "book-check",
            "book-copy",
            "book-dashed",
            "book-down",
            "book-headphones",
            "book-heart",
            "book-image",
            "book-key",
            "book-lock",
            "book-marked",
            "book-minus",
            "book-open",
            "book-open-check",
            "book-open-text",
            "book-plus",
            "book-search",
            "book-template",
            "book-text",
            "book-type",
            "book-up",
            "book-up-2",
            "book-user",
            "book-x",
            "bookmark",
            "bookmark-check",
            "bookmark-minus",
            "bookmark-off",
            "bookmark-plus",
            "bookmark-x",
            "boom-box",
            "bot",
            "bot-message-square",
            "bot-off",
            "bottle-wine",
            "bow-arrow",
            "box",
            "box-select",
            "boxes",
            "boxes-linked",
            "braces",
            "brackets",
            "brain",
            "brain-circuit",
            "brain-cog",
            "brain-off",
            "brick-wall",
            "brick-wall-fire",
            "brick-wall-shield",
            "bridge",
            "briefcase",
            "briefcase-business",
            "briefcase-conveyor-belt",
            "briefcase-medical",
            "briefcase-plus",
            "bring-to-front",
            "broccoli",
            "broom",
            "broom-sparkles",
            "brush",
            "brush-cleaning",
            "bubbles",
            "bug",
            "bug-off",
            "bug-play",
            "building",
            "building-2",
            "building-complex",
            "building-complex-plus",
            "bus",
            "bus-front",
            "cable",
            "cable-car",
            "cake",
            "cake-slice",
            "calculator",
            "calendar",
            "calendar-1",
            "calendar-arrow-down",
            "calendar-arrow-left",
            "calendar-arrow-right",
            "calendar-arrow-up",
            "calendar-check",
            "calendar-check-2",
            "calendar-chevrons-left",
            "calendar-chevrons-right",
            "calendar-clock",
            "calendar-cog",
            "calendar-days",
            "calendar-fold",
            "calendar-heart",
            "calendar-minus",
            "calendar-minus-2",
            "calendar-off",
            "calendar-plus",
            "calendar-plus-2",
            "calendar-range",
            "calendar-search",
            "calendar-sync",
            "calendar-x",
            "calendar-x-2",
            "calendars",
            "camera",
            "camera-off",
            "can",
            "can-soda",
            "candlestick-chart",
            "candy",
            "candy-cane",
            "candy-off",
            "cannabis",
            "cannabis-off",
            "captions",
            "captions-off",
            "car",
            "car-battery",
            "car-front",
            "car-taxi-front",
            "caravan",
            "card-sim",
            "carrot",
            "carton",
            "carton-off",
            "case-lower",
            "case-sensitive",
            "case-upper",
            "cassette-tape",
            "cast",
            "castle",
            "cat",
            "cctv",
            "cctv-off",
            "certificate",
            "chart-area",
            "chart-bar",
            "chart-bar-big",
            "chart-bar-decreasing",
            "chart-bar-increasing",
            "chart-bar-stacked",
            "chart-bell-curve",
            "chart-candlestick",
            "chart-column",
            "chart-column-big",
            "chart-column-decreasing",
            "chart-column-increasing",
            "chart-column-stacked",
            "chart-gantt",
            "chart-line",
            "chart-network",
            "chart-no-axes-column",
            "chart-no-axes-column-decreasing",
            "chart-no-axes-column-increasing",
            "chart-no-axes-combined",
            "chart-no-axes-gantt",
            "chart-pie",
            "chart-quadrant",
            "chart-radar",
            "chart-scatter",
            "chart-spline",
            "check",
            "check-check",
            "check-circle",
            "check-circle-2",
            "check-line",
            "check-square",
            "check-square-2",
            "chef-hat",
            "cherry",
            "chess-bishop",
            "chess-king",
            "chess-knight",
            "chess-pawn",
            "chess-queen",
            "chess-rook",
            "chevron-down",
            "chevron-down-circle",
            "chevron-down-square",
            "chevron-first",
            "chevron-last",
            "chevron-left",
            "chevron-left-circle",
            "chevron-left-square",
            "chevron-right",
            "chevron-right-circle",
            "chevron-right-square",
            "chevron-up",
            "chevron-up-circle",
            "chevron-up-square",
            "chevrons-down",
            "chevrons-down-up",
            "chevrons-left",
            "chevrons-left-right",
            "chevrons-left-right-ellipsis",
            "chevrons-right",
            "chevrons-right-left",
            "chevrons-up",
            "chevrons-up-down",
            "church",
            "cigarette",
            "cigarette-off",
            "circle",
            "circle-alert",
            "circle-arrow-down",
            "circle-arrow-left",
            "circle-arrow-out-down-left",
            "circle-arrow-out-down-right",
            "circle-arrow-out-up-left",
            "circle-arrow-out-up-right",
            "circle-arrow-right",
            "circle-arrow-up",
            "circle-check",
            "circle-check-big",
            "circle-chevron-down",
            "circle-chevron-left",
            "circle-chevron-right",
            "circle-chevron-up",
            "circle-chevrons-down",
            "circle-chevrons-left",
            "circle-chevrons-right",
            "circle-chevrons-up",
            "circle-dashed",
            "circle-dashed-check",
            "circle-divide",
            "circle-dollar-sign",
            "circle-dot",
            "circle-dot-dashed",
            "circle-ellipsis",
            "circle-equal",
            "circle-euro",
            "circle-fading-arrow-up",
            "circle-fading-play",
            "circle-fading-plus",
            "circle-gauge",
            "circle-help",
            "circle-letter-a",
            "circle-letter-b",
            "circle-letter-c",
            "circle-letter-d",
            "circle-letter-e",
            "circle-letter-f",
            "circle-letter-g",
            "circle-letter-h",
            "circle-letter-i",
            "circle-letter-j",
            "circle-letter-k",
            "circle-letter-l",
            "circle-letter-m",
            "circle-letter-n",
            "circle-letter-o",
            "circle-letter-p",
            "circle-letter-p-off",
            "circle-letter-q",
            "circle-letter-r",
            "circle-letter-s",
            "circle-letter-t",
            "circle-letter-u",
            "circle-letter-v",
            "circle-letter-w",
            "circle-letter-x",
            "circle-letter-y",
            "circle-letter-z",
            "circle-minus",
            "circle-off",
            "circle-parking",
            "circle-parking-off",
            "circle-pause",
            "circle-percent",
            "circle-pile",
            "circle-play",
            "circle-plus",
            "circle-pound-sterling",
            "circle-power",
            "circle-question-mark",
            "circle-slash",
            "circle-slash-2",
            "circle-slashed",
            "circle-small",
            "circle-star",
            "circle-stop",
            "circle-user",
            "circle-user-round",
            "circle-x",
            "circuit-board",
            "citrus",
            "clapperboard",
            "clef-alto",
            "clef-bass",
            "clef-treble",
            "clipboard",
            "clipboard-check",
            "clipboard-clock",
            "clipboard-copy",
            "clipboard-edit",
            "clipboard-list",
            "clipboard-minus",
            "clipboard-paste",
            "clipboard-pen",
            "clipboard-pen-line",
            "clipboard-plus",
            "clipboard-signature",
            "clipboard-type",
            "clipboard-x",
            "clock",
            "clock-1",
            "clock-10",
            "clock-11",
            "clock-12",
            "clock-2",
            "clock-3",
            "clock-4",
            "clock-5",
            "clock-6",
            "clock-7",
            "clock-8",
            "clock-9",
            "clock-alert",
            "clock-arrow-down",
            "clock-arrow-left",
            "clock-arrow-right",
            "clock-arrow-up",
            "clock-check",
            "clock-fading",
            "clock-plus",
            "closed-caption",
            "cloud",
            "cloud-alert",
            "cloud-backup",
            "cloud-check",
            "cloud-cog",
            "cloud-download",
            "cloud-drizzle",
            "cloud-fog",
            "cloud-hail",
            "cloud-lightning",
            "cloud-moon",
            "cloud-moon-rain",
            "cloud-off",
            "cloud-rain",
            "cloud-rain-wind",
            "cloud-snow",
            "cloud-sun",
            "cloud-sun-rain",
            "cloud-sync",
            "cloud-upload",
            "cloudy",
            "clover",
            "club",
            "code",
            "code-2",
            "code-square",
            "code-xml",
            "coffee",
            "cog",
            "coins",
            "columns",
            "columns-2",
            "columns-3",
            "columns-3-cog",
            "columns-4",
            "columns-settings",
            "combine",
            "command",
            "compass",
            "component",
            "computer",
            "concierge-bell",
            "cone",
            "construction",
            "contact",
            "contact-2",
            "contact-round",
            "container",
            "contrast",
            "cookie",
            "cooking-pot",
            "copy",
            "copy-check",
            "copy-minus",
            "copy-plus",
            "copy-slash",
            "copy-x",
            "copyleft",
            "copyright",
            "corner-down-left",
            "corner-down-right",
            "corner-left-down",
            "corner-left-up",
            "corner-right-down",
            "corner-right-up",
            "corner-up-left",
            "corner-up-right",
            "cpu",
            "cpu-ai",
            "creative-commons",
            "credit-card",
            "credit-card-check",
            "credit-card-minus",
            "credit-card-plus",
            "credit-card-reader",
            "credit-card-x",
            "croissant",
            "crop",
            "cross",
            "crosshair",
            "crown",
            "cuboid",
            "cup-soda",
            "cupcake",
            "curly-braces",
            "currency",
            "cylinder",
            "dam",
            "database",
            "database-arrow-down",
            "database-arrow-up",
            "database-backup",
            "database-check",
            "database-cog",
            "database-minus",
            "database-plus",
            "database-search",
            "database-x",
            "database-zap",
            "decimals-arrow-left",
            "decimals-arrow-right",
            "delete",
            "dessert",
            "diameter",
            "diamond",
            "diamond-minus",
            "diamond-percent",
            "diamond-plus",
            "dice-1",
            "dice-2",
            "dice-3",
            "dice-4",
            "dice-5",
            "dice-6",
            "dices",
            "diff",
            "disc",
            "disc-2",
            "disc-3",
            "disc-album",
            "divide",
            "divide-circle",
            "divide-square",
            "dna",
            "dna-off",
            "dock",
            "dog",
            "dollar-sign",
            "dome",
            "donut",
            "door-closed",
            "door-closed-cog",
            "door-closed-locked",
            "door-closed-package",
            "door-open",
            "door-stairwell",
            "dot",
            "dot-square",
            "download",
            "download-cloud",
            "drafting-compass",
            "drama",
            "drill",
            "drone",
            "droplet",
            "droplet-off",
            "droplets",
            "drum",
            "drumstick",
            "dumbbell",
            "ear",
            "ear-off",
            "earbuds",
            "earbuds-off",
            "earth",
            "earth-lock",
            "eclipse",
            "edit",
            "edit-2",
            "edit-3",
            "egg",
            "egg-fried",
            "egg-off",
            "eighth-note",
            "eighth-notes",
            "eject",
            "ellipse",
            "ellipsis",
            "ellipsis-vertical",
            "emirati-dirham",
            "engine",
            "equal",
            "equal-approximately",
            "equal-approximately-not",
            "equal-not",
            "equal-square",
            "eraser",
            "ethernet-port",
            "euro",
            "ev-charger",
            "expand",
            "external-link",
            "eye",
            "eye-closed",
            "eye-dashed",
            "eye-off",
            "face-angry",
            "face-expressionless",
            "face-grinning",
            "face-neutral",
            "face-slightly-frowning",
            "face-slightly-smiling",
            "face-slightly-smiling-plus",
            "factory",
            "fan",
            "fast-forward",
            "faucet",
            "feather",
            "fence",
            "ferris-wheel",
            "file",
            "file-archive",
            "file-audio",
            "file-audio-2",
            "file-axis-3-d",
            "file-axis-3d",
            "file-badge",
            "file-badge-2",
            "file-bar-chart",
            "file-bar-chart-2",
            "file-box",
            "file-braces",
            "file-braces-corner",
            "file-chart-column",
            "file-chart-column-increasing",
            "file-chart-line",
            "file-chart-pie",
            "file-check",
            "file-check-2",
            "file-check-corner",
            "file-clock",
            "file-code",
            "file-code-2",
            "file-code-corner",
            "file-cog",
            "file-cog-2",
            "file-database",
            "file-diff",
            "file-digit",
            "file-down",
            "file-edit",
            "file-exclamation-point",
            "file-headphone",
            "file-heart",
            "file-image",
            "file-input",
            "file-json",
            "file-json-2",
            "file-key",
            "file-key-2",
            "file-line-chart",
            "file-lock",
            "file-lock-2",
            "file-minus",
            "file-minus-2",
            "file-minus-corner",
            "file-music",
            "file-output",
            "file-pen",
            "file-pen-line",
            "file-pie-chart",
            "file-play",
            "file-plus",
            "file-plus-2",
            "file-plus-corner",
            "file-question",
            "file-question-mark",
            "file-scan",
            "file-search",
            "file-search-2",
            "file-search-corner",
            "file-signal",
            "file-signature",
            "file-sliders",
            "file-spreadsheet",
            "file-stack",
            "file-symlink",
            "file-terminal",
            "file-text",
            "file-type",
            "file-type-2",
            "file-type-corner",
            "file-up",
            "file-user",
            "file-video",
            "file-video-2",
            "file-video-camera",
            "file-volume",
            "file-volume-2",
            "file-warning",
            "file-x",
            "file-x-2",
            "file-x-corner",
            "files",
            "film",
            "filter",
            "filter-x",
            "fingerprint",
            "fingerprint-pattern",
            "fire-extinguisher",
            "fish",
            "fish-off",
            "fish-symbol",
            "fishing-hook",
            "fishing-rod",
            "flag",
            "flag-off",
            "flag-triangle-left",
            "flag-triangle-right",
            "flame",
            "flame-kindling",
            "flashlight",
            "flashlight-off",
            "flask-conical",
            "flask-conical-off",
            "flask-round",
            "flip-horizontal",
            "flip-horizontal-2",
            "flip-vertical",
            "flip-vertical-2",
            "flower",
            "flower-2",
            "focus",
            "fold-horizontal",
            "fold-vertical",
            "folder",
            "folder-archive",
            "folder-bookmark",
            "folder-check",
            "folder-clock",
            "folder-closed",
            "folder-code",
            "folder-cog",
            "folder-cog-2",
            "folder-dot",
            "folder-down",
            "folder-edit",
            "folder-git",
            "folder-git-2",
            "folder-heart",
            "folder-input",
            "folder-kanban",
            "folder-key",
            "folder-lock",
            "folder-minus",
            "folder-open",
            "folder-open-dot",
            "folder-output",
            "folder-pen",
            "folder-plus",
            "folder-root",
            "folder-search",
            "folder-search-2",
            "folder-symlink",
            "folder-sync",
            "folder-tree",
            "folder-up",
            "folder-user",
            "folder-users",
            "folder-x",
            "folders",
            "footprints",
            "fork-knife",
            "fork-knife-crossed",
            "forklift",
            "form",
            "form-input",
            "forward",
            "frame",
            "frown",
            "fuel",
            "fullscreen",
            "function-square",
            "funnel",
            "funnel-plus",
            "funnel-x",
            "galaxy",
            "gallery-horizontal",
            "gallery-horizontal-end",
            "gallery-thumbnails",
            "gallery-vertical",
            "gallery-vertical-end",
            "gamepad",
            "gamepad-2",
            "gamepad-directional",
            "gantt-chart",
            "gantt-chart-square",
            "gap-horizontal",
            "gap-vertical",
            "gauge",
            "gauge-circle",
            "gavel",
            "gem",
            "georgian-lari",
            "germ",
            "germ-off",
            "ghost",
            "gift",
            "git-branch",
            "git-branch-minus",
            "git-branch-plus",
            "git-commit",
            "git-commit-horizontal",
            "git-commit-vertical",
            "git-compare",
            "git-compare-arrows",
            "git-fork",
            "git-graph",
            "git-merge",
            "git-merge-conflict",
            "git-pull-request",
            "git-pull-request-arrow",
            "git-pull-request-closed",
            "git-pull-request-create",
            "git-pull-request-create-arrow",
            "git-pull-request-draft",
            "glass-water",
            "glasses",
            "globe",
            "globe-2",
            "globe-check",
            "globe-code",
            "globe-lock",
            "globe-off",
            "globe-x",
            "goal",
            "gpu",
            "grab",
            "graduation-cap",
            "grape",
            "graph-hub",
            "graph-star",
            "grid",
            "grid-2-x-2",
            "grid-2-x-2-check",
            "grid-2-x-2-plus",
            "grid-2-x-2-x",
            "grid-2x2",
            "grid-2x2-check",
            "grid-2x2-plus",
            "grid-2x2-x",
            "grid-3-x-3",
            "grid-3x2",
            "grid-3x3",
            "grip",
            "grip-horizontal",
            "grip-vertical",
            "groceries",
            "group",
            "guitar",
            "guitar-electric",
            "half-note",
            "ham",
            "hamburger",
            "hammer",
            "hand",
            "hand-coins",
            "hand-fist",
            "hand-grab",
            "hand-heart",
            "hand-helping",
            "hand-metal",
            "hand-platter",
            "handbag",
            "handle-bottom-right",
            "handshake",
            "hard-drive",
            "hard-drive-download",
            "hard-drive-upload",
            "hard-hat",
            "hash",
            "hat-glasses",
            "haze",
            "hd",
            "hdmi-port",
            "heading",
            "heading-1",
            "heading-2",
            "heading-3",
            "heading-4",
            "heading-5",
            "heading-6",
            "headphone-off",
            "headphones",
            "headset",
            "heart",
            "heart-crack",
            "heart-handshake",
            "heart-minus",
            "heart-off",
            "heart-plus",
            "heart-pulse",
            "heart-x",
            "heater",
            "helicopter",
            "help-circle",
            "helping-hand",
            "hexagon",
            "highlighter",
            "hiking-stick",
            "history",
            "hockey-stick",
            "home",
            "hop",
            "hop-off",
            "hospital",
            "hot-air-balloon",
            "hotel",
            "hourglass",
            "hourglass-cog",
            "house",
            "house-cog",
            "house-heart",
            "house-plug",
            "house-plus",
            "house-wifi",
            "houses",
            "ice-cream",
            "ice-cream-2",
            "ice-cream-bowl",
            "ice-cream-cone",
            "id-card",
            "id-card-lanyard",
            "image",
            "image-down",
            "image-minus",
            "image-off",
            "image-play",
            "image-plus",
            "image-up",
            "image-upscale",
            "images",
            "import",
            "inbox",
            "indent",
            "indent-decrease",
            "indent-increase",
            "indian-rupee",
            "infinity",
            "info",
            "inspect",
            "inspection-panel",
            "italic",
            "iteration-ccw",
            "iteration-cw",
            "iv-bag",
            "japanese-yen",
            "joystick",
            "kanban",
            "kanban-square",
            "kanban-square-dashed",
            "kayak",
            "kazakh-tenge",
            "key",
            "key-round",
            "key-square",
            "keyboard",
            "keyboard-music",
            "keyboard-off",
            "lambda",
            "lamp",
            "lamp-ceiling",
            "lamp-desk",
            "lamp-floor",
            "lamp-wall-down",
            "lamp-wall-up",
            "land-plot",
            "landmark",
            "languages",
            "laptop",
            "laptop-2",
            "laptop-minimal",
            "laptop-minimal-check",
            "laser",
            "lasso",
            "lasso-select",
            "laugh",
            "layer-arrow-down",
            "layer-arrow-up",
            "layers",
            "layers-2",
            "layers-3",
            "layers-arrow-down",
            "layers-arrow-up",
            "layers-minus",
            "layers-plus",
            "layout",
            "layout-arrow-down",
            "layout-arrow-right",
            "layout-dashboard",
            "layout-freeform",
            "layout-grid",
            "layout-grid-circles",
            "layout-list",
            "layout-panel-left",
            "layout-panel-top",
            "layout-template",
            "leaf",
            "leafy-green",
            "lectern",
            "lens",
            "lens-concave",
            "lens-convex",
            "letter-a",
            "letter-b",
            "letter-c",
            "letter-d",
            "letter-e",
            "letter-f",
            "letter-g",
            "letter-h",
            "letter-i",
            "letter-j",
            "letter-k",
            "letter-l",
            "letter-m",
            "letter-n",
            "letter-o",
            "letter-p",
            "letter-q",
            "letter-r",
            "letter-s",
            "letter-spacing",
            "letter-t",
            "letter-text",
            "letter-u",
            "letter-v",
            "letter-w",
            "letter-x",
            "letter-y",
            "letter-z",
            "letters",
            "library",
            "library-big",
            "library-square",
            "life-buoy",
            "ligature",
            "lightbulb",
            "lightbulb-off",
            "lighthouse",
            "line-chart",
            "line-dot-bottom-vertical",
            "line-dot-left-horizontal",
            "line-dot-right-horizontal",
            "line-dot-top-vertical",
            "line-height",
            "line-squiggle",
            "line-style",
            "link",
            "link-2",
            "link-2-off",
            "list",
            "list-check",
            "list-checks",
            "list-chevrons-down-up",
            "list-chevrons-up-down",
            "list-clock",
            "list-collapse",
            "list-end",
            "list-filter",
            "list-filter-plus",
            "list-indent-decrease",
            "list-indent-increase",
            "list-minus",
            "list-music",
            "list-ordered",
            "list-plus",
            "list-restart",
            "list-sort-ascending",
            "list-sort-descending",
            "list-start",
            "list-todo",
            "list-tree",
            "list-video",
            "list-x",
            "loader",
            "loader-2",
            "loader-circle",
            "loader-pinwheel",
            "locate",
            "locate-fixed",
            "locate-off",
            "location-edit",
            "lock",
            "lock-keyhole",
            "lock-keyhole-open",
            "lock-open",
            "log-in",
            "log-out",
            "logs",
            "lollipop",
            "luggage",
            "m-square",
            "magnet",
            "mail",
            "mail-badge",
            "mail-check",
            "mail-clock",
            "mail-dot",
            "mail-minus",
            "mail-open",
            "mail-pen",
            "mail-plus",
            "mail-question",
            "mail-question-mark",
            "mail-search",
            "mail-warning",
            "mail-x",
            "mailbox",
            "mails",
            "map",
            "map-minus",
            "map-pin",
            "map-pin-check",
            "map-pin-check-inside",
            "map-pin-house",
            "map-pin-minus",
            "map-pin-minus-inside",
            "map-pin-off",
            "map-pin-pen",
            "map-pin-plus",
            "map-pin-plus-inside",
            "map-pin-search",
            "map-pin-x",
            "map-pin-x-inside",
            "map-pinned",
            "map-plus",
            "margin",
            "margin-bottom",
            "margin-horizontal",
            "margin-left",
            "margin-right",
            "margin-top",
            "margin-vertical",
            "mars",
            "mars-stroke",
            "martini",
            "maximize",
            "maximize-2",
            "medal",
            "megaphone",
            "megaphone-off",
            "meh",
            "memory-stick",
            "menu",
            "menu-square",
            "merge",
            "message-circle",
            "message-circle-check",
            "message-circle-code",
            "message-circle-dashed",
            "message-circle-dashed-check",
            "message-circle-heart",
            "message-circle-more",
            "message-circle-off",
            "message-circle-plus",
            "message-circle-question",
            "message-circle-question-mark",
            "message-circle-reply",
            "message-circle-text",
            "message-circle-warning",
            "message-circle-x",
            "message-square",
            "message-square-check",
            "message-square-code",
            "message-square-dashed",
            "message-square-diff",
            "message-square-dot",
            "message-square-heart",
            "message-square-lock",
            "message-square-more",
            "message-square-off",
            "message-square-plus",
            "message-square-quote",
            "message-square-reply",
            "message-square-share",
            "message-square-text",
            "message-square-warning",
            "message-square-x",
            "messages-circle",
            "messages-square",
            "metronome",
            "mic",
            "mic-2",
            "mic-audio-lines",
            "mic-off",
            "mic-signal",
            "mic-vocal",
            "microchip",
            "microscope",
            "microwave",
            "midi-port",
            "milestone",
            "milk",
            "milk-off",
            "minimize",
            "minimize-2",
            "minus",
            "minus-circle",
            "minus-square",
            "mirror-rectangular",
            "mirror-round",
            "monitor",
            "monitor-activity",
            "monitor-check",
            "monitor-cloud",
            "monitor-cog",
            "monitor-dot",
            "monitor-down",
            "monitor-off",
            "monitor-pause",
            "monitor-pc",
            "monitor-play",
            "monitor-smartphone",
            "monitor-speaker",
            "monitor-stop",
            "monitor-up",
            "monitor-x",
            "moon",
            "moon-star",
            "mop",
            "mop-sparkles",
            "more-horizontal",
            "more-vertical",
            "mosque",
            "motorbike",
            "mountain",
            "mountain-snow",
            "mouse",
            "mouse-left",
            "mouse-off",
            "mouse-pointer",
            "mouse-pointer-2",
            "mouse-pointer-2-off",
            "mouse-pointer-ban",
            "mouse-pointer-click",
            "mouse-pointer-square-dashed",
            "mouse-right",
            "mouth",
            "mouth-off",
            "move",
            "move-3-d",
            "move-3d",
            "move-diagonal",
            "move-diagonal-2",
            "move-down",
            "move-down-left",
            "move-down-right",
            "move-horizontal",
            "move-left",
            "move-right",
            "move-up",
            "move-up-left",
            "move-up-right",
            "move-vertical",
            "music",
            "music-2",
            "music-3",
            "music-4",
            "navigation",
            "navigation-2",
            "navigation-2-off",
            "navigation-off",
            "nepali-rupee",
            "network",
            "newspaper",
            "nfc",
            "non-binary",
            "notebook",
            "notebook-dot",
            "notebook-pen",
            "notebook-tabs",
            "notebook-text",
            "notepad-text",
            "notepad-text-dashed",
            "numbers",
            "nut",
            "nut-off",
            "octagon",
            "octagon-alert",
            "octagon-minus",
            "octagon-pause",
            "octagon-x",
            "omega",
            "option",
            "orbit",
            "origami",
            "outdent",
            "package",
            "package-2",
            "package-check",
            "package-minus",
            "package-open",
            "package-plus",
            "package-search",
            "package-x",
            "paint-bucket",
            "paint-roller",
            "paintbrush",
            "paintbrush-2",
            "paintbrush-vertical",
            "palette",
            "palmtree",
            "panda",
            "panel-bottom",
            "panel-bottom-close",
            "panel-bottom-dashed",
            "panel-bottom-inactive",
            "panel-bottom-open",
            "panel-left",
            "panel-left-close",
            "panel-left-dashed",
            "panel-left-inactive",
            "panel-left-open",
            "panel-left-right-dashed",
            "panel-right",
            "panel-right-close",
            "panel-right-dashed",
            "panel-right-inactive",
            "panel-right-open",
            "panel-top",
            "panel-top-bottom-dashed",
            "panel-top-close",
            "panel-top-dashed",
            "panel-top-inactive",
            "panel-top-open",
            "panels-left-bottom",
            "panels-left-right",
            "panels-right-bottom",
            "panels-top-bottom",
            "panels-top-left",
            "paper-bag",
            "paperclip",
            "parasol",
            "parentheses",
            "park",
            "parking-circle",
            "parking-circle-off",
            "parking-meter",
            "parking-square",
            "parking-square-off",
            "party-popper",
            "pause",
            "pause-circle",
            "pause-octagon",
            "paw-print",
            "pc-case",
            "pen",
            "pen-box",
            "pen-line",
            "pen-off",
            "pen-square",
            "pen-tool",
            "pencil",
            "pencil-line",
            "pencil-off",
            "pencil-ruler",
            "pencil-sparkles",
            "pentagon",
            "percent",
            "percent-circle",
            "percent-diamond",
            "percent-square",
            "perfume-bottle",
            "person-standing",
            "phi",
            "philippine-peso",
            "phone",
            "phone-call",
            "phone-forwarded",
            "phone-incoming",
            "phone-log",
            "phone-missed",
            "phone-off",
            "phone-outgoing",
            "pi",
            "pi-square",
            "piano",
            "pickaxe",
            "picture-in-picture",
            "picture-in-picture-2",
            "pie-chart",
            "piggy-bank",
            "pilcrow",
            "pilcrow-left",
            "pilcrow-right",
            "pilcrow-square",
            "pill",
            "pill-bottle",
            "pin",
            "pin-off",
            "pipette",
            "pizza",
            "plane",
            "plane-landing",
            "plane-takeoff",
            "plant-pot",
            "play",
            "play-circle",
            "play-off",
            "play-square",
            "playing-card",
            "playing-cards",
            "playing-cards-fan",
            "plug",
            "plug-2",
            "plug-zap",
            "plug-zap-2",
            "plus",
            "plus-circle",
            "plus-square",
            "pocket-knife",
            "podcast",
            "podium",
            "pointer",
            "pointer-off",
            "popcorn",
            "popsicle",
            "pound-sterling",
            "power",
            "power-circle",
            "power-off",
            "power-square",
            "presentation",
            "printer",
            "printer-3d",
            "printer-check",
            "printer-x",
            "projector",
            "proportions",
            "puzzle",
            "pyramid",
            "qr-code",
            "quote",
            "rabbit",
            "radar",
            "radiation",
            "radical",
            "radio",
            "radio-off",
            "radio-receiver",
            "radio-tower",
            "radius",
            "rainbow",
            "rat",
            "ratio",
            "receipt",
            "receipt-cent",
            "receipt-euro",
            "receipt-indian-rupee",
            "receipt-japanese-yen",
            "receipt-pound-sterling",
            "receipt-russian-ruble",
            "receipt-swiss-franc",
            "receipt-text",
            "receipt-turkish-lira",
            "rectangle-circle",
            "rectangle-ellipsis",
            "rectangle-goggles",
            "rectangle-horizontal",
            "rectangle-vertical",
            "recycle",
            "redo",
            "redo-2",
            "redo-dot",
            "refresh-ccw",
            "refresh-ccw-dot",
            "refresh-cw",
            "refresh-cw-off",
            "refrigerator",
            "regex",
            "remove-formatting",
            "repeat",
            "repeat-1",
            "repeat-2",
            "repeat-off",
            "replace",
            "replace-all",
            "reply",
            "reply-all",
            "rewind",
            "ribbon",
            "road",
            "robot-arm",
            "robot-vacuum",
            "rocket",
            "rocking-chair",
            "roller-coaster",
            "rose",
            "rotate-3-d",
            "rotate-3d",
            "rotate-ccw",
            "rotate-ccw-clock",
            "rotate-ccw-key",
            "rotate-ccw-square",
            "rotate-cw",
            "rotate-cw-clock",
            "rotate-cw-fading-clock",
            "rotate-cw-square",
            "route",
            "route-off",
            "router",
            "rows",
            "rows-2",
            "rows-3",
            "rows-4",
            "rss",
            "rugby-ball",
            "ruler",
            "ruler-dimension-line",
            "russian-ruble",
            "sailboat",
            "salad",
            "sandwich",
            "satellite",
            "satellite-dish",
            "saudi-riyal",
            "save",
            "save-all",
            "save-check",
            "save-off",
            "save-pen",
            "save-plus",
            "scale",
            "scale-3-d",
            "scale-3d",
            "scaling",
            "scan",
            "scan-barcode",
            "scan-box",
            "scan-eye",
            "scan-face",
            "scan-heart",
            "scan-line",
            "scan-qr-code",
            "scan-search",
            "scan-square",
            "scan-text",
            "scatter-chart",
            "school",
            "school-2",
            "scissors",
            "scissors-line-dashed",
            "scissors-square",
            "scissors-square-dashed-bottom",
            "scooter",
            "scratch-blocks",
            "screen-share",
            "screen-share-off",
            "screw",
            "scroll",
            "scroll-text",
            "search",
            "search-alert",
            "search-check",
            "search-code",
            "search-slash",
            "search-x",
            "section",
            "send",
            "send-horizonal",
            "send-horizontal",
            "send-to-back",
            "separator-horizontal",
            "separator-vertical",
            "server",
            "server-cog",
            "server-crash",
            "server-nas",
            "server-off",
            "server-plus",
            "settings",
            "settings-2",
            "shapes",
            "share",
            "share-2",
            "sheet",
            "shell",
            "shelving-unit",
            "shield",
            "shield-alert",
            "shield-ban",
            "shield-check",
            "shield-close",
            "shield-cog",
            "shield-cog-corner",
            "shield-ellipsis",
            "shield-half",
            "shield-house",
            "shield-keyhole",
            "shield-lock",
            "shield-minus",
            "shield-off",
            "shield-plus",
            "shield-question",
            "shield-question-mark",
            "shield-user",
            "shield-x",
            "ship",
            "ship-cargo",
            "ship-wheel",
            "shirt",
            "shopping-bag",
            "shopping-basket",
            "shopping-cart",
            "shopping-cart-minus",
            "shopping-cart-plus",
            "shovel",
            "shower-head",
            "shredder",
            "shrimp",
            "shrimp-off",
            "shrink",
            "shrub",
            "shuffle",
            "sidebar",
            "sidebar-close",
            "sidebar-open",
            "sigma",
            "sigma-square",
            "signal",
            "signal-high",
            "signal-low",
            "signal-medium",
            "signal-zero",
            "signature",
            "signpost",
            "signpost-big",
            "siren",
            "siren-off",
            "sixteenth-notes",
            "skip-back",
            "skip-forward",
            "skull",
            "slash",
            "slash-square",
            "slice",
            "sliders",
            "sliders-horizontal",
            "sliders-vertical",
            "smartphone",
            "smartphone-charging",
            "smartphone-nfc",
            "smartphone-user-round",
            "smile",
            "smile-plus",
            "snail",
            "snowflake",
            "soap-dispenser-droplet",
            "sofa",
            "solar-panel",
            "sort-asc",
            "sort-desc",
            "soup",
            "space",
            "spade",
            "sparkle",
            "sparkles",
            "speaker",
            "speech",
            "spell-check",
            "spell-check-2",
            "spline",
            "spline-pointer",
            "split",
            "split-square-horizontal",
            "split-square-vertical",
            "spool",
            "sport-shoe",
            "spotlight",
            "spray-can",
            "sprout",
            "square",
            "square-activity",
            "square-arrow-down",
            "square-arrow-down-left",
            "square-arrow-down-right",
            "square-arrow-left",
            "square-arrow-out-down-left",
            "square-arrow-out-down-right",
            "square-arrow-out-up-left",
            "square-arrow-out-up-right",
            "square-arrow-right",
            "square-arrow-right-enter",
            "square-arrow-right-exit",
            "square-arrow-up",
            "square-arrow-up-left",
            "square-arrow-up-right",
            "square-asterisk",
            "square-bookmark",
            "square-bottom-dashed-scissors",
            "square-centerline-dashed-horizontal",
            "square-centerline-dashed-vertical",
            "square-chart-gantt",
            "square-check",
            "square-check-big",
            "square-chevron-down",
            "square-chevron-left",
            "square-chevron-right",
            "square-chevron-up",
            "square-chevrons-down",
            "square-chevrons-left",
            "square-chevrons-right",
            "square-chevrons-up",
            "square-code",
            "square-dashed",
            "square-dashed-bottom",
            "square-dashed-bottom-code",
            "square-dashed-kanban",
            "square-dashed-mouse-pointer",
            "square-dashed-plus",
            "square-dashed-text",
            "square-dashed-top-solid",
            "square-dashed-x",
            "square-dashed-x-corner",
            "square-dimensions",
            "square-divide",
            "square-dot",
            "square-equal",
            "square-exclamation-point",
            "square-function",
            "square-gantt-chart",
            "square-kanban",
            "square-kanban-plus",
            "square-letter-a",
            "square-letter-b",
            "square-letter-c",
            "square-letter-d",
            "square-letter-e",
            "square-letter-f",
            "square-letter-g",
            "square-letter-h",
            "square-letter-i",
            "square-letter-j",
            "square-letter-k",
            "square-letter-l",
            "square-letter-m",
            "square-letter-n",
            "square-letter-o",
            "square-letter-p",
            "square-letter-p-off",
            "square-letter-q",
            "square-letter-r",
            "square-letter-s",
            "square-letter-t",
            "square-letter-u",
            "square-letter-v",
            "square-letter-w",
            "square-letter-x",
            "square-letter-y",
            "square-letter-z",
            "square-library",
            "square-m",
            "square-menu",
            "square-minus",
            "square-mouse-pointer",
            "square-off",
            "square-padding",
            "square-padding-bottom",
            "square-padding-horizontal",
            "square-padding-left",
            "square-padding-right",
            "square-padding-top",
            "square-padding-vertical",
            "square-parking",
            "square-parking-off",
            "square-pause",
            "square-pen",
            "square-percent",
            "square-pi",
            "square-pilcrow",
            "square-play",
            "square-plus",
            "square-power",
            "square-radical",
            "square-round-corner",
            "square-scissors",
            "square-sigma",
            "square-slash",
            "square-sparkles",
            "square-split-horizontal",
            "square-split-vertical",
            "square-square",
            "square-stack",
            "square-star",
            "square-stop",
            "square-terminal",
            "square-text",
            "square-user",
            "square-user-round",
            "square-x",
            "squares-exclude",
            "squares-intersect",
            "squares-subtract",
            "squares-unite",
            "squircle",
            "squircle-dashed",
            "squirrel",
            "stamp",
            "star",
            "star-check",
            "star-half",
            "star-minus",
            "star-off",
            "star-plus",
            "star-x",
            "stars",
            "step-back",
            "step-forward",
            "stethoscope",
            "sticker",
            "sticky-note",
            "sticky-note-check",
            "sticky-note-minus",
            "sticky-note-off",
            "sticky-note-plus",
            "sticky-note-x",
            "sticky-notes",
            "stone",
            "stop-circle",
            "store",
            "stretch-horizontal",
            "stretch-vertical",
            "strikethrough",
            "subscript",
            "subtitles",
            "summary",
            "sun",
            "sun-dim",
            "sun-medium",
            "sun-moon",
            "sun-snow",
            "sunrise",
            "sunset",
            "superscript",
            "swatch-book",
            "swiss-franc",
            "switch-camera",
            "sword",
            "swords",
            "syringe",
            "table",
            "table-2",
            "table-cells-merge",
            "table-cells-split",
            "table-columns-split",
            "table-config",
            "table-of-contents",
            "table-properties",
            "table-rows-split",
            "tablet",
            "tablet-smartphone",
            "tablets",
            "tag",
            "tag-plus",
            "tag-x",
            "tags",
            "tally-1",
            "tally-2",
            "tally-3",
            "tally-4",
            "tally-5",
            "tangent",
            "target",
            "telescope",
            "tent",
            "tent-tree",
            "terminal",
            "terminal-square",
            "test-tube",
            "test-tube-2",
            "test-tube-diagonal",
            "test-tubes",
            "text",
            "text-align-center",
            "text-align-end",
            "text-align-justify",
            "text-align-justify-center",
            "text-align-justify-end",
            "text-align-justify-start",
            "text-align-start",
            "text-cursor",
            "text-cursor-input",
            "text-initial",
            "text-quote",
            "text-search",
            "text-select",
            "text-selection",
            "text-wrap",
            "theater",
            "thermometer",
            "thermometer-snowflake",
            "thermometer-sun",
            "thumbs-down",
            "thumbs-up",
            "tic-tac-toe",
            "ticket",
            "ticket-check",
            "ticket-minus",
            "ticket-percent",
            "ticket-plus",
            "ticket-slash",
            "ticket-x",
            "tickets",
            "tickets-plane",
            "timeline",
            "timer",
            "timer-off",
            "timer-reset",
            "toggle-left",
            "toggle-right",
            "toilet",
            "tombstone",
            "tool-case",
            "toolbox",
            "toothbrush",
            "toothbrush-sparkles",
            "tornado",
            "torus",
            "touchpad",
            "touchpad-off",
            "towel-rack",
            "tower-control",
            "toy-brick",
            "tractor",
            "traffic-cone",
            "trailer",
            "train",
            "train-front",
            "train-front-tunnel",
            "train-track",
            "tram-front",
            "transgender",
            "trash",
            "trash-2",
            "trash-off",
            "tree-deciduous",
            "tree-palm",
            "tree-pine",
            "trees",
            "trending-down",
            "trending-up",
            "trending-up-down",
            "triangle",
            "triangle-alert",
            "triangle-dashed",
            "triangle-right",
            "triangles-centerline-dashed-horizontal",
            "triangles-centerline-dashed-vertical",
            "trophy",
            "truck",
            "truck-electric",
            "tube-lotion",
            "turkish-lira",
            "turntable",
            "turtle",
            "tv",
            "tv-2",
            "tv-minimal",
            "tv-minimal-play",
            "type",
            "type-outline",
            "umbrella",
            "umbrella-off",
            "underline",
            "undo",
            "undo-2",
            "undo-dot",
            "unfold-horizontal",
            "unfold-vertical",
            "ungroup",
            "university",
            "unlink",
            "unlink-2",
            "unlock",
            "unlock-keyhole",
            "unplug",
            "upload",
            "upload-cloud",
            "usb",
            "usb-c-port",
            "user",
            "user-2",
            "user-check",
            "user-check-2",
            "user-circle",
            "user-circle-2",
            "user-cog",
            "user-cog-2",
            "user-group",
            "user-key",
            "user-lock",
            "user-minus",
            "user-minus-2",
            "user-pen",
            "user-plus",
            "user-plus-2",
            "user-round",
            "user-round-arrow-left",
            "user-round-check",
            "user-round-cog",
            "user-round-group",
            "user-round-key",
            "user-round-minus",
            "user-round-pen",
            "user-round-plus",
            "user-round-search",
            "user-round-shield",
            "user-round-star",
            "user-round-x",
            "user-search",
            "user-shield",
            "user-square",
            "user-square-2",
            "user-star",
            "user-x",
            "user-x-2",
            "users",
            "users-2",
            "users-collaboration",
            "users-round",
            "users-round-collaboration",
            "utensils",
            "utensils-crossed",
            "utility-pole",
            "van",
            "variable",
            "vault",
            "vector-polygon",
            "vector-square",
            "vegan",
            "venetian-mask",
            "venus",
            "venus-and-mars",
            "verified",
            "vibrate",
            "vibrate-off",
            "video",
            "video-off",
            "videotape",
            "view",
            "virus",
            "virus-off",
            "voicemail",
            "volleyball",
            "volume",
            "volume-1",
            "volume-2",
            "volume-off",
            "volume-x",
            "vote",
            "wallet",
            "wallet-2",
            "wallet-cards",
            "wallet-minimal",
            "wallpaper",
            "wand",
            "wand-2",
            "wand-sparkles",
            "warehouse",
            "washing-machine",
            "watch",
            "waves",
            "waves-arrow-down",
            "waves-arrow-up",
            "waves-horizontal",
            "waves-ladder",
            "waves-vertical",
            "waypoints",
            "webcam",
            "webcam-off",
            "webhook",
            "webhook-off",
            "weight",
            "weight-tilde",
            "wheat",
            "wheat-off",
            "whistle",
            "whole-word",
            "wifi",
            "wifi-cog",
            "wifi-high",
            "wifi-lock",
            "wifi-low",
            "wifi-off",
            "wifi-pen",
            "wifi-sync",
            "wifi-zero",
            "wind",
            "wind-arrow-down",
            "wind-arrow-up",
            "wine",
            "wine-off",
            "workflow",
            "worm",
            "wrap-text",
            "wrench",
            "wrench-off",
            "x",
            "x-circle",
            "x-line-top",
            "x-octagon",
            "x-square",
            "zap",
            "zap-off",
            "zodiac-aquarius",
            "zodiac-aries",
            "zodiac-cancer",
            "zodiac-capricorn",
            "zodiac-gemini",
            "zodiac-leo",
            "zodiac-libra",
            "zodiac-ophiuchus",
            "zodiac-pisces",
            "zodiac-sagittarius",
            "zodiac-scorpio",
            "zodiac-taurus",
            "zodiac-virgo",
            "zoom-in",
            "zoom-out",
        };

        /// <summary>全部图标名（CSS 里的 icon- 后面的名字，例如 "cpu" / "chart-line"）。</summary>
        public static IReadOnlyList<string> Names { get { return AllNames; } }

        /// <summary>按名字取字形；名字可带或不带 "icon-" 前缀，未知返回空串。</summary>
        public static string Get(string name)
        {
            string key = (name ?? "").Trim().ToLowerInvariant();
            if (key.StartsWith("icon-", StringComparison.Ordinal)) key = key.Substring(5);
            string glyph;
            return Glyphs.TryGetValue(key, out glyph) ? glyph : "";
        }

        /// <summary>按名字取字形；未知返回 false。</summary>
        public static bool TryGet(string name, out string glyph)
        {
            string key = (name ?? "").Trim().ToLowerInvariant();
            if (key.StartsWith("icon-", StringComparison.Ordinal)) key = key.Substring(5);
            return Glyphs.TryGetValue(key, out glyph);
        }
    }
}
