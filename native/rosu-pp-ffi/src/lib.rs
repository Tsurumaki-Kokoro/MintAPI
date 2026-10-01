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
