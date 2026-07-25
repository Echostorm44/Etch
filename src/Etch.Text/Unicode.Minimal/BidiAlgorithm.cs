using System;
using System.Buffers;

namespace Etch.Text.Unicode.Minimal;

/// <summary>
/// Minimal but correct implementation of the Unicode Bidirectional Algorithm
/// (UAX #9). Covers Latin, Arabic, and Hebrew. Uses stackalloc for
/// paragraphs ≤ 256 chars; ArrayPool fallback above.
/// </summary>
public static class BidiAlgorithm
{
    private const int StackBufferSize = 256;
    private const int MaxDepth = 125;   // UAX #9 maximum embedding depth

    /// <summary>
    /// Analyse a single paragraph and return visual-order runs.
    /// </summary>
    /// <param name="text">Paragraph text.</param>
    /// <param name="paragraphLevel">0 = LTR, 1 = RTL, -1 = auto-detect.</param>
    public static BidiParagraphResult Analyze(ReadOnlySpan<char> text, sbyte paragraphLevel = -1)
    {
        int len = text.Length;
        if (len == 0)
            return new BidiParagraphResult(0, Array.Empty<BidiRun>());

        // ---------- buffers ----------
        bool usePool = len > StackBufferSize;
        BidiClass[]? pooledTypes = null;
        sbyte[]? pooledLevels = null;

        Span<BidiClass> types = usePool
            ? (pooledTypes = ArrayPool<BidiClass>.Shared.Rent(len)).AsSpan(0, len)
            : stackalloc BidiClass[StackBufferSize];
        Span<sbyte> levels = usePool
            ? (pooledLevels = ArrayPool<sbyte>.Shared.Rent(len)).AsSpan(0, len)
            : stackalloc sbyte[StackBufferSize];

        if (!usePool)
        {
            types = types.Slice(0, len);
            levels = levels.Slice(0, len);
        }

        try
        {
            // Phase 1: classify characters
            for (int i = 0; i < len; i++)
                types[i] = BidiClassTable.Get(text[i]);

            // Phase 2: determine paragraph level (P2-P3)
            if (paragraphLevel < 0)
                paragraphLevel = DetermineParagraphLevel(types);

            // Phase 3: resolve explicit levels (X1-X8)
            ResolveExplicitLevels(types, levels, paragraphLevel);

            // Phase 4: resolve weak types (W1-W7)
            ResolveWeakTypes(types, levels, paragraphLevel);

            // Phase 4.5: resolve paired brackets (N0, UAX #9 + BD16)
            ResolveBracketPairs(text, types, levels);

            // Phase 5: resolve neutral types (N1-N2)
            ResolveNeutrals(types, levels, paragraphLevel);

            // Phase 6: resolve implicit levels (I1-I2)
            ResolveImplicitLevels(types, levels, paragraphLevel);

            // Phase 7: reorder into runs (L1-L2)
            var runs = CreateRuns(levels, text, paragraphLevel);

            return new BidiParagraphResult((byte)paragraphLevel, runs);
        }
        finally
        {
            if (pooledTypes != null)
                ArrayPool<BidiClass>.Shared.Return(pooledTypes);
            if (pooledLevels != null)
                ArrayPool<sbyte>.Shared.Return(pooledLevels);
        }
    }

    // =====================================================================
    // Phase 1 — Paragraph level (P2-P3)
    // =====================================================================
    private static sbyte DetermineParagraphLevel(ReadOnlySpan<BidiClass> types)
    {
        // P2: scan for first strong character
        for (int i = 0; i < types.Length; i++)
        {
            switch (types[i])
            {
                case BidiClass.L:
                    return 0;
                case BidiClass.R:
                case BidiClass.AL:
                    return 1;
            }
        }
        // P3: no strong character → default LTR
        return 0;
    }

