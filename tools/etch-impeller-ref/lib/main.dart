import 'dart:async';
import 'dart:io';
import 'dart:math' as math;
import 'dart:typed_data';
import 'dart:ui' as ui;

const int width = 64;
const int height = 64;

Future<void> main() async {
  final outDir = Directory(r'..\..\tests\Etch.ClipBlendGradient.Tests\Fixtures\impeller');
  outDir.createSync(recursive: true);

  final scenes = <String, void Function(ui.Canvas)>{
    'nested-circles': drawNestedCircles,
    'rect-minus-circle': drawRectMinusCircle,
    'soft-clipped-rect': drawSoftClippedRect,
    '8-level-nesting': drawEightLevelNesting,
    'clip-around-solid': drawClipAroundSolid,
    'overlapping-clips': drawOverlappingClips,
    'clip-then-translate': drawClipThenTranslate,
    'clip-rotate': drawClipRotate,
    'clip-scale': drawClipScale,
    'non-convex-clip': drawNonConvexClip,
  };

  for (final entry in scenes.entries) {
    final bytes = await renderScene(entry.value);
    final path = File('${outDir.path}\\${entry.key}.impeller.png');
    path.writeAsBytesSync(bytes);
    print('Wrote ${path.path}');
  }

  print('Done.');
}

Future<Uint8List> renderScene(void Function(ui.Canvas) draw) async {
  final recorder = ui.PictureRecorder();
  final canvas = ui.Canvas(recorder, ui.Rect.fromLTRB(0, 0, width.toDouble(), height.toDouble()));

  // Clear to black
  canvas.drawRect(
    ui.Rect.fromLTRB(0, 0, width.toDouble(), height.toDouble()),
    ui.Paint()..color = const ui.Color(0xFF000000),
  );

  draw(canvas);

  final picture = recorder.endRecording();
  final image = await picture.toImage(width, height);
  final byteData = await image.toByteData(format: ui.ImageByteFormat.png);
  return byteData!.buffer.asUint8List();
}

// ---------------------------------------------------------------------------
// Scene helpers
// ---------------------------------------------------------------------------

ui.Path circlePath(double cx, double cy, double radius, int segments) {
  final path = ui.Path();
  for (int i = 0; i < segments; i++) {
    final a0 = 2 * math.pi * i / segments;
    final a1 = 2 * math.pi * (i + 1) / segments;
    final x0 = cx + radius * math.cos(a0);
    final y0 = cy + radius * math.sin(a0);
    final x1 = cx + radius * math.cos(a1);
    final y1 = cy + radius * math.sin(a1);

    if (i == 0) {
      path.moveTo(x0, y0);
    }

    final mx = cx + radius * math.cos((a0 + a1) / 2);
    final my = cy + radius * math.sin((a0 + a1) / 2);
    final cpx = 2 * mx - (x0 + x1) / 2;
    final cpy = 2 * my - (y0 + y1) / 2;

    path.quadraticBezierTo(cpx, cpy, x1, y1);
  }
  path.close();
  return path;
}

ui.Path rectPath(double x0, double y0, double x1, double y1) {
  return ui.Path()
    ..moveTo(x0, y0)
    ..lineTo(x1, y0)
    ..lineTo(x1, y1)
    ..lineTo(x0, y1)
    ..close();
}

ui.Path roundedRectPath(double x0, double y0, double x1, double y1, double r) {
  return ui.Path()
    ..moveTo(x0 + r, y0)
    ..lineTo(x1 - r, y0)
    ..quadraticBezierTo(x1, y0, x1, y0 + r)
    ..lineTo(x1, y1 - r)
    ..quadraticBezierTo(x1, y1, x1 - r, y1)
    ..lineTo(x0 + r, y1)
    ..quadraticBezierTo(x0, y1, x0, y1 - r)
    ..lineTo(x0, y0 + r)
    ..quadraticBezierTo(x0, y0, x0 + r, y0)
    ..close();
}

ui.Path starPath(double cx, double cy, double outerR, double innerR, int points) {
  final path = ui.Path();
  final total = points * 2;
  for (int i = 0; i < total; i++) {
    final angle = math.pi / 2 + 2 * math.pi * i / total;
    final r = (i % 2 == 0) ? outerR : innerR;
    final x = cx + r * math.cos(angle);
    final y = cy - r * math.sin(angle);
    if (i == 0) {
      path.moveTo(x, y);
    } else {
      path.lineTo(x, y);
    }
  }
  path.close();
  return path;
}

// ---------------------------------------------------------------------------
// Scenes
// ---------------------------------------------------------------------------

