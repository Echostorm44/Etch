namespace Etch.Text.Unicode.Minimal;

/// <summary>
/// Unicode Bidi_Class property values per UAX #9.
/// Only values needed for the core algorithm are included.
/// </summary>
#pragma warning disable CA1028 // Enum underlying type is byte for cache efficiency
public enum BidiClass : byte
{
    /// <summary>Left-to-Right — strong LTR (Latin, CJK, etc.)</summary>
    L = 0,
    /// <summary>Right-to-Left — strong RTL (Hebrew, etc.)</summary>
    R = 1,
    /// <summary>Arabic Letter — strong RTL (Arabic, Syriac, etc.)</summary>
    AL = 2,
    /// <summary>European Number — weak EN</summary>
    EN = 3,
    /// <summary>European Number Separator — weak ES</summary>
    ES = 4,
    /// <summary>European Number Terminator — weak ET</summary>
    ET = 5,
    /// <summary>Arabic Number — weak AN</summary>
    AN = 6,
    /// <summary>Common Number Separator — weak CS</summary>
    CS = 7,
    /// <summary>Nonspacing Mark — weak NSM</summary>
    NSM = 8,
    /// <summary>Boundary Neutral — weak BN</summary>
    BN = 9,
    /// <summary>Paragraph Separator — neutral B</summary>
    B = 10,
    /// <summary>Segment Separator — neutral S</summary>
    S = 11,
    /// <summary>Whitespace — neutral WS</summary>
    WS = 12,
    /// <summary>Other Neutral — neutral ON</summary>
    ON = 13,
    /// <summary>Left-to-Right Embedding — explicit LRE</summary>
    LRE = 14,
    /// <summary>Left-to-Right Override — explicit LRO</summary>
    LRO = 15,
    /// <summary>Right-to-Left Embedding — explicit RLE</summary>
    RLE = 16,
    /// <summary>Right-to-Left Override — explicit RLO</summary>
    RLO = 17,
    /// <summary>Pop Directional Format — explicit PDF</summary>
    PDF = 18,
    /// <summary>Left-to-Right Isolate — explicit LRI</summary>
    LRI = 19,
    /// <summary>Right-to-Left Isolate — explicit RLI</summary>
    RLI = 20,
    /// <summary>First Strong Isolate — explicit FSI</summary>
    FSI = 21,
    /// <summary>Pop Directional Isolate — explicit PDI</summary>
    PDI = 22,
}

/// <summary>
/// Lookup table for Bidi_Class. Covers ASCII, Hebrew, Arabic, and common
/// punctuation. Unknown code points default to ON (safe neutral fallback).
/// </summary>
public static class BidiClassTable
{
    /// <summary>Return the Bidi_Class for a Unicode code point.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static BidiClass Get(char c)
    {
        int cp = c;
        if (cp < 128)
            return AsciiTable[cp];
        if (cp is >= 0x0590 and < 0x0600)
            return HebrewTable[cp - 0x0590];
        if (cp is >= 0x0600 and < 0x0700)
            return ArabicTable[cp - 0x0600];
        if (cp is >= 0x200E and <= 0x2069)
            return FormatTable[cp - 0x200E];
        // Combining Diacritical Marks (U+0300–036F) are all NSM (UAX #9 W1) — they
        // attach to base letters of any script, including in RTL runs, and were
        // defaulting to ON. (Other scripts' marks are mixed L/NSM and are left to
        // a full class table; these are the common, corpus-exercised ones.)
        if (cp is >= 0x0300 and <= 0x036F)
            return BidiClass.NSM;
        // Everything else → ON (safe neutral fallback)
        return BidiClass.ON;
    }

