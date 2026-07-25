// WP-3534: CoreText reference capture for the text-quality benchmark.
//
// Renders the SAME specimen as Etch.Text.Tests/TextParity/TextQualityBenchmark
// (corpus "HMEOBRAWNhmeobgjqpadtsn0258", sizes {9,11,13,16,20,24,32,48} pt at the
// benchmark's 96/72 raster scale) as black-on-white through CoreText, and reports
// the SAME metric: mean displayed ink (1 - luminance) over inked pixels (> 0.04),
// per glyph, averaged over the corpus per size. Emits the corpus as JSON on stdout.
//
// macOS-only. Build + run with just the Command Line Tools (no Xcode project):
//   clang -framework Foundation -framework CoreText -framework CoreGraphics \
//       -o coretext_capture coretext_capture.m
//   ./coretext_capture /path/to/Roboto-Regular.ttf > CoreTextCorpus.json
//
// The same Roboto-Regular the benchmark downloads:
//   curl -L "https://fonts.gstatic.com/s/roboto/v32/KFOmCnqEu92Fr1Me5Q.ttf" -o Roboto-Regular.ttf
//
// NOTE on macOS version: Mojave (10.14) dropped subpixel-AA, so 10.14+ reflects
// modern macOS grayscale rendering; the OS version is recorded in the JSON so the
// reference is interpreted correctly. No ARC (manual CF release in the hot loop).

#import <Foundation/Foundation.h>
#import <CoreText/CoreText.h>
#import <CoreGraphics/CoreGraphics.h>
#include <math.h>
#include <stdio.h>

static const double RASTER_DPI_SCALE = 96.0 / 72.0;          // matches the benchmark
static const double SIZES[] = {9, 11, 13, 16, 20, 24, 32, 48};
static const int NSIZES = (int)(sizeof(SIZES) / sizeof(SIZES[0]));
static const char *CORPUS = "HMEOBRAWNhmeobgjqpadtsn0258"; // matches the benchmark
static const double INK_THRESHOLD = 0.04;

static double MeanInkForGlyph(CGFontRef cgFont, unichar ch, double ppem) {
    int canvas = (int)ceil(ppem * 3.0) + 8;
    CGColorSpaceRef cs = CGColorSpaceCreateDeviceRGB();
    size_t bpr = (size_t)canvas * 4;
    unsigned char *data = (unsigned char *)calloc((size_t)canvas * canvas, 4);
    CGContextRef ctx = CGBitmapContextCreate(data, canvas, canvas, 8, bpr, cs,
                                             kCGImageAlphaPremultipliedLast);
    // White background.
    CGContextSetRGBFillColor(ctx, 1, 1, 1, 1);
    CGContextFillRect(ctx, CGRectMake(0, 0, canvas, canvas));
    CGContextSetShouldAntialias(ctx, true);
    CGContextSetAllowsAntialiasing(ctx, true);

    // Black glyph via CoreText. (Skia drew at top-down y = canvas*0.75; CoreGraphics
    // is bottom-up, so the baseline is at canvas*0.25 from the bottom.)
    CTFontRef font = CTFontCreateWithGraphicsFont(cgFont, ppem, NULL, NULL);
    CGFloat black[] = {0, 0, 0, 1};
    CGColorRef blackColor = CGColorCreate(cs, black);
    CFStringRef keys[] = {kCTFontAttributeName, kCTForegroundColorAttributeName};
    CFTypeRef vals[] = {font, blackColor};
    CFDictionaryRef attrs = CFDictionaryCreate(NULL, (const void **)keys, (const void **)vals, 2,
                                               &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CFStringRef str = CFStringCreateWithCharacters(NULL, &ch, 1);
    CFAttributedStringRef as = CFAttributedStringCreate(NULL, str, attrs);
    CTLineRef line = CTLineCreateWithAttributedString(as);
    CGContextSetTextPosition(ctx, 4, canvas * 0.25);
    CTLineDraw(line, ctx);
    CGContextFlush(ctx);

    double sum = 0;
    long n = 0;
    for (int i = 0; i < canvas * canvas; i++) {
        double r = data[i * 4], g = data[i * 4 + 1], b = data[i * 4 + 2];
        double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
        double ink = 1.0 - lum;
        if (ink > INK_THRESHOLD) { sum += ink; n++; }
    }

    CFRelease(line);
    CFRelease(as);
    CFRelease(str);
    CFRelease(attrs);
    CGColorRelease(blackColor);
    CFRelease(font);
    CGContextRelease(ctx);
    CGColorSpaceRelease(cs);
    free(data);
    return n == 0 ? 0.0 : sum / n;
}

int main(int argc, const char *argv[]) {
    if (argc < 2) {
        fprintf(stderr, "usage: coretext_capture <font.ttf> > CoreTextCorpus.json\n");
        return 1;
    }
    @autoreleasepool {
        NSData *fontData = [NSData dataWithContentsOfFile:[NSString stringWithUTF8String:argv[1]]];
        if (!fontData) { fprintf(stderr, "error: cannot read font %s\n", argv[1]); return 1; }
        CGDataProviderRef dp = CGDataProviderCreateWithCFData((CFDataRef)fontData);
        CGFontRef cgFont = CGFontCreateWithDataProvider(dp);
        if (!cgFont) { fprintf(stderr, "error: cannot load font\n"); return 1; }

        NSString *os = [[NSProcessInfo processInfo] operatingSystemVersionString];
        size_t corpusLen = strlen(CORPUS);

        NSMutableString *out = [NSMutableString string];
        [out appendString:@"{\n"];
        [out appendString:@"  \"engine\": \"CoreText\",\n"];
        [out appendString:@"  \"font\": \"Roboto-Regular\",\n"];
        [out appendFormat:@"  \"os\": \"%@\",\n", os];
        [out appendString:@"  \"metric\": \"meanInkBlackOnWhite\",\n"];
        [out appendFormat:@"  \"rasterDpiScale\": %.6f,\n", RASTER_DPI_SCALE];
        [out appendFormat:@"  \"corpus\": \"%s\",\n", CORPUS];
        [out appendString:@"  \"bySize\": {\n"];

        for (int s = 0; s < NSIZES; s++) {
            double ppem = SIZES[s] * RASTER_DPI_SCALE;
            double total = 0;
            int count = 0;
            for (size_t c = 0; c < corpusLen; c++) {
                total += MeanInkForGlyph(cgFont, (unichar)CORPUS[c], ppem);
                count++;
            }
            double avg = count == 0 ? 0.0 : total / count;
            [out appendFormat:@"    \"%g\": %.4f%@\n", SIZES[s], avg, (s == NSIZES - 1 ? @"" : @",")];
        }
        [out appendString:@"  }\n}\n"];

        printf("%s", [out UTF8String]);
        CGFontRelease(cgFont);
        CGDataProviderRelease(dp);
    }
    return 0;
}