    // =====================================================================
    // Phase 2 — Explicit levels (X1-X8)
    // =====================================================================
    private static void ResolveExplicitLevels(
        Span<BidiClass> types, Span<sbyte> levels, sbyte paraLevel)
    {
        int len = types.Length;
        levels.Fill(paraLevel);

        // Stack for explicit levels: (level, directionalOverride, isolateStatus)
        // Each entry is a tuple of (level, overrideState, isolate)
        // Override: 0=neutral, 1=L, 2=R
        Span<sbyte> stackLevels = stackalloc sbyte[MaxDepth + 2];
        Span<byte> stackOverrides = stackalloc byte[MaxDepth + 2];
        Span<byte> stackIsolates = stackalloc byte[MaxDepth + 2];
        int stackTop = 0;
        stackLevels[0] = paraLevel;
        stackOverrides[0] = 0;   // neutral
        stackIsolates[0] = 0;    // not isolate

        // Counter for unresolved isolates
        int isolateCount = 0;

        for (int i = 0; i < len; i++)
        {
            var t = types[i];

            // Isolates (UAX #9 rule X5a-X5c, X6a)
            switch (t)
            {
                case BidiClass.LRI:
                case BidiClass.RLI:
                case BidiClass.FSI:
                    // X5a-X5c, X5d
                    sbyte newLevel = (sbyte)((stackLevels[stackTop] + (t == BidiClass.LRI ? 2 : 1)) & ~1);
                    if (t == BidiClass.RLI) newLevel = (sbyte)((newLevel + 1) | 1);
                    if (t == BidiClass.FSI)
                    {
                        // FSI: scan ahead for first strong in isolate
                        sbyte fsiLevel = paraLevel;
                        for (int j = i + 1; j < len; j++)
                        {
                            var ft = types[j];
                            if (ft == BidiClass.PDI) break;
                            if (ft == BidiClass.L) { fsiLevel = 0; break; }
                            if (ft == BidiClass.R || ft == BidiClass.AL) { fsiLevel = 1; break; }
                        }
                        newLevel = (sbyte)((stackLevels[stackTop] + (fsiLevel == 0 ? 2 : 1)) & ~1);
                        if (fsiLevel == 1) newLevel = (sbyte)(newLevel | 1);
                    }
                    if (newLevel <= MaxDepth && isolateCount == 0)
                    {
                        stackTop++;
                        stackLevels[stackTop] = newLevel;
                        stackOverrides[stackTop] = 0; // neutral override for isolates
                        stackIsolates[stackTop] = 1;
                    }
                    isolateCount++;
                    types[i] = BidiClass.BN; // Remove explicit from further processing
                    levels[i] = stackLevels[stackTop];
                    continue;

                case BidiClass.PDI:
                    // X6a
                    if (isolateCount > 0)
                    {
                        isolateCount--;
                        if (isolateCount == 0)
                        {
                            // Pop to last isolate
                            while (stackTop > 0 && stackIsolates[stackTop] == 0)
                                stackTop--;
                            if (stackTop > 0)
                                stackTop--;
                        }
                    }
                    types[i] = BidiClass.BN;
                    levels[i] = stackLevels[stackTop];
                    continue;
            }

            // Skip if inside an isolate (explicit handling done by the isolate push/pop)
            if (isolateCount > 0)
            {
                levels[i] = stackLevels[stackTop];
                if (t == BidiClass.BN || IsExplicitFormatting(t))
                    types[i] = BidiClass.BN;
                continue;
            }

            // Standard explicit formatting characters (X2-X8)
            switch (t)
            {
                case BidiClass.LRE:
                    {
                        sbyte nl = (sbyte)(((stackLevels[stackTop] + 2) & ~1));
                        if (nl <= MaxDepth)
                        {
                            stackTop++;
                            stackLevels[stackTop] = nl;
                            stackOverrides[stackTop] = 0;
                            stackIsolates[stackTop] = 0;
                        }
                        types[i] = BidiClass.BN;
                        levels[i] = stackLevels[stackTop];
                        break;
                    }
                case BidiClass.RLE:
                    {
                        sbyte nl = (sbyte)(((stackLevels[stackTop] + 1) | 1));
                        if (nl <= MaxDepth)
                        {
                            stackTop++;
                            stackLevels[stackTop] = nl;
                            stackOverrides[stackTop] = 0;
                            stackIsolates[stackTop] = 0;
                        }
                        types[i] = BidiClass.BN;
                        levels[i] = stackLevels[stackTop];
                        break;
                    }
                case BidiClass.LRO:
                    {
                        sbyte nl = (sbyte)(((stackLevels[stackTop] + 2) & ~1));
                        if (nl <= MaxDepth)
                        {
                            stackTop++;
                            stackLevels[stackTop] = nl;
                            stackOverrides[stackTop] = 1; // L override
                            stackIsolates[stackTop] = 0;
                        }
                        types[i] = BidiClass.BN;
                        levels[i] = stackLevels[stackTop];
                        break;
                    }
                case BidiClass.RLO:
                    {
                        sbyte nl = (sbyte)(((stackLevels[stackTop] + 1) | 1));
                        if (nl <= MaxDepth)
                        {
                            stackTop++;
                            stackLevels[stackTop] = nl;
                            stackOverrides[stackTop] = 2; // R override
                            stackIsolates[stackTop] = 0;
                        }
                        types[i] = BidiClass.BN;
                        levels[i] = stackLevels[stackTop];
                        break;
                    }
                case BidiClass.PDF:
                    {
                        // X7: pop directional formatting
                        if (stackTop > 0 && stackIsolates[stackTop] == 0)
                            stackTop--;
                        types[i] = BidiClass.BN;
                        levels[i] = stackLevels[stackTop];
                        break;
                    }
                default:
                    {
                        // X6: set level and apply override
                        levels[i] = stackLevels[stackTop];
                        byte ov = stackOverrides[stackTop];
                        if (ov == 1) types[i] = BidiClass.L;
                        else if (ov == 2) types[i] = BidiClass.R;
                        break;
                    }
            }
        }
    }