    // ------------------------------------------------------------------
    // ASCII (U+0000..U+007F)
    // ------------------------------------------------------------------
    private static readonly BidiClass[] AsciiTable = new BidiClass[128]
    {
        // 0x00-0x07
        BidiClass.BN, BidiClass.BN, BidiClass.BN, BidiClass.BN,
        BidiClass.BN, BidiClass.BN, BidiClass.BN, BidiClass.BN,
        // 0x08-0x0F
        BidiClass.BN, BidiClass.S,  BidiClass.B,  BidiClass.S,
        BidiClass.S,  BidiClass.B,  BidiClass.BN, BidiClass.BN,
        // 0x10-0x17
        BidiClass.BN, BidiClass.BN, BidiClass.BN, BidiClass.BN,
        BidiClass.BN, BidiClass.BN, BidiClass.BN, BidiClass.BN,
        // 0x18-0x1F
        BidiClass.BN, BidiClass.BN, BidiClass.BN, BidiClass.B,
        BidiClass.BN, BidiClass.BN, BidiClass.BN, BidiClass.BN,
        // 0x20-0x27   space ! " # $ % & '
        BidiClass.WS, BidiClass.ON, BidiClass.ON, BidiClass.ET,
        BidiClass.ET, BidiClass.ET, BidiClass.ON, BidiClass.ON,
        // 0x28-0x2F   ( ) * + , - . /
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ES,
        BidiClass.CS, BidiClass.ES, BidiClass.CS, BidiClass.CS,
        // 0x30-0x37   0-7
        BidiClass.EN, BidiClass.EN, BidiClass.EN, BidiClass.EN,
        BidiClass.EN, BidiClass.EN, BidiClass.EN, BidiClass.EN,
        // 0x38-0x3F   8 9 : ; < = > ?
        BidiClass.EN, BidiClass.EN, BidiClass.CS, BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        // 0x40-0x47   @ A B C D E F G
        BidiClass.ON, BidiClass.L,  BidiClass.L,  BidiClass.L,
        BidiClass.L,  BidiClass.L,  BidiClass.L,  BidiClass.L,
        // 0x48-0x4F   H I J K L M N O
        BidiClass.L,  BidiClass.L,  BidiClass.L,  BidiClass.L,
        BidiClass.L,  BidiClass.L,  BidiClass.L,  BidiClass.L,
        // 0x50-0x57   P Q R S T U V W
        BidiClass.L,  BidiClass.L,  BidiClass.L,  BidiClass.L,
        BidiClass.L,  BidiClass.L,  BidiClass.L,  BidiClass.L,
        // 0x58-0x5F   X Y Z [ \ ] ^ _
        BidiClass.L,  BidiClass.L,  BidiClass.L,  BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        // 0x60-0x67   ` a b c d e f g
        BidiClass.ON, BidiClass.L,  BidiClass.L,  BidiClass.L,
        BidiClass.L,  BidiClass.L,  BidiClass.L,  BidiClass.L,
        // 0x68-0x6F   h i j k l m n o
        BidiClass.L,  BidiClass.L,  BidiClass.L,  BidiClass.L,
        BidiClass.L,  BidiClass.L,  BidiClass.L,  BidiClass.L,
        // 0x70-0x77   p q r s t u v w
        BidiClass.L,  BidiClass.L,  BidiClass.L,  BidiClass.L,
        BidiClass.L,  BidiClass.L,  BidiClass.L,  BidiClass.L,
        // 0x78-0x7F   x y z { | } ~ DEL
        BidiClass.L,  BidiClass.L,  BidiClass.L,  BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.BN,
    };

    // ------------------------------------------------------------------
    // Hebrew block  (U+0590..U+05FF)  — 112 code points
    // ------------------------------------------------------------------
    // 0=missing/unassigned, 1=R, 2=NSM, 3=ON, 4=BN
    private static readonly BidiClass[] HebrewTable = GenerateHebrewTable();

    private static BidiClass[] GenerateHebrewTable()
    {
        var t = new BidiClass[112];
        for (int i = 0; i < 112; i++)
            t[i] = BidiClass.ON;          // default

        // U+0590..U+0591  unassigned / marks
        t[0x00] = BidiClass.BN;
        t[0x01] = BidiClass.NSM;
        // U+0592..U+0595  marks
        for (int i = 0x02; i <= 0x05; i++) t[i] = BidiClass.NSM;
        // U+0596..U+059F  marks
        for (int i = 0x06; i <= 0x0F; i++) t[i] = BidiClass.NSM;
        // U+05A0..U+05A1  marks
        for (int i = 0x10; i <= 0x11; i++) t[i] = BidiClass.NSM;
        // U+05A2  mark
        t[0x12] = BidiClass.NSM;
        // U+05A3..U+05AF  marks
        for (int i = 0x13; i <= 0x1F; i++) t[i] = BidiClass.NSM;
        // U+05B0..U+05BD  marks
        for (int i = 0x20; i <= 0x2D; i++) t[i] = BidiClass.NSM;
        // U+05BE  ON (maqaf)
        t[0x2E] = BidiClass.ON;
        // U+05BF  mark
        t[0x2F] = BidiClass.NSM;
        // U+05C0  ON (paseq)
        t[0x30] = BidiClass.ON;
        // U+05C1..U+05C2  marks
        t[0x31] = BidiClass.NSM;
        t[0x32] = BidiClass.NSM;
        // U+05C3  ON (sof pasuq)
        t[0x33] = BidiClass.ON;
        // U+05C4..U+05C5  marks
        t[0x34] = BidiClass.NSM;
        t[0x35] = BidiClass.NSM;
        // U+05C6  ON (nun hafukha)
        t[0x36] = BidiClass.ON;
        // U+05C7  mark
        t[0x37] = BidiClass.NSM;
        // U+05C8..U+05CF  unassigned
        for (int i = 0x38; i <= 0x3F; i++) t[i] = BidiClass.BN;
        // U+05D0..U+05EA  Hebrew letters → R
        for (int i = 0x40; i <= 0x5A; i++) t[i] = BidiClass.R;
        // U+05EB..U+05EF  unassigned
        for (int i = 0x5B; i <= 0x5F; i++) t[i] = BidiClass.BN;
        // U+05F0..U+05F2  Yiddish digraphs → R
        for (int i = 0x60; i <= 0x62; i++) t[i] = BidiClass.R;
        // U+05F3..U+05F4  punctuation → ON
        t[0x63] = BidiClass.ON;
        t[0x64] = BidiClass.ON;
        // U+05F5..U+05FF  unassigned
        for (int i = 0x65; i < 112; i++) t[i] = BidiClass.BN;

        return t;
    }