void drawNestedCircles(ui.Canvas canvas) {
  final outer = circlePath(32, 32, 28, 32);
  final inner = circlePath(32, 32, 14, 24);

  canvas.save();
  canvas.clipPath(outer);
  canvas.clipPath(inner);
  canvas.drawRect(
    ui.Rect.fromLTRB(0, 0, 64, 64),
    ui.Paint()..color = const ui.Color(0xFFFF0000),
  );
  canvas.restore();
}

void drawRectMinusCircle(ui.Canvas canvas) {
  final rect = rectPath(4, 4, 60, 60);
  final hole = circlePath(32, 32, 16, 24);

  final clipPath = ui.Path.combine(ui.PathOperation.difference, rect, hole);

  canvas.save();
  canvas.clipPath(clipPath);
  canvas.drawRect(
    ui.Rect.fromLTRB(0, 0, 64, 64),
    ui.Paint()..color = const ui.Color(0xFFFF0000),
  );
  canvas.restore();
}

void drawSoftClippedRect(ui.Canvas canvas) {
  final rr = roundedRectPath(8, 8, 56, 56, 8);

  canvas.save();
  canvas.clipPath(rr);
  canvas.drawRect(
    ui.Rect.fromLTRB(0, 0, 64, 64),
    ui.Paint()..color = const ui.Color(0xFFFF0000),
  );
  canvas.restore();
}

void drawEightLevelNesting(ui.Canvas canvas) {
  canvas.save();
  for (int i = 0; i < 8; i++) {
    final inset = i * 3.0 + 2.0;
    final rect = rectPath(inset, inset, 64 - inset, 64 - inset);
    canvas.clipPath(rect);
  }
  canvas.drawRect(
    ui.Rect.fromLTRB(0, 0, 64, 64),
    ui.Paint()..color = const ui.Color(0xFFFF0000),
  );
  canvas.restore();
}

void drawClipAroundSolid(ui.Canvas canvas) {
  final star = starPath(32, 32, 28, 12, 5);

  canvas.save();
  canvas.clipPath(star);
  canvas.drawRect(
    ui.Rect.fromLTRB(0, 0, 64, 64),
    ui.Paint()..color = const ui.Color(0xFF0000FF),
  );
  canvas.restore();
}

void drawOverlappingClips(ui.Canvas canvas) {
  final left = circlePath(20, 32, 16, 24);
  final right = circlePath(44, 32, 16, 24);

  canvas.save();
  canvas.clipPath(left);
  canvas.drawRect(
    ui.Rect.fromLTRB(0, 0, 64, 64),
    ui.Paint()..color = const ui.Color(0xFFFF0000),
  );
  canvas.restore();

  canvas.save();
  canvas.clipPath(right);
  canvas.drawRect(
    ui.Rect.fromLTRB(0, 0, 64, 64),
    ui.Paint()..color = const ui.Color(0xFF0000FF),
  );
  canvas.restore();
}

void drawClipThenTranslate(ui.Canvas canvas) {
  final rect = rectPath(8, 8, 24, 24);

  canvas.save();
  canvas.clipPath(rect);
  canvas.translate(16, 16);
  canvas.drawRect(
    ui.Rect.fromLTRB(0, 0, 16, 16),
    ui.Paint()..color = const ui.Color(0xFFFF0000),
  );
  canvas.restore();
}

void drawClipRotate(ui.Canvas canvas) {
  final rect = rectPath(20, 12, 44, 52);

  canvas.save();
  canvas.clipPath(rect);
  canvas.translate(32, 32);
  canvas.rotate(math.pi / 6.0);
  canvas.translate(-32, -32);
  canvas.drawRect(
    ui.Rect.fromLTRB(16, 16, 48, 48),
    ui.Paint()..color = const ui.Color(0xFFFF0000),
  );
  canvas.restore();
}

void drawClipScale(ui.Canvas canvas) {
  final rect = rectPath(16, 16, 48, 48);

  canvas.save();
  canvas.clipPath(rect);
  canvas.translate(32, 32);
  canvas.scale(0.5, 0.5);
  canvas.translate(-32, -32);
  canvas.drawRect(
    ui.Rect.fromLTRB(0, 0, 128, 128),
    ui.Paint()..color = const ui.Color(0xFFFF0000),
  );
  canvas.restore();
}

void drawNonConvexClip(ui.Canvas canvas) {
  final star = starPath(32, 32, 30, 10, 6);

  canvas.save();
  canvas.clipPath(star);
  canvas.drawRect(
    ui.Rect.fromLTRB(0, 0, 64, 64),
    ui.Paint()..color = const ui.Color(0xFF00FF00),
  );
  canvas.restore();
}
