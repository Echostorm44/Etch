use kurbo::{Affine, CubicBez, ParamCurve, Point, QuadBez, Shape, BezPath, PathEl, flatten};

#[no_mangle]
pub unsafe extern "C" fn affine_compose(
    a: *const f64,
    b: *const f64,
    out: *mut f64,
) {
    let aa = Affine::new([
        *a.add(0),
        *a.add(1),
        *a.add(2),
        *a.add(3),
        *a.add(4),
        *a.add(5),
    ]);
    let bb = Affine::new([
        *b.add(0),
        *b.add(1),
        *b.add(2),
        *b.add(3),
        *b.add(4),
        *b.add(5),
    ]);
    let cc = aa * bb;
    let coeffs = cc.as_coeffs();
    for i in 0..6 {
        *out.add(i) = coeffs[i];
    }
}

#[no_mangle]
pub unsafe extern "C" fn affine_inverse(a: *const f64, out: *mut f64) {
    let aa = Affine::new([
        *a.add(0),
        *a.add(1),
        *a.add(2),
        *a.add(3),
        *a.add(4),
        *a.add(5),
    ]);
    let inv = aa.inverse();
    let coeffs = inv.as_coeffs();
    for i in 0..6 {
        *out.add(i) = coeffs[i];
    }
}

#[no_mangle]
pub unsafe extern "C" fn point_transform(
    affine: *const f64,
    pts: *const f64,
    count: usize,
    out: *mut f64,
) {
    let aa = Affine::new([
        *affine.add(0),
        *affine.add(1),
        *affine.add(2),
        *affine.add(3),
        *affine.add(4),
        *affine.add(5),
    ]);
    for i in 0..count {
        let px = *pts.add(i * 2);
        let py = *pts.add(i * 2 + 1);
        let pt = aa * Point::new(px, py);
        *out.add(i * 2) = pt.x;
        *out.add(i * 2 + 1) = pt.y;
    }
}

#[no_mangle]
pub unsafe extern "C" fn cubic_eval(
    cubic: *const f64,
    t: f64,
    start_x: f64,
    start_y: f64,
    out: *mut f64,
) {
    let cb = CubicBez::new(
        Point::new(start_x, start_y),
        Point::new(*cubic.add(0), *cubic.add(1)),
        Point::new(*cubic.add(2), *cubic.add(3)),
        Point::new(*cubic.add(4), *cubic.add(5)),
    );
    let pt = cb.eval(t);
    *out = pt.x;
    *out.add(1) = pt.y;
}

#[no_mangle]
pub unsafe extern "C" fn cubic_subdivide(
    cubic: *const f64,
    _t: f64,
    start_x: f64,
    start_y: f64,
    left: *mut f64,
    right: *mut f64,
) {
    let cb = CubicBez::new(
        Point::new(start_x, start_y),
        Point::new(*cubic.add(0), *cubic.add(1)),
        Point::new(*cubic.add(2), *cubic.add(3)),
        Point::new(*cubic.add(4), *cubic.add(5)),
    );
    let (l, r) = cb.subdivide();
    *left.add(0) = l.p1.x;
    *left.add(1) = l.p1.y;
    *left.add(2) = l.p2.x;
    *left.add(3) = l.p2.y;
    *left.add(4) = l.p3.x;
    *left.add(5) = l.p3.y;
    *right.add(0) = r.p1.x;
    *right.add(1) = r.p1.y;
    *right.add(2) = r.p2.x;
    *right.add(3) = r.p2.y;
    *right.add(4) = r.p3.x;
    *right.add(5) = r.p3.y;
}

#[no_mangle]
pub unsafe extern "C" fn cubic_aabb(
    cubic: *const f64,
    start_x: f64,
    start_y: f64,
    rect: *mut f64,
) {
    let cb = CubicBez::new(
        Point::new(start_x, start_y),
        Point::new(*cubic.add(0), *cubic.add(1)),
        Point::new(*cubic.add(2), *cubic.add(3)),
        Point::new(*cubic.add(4), *cubic.add(5)),
    );
    let aabb = cb.bounding_box();
    *rect = aabb.x0;
    *rect.add(1) = aabb.y0;
    *rect.add(2) = aabb.x1;
    *rect.add(3) = aabb.y1;
}