    // ------------------------------------------------------------------
    // Arabic block  (U+0600..U+06FF)  — 256 code points
    // ------------------------------------------------------------------
    private static readonly BidiClass[] ArabicTable = GenerateArabicTable();

    private static BidiClass[] GenerateArabicTable()
    {
        var t = new BidiClass[256];
        for (int i = 0; i < 256; i++)
            t[i] = BidiClass.ON;          // default

        // U+0600..U+0605  Arabic format controls → AN (or AL for some)
        t[0x00] = BidiClass.AN; t[0x01] = BidiClass.AN;
        t[0x02] = BidiClass.AL; t[0x03] = BidiClass.AN;
        t[0x04] = BidiClass.AN; t[0x05] = BidiClass.AN;
        // U+0606..U+0608  Arabic math symbols → ON
        for (int i = 0x06; i <= 0x08; i++) t[i] = BidiClass.ON;
        // U+0609..U+060A  Arabic signs → ET
        t[0x09] = BidiClass.ET; t[0x0A] = BidiClass.ET;
        // U+060B  Afghan sign → ET
        t[0x0B] = BidiClass.ET;
        // U+060C  Arabic comma → CS
        t[0x0C] = BidiClass.CS;
        // U+060D  Arabic date separator → AL
        t[0x0D] = BidiClass.AL;
        // U+060E..U+060F  → ON
        t[0x0E] = BidiClass.ON; t[0x0F] = BidiClass.ON;

        // U+0610..U+061A  Arabic marks → NSM
        for (int i = 0x10; i <= 0x1A; i++) t[i] = BidiClass.NSM;
        // U+061B  Arabic semicolon → AL
        t[0x1B] = BidiClass.AL;
        // U+061C  ALM → AL
        t[0x1C] = BidiClass.AL;
        // U+061D  → AL
        t[0x1D] = BidiClass.AL;
        // U+061E..U+061F  → AL / ON
        t[0x1E] = BidiClass.AL; t[0x1F] = BidiClass.ON;

        // U+0620..U+063F  Arabic letters → AL
        for (int i = 0x20; i <= 0x3F; i++) t[i] = BidiClass.AL;
        // U+0640  Tatweel → AL
        t[0x40] = BidiClass.AL;
        // U+0641..U+064A  Arabic letters → AL
        for (int i = 0x41; i <= 0x4A; i++) t[i] = BidiClass.AL;
        // U+064B..U+065F  Arabic marks → NSM
        for (int i = 0x4B; i <= 0x5F; i++) t[i] = BidiClass.NSM;
        // U+0660..U+0669  Arabic-Indic digits → AN
        for (int i = 0x60; i <= 0x69; i++) t[i] = BidiClass.AN;
        // U+066A  Arabic percent → ET
        t[0x6A] = BidiClass.ET;
        // U+066B..U+066C  Arabic decimal/thousands → CS
        t[0x6B] = BidiClass.CS; t[0x6C] = BidiClass.CS;
        // U+066D  Arabic five-pointed star → ON
        t[0x6D] = BidiClass.ON;
        // U+066E..U+066F  Arabic letters → AL
        t[0x6E] = BidiClass.AL; t[0x6F] = BidiClass.AL;

        // U+0670  Arabic letter superscript alef → NSM
        t[0x70] = BidiClass.NSM;
        // U+0671..U+06BF  Arabic extended letters → AL
        for (int i = 0x71; i <= 0xBF; i++) t[i] = BidiClass.AL;
        // U+06C0..U+06D3  Arabic letters → AL
        for (int i = 0xC0; i <= 0xD3; i++) t[i] = BidiClass.AL;
        // U+06D4  Arabic full stop → AL
        t[0xD4] = BidiClass.AL;
        // U+06D5  Arabic letter Ae → AL
        t[0xD5] = BidiClass.AL;
        // U+06D6..U+06DC  Arabic small marks → NSM
        for (int i = 0xD6; i <= 0xDC; i++) t[i] = BidiClass.NSM;
        // U+06DD  Arabic end of ayah → AN
        t[0xDD] = BidiClass.AN;
        // U+06DE  Arabic start of rub el hizb → ON
        t[0xDE] = BidiClass.ON;
        // U+06DF..U+06E4  Arabic small marks → NSM
        for (int i = 0xDF; i <= 0xE4; i++) t[i] = BidiClass.NSM;
        // U+06E5..U+06E6  Arabic small marks → NSM
        t[0xE5] = BidiClass.NSM; t[0xE6] = BidiClass.NSM;
        // U+06E7..U+06E8  Arabic small marks → NSM
        t[0xE7] = BidiClass.NSM; t[0xE8] = BidiClass.NSM;
        // U+06E9  Arabic place of sajda → ON
        t[0xE9] = BidiClass.ON;
        // U+06EA..U+06ED  Arabic marks → NSM
        for (int i = 0xEA; i <= 0xED; i++) t[i] = BidiClass.NSM;
        // U+06EE..U+06EF  Arabic letters → AL
        t[0xEE] = BidiClass.AL; t[0xEF] = BidiClass.AL;
        // U+06F0..U+06F9  Extended Arabic-Indic digits → EN (yes, EN not AN)
        for (int i = 0xF0; i <= 0xF9; i++) t[i] = BidiClass.EN;
        // U+06FA..U+06FE  Arabic letters → AL
        for (int i = 0xFA; i <= 0xFE; i++) t[i] = BidiClass.AL;
        // U+06FF  Arabic letter mark → AL
        t[0xFF] = BidiClass.AL;

        return t;
    }