    private static bool IsExplicitFormatting(BidiClass c) =>
        c is BidiClass.LRE or BidiClass.RLE or BidiClass.LRO
           or BidiClass.RLO or BidiClass.PDF or BidiClass.LRI
           or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI;

    // =====================================================================
    // Phase 3 — Weak types (W1-W7)
    // =====================================================================
    private static void ResolveWeakTypes(
        Span<BidiClass> types, ReadOnlySpan<sbyte> levels, sbyte paraLevel)
    {
        int len = types.Length;

        // W1: NSM → previous type (after explicit resolution)
        for (int i = 0; i < len; i++)
        {
            if (types[i] == BidiClass.NSM)
            {
                if (i > 0)
                {
                    var prev = types[i - 1];
                    types[i] = prev;
                }
            }
        }

        // W2: EN after AL → AN
        for (int i = 0; i < len; i++)
        {
            if (types[i] == BidiClass.EN)
            {
                for (int j = i - 1; j >= 0; j--)
                {
                    if (types[j] == BidiClass.AL)
                    {
                        types[i] = BidiClass.AN;
                        break;
                    }
                    if (types[j] == BidiClass.L || types[j] == BidiClass.R)
                        break;
                }
            }
        }

        // W3: AL → R
        for (int i = 0; i < len; i++)
        {
            if (types[i] == BidiClass.AL)
                types[i] = BidiClass.R;
        }

        // W4: ES/CS between two numbers → number
        for (int i = 1; i < len - 1; i++)
        {
            if (types[i] is BidiClass.ES or BidiClass.CS)
            {
                if ((types[i - 1] is BidiClass.EN or BidiClass.AN) &&
                    (types[i + 1] is BidiClass.EN or BidiClass.AN))
                {
                    types[i] = types[i - 1];
                }
            }
        }

        // W5: ET sequences after numbers → EN
        for (int i = 0; i < len; i++)
        {
            if (types[i] == BidiClass.ET)
            {
                // Look ahead for EN
                bool foundEn = false;
                for (int j = i + 1; j < len; j++)
                {
                    if (types[j] == BidiClass.EN) { foundEn = true; break; }
                    if (types[j] != BidiClass.ET) break;
                }
                // Look behind for EN
                if (!foundEn)
                {
                    for (int j = i - 1; j >= 0; j--)
                    {
                        if (types[j] == BidiClass.EN) { foundEn = true; break; }
                        if (types[j] != BidiClass.ET) break;
                    }
                }
                if (foundEn)
                    types[i] = BidiClass.EN;
            }
        }

        // W6: remaining ES/ET/CS → ON
        for (int i = 0; i < len; i++)
        {
            if (types[i] is BidiClass.ES or BidiClass.ET or BidiClass.CS)
                types[i] = BidiClass.ON;
        }

        // W7: EN after L → L
        for (int i = 0; i < len; i++)
        {
            if (types[i] == BidiClass.EN)
            {
                for (int j = i - 1; j >= 0; j--)
                {
                    if (types[j] == BidiClass.L)
                    {
                        types[i] = BidiClass.L;
                        break;
                    }
                    if (types[j] == BidiClass.R)
                        break;
                }
            }
        }
    }