const FLATTEN_MAX_POINTS: usize = 8192;

fn flatten_quad_to_points(p0: Point, p1: Point, p2: Point, tolerance: f64, output: &mut Vec<f64>) {
    let q = QuadBez::new(p0, p1, p2);
    let deviation = dev_from_chord(&q);
    if deviation <= tolerance {
        output.push(p2.x);
        output.push(p2.y);
        return;
    }
    let (left, right) = q.subdivide();
    flatten_quad_to_points(left.p0, left.p1, left.p2, tolerance, output);
    flatten_quad_to_points(right.p0, right.p1, right.p2, tolerance, output);
}

fn flatten_cubic_to_points(p0: Point, p1: Point, p2: Point, p3: Point, tolerance: f64, output: &mut Vec<f64>) {
    let c = CubicBez::new(p0, p1, p2, p3);
    let deviation = dev_from_chord_cubic(&c);
    if deviation <= tolerance {
        output.push(p3.x);
        output.push(p3.y);
        return;
    }
    let (left, right) = c.subdivide();
    flatten_cubic_to_points(left.p0, left.p1, left.p2, left.p3, tolerance, output);
    flatten_cubic_to_points(right.p0, right.p1, right.p2, right.p3, tolerance, output);
}

fn dev_from_chord(q: &QuadBez) -> f64 {
    let p = q.p1;
    let a = q.p0;
    let b = q.p2;
    let ab = b - a;
    let ab_len_sq = ab.x * ab.x + ab.y * ab.y;
    if ab_len_sq < 1e-20 {
        let ap = p - a;
        return (ap.x * ap.x + ap.y * ap.y).sqrt();
    }
    let ap = p - a;
    let t = (ap.x * ab.x + ap.y * ab.y) / ab_len_sq;
    let t = t.max(0.0).min(1.0);
    let proj = Point::new(a.x + t * ab.x, a.y + t * ab.y);
    let diff = p - proj;
    (diff.x * diff.x + diff.y * diff.y).sqrt()
}

fn dev_from_chord_cubic(c: &CubicBez) -> f64 {
    let p1_dev = dev_from_chord(&QuadBez::new(c.p0, c.p1, c.p3));
    let p2_dev = dev_from_chord(&QuadBez::new(c.p0, c.p2, c.p3));
    p1_dev.max(p2_dev)
}

#[no_mangle]
pub unsafe extern "C" fn cubic_flatten(
    cubic: *const f64,
    start_x: f64,
    start_y: f64,
    tolerance: f64,
    output: *mut f64,
    max_output: usize,
    out_count: *mut usize,
) -> bool {
    let cb = CubicBez::new(
        Point::new(start_x, start_y),
        Point::new(*cubic.add(0), *cubic.add(1)),
        Point::new(*cubic.add(2), *cubic.add(3)),
        Point::new(*cubic.add(4), *cubic.add(5)),
    );

    let mut buf: Vec<f64> = Vec::with_capacity(FLATTEN_MAX_POINTS * 2);
    buf.push(start_x);
    buf.push(start_y);
    flatten_cubic_to_points(cb.p0, cb.p1, cb.p2, cb.p3, tolerance, &mut buf);

    let count = buf.len() / 2;
    let actual = if count > max_output { max_output } else { count };
    if actual > 0 {
        std::ptr::copy_nonoverlapping(buf.as_ptr(), output, actual * 2);
    }
    *out_count = actual;
    count <= max_output
}

#[no_mangle]
pub unsafe extern "C" fn quad_flatten(
    quad: *const f64,
    start_x: f64,
    start_y: f64,
    tolerance: f64,
    output: *mut f64,
    max_output: usize,
    out_count: *mut usize,
) -> bool {
    let qb = QuadBez::new(
        Point::new(start_x, start_y),
        Point::new(*quad.add(0), *quad.add(1)),
        Point::new(*quad.add(2), *quad.add(3)),
    );

    let mut buf: Vec<f64> = Vec::with_capacity(FLATTEN_MAX_POINTS * 2);
    buf.push(start_x);
    buf.push(start_y);
    flatten_quad_to_points(qb.p0, qb.p1, qb.p2, tolerance, &mut buf);

    let count = buf.len() / 2;
    let actual = if count > max_output { max_output } else { count };
    if actual > 0 {
        std::ptr::copy_nonoverlapping(buf.as_ptr(), output, actual * 2);
    }
    *out_count = actual;
    count <= max_output
}

