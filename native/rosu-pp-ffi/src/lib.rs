use std::ffi::{CStr, c_char};
use rosu_pp::{Beatmap, Difficulty, Performance, any::DifficultyAttributes};

// ── Beatmap ───────────────────────────────────────────────────────────────────

pub struct BeatmapHandle(Beatmap);

#[unsafe(no_mangle)]
pub extern "C" fn beatmap_from_path(path: *const c_char) -> *mut BeatmapHandle {
    if path.is_null() { return std::ptr::null_mut(); }
    let path = unsafe { CStr::from_ptr(path) }.to_string_lossy();
    match Beatmap::from_path(path.as_ref()) {
        Ok(map) => Box::into_raw(Box::new(BeatmapHandle(map))),
        Err(_) => std::ptr::null_mut(),
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn beatmap_from_bytes(data: *const u8, len: usize) -> *mut BeatmapHandle {
    if data.is_null() { return std::ptr::null_mut(); }
    let bytes = unsafe { std::slice::from_raw_parts(data, len) };
    match Beatmap::from_bytes(bytes) {
        Ok(map) => Box::into_raw(Box::new(BeatmapHandle(map))),
        Err(_) => std::ptr::null_mut(),
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn beatmap_free(handle: *mut BeatmapHandle) {
    if !handle.is_null() { unsafe { drop(Box::from_raw(handle)) }; }
}

// ── Difficulty ────────────────────────────────────────────────────────────────

pub struct DifficultyHandle(Difficulty);

#[unsafe(no_mangle)]
pub extern "C" fn difficulty_new() -> *mut DifficultyHandle {
    Box::into_raw(Box::new(DifficultyHandle(Difficulty::new())))
}

#[unsafe(no_mangle)]
pub extern "C" fn difficulty_free(handle: *mut DifficultyHandle) {
    if !handle.is_null() { unsafe { drop(Box::from_raw(handle)) }; }
}

macro_rules! diff_setter {
    ($name:ident, $arg:ident: $ty:ty) => {
        #[unsafe(no_mangle)]
        pub extern "C" fn $name(handle: *mut DifficultyHandle, $arg: $ty) {
            let h = unsafe { &mut *handle };
            let d = std::mem::replace(&mut h.0, Difficulty::new());
            h.0 = d.$arg($arg);
        }
    };
}

diff_setter!(difficulty_mods, mods: u32);
diff_setter!(difficulty_clock_rate, clock_rate: f64);
diff_setter!(difficulty_passed_objects, passed_objects: u32);

#[unsafe(no_mangle)]
pub extern "C" fn difficulty_ar(handle: *mut DifficultyHandle, ar: f32, fixed: bool) {
    let h = unsafe { &mut *handle };
    let d = std::mem::replace(&mut h.0, Difficulty::new());
    h.0 = d.ar(ar, fixed);
}

#[unsafe(no_mangle)]
pub extern "C" fn difficulty_cs(handle: *mut DifficultyHandle, cs: f32, fixed: bool) {
    let h = unsafe { &mut *handle };
    let d = std::mem::replace(&mut h.0, Difficulty::new());
    h.0 = d.cs(cs, fixed);
}

#[unsafe(no_mangle)]
pub extern "C" fn difficulty_hp(handle: *mut DifficultyHandle, hp: f32, fixed: bool) {
    let h = unsafe { &mut *handle };
    let d = std::mem::replace(&mut h.0, Difficulty::new());
    h.0 = d.hp(hp, fixed);
}

#[unsafe(no_mangle)]
pub extern "C" fn difficulty_od(handle: *mut DifficultyHandle, od: f32, fixed: bool) {
    let h = unsafe { &mut *handle };
    let d = std::mem::replace(&mut h.0, Difficulty::new());
    h.0 = d.od(od, fixed);
}

#[unsafe(no_mangle)]
pub extern "C" fn difficulty_lazer(handle: *mut DifficultyHandle, lazer: bool) {
    let h = unsafe { &mut *handle };
    let d = std::mem::replace(&mut h.0, Difficulty::new());
    h.0 = d.lazer(lazer);
}

// ── DifficultyAttributes ──────────────────────────────────────────────────────

pub struct DifficultyAttrsHandle(DifficultyAttributes);

/// Calculate difficulty. Returns an owned DifficultyAttrsHandle; caller must free it.
#[unsafe(no_mangle)]
pub extern "C" fn difficulty_calculate(
    diff: *const DifficultyHandle,
    map: *const BeatmapHandle,
) -> *mut DifficultyAttrsHandle {
    let attrs = unsafe { &(*diff).0 }.calculate(unsafe { &(*map).0 });
    Box::into_raw(Box::new(DifficultyAttrsHandle(attrs)))
}

#[unsafe(no_mangle)]
pub extern "C" fn difficulty_attrs_free(handle: *mut DifficultyAttrsHandle) {
    if !handle.is_null() { unsafe { drop(Box::from_raw(handle)) }; }
}

#[unsafe(no_mangle)]
pub extern "C" fn difficulty_attrs_stars(handle: *const DifficultyAttrsHandle) -> f64 {
    unsafe { (*handle).0.stars() }
}

#[unsafe(no_mangle)]
pub extern "C" fn difficulty_attrs_max_combo(handle: *const DifficultyAttrsHandle) -> u32 {
    unsafe { (*handle).0.max_combo() }
}

/// 0=osu, 1=taiko, 2=catch, 3=mania
#[unsafe(no_mangle)]
pub extern "C" fn difficulty_attrs_mode(handle: *const DifficultyAttrsHandle) -> u8 {
    match unsafe { &(*handle).0 } {
        DifficultyAttributes::Osu(_) => 0,
        DifficultyAttributes::Taiko(_) => 1,
        DifficultyAttributes::Catch(_) => 2,
        DifficultyAttributes::Mania(_) => 3,
    }
}

// ── Performance ───────────────────────────────────────────────────────────────

// Performance<'map> holds a reference to the beatmap. We extend the lifetime
// to 'static via transmute; the caller must keep the beatmap alive.
pub struct PerformanceHandle(Performance<'static>);

/// Create a Performance builder from a beatmap. The beatmap must outlive this handle.
#[unsafe(no_mangle)]
pub extern "C" fn performance_from_beatmap(map: *const BeatmapHandle) -> *mut PerformanceHandle {
    let map_ref: &Beatmap = unsafe { &(*map).0 };
    let perf: Performance<'_> = Performance::new(map_ref);
    let perf: Performance<'static> = unsafe { std::mem::transmute(perf) };
    Box::into_raw(Box::new(PerformanceHandle(perf)))
}

/// Create a Performance builder from previously calculated difficulty attributes.
/// Takes ownership of the attrs handle (it is freed).
#[unsafe(no_mangle)]
pub extern "C" fn performance_from_attrs(attrs: *mut DifficultyAttrsHandle) -> *mut PerformanceHandle {
    let owned = unsafe { Box::from_raw(attrs) };
    let perf: Performance<'static> = Performance::new(owned.0);
    Box::into_raw(Box::new(PerformanceHandle(perf)))
}

#[unsafe(no_mangle)]
pub extern "C" fn performance_free(handle: *mut PerformanceHandle) {
    if !handle.is_null() { unsafe { drop(Box::from_raw(handle)) }; }
}

macro_rules! perf_setter {
    ($name:ident, $method:ident, $arg:ident: $ty:ty) => {
        #[unsafe(no_mangle)]
        pub extern "C" fn $name(handle: *mut PerformanceHandle, $arg: $ty) {
            let h = unsafe { &mut *handle };
            // Safety: we immediately put the value back; the 'static lifetime
            // is a FFI fiction — the beatmap is kept alive by the caller.
            let p = unsafe { std::ptr::read(&h.0) };
            let p = p.$method($arg);
            unsafe { std::ptr::write(&mut h.0, p) };
        }
    };
}

perf_setter!(performance_mods,           mods,           mods:     u32);
perf_setter!(performance_accuracy,       accuracy,       accuracy: f64);
perf_setter!(performance_combo,          combo,          combo:    u32);
perf_setter!(performance_n300,           n300,           n300:     u32);
perf_setter!(performance_n100,           n100,           n100:     u32);
perf_setter!(performance_n50,            n50,            n50:      u32);
perf_setter!(performance_misses,         misses,         misses:   u32);
perf_setter!(performance_n_geki,         n_geki,         n_geki:   u32);
perf_setter!(performance_n_katu,         n_katu,         n_katu:   u32);
perf_setter!(performance_passed_objects, passed_objects, passed_objects: u32);
perf_setter!(performance_clock_rate,     clock_rate,     clock_rate: f64);

#[unsafe(no_mangle)]
pub extern "C" fn performance_lazer(handle: *mut PerformanceHandle, lazer: bool) {
    let h = unsafe { &mut *handle };
    let p = unsafe { std::ptr::read(&h.0) };
    unsafe { std::ptr::write(&mut h.0, p.lazer(lazer)) };
}

// ── PerformanceResult ─────────────────────────────────────────────────────────

#[repr(C)]
pub struct PerformanceResult {
    pub pp: f64,
    pub stars: f64,
    pub pp_aim: f64,
    pub pp_speed: f64,
    pub pp_acc: f64,
    pub max_combo: u32,
    /// 0=osu, 1=taiko, 2=catch, 3=mania
    pub mode: u8,
}

/// Consume the performance handle and calculate. The handle is freed.
#[unsafe(no_mangle)]
pub extern "C" fn performance_calculate_v2(handle: *mut PerformanceHandle) -> PerformanceResult {
    let owned = unsafe { Box::from_raw(handle) };
    let attrs = owned.0.calculate();
    let (pp_aim, pp_speed, pp_acc) = match &attrs {
        rosu_pp::any::PerformanceAttributes::Osu(osu) => (osu.pp_aim, osu.pp_speed, osu.pp_acc),
        _ => (f64::NAN, f64::NAN, f64::NAN),
    };
    let mode = match &attrs {
        rosu_pp::any::PerformanceAttributes::Osu(_) => 0,
        rosu_pp::any::PerformanceAttributes::Taiko(_) => 1,
        rosu_pp::any::PerformanceAttributes::Catch(_) => 2,
        rosu_pp::any::PerformanceAttributes::Mania(_) => 3,
    };
    PerformanceResult { pp: attrs.pp(), stars: attrs.stars(), pp_aim, pp_speed, pp_acc, max_combo: attrs.max_combo(), mode }
}

// Keep the original ABI for already-running managed clients.
#[repr(C)]
pub struct LegacyPerformanceResult {
    pub pp: f64,
    pub stars: f64,
    pub max_combo: u32,
    pub mode: u8,
}

/// Consume the handle and return the original, component-free result layout.
#[unsafe(no_mangle)]
pub extern "C" fn performance_calculate(handle: *mut PerformanceHandle) -> LegacyPerformanceResult {
    let result = performance_calculate_v2(handle);
    LegacyPerformanceResult { pp: result.pp, stars: result.stars, max_combo: result.max_combo, mode: result.mode }
}

// Four-mode timeline for native preview selection. Values are local skill scores, not stars.
#[repr(C)]
#[derive(Clone, Copy)]
pub struct OsuStrainPoint {
    pub end_time_ms: f64,
    pub aim: f64,
    pub speed: f64,
}

pub struct OsuStrainsHandle {
    points: Vec<OsuStrainPoint>,
    first_object_ms: f64,
    last_object_ms: f64,
    section_ms: f64,
}

#[unsafe(no_mangle)]
pub extern "C" fn difficulty_osu_strains(diff: *const DifficultyHandle, map: *const BeatmapHandle) -> *mut OsuStrainsHandle {
    difficulty_preview_strains(diff, map, false, false)
}

#[unsafe(no_mangle)]
pub extern "C" fn difficulty_preview_strains(diff: *const DifficultyHandle, map: *const BeatmapHandle, inverse: bool, hold_off: bool) -> *mut OsuStrainsHandle {
    if diff.is_null() || map.is_null() { return std::ptr::null_mut(); }
    let (difficulty, map) = unsafe { (&(*diff).0, &(*map).0) };
    if map.hit_objects.is_empty() { return std::ptr::null_mut(); }
    let original_first = map.hit_objects[0].start_time;
    let mut prepared = map.clone();
    if prepared.mode == rosu_pp::model::mode::GameMode::Mania {
        prepare_preview_mania(&mut prepared, inverse, hold_off);
    }
    let map = &prepared;
    if map.hit_objects.is_empty() {
        return Box::into_raw(Box::new(OsuStrainsHandle { points: Vec::new(), first_object_ms: original_first,
            last_object_ms: original_first, section_ms: 400.0 }));
    }
    let Ok(strains) = difficulty.checked_strains(map) else { return std::ptr::null_mut(); };
    let rate = map.attributes().difficulty(difficulty).build().clock_rate();
    let (section, values): (f64, Vec<(f64, f64)>) = match strains {
        rosu_pp::any::Strains::Osu(s) => (400.0, s.aim.into_iter().zip(s.speed).collect()),
        rosu_pp::any::Strains::Taiko(s) => {
            // Native-map local approximation of rosu-pp 4's weighted skill combination.
            // Global pattern/length bonuses are intentionally excluded from local ranking.
            let values = s.color.iter().zip(&s.stamina).zip(&s.rhythm).zip(&s.reading)
                .map(|(((&color, &stamina), &rhythm), &reading)| {
                    let physical = ((color * 0.375).powf(1.5) + (stamina * 0.445).powf(1.5)).powf(1.0 / 1.5);
                    ((physical.powi(2) + (rhythm * 0.75).powi(2) + (reading * 0.1).powi(2)).sqrt(), 0.0)
                }).collect();
            (400.0, values)
        }
        rosu_pp::any::Strains::Catch(s) => (750.0, s.movement.into_iter().map(|s| (s, 0.0)).collect()),
        rosu_pp::any::Strains::Mania(s) => (400.0, s.strains.into_iter().map(|s| (s, 0.0)).collect()),
    };
    let (processed_times, last_object_ms) = preview_object_times(map);
    let second = processed_times.get(if map.mode == rosu_pp::model::mode::GameMode::Taiko { 2 } else { 1 }).or(processed_times.first()).copied().unwrap_or(map.hit_objects[0].start_time);
    let first_end = (second / rate / section).ceil() * section;
    let points = values.into_iter().enumerate().map(|(index, (aim, speed))| {
        OsuStrainPoint { end_time_ms: (first_end + index as f64 * section) * rate, aim, speed }
    }).collect();
    Box::into_raw(Box::new(OsuStrainsHandle {
        points,
        first_object_ms: original_first,
        last_object_ms,
        section_ms: section * rate,
    }))
}

#[unsafe(no_mangle)]
pub extern "C" fn osu_strains_count(handle: *const OsuStrainsHandle) -> usize {
    if handle.is_null() { return 0; }
    unsafe { (*handle).points.len() }
}

#[unsafe(no_mangle)]
pub extern "C" fn osu_strains_point(handle: *const OsuStrainsHandle, index: usize, output: *mut OsuStrainPoint) -> bool {
    if handle.is_null() || output.is_null() { return false; }
    if let Some(point) = unsafe { &(*handle).points }.get(index) {
        unsafe { *output = *point; }
        true
    } else { false }
}

#[unsafe(no_mangle)]
pub extern "C" fn osu_strains_bounds(handle: *const OsuStrainsHandle, first: *mut f64, last: *mut f64, section: *mut f64) {
    if handle.is_null() || first.is_null() || last.is_null() || section.is_null() { return; }
    unsafe { *first = (*handle).first_object_ms; *last = (*handle).last_object_ms; *section = (*handle).section_ms; }
}

#[unsafe(no_mangle)]
pub extern "C" fn osu_strains_free(handle: *mut OsuStrainsHandle) {
    if !handle.is_null() { unsafe { drop(Box::from_raw(handle)) }; }
}

// Reproduce Catch palpable-event timing (tiny droplets and bananas do not generate strain).
fn preview_object_times(map: &rosu_pp::Beatmap) -> (Vec<f64>, f64) {
    use rosu_pp::model::hit_object::HitObjectKind;
    use rosu_map::section::hit_objects::{Curve, CurveBuffers, SliderEventsIter, SliderEventType};
    let catch = map.mode == rosu_pp::model::mode::GameMode::Catch;
    let mut times = Vec::new();
    let mut last: f64 = map.hit_objects[0].start_time;
    let mut buffers = CurveBuffers::default();
    let mut ticks = Vec::new();
    for h in &map.hit_objects {
        match &h.kind {
            HitObjectKind::Slider(slider) => {
                let beat = map.timing_points.get(map.timing_points.binary_search_by(|p| p.time.total_cmp(&h.start_time)).unwrap_or_else(|i| i.saturating_sub(1))).map_or(500.0, |p| p.beat_len);
                let sv = map.difficulty_points.binary_search_by(|p| p.time.total_cmp(&h.start_time)).map_or_else(|i| i.checked_sub(1), Some).and_then(|i| map.difficulty_points.get(i)).map_or(1.0, |p| p.slider_velocity);
                let multiplier = f64::from(((-100.0 / sv) as f32).clamp(-10000.0, -10.0).abs()) / 100.0;
                let velocity = 100.0 * map.slider_multiplier / (beat * multiplier);
                let curve = Curve::new(map.mode, &slider.control_points, slider.expected_dist, &mut buffers);
                let span_duration = curve.dist() / velocity;
                last = last.max(h.start_time + span_duration * slider.span_count() as f64);
                if catch {
                    let tick_dist = velocity * beat / map.slider_tick_rate * if map.version < 8 { 1.0 / sv } else { 1.0 };
                    times.extend(SliderEventsIter::new(h.start_time, span_duration, velocity, tick_dist,
                        curve.dist(), slider.span_count() as i32, &mut ticks)
                        .filter(|e| !matches!(e.kind, SliderEventType::LastTick)).map(|e| e.time));
                } else { times.push(h.start_time); }
            }
            HitObjectKind::Spinner(s) => { last = last.max(h.start_time + s.duration); if !catch { times.push(h.start_time); } }
            HitObjectKind::Hold(s) => { last = last.max(h.start_time + s.duration); if !catch { times.push(h.start_time); } }
            HitObjectKind::Circle => { last = last.max(h.start_time); times.push(h.start_time); }
        }
    }
    times.sort_by(f64::total_cmp);
    (times, last)
}

// Match the preview engine's native Mania IN/HO transformations, rather than assuming
// rosu-pp's similarly named mods have identical rendering semantics.
fn prepare_preview_mania(map: &mut Beatmap, inverse: bool, hold_off: bool) {
    use rosu_pp::model::hit_object::{HitObjectKind, HoldNote};
    if inverse {
        let columns = map.cs.round_ties_even().max(1.0) as usize;
        let mut lanes = vec![Vec::new(); columns];
        for h in &map.hit_objects {
            let lane = ((h.pos.x as f64 * columns as f64 / 512.0).floor() as usize).min(columns - 1);
            lanes[lane].push(h.clone());
        }
        let mut objects = Vec::new();
        for lane in lanes {
            for pair in lane.windows(2) {
                let mut h = pair[0].clone();
                let next = pair[1].start_time;
                let beat = map.timing_points.iter().rev().find(|p| p.time <= next).map_or(500.0, |p| p.beat_len);
                let gap = next - h.start_time;
                let end = (h.start_time + (gap / 2.0).max(gap - beat / 4.0)).round_ties_even().max(h.start_time);
                h.kind = if end > h.start_time { HitObjectKind::Hold(HoldNote { duration: end - h.start_time }) } else { HitObjectKind::Circle };
                objects.push(h);
            }
        }
        objects.sort_by(|a, b| a.start_time.total_cmp(&b.start_time));
        map.hit_objects = objects;
    }
    if hold_off {
        for h in &mut map.hit_objects { h.kind = HitObjectKind::Circle; }
    }
}