    // =====================================================================
    // Phase 4 — Neutral types (N1-N2)
    // =====================================================================
    private static void ResolveNeutrals(
        Span<BidiClass> types, ReadOnlySpan<sbyte> levels, sbyte paraLevel)
    {
        int len = types.Length;

        // N1/N2: neutrals (ON, WS, S, B) take the surrounding embedding direction
        for (int i = 0; i < len; i++)
        {
            if (!IsNeutral(types[i]))
                continue;

            // Find the type of the preceding strong character
            BidiClass? before = null;
            for (int j = i - 1; j >= 0; j--)
            {
                if (IsStrong(types[j]) || IsNumber(types[j]))
                {
                    before = types[j];
                    break;
                }
            }

            // Find the type of the following strong character
            BidiClass? after = null;
            for (int j = i + 1; j < len; j++)
            {
                if (IsStrong(types[j]) || IsNumber(types[j]))
                {
                    after = types[j];
                    break;
                }
            }

            // N1: if both sides agree, use that type
            if (before.HasValue && after.HasValue)
            {
                if (before.Value == after.Value)
                {
                    types[i] = before.Value;
                    continue;
                }

                // N1: European/Arabic number interaction
                if (before.Value is BidiClass.EN or BidiClass.AN &&
                    after.Value is BidiClass.EN or BidiClass.AN)
                {
                    types[i] = (levels[i] & 1) == 0 ? BidiClass.L : BidiClass.R;
                    continue;
                }
            }

            // N2: default to embedding direction
            types[i] = (levels[i] & 1) == 0 ? BidiClass.L : BidiClass.R;
        }
    }

    private static bool IsStrong(BidiClass c) => c is BidiClass.L or BidiClass.R;
    private static bool IsNumber(BidiClass c) => c is BidiClass.EN or BidiClass.AN;
    private static bool IsNeutral(BidiClass c) => c is BidiClass.ON or BidiClass.WS or BidiClass.S or BidiClass.B;

    // =====================================================================
    // Phase 4.5 — Paired brackets (N0, UAX #9, using BD16 bracket pairing)
    // =====================================================================