#[repr(C)]
pub struct OracleFlattenSink {
    pub emit: unsafe extern "C" fn(*const f64),
}

#[no_mangle]
pub unsafe extern "C" fn bezpath_flatten(
    verbs: *const u8,
    coords: *const f64,
    verb_count: usize,
    tolerance: f64,
    sink: *const OracleFlattenSink,
) {
    let mut path: Vec<PathEl> = Vec::with_capacity(verb_count * 2);
    let mut coord_idx = 0usize;

    for vi in 0..verb_count {
        let verb = *verbs.add(vi);
        match verb {
            0 => {
                let pt = Point::new(*coords.add(coord_idx), *coords.add(coord_idx + 1));
                path.push(PathEl::MoveTo(pt));
                coord_idx += 2;
            }
            1 => {
                let pt = Point::new(*coords.add(coord_idx), *coords.add(coord_idx + 1));
                path.push(PathEl::LineTo(pt));
                coord_idx += 2;
            }
            2 => {
                let p1 = Point::new(*coords.add(coord_idx), *coords.add(coord_idx + 1));
                let p2 = Point::new(*coords.add(coord_idx + 2), *coords.add(coord_idx + 3));
                path.push(PathEl::QuadTo(p1, p2));
                coord_idx += 4;
            }
            3 => {
                let p1 = Point::new(*coords.add(coord_idx), *coords.add(coord_idx + 1));
                let p2 = Point::new(*coords.add(coord_idx + 2), *coords.add(coord_idx + 3));
                let p3 = Point::new(*coords.add(coord_idx + 4), *coords.add(coord_idx + 5));
                path.push(PathEl::CurveTo(p1, p2, p3));
                coord_idx += 6;
            }
            4 => {
                path.push(PathEl::ClosePath);
            }
            _ => {}
        }
    }

    let bezpath: BezPath = path.into_iter().collect();
    let mut line_buf = [0.0, 0.0];
    let emit = (*sink).emit;

    let mut current = Point::new(0.0, 0.0);
    for el in bezpath.iter() {
        match el {
            PathEl::MoveTo(pt) => {
                current = pt;
                line_buf[0] = pt.x;
                line_buf[1] = pt.y;
                emit(line_buf.as_ptr());
            }
            PathEl::LineTo(pt) => {
                current = pt;
                line_buf[0] = pt.x;
                line_buf[1] = pt.y;
                emit(line_buf.as_ptr());
            }
            PathEl::QuadTo(p1, p2) => {
                let mut tmp_path: BezPath = BezPath::new();
                tmp_path.push(PathEl::MoveTo(current));
                tmp_path.push(PathEl::QuadTo(p1, p2));
                let mut first = true;
                flatten(tmp_path, tolerance, |el| {
                    match el {
                        PathEl::MoveTo(p) | PathEl::LineTo(p) => {
                            if !first {
                                line_buf[0] = p.x;
                                line_buf[1] = p.y;
                                emit(line_buf.as_ptr());
                            }
                            first = false;
                        }
                        _ => {}
                    }
                });
                current = p2;
            }
            PathEl::CurveTo(p1, p2, p3) => {
                let mut tmp_path: BezPath = BezPath::new();
                tmp_path.push(PathEl::MoveTo(current));
                tmp_path.push(PathEl::CurveTo(p1, p2, p3));
                let mut first = true;
                flatten(tmp_path, tolerance, |el| {
                    match el {
                        PathEl::MoveTo(p) | PathEl::LineTo(p) => {
                            if !first {
                                line_buf[0] = p.x;
                                line_buf[1] = p.y;
                                emit(line_buf.as_ptr());
                            }
                            first = false;
                        }
                        _ => {}
                    }
                });
                current = p3;
            }
            PathEl::ClosePath => {
                current = Point::new(0.0, 0.0);
            }
        }
    }
}