    // ------------------------------------------------------------------
    // Explicit directional formatting characters (U+200E..U+2069)
    // ------------------------------------------------------------------
    // Indices: 0=U+200E, 1=U+200F, ..., 92=U+206A (but we only need up to 0x2069)
    private static readonly BidiClass[] FormatTable = new BidiClass[92]
    {
        // U+200E LRM → L
        BidiClass.L,
        // U+200F RLM → R
        BidiClass.R,
        // U+2010..U+2028  (none are formatting)
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.B,
        BidiClass.B,
        // U+2029  paragraph → B
        BidiClass.B,
        // U+202A  LRE
        BidiClass.LRE,
        // U+202B  RLE
        BidiClass.RLE,
        // U+202C  PDF
        BidiClass.PDF,
        // U+202D  LRO
        BidiClass.LRO,
        // U+202E  RLO
        BidiClass.RLO,
        // U+202F..U+2043
        BidiClass.WS, BidiClass.BN, BidiClass.BN, BidiClass.BN,
        BidiClass.BN, BidiClass.BN, BidiClass.BN, BidiClass.BN,
        BidiClass.BN, BidiClass.BN, BidiClass.BN, BidiClass.BN,
        BidiClass.BN, BidiClass.BN, BidiClass.BN, BidiClass.BN,
        BidiClass.BN, BidiClass.BN, BidiClass.BN, BidiClass.BN,
        BidiClass.BN,
        // U+2044  fraction slash → CS
        BidiClass.CS,
        // U+2045..U+205F
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.ON, BidiClass.ON,
        BidiClass.ON, BidiClass.ON, BidiClass.WS,
        // U+2060  WORD JOINER → BN
        BidiClass.BN,
        // U+2061..U+2063  invisible ops → BN
        BidiClass.BN, BidiClass.BN, BidiClass.BN,
        // U+2064  invisible plus → BN
        BidiClass.BN,
        // U+2065  unassigned
        BidiClass.BN,
        // U+2066  LRI
        BidiClass.LRI,
        // U+2067  RLI
        BidiClass.RLI,
        // U+2068  FSI
        BidiClass.FSI,
        // U+2069  PDI
        BidiClass.PDI,
    };
}
#pragma warning restore CA1028