    // Bidi_Paired_Bracket data (UCD BidiBrackets.txt): opening → closing bracket
    // codepoint. Stored as raw codepoints (no glyph literals — U+2329 and U+3008
    // are visually identical) and folded for canonical equivalence by Canon().
    private static readonly (int Open, int Close)[] BracketPairs =
    {
        (0x0028, 0x0029), (0x005B, 0x005D), (0x007B, 0x007D),
        (0x0F3A, 0x0F3B), (0x0F3C, 0x0F3D), (0x169B, 0x169C),
        (0x2045, 0x2046), (0x207D, 0x207E), (0x208D, 0x208E),
        (0x2308, 0x2309), (0x230A, 0x230B), (0x2329, 0x232A),
        (0x2768, 0x2769), (0x276A, 0x276B), (0x276C, 0x276D),
        (0x276E, 0x276F), (0x2770, 0x2771), (0x2772, 0x2773),
        (0x2774, 0x2775), (0x27C5, 0x27C6), (0x27E6, 0x27E7),
        (0x27E8, 0x27E9), (0x27EA, 0x27EB), (0x27EC, 0x27ED),
        (0x27EE, 0x27EF), (0x2983, 0x2984), (0x2985, 0x2986),
        (0x2987, 0x2988), (0x2989, 0x298A), (0x298B, 0x298C),
        (0x298D, 0x2990), (0x298F, 0x298E), (0x2991, 0x2992),
        (0x2993, 0x2994), (0x2995, 0x2996), (0x2997, 0x2998),
        (0x29D8, 0x29D9), (0x29DA, 0x29DB), (0x29FC, 0x29FD),
        (0x2E22, 0x2E23), (0x2E24, 0x2E25), (0x2E26, 0x2E27),
        (0x2E28, 0x2E29), (0x2E55, 0x2E56), (0x2E57, 0x2E58),
        (0x2E59, 0x2E5A), (0x2E5B, 0x2E5C), (0x3008, 0x3009),
        (0x300A, 0x300B), (0x300C, 0x300D), (0x300E, 0x300F),
        (0x3010, 0x3011), (0x3014, 0x3015), (0x3016, 0x3017),
        (0x3018, 0x3019), (0x301A, 0x301B), (0xFE59, 0xFE5A),
        (0xFE5B, 0xFE5C), (0xFE5D, 0xFE5E), (0xFF08, 0xFF09),
        (0xFF3B, 0xFF3D), (0xFF5B, 0xFF5D), (0xFF5F, 0xFF60),
        (0xFF62, 0xFF63),
    };

    private static readonly Dictionary<char, char> OpenToClose = BuildBracketMap(open: true);
    private static readonly Dictionary<char, char> CloseToOpen = BuildBracketMap(open: false);

    private static Dictionary<char, char> BuildBracketMap(bool open)
    {
        var d = new Dictionary<char, char>(BracketPairs.Length);
        foreach (var (o, c) in BracketPairs)
        {
            if (open) { d[(char)o] = (char)c; }
            else { d[(char)c] = (char)o; }
        }
        return d;
    }

    // Canonical-equivalence folding for the only bracket chars with a canonical
    // decomposition: U+2329 ≡ U+3008 (LEFT-POINTING ANGLE), U+232A ≡ U+3009.
    private static char Canon(char c) => c switch
    {
        '〈' => '〈',
        '〉' => '〉',
        _ => c,
    };

    /// <summary>
    /// N0: resolve the direction of paired brackets so that e.g. parentheses in an
    /// opposite-direction context adopt the surrounding direction. BD16 pairs
    /// brackets with a bounded stack; N0 then sets each pair from the strong types
    /// it encloses (numbers counting as R) and the preceding context.
    /// </summary>
    private static void ResolveBracketPairs(ReadOnlySpan<char> text, Span<BidiClass> types, ReadOnlySpan<sbyte> levels)
    {
        int len = text.Length;
        const int MaxPairs = 63; // BD16 stack limit

        Span<char> stackExpected = stackalloc char[MaxPairs]; // canonical closing we expect
        Span<int> stackPos = stackalloc int[MaxPairs];
        int top = 0;

        Span<int> pairOpen = stackalloc int[MaxPairs];
        Span<int> pairClose = stackalloc int[MaxPairs];
        int pairCount = 0;

        for (int i = 0; i < len && pairCount < MaxPairs; i++)
        {
            // Only characters still typed ON are eligible paired brackets (BD14/BD15).
            if (types[i] != BidiClass.ON)
                continue;

            char ch = text[i];
            if (OpenToClose.TryGetValue(ch, out char close))
            {
                if (top == MaxPairs)
                    break; // stack full → stop processing per BD16
                stackExpected[top] = Canon(close);
                stackPos[top] = i;
                top++;
            }
            else if (CloseToOpen.ContainsKey(ch))
            {
                char cc = Canon(ch);
                for (int s = top - 1; s >= 0; s--)
                {
                    if (stackExpected[s] == cc)
                    {
                        pairOpen[pairCount] = stackPos[s];
                        pairClose[pairCount] = i;
                        pairCount++;
                        top = s; // pop this entry and everything above it
                        break;
                    }
                }
            }
        }

        if (pairCount == 0)
            return;

        // Process in order of opening position (insertion sort; pairCount ≤ 63,
        // pairs are discovered in closing order).
        for (int a = 1; a < pairCount; a++)
        {
            int o = pairOpen[a], c = pairClose[a];
            int b = a - 1;
            while (b >= 0 && pairOpen[b] > o)
            {
                pairOpen[b + 1] = pairOpen[b];
                pairClose[b + 1] = pairClose[b];
                b--;
            }
            pairOpen[b + 1] = o;
            pairClose[b + 1] = c;
        }

        for (int p = 0; p < pairCount; p++)
        {
            int o = pairOpen[p];
            int c = pairClose[p];
            BidiClass e = (levels[o] & 1) == 0 ? BidiClass.L : BidiClass.R; // embedding dir
            BidiClass opp = e == BidiClass.L ? BidiClass.R : BidiClass.L;

            bool foundE = false, foundOpp = false;
            for (int k = o + 1; k < c; k++)
            {
                BidiClass s = StrongForN0(types[k]);
                if (s == e) { foundE = true; break; }
                if (s == opp) { foundOpp = true; }
            }

            BidiClass set;
            if (foundE)
            {
                set = e; // N0 (b): strong type matching embedding direction inside
            }
            else if (foundOpp)
            {
                // N0 (c): opposite-direction strong inside — adopt it only if the
                // established (preceding) context is also that opposite direction.
                BidiClass context = e;
                for (int k = o - 1; k >= 0; k--)
                {
                    BidiClass s = StrongForN0(types[k]);
                    if (s != BidiClass.ON) { context = s; break; }
                }
                set = context == opp ? opp : e;
            }
            else
            {
                continue; // N0 (d): no strong inside → leave for N1/N2
            }

            types[o] = set;
            types[c] = set;

            // N0 (final bullet): any characters whose ORIGINAL type was NSM that
            // immediately follow a bracket changed under N0 take the bracket's new
            // type. W1 already rewrote NSM, so re-derive the original from the char.
            for (int k = c + 1; k < len && BidiClassTable.Get(text[k]) == BidiClass.NSM; k++)
                types[k] = set;
            for (int k = o + 1; k < len && BidiClassTable.Get(text[k]) == BidiClass.NSM; k++)
                types[k] = set;
        }
    }

    // For N0 a "strong" type is L, or R (with EN/AN counting as R); anything else
    // returns ON as the "not strong" sentinel.
    private static BidiClass StrongForN0(BidiClass c) => c switch
    {
        BidiClass.L => BidiClass.L,
        BidiClass.R or BidiClass.EN or BidiClass.AN => BidiClass.R,
        _ => BidiClass.ON,
    };

    // =====================================================================
    // Phase 5 — Implicit levels (I1-I2)
    // =====================================================================
    private static void ResolveImplicitLevels(
        Span<BidiClass> types, Span<sbyte> levels, sbyte paraLevel)
    {
        int len = types.Length;
        for (int i = 0; i < len; i++)
        {
            sbyte level = levels[i];
            bool even = (level & 1) == 0;
            var t = types[i];

            if (even)
            {
                // I1: even level + R → level+1, AN/EN → level+2
                if (t == BidiClass.R)
                    levels[i] = (sbyte)(level + 1);
                else if (t is BidiClass.AN or BidiClass.EN)
                    levels[i] = (sbyte)(level + 2);
            }
            else
            {
                // I2: odd level + L/EN/AN → level+1
                if (t is BidiClass.L or BidiClass.EN or BidiClass.AN)
                    levels[i] = (sbyte)(level + 1);
            }
        }
    }

    // =====================================================================
    // Phase 6 — Reordering into runs (L1-L2)
    // =====================================================================
    private static BidiRun[] CreateRuns(
        Span<sbyte> levels, ReadOnlySpan<char> text, sbyte paraLevel)
    {
        int len = levels.Length;
        if (len == 0)
            return Array.Empty<BidiRun>();

        // L1: trailing whitespace / segment / paragraph separators
        //     get reset to paragraph embedding level.
        for (int i = len - 1; i >= 0; i--)
        {
            var bc = BidiClassTable.Get(text[i]);
            if (bc is BidiClass.WS or BidiClass.S or BidiClass.B)
                levels[i] = paraLevel;
            else
                break;
        }

        // Build visual index map (L2)
        bool usePool = len > StackBufferSize;
        int[]? pooledMap = null;
        Span<int> visualMap = usePool
            ? (pooledMap = ArrayPool<int>.Shared.Rent(len)).AsSpan(0, len)
            : stackalloc int[StackBufferSize];
        if (!usePool)
            visualMap = visualMap.Slice(0, len);

        try
        {
            for (int i = 0; i < len; i++)
                visualMap[i] = i;

            sbyte maxLevel = 0;
            for (int i = 0; i < len; i++)
                if (levels[i] > maxLevel) maxLevel = levels[i];

            for (sbyte level = maxLevel; level >= 1; level--)
            {
                int start = 0;
                while (start < len)
                {
                    while (start < len && levels[visualMap[start]] < level)
                        start++;
                    if (start >= len) break;

                    int end = start;
                    while (end < len && levels[visualMap[end]] >= level)
                        end++;

                    // Reverse visualMap[start..end-1]
                    int l = start, r = end - 1;
                    while (l < r)
                    {
                        int tmp = visualMap[l];
                        visualMap[l] = visualMap[r];
                        visualMap[r] = tmp;
                        l++;
                        r--;
                    }

                    start = end;
                }
            }

            // Group visualMap into runs of equal level
            int runCount = 1;
            for (int i = 1; i < len; i++)
                if (levels[visualMap[i]] != levels[visualMap[i - 1]])
                    runCount++;

            var runs = new BidiRun[runCount];
            int runStart = 0;
            sbyte currentLevel = levels[visualMap[0]];
            int runIdx = 0;

            for (int i = 1; i <= len; i++)
            {
                if (i == len || levels[visualMap[i]] != currentLevel)
                {
                    int min = visualMap[runStart];
                    int max = visualMap[runStart];
                    for (int j = runStart + 1; j < i; j++)
                    {
                        int idx = visualMap[j];
                        if (idx < min) min = idx;
                        if (idx > max) max = idx;
                    }
                    runs[runIdx++] = new BidiRun(min, max - min + 1, (byte)currentLevel);
                    if (i < len)
                    {
                        runStart = i;
                        currentLevel = levels[visualMap[i]];
                    }
                }
            }

            return runs;
        }
        finally
        {
            if (pooledMap != null)
                ArrayPool<int>.Shared.Return(pooledMap);
        }
    }
}
