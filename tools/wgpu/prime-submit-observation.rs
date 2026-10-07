//! Bounded CPU-only observations for an explicitly owned check submission.
//! No graphics object, callback, user pointer, logger ownership or wait is retained.
use std::{cell::RefCell, fmt::Write as _, io::Write as _, marker::PhantomData,
    panic::{catch_unwind, AssertUnwindSafe}, rc::Rc, sync::{atomic::{AtomicU64, Ordering}, Mutex},
    thread::{self, ThreadId}, time::{Instant, SystemTime, UNIX_EPOCH}};

pub const MAGIC: u32 = 0x3153_424f; // OBS1 in the paired little-endian scalar ABI.
pub const MAX_SUBMIT: u32 = 64;
pub const MAX_READ: u32 = 128;
pub const MAX_FIXTURE: u32 = 1024;
pub const MAX_PROCESS: u32 = 2048;
const MAX_FIXTURES: usize = 16;
const MAX_RECORD_BYTES: usize = 512;

#[repr(C)]
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Identity {
    pub fixture: u64,
    pub scope: u64,
    pub managed_thread: u32,
    pub read: u16,
    pub submit: u16,
    pub magic: u32,
    pub reserved: u32,
}
const _: [(); 32] = [(); std::mem::size_of::<Identity>()];
const _: [(); 8] = [(); std::mem::align_of::<Identity>()];
impl Identity {
    fn valid(self) -> bool {
        cfg!(target_endian = "little") && self.magic == MAGIC && self.reserved == 0
            && self.fixture != 0 && self.scope != 0 && self.managed_thread != 0
            && (1..=8).contains(&self.read) && (1..=8).contains(&self.submit)
    }
}

pub fn enabled(d: Option<&str>, v: Option<&str>, g: Option<&str>) -> bool {
    d == Some("1") && v == Some("1") && g == Some("1")
}

struct Fixture { id: u64, owner: ThreadId, scope: u64, managed_thread: u32,
    started: Instant, attempts: u32, reads: [u32; 8] }
struct Global { attempts: u32, fixtures: Vec<Fixture> }
static GLOBAL: Mutex<Global> = Mutex::new(Global { attempts: 0, fixtures: Vec::new() });
static TOKENS: AtomicU64 = AtomicU64::new(0);
#[derive(Clone, Copy)]
struct Active { token: u64, identity: Identity, native_thread: ThreadId,
    fixture_start: Instant, submit_start: Instant, attempts: u32, depth: u32, suppressed: u32 }
thread_local! { static ACTIVE: RefCell<Option<Active>> = const { RefCell::new(None) }; }

pub struct SubmissionGuard { token: Option<u64>, suppression: Option<u64>, _not_send: PhantomData<Rc<()>> }
impl SubmissionGuard {
    fn empty() -> Self { Self { token: None, suppression: None, _not_send: PhantomData } }
    pub fn mark(&self, phase: &str, edge: &str) {
        if let Some(token) = self.token { mark_at(Some(token), 0, phase, edge); }
    }
}
impl Drop for SubmissionGuard {
    fn drop(&mut self) {
        let token = self.token.take();
        let suppression = self.suppression.take();
        let _ = catch_unwind(AssertUnwindSafe(|| {
            let _ = ACTIVE.try_with(|slot| {
                if let Ok(mut slot) = slot.try_borrow_mut() {
                    if token.is_some() && slot.as_ref().map(|c| c.token) == token { *slot = None; }
                    else if let Some(c) = slot.as_mut() {
                        if suppression == Some(c.token) && c.suppressed != 0 { c.suppressed -= 1; }
                    }
                }
            });
        })); // No sink or clock in teardown, even after an original error/panic.
    }
}

/// Copies only the scalar metadata; callers must supply a live readable
/// identity allocation when the pointer is non-null and aligned.
pub unsafe fn copy_identity(pointer: *const Identity) -> Option<Identity> {
    if pointer.is_null() || (pointer as usize) % std::mem::align_of::<Identity>() != 0 { return None; }
    Some(pointer.read())
}

// A nested observed ABI must not attribute its original Core calls to an outer
// ABI that has not yet entered Core. Suppression never changes the outer token.
fn suppress_nested() -> Option<SubmissionGuard> {
    catch_unwind(AssertUnwindSafe(|| ACTIVE.try_with(|slot| {
        let Ok(mut slot) = slot.try_borrow_mut() else { return Some(SubmissionGuard::empty()); };
        let Some(c) = slot.as_mut() else { return None; };
        let Some(suppressed) = c.suppressed.checked_add(1) else { *slot = None; return Some(SubmissionGuard::empty()); };
        c.suppressed = suppressed;
        Some(SubmissionGuard { token: None, suppression: Some(c.token), _not_send: PhantomData })
    }).unwrap_or_else(|_| Some(SubmissionGuard::empty())))).unwrap_or_else(|_| Some(SubmissionGuard::empty()))
}

pub fn begin_submission(identity: Option<Identity>) -> SubmissionGuard {
    catch_unwind(AssertUnwindSafe(|| {
    if let Some(suppression) = suppress_nested() { return suppression; }
    // The original Submit ABI never invokes this function or samples environment.
    let Some(identity) = identity.filter(|i| i.valid()) else { return SubmissionGuard::empty(); };
    let d = std::env::var("PRIME_WGPU_SHADER_DIAGNOSTICS").ok();
    let v = std::env::var("PRIME_WGPU_VALIDATION").ok();
    let g = std::env::var("PRIME_WGPU_GPU_VALIDATION").ok();
    begin_owned(identity, enabled(d.as_deref(), v.as_deref(), g.as_deref()))
    })).unwrap_or_else(|_| SubmissionGuard::empty())
}
fn begin_owned(identity: Identity, admission: bool) -> SubmissionGuard {
    if let Some(suppression) = suppress_nested() { return suppression; }
    if !admission || !identity.valid() { return SubmissionGuard::empty(); }
    catch_unwind(AssertUnwindSafe(|| {
        ACTIVE.try_with(|slot| {
            let Ok(mut slot) = slot.try_borrow_mut() else { return SubmissionGuard::empty(); };
            if slot.is_some() { return SubmissionGuard::empty(); }
            let Ok(mut global) = GLOBAL.try_lock() else { return SubmissionGuard::empty(); };
            if global.attempts >= MAX_PROCESS { return SubmissionGuard::empty(); }
            let owner = thread::current().id();
            let index = match global.fixtures.iter().position(|f| f.id == identity.fixture) {
                Some(index) => index,
                None => {
                    if global.fixtures.len() >= MAX_FIXTURES { return SubmissionGuard::empty(); }
                    global.fixtures.push(Fixture { id: identity.fixture, owner, scope: identity.scope,
                        managed_thread: identity.managed_thread, started: Instant::now(), attempts: 0, reads: [0; 8] });
                    global.fixtures.len() - 1
                }
            };
            let fixture = &global.fixtures[index];
            if fixture.owner != owner || fixture.scope != identity.scope
                || fixture.managed_thread != identity.managed_thread || fixture.attempts >= MAX_FIXTURE
                || fixture.reads[usize::from(identity.read - 1)] >= MAX_READ {
                return SubmissionGuard::empty();
            }
            let Ok(previous) = TOKENS.fetch_update(Ordering::Relaxed, Ordering::Relaxed, |n| n.checked_add(1))
                else { return SubmissionGuard::empty(); };
            let token = previous + 1;
            *slot = Some(Active { token, identity, native_thread: owner, fixture_start: fixture.started,
                submit_start: Instant::now(), attempts: 0, depth: 0, suppressed: 0 });
            SubmissionGuard { token: Some(token), suppression: None, _not_send: PhantomData }
        }).unwrap_or_else(|_| SubmissionGuard::empty())
    })).unwrap_or_else(|_| SubmissionGuard::empty())
}

pub struct CoreGuard { token: Option<u64>, depth: u32, _not_send: PhantomData<Rc<()>> }
impl Drop for CoreGuard {
    fn drop(&mut self) {
        let Some(token) = self.token.take() else { return; };
        let _ = catch_unwind(AssertUnwindSafe(|| {
            let _ = ACTIVE.try_with(|slot| {
                if let Ok(mut slot) = slot.try_borrow_mut() {
                    if let Some(c) = slot.as_mut() {
                        if c.token == token {
                            if c.depth == self.depth { c.depth -= 1; }
                            else { *slot = None; } // Unexpected guard ordering fails closed.
                        }
                    }
                }
            });
        }));
    }
}
pub fn enter_core() -> CoreGuard {
    let empty = || CoreGuard { token: None, depth: 0, _not_send: PhantomData };
    catch_unwind(AssertUnwindSafe(|| ACTIVE.try_with(|slot| {
        let Ok(mut slot) = slot.try_borrow_mut() else { return empty(); };
        let Some(c) = slot.as_mut() else { return empty(); };
        let Some(depth) = c.depth.checked_add(1) else { return empty(); };
        c.depth = depth;
        CoreGuard { token: Some(c.token), depth, _not_send: PhantomData }
    }).unwrap_or_else(|_| empty()))).unwrap_or_else(|_| empty())
}
pub fn mark_core(phase: &str, edge: &str) { mark_at(None, 1, phase, edge); }

#[derive(Clone, Copy)]
struct Seed { active: Active, sequence: u32 }
fn known_phase(phase: &str) -> bool {
    matches!(phase, "checked-abi" | "core-preparation" | "core-hal-submit" | "core-post-submit"
        | "core-life-tracker" | "core-maintain-poll" | "core-post-life" | "core-callbacks"
        | "dx12-temp-lists-lock" | "dx12-execute-command-lists" | "dx12-signal")
}
fn reserve(token: Option<u64>, depth: u32, phase: &str, edge: &str) -> Option<Seed> {
    if !known_phase(phase) || !matches!(edge, "START" | "DONE") { return None; }
    ACTIVE.try_with(|slot| {
        let mut slot = slot.try_borrow_mut().ok()?;
        let c = slot.as_mut()?;
        if c.suppressed != 0 || c.depth != depth || token.map(|t| t != c.token).unwrap_or(false) || c.attempts >= MAX_SUBMIT { return None; }
        let mut global = GLOBAL.try_lock().ok()?;
        let index = global.fixtures.iter().position(|f| f.id == c.identity.fixture)?;
        if global.attempts >= MAX_PROCESS || global.fixtures[index].attempts >= MAX_FIXTURE
            || global.fixtures[index].reads[usize::from(c.identity.read - 1)] >= MAX_READ { return None; }
        // Reserve all attempts before sampling clocks, allocating/formating or
        // invoking a sink. No TLS borrow or mutex guard survives this function.
        global.attempts += 1;
        let f = &mut global.fixtures[index];
        f.attempts += 1;
        f.reads[usize::from(c.identity.read - 1)] += 1;
        c.attempts += 1;
        Some(Seed { active: *c, sequence: f.attempts })
    }).ok().flatten()
}
#[derive(Clone, Copy)]
struct Sample { unix_seconds: u64, unix_nanos: u32, fixture_nanos: u128, submit_nanos: u128 }
fn clock(seed: Seed) -> Option<Sample> {
    let utc = SystemTime::now().duration_since(UNIX_EPOCH).ok()?;
    let now = Instant::now();
    Some(Sample { unix_seconds: utc.as_secs(), unix_nanos: utc.subsec_nanos(),
        fixture_nanos: now.checked_duration_since(seed.active.fixture_start)?.as_nanos(),
        submit_nanos: now.checked_duration_since(seed.active.submit_start)?.as_nanos() })
}
fn observe_with<F, W>(token: Option<u64>, depth: u32, phase: &str, edge: &str, sample: F, sink: W)
where F: FnOnce(Seed) -> Option<Sample>, W: FnOnce(&str) -> std::io::Result<()> {
    let _ = catch_unwind(AssertUnwindSafe(|| {
        let Some(seed) = reserve(token, depth, phase, edge) else { return; };
        let Some(time) = sample(seed) else { return; };
        let mut line = String::with_capacity(384);
        let i = seed.active.identity;
        let _ = write!(line, "DIAGNOSTIC native-submit fixture={} scope={} read={} submit={} managedThread={} nativeThread={:?} pid={} phase={} {} utcUnixSeconds={} utcNanos={:09} fixtureElapsedNs={} submitElapsedNs={} sequence={}",
            i.fixture, i.scope, i.read, i.submit, i.managed_thread, seed.active.native_thread,
            std::process::id(), phase, edge, time.unix_seconds, time.unix_nanos,
            time.fixture_nanos, time.submit_nanos, seed.sequence);
        if line.len() <= MAX_RECORD_BYTES { let _ = sink(&line); }
    }));
}
fn mark_at(token: Option<u64>, depth: u32, phase: &str, edge: &str) {
    observe_with(token, depth, phase, edge, clock, |line| {
        // Separate recognized lane: never enters the already-saturated generic
        // native logger/callback ring. Its independent attempt caps still apply.
        writeln!(std::io::stderr().lock(), "{}", line)
    });
}

#[cfg(test)]
mod prime_submit_observation_tests {
    use super::*;
    use std::sync::atomic::{AtomicU32, Ordering as O};
    static SERIAL: Mutex<()> = Mutex::new(());
    fn reset() {
        ACTIVE.with(|s| *s.borrow_mut() = None);
        let mut g = GLOBAL.lock().unwrap();g.attempts=0;g.fixtures.clear();TOKENS.store(0,O::Relaxed);
    }
    fn id(fixture:u64,read:u16,submit:u16)->Identity { Identity {fixture,scope:1,managed_thread:7,read,submit,magic:MAGIC,reserved:0} }
    fn fake(_:Seed)->Option<Sample> { Some(Sample {unix_seconds:1_790_000_000,unix_nanos:123_456_789,fixture_nanos:200,submit_nanos:100}) }
    fn emit()->bool {
        let hit=std::cell::Cell::new(false);
        observe_with(None,1,"core-hal-submit","START",fake, |_|{hit.set(true);Ok(())});hit.get()
    }
    #[test] fn exact_flags_and_invalid_identity_never_admit_or_sample_default() {
        let _serial=SERIAL.lock().unwrap();reset();
        for d in [None,Some(""),Some("0"),Some("true"),Some(" 1"),Some("1")]
        { for v in [None,Some("0"),Some("1")] { for g in [None,Some("0"),Some("1")]
        { assert_eq!(enabled(d,v,g),d==Some("1")&&v==Some("1")&&g==Some("1")); } } }
        let samples=AtomicU32::new(0);
        for _ in 0..3000 { observe_with(None,1,"core-hal-submit","START", |_|{samples.fetch_add(1,O::Relaxed);fake(Seed{active:Active{token:0,identity:id(1,1,1),native_thread:thread::current().id(),fixture_start:Instant::now(),submit_start:Instant::now(),attempts:0,depth:0,suppressed:0},sequence:0})}, |_|panic!("default sink accessed")); }
        assert_eq!(samples.load(O::Relaxed),0);assert_eq!(GLOBAL.lock().unwrap().attempts,0);
        for invalid in [Identity {fixture:0,..id(1,1,1)},Identity{scope:0,..id(1,1,1)},Identity{managed_thread:0,..id(1,1,1)},Identity{read:9,..id(1,1,1)},Identity{submit:9,..id(1,1,1)},Identity{magic:0,..id(1,1,1)},Identity{reserved:1,..id(1,1,1)}] { assert!(begin_owned(invalid,true).token.is_none()); }
        assert!(begin_owned(id(1,1,1),false).token.is_none());assert_eq!(GLOBAL.lock().unwrap().fixtures.len(),0);
    }
    #[test] fn scalar_abi_is_exact_32_bytes_and_roundtrips_reserved_magic_fields() {
        let _serial=SERIAL.lock().unwrap();reset();
        assert_eq!(std::mem::size_of::<Identity>(),32);assert_eq!(std::mem::align_of::<Identity>(),8);
        let v=id(0x0807_0605_0403_0201,6,3);let bytes=unsafe{std::slice::from_raw_parts(&v as *const Identity as *const u8,32)};
        assert_eq!(&bytes[0..8],&v.fixture.to_le_bytes());assert_eq!(&bytes[8..16],&v.scope.to_le_bytes());assert_eq!(&bytes[16..20],&7u32.to_le_bytes());assert_eq!(&bytes[20..22],&6u16.to_le_bytes());assert_eq!(&bytes[22..24],&3u16.to_le_bytes());assert_eq!(&bytes[24..28],b"OBS1");assert_eq!(&bytes[28..32],&[0;4]);
        let decoded=Identity{fixture:u64::from_le_bytes(bytes[0..8].try_into().unwrap()),scope:u64::from_le_bytes(bytes[8..16].try_into().unwrap()),managed_thread:u32::from_le_bytes(bytes[16..20].try_into().unwrap()),read:u16::from_le_bytes(bytes[20..22].try_into().unwrap()),submit:u16::from_le_bytes(bytes[22..24].try_into().unwrap()),magic:u32::from_le_bytes(bytes[24..28].try_into().unwrap()),reserved:u32::from_le_bytes(bytes[28..32].try_into().unwrap())};assert_eq!(decoded,v);assert!(decoded.valid());
    }
    #[test] fn scalar_pointer_copy_rejects_null_misaligned_and_retains_no_borrow() {
        let _serial=SERIAL.lock().unwrap();reset();let mut original=id(1,6,2);
        assert!(unsafe{copy_identity(std::ptr::null())}.is_none());
        let misaligned=unsafe{(&original as *const Identity as *const u8).add(1)} as *const Identity;
        assert!(unsafe{copy_identity(misaligned)}.is_none());
        let copied=unsafe{copy_identity(&original)}.unwrap();original.fixture=99;
        assert_eq!(copied.fixture,1);assert_eq!(original.fixture,99);
    }
    #[test] fn nested_core_default_and_observed_calls_cannot_steal_outer_identity() {
        let _serial=SERIAL.lock().unwrap();reset();let outer=begin_owned(id(1,1,1),true);let core=enter_core();assert!(emit());
        {let nested=begin_owned(id(2,1,1),true);assert!(nested.token.is_none());let inner=enter_core();assert!(!emit());drop(inner);drop(nested);}
        assert!(emit());drop(core);assert!(!emit());drop(outer);assert!(!emit());assert_eq!(GLOBAL.lock().unwrap().attempts,2);
    }
    #[test] fn nested_observed_abi_before_core_and_invalid_metadata_cannot_attribute_to_outer() {
        let _serial=SERIAL.lock().unwrap();reset();let outer=begin_owned(id(1,1,1),true);
        { let nested=begin_owned(id(2,1,1),true);assert!(nested.token.is_none());
            let _core=enter_core();assert!(!emit()); }
        { let invalid=begin_submission(None);let _core=enter_core();assert!(!emit());drop(invalid); }
        { let disabled=begin_owned(id(3,1,1),false);let _core=enter_core();assert!(!emit());drop(disabled); }
        let _core=enter_core();assert!(emit());drop(outer);assert!(!emit());
        assert_eq!(GLOBAL.lock().unwrap().attempts,1);
    }
    #[test] fn stale_suppression_guard_cannot_revoke_replacement_token() {
        let _serial=SERIAL.lock().unwrap();reset();let outer=begin_owned(id(1,1,1),true);
        let stale=begin_owned(id(2,1,1),true);assert!(stale.suppression.is_some());drop(outer);
        let replacement=begin_owned(id(3,1,1),true);let _core=enter_core();assert!(emit());
        drop(stale);assert!(emit());drop(replacement);assert!(!emit());
        assert_eq!(GLOBAL.lock().unwrap().attempts,2);
    }
    #[test] fn unexpected_guard_drop_order_fails_closed_without_revoking_a_new_scope() {
        let _serial=SERIAL.lock().unwrap();reset();let original=begin_owned(id(1,1,1),true);
        let a=enter_core();let b=enter_core();drop(a);assert!(!emit());
        let replacement=begin_owned(id(2,1,1),true);assert!(replacement.token.is_some());
        drop(b);drop(original);let _core=enter_core();assert!(emit());drop(replacement);assert!(!emit());
    }
    #[test] fn managed_native_golden_scalar_bytes_roundtrip_without_graphics() {
        let _serial=SERIAL.lock().unwrap();reset();
        let golden=Identity{fixture:0x0807_0605_0403_0201,scope:0x1817_1615_1413_1211,
            managed_thread:0x2423_2221,read:6,submit:3,magic:MAGIC,reserved:0};
        let bytes=unsafe{std::slice::from_raw_parts(&golden as *const Identity as *const u8,32)};
        let expected=[1,2,3,4,5,6,7,8,0x11,0x12,0x13,0x14,0x15,0x16,0x17,0x18,
            0x21,0x22,0x23,0x24,6,0,3,0,b'O',b'B',b'S',b'1',0,0,0,0];
        assert_eq!(bytes,&expected);assert!(golden.valid());
        let Some(dir)=std::env::var_os("PRIME_SUBMIT_ABI_TEST_DIRECTORY") else { return; };
        let dir=std::path::PathBuf::from(dir);
        let managed=std::fs::read(dir.join("managed-golden.bin")).unwrap();
        assert_eq!(managed.as_slice(),bytes);
        let mut copied=std::mem::MaybeUninit::<Identity>::uninit();
        unsafe{std::ptr::copy_nonoverlapping(managed.as_ptr(),copied.as_mut_ptr() as *mut u8,32);
            assert_eq!(copy_identity(copied.as_ptr()).unwrap(),golden);}
        std::fs::write(dir.join("native-golden.bin"),bytes).unwrap();
    }
    #[test] fn foreign_thread_identity_cannot_consume_existing_fixture_or_outer_events() {
        let _serial=SERIAL.lock().unwrap();reset();let outer=begin_owned(id(1,1,1),true);let core=enter_core();assert!(emit());
        thread::spawn(||{assert!(!emit());let foreign=begin_owned(id(1,1,1),true);assert!(foreign.token.is_none());let _core=enter_core();assert!(!emit());}).join().unwrap();
        assert!(emit());drop(core);drop(outer);assert_eq!(GLOBAL.lock().unwrap().attempts,2);
    }
    #[test] fn sink_reentry_has_no_tls_borrow_or_global_lock_held() {
        let _serial=SERIAL.lock().unwrap();reset();let outer=begin_owned(id(1,1,1),true);let _core=enter_core();
        observe_with(None,1,"core-hal-submit","START",fake, |_|{assert!(ACTIVE.with(|s|s.try_borrow_mut().is_ok()));assert!(GLOBAL.try_lock().is_ok());let inner=enter_core();assert!(!emit());drop(inner);Ok(())});assert!(emit());drop(outer);
    }
    #[test] fn errors_panics_and_scope_teardown_preserve_original_results_and_restore_tls() {
        let _serial=SERIAL.lock().unwrap();reset();
        let original=catch_unwind(AssertUnwindSafe(||{let _scope=begin_owned(id(1,1,1),true);let _core=enter_core();observe_with(None,1,"core-hal-submit","START", |_|panic!("diagnostic clock"), |_|Ok(()));observe_with(None,1,"core-hal-submit","START",fake, |_|Err(std::io::Error::other("diagnostic sink")));panic!("original submission error");}));
        assert_eq!(*original.unwrap_err().downcast::<&str>().unwrap(),"original submission error");assert!(!emit());assert!(begin_owned(id(2,1,1),true).token.is_some());assert_eq!(GLOBAL.lock().unwrap().attempts,2);
    }
    #[test] fn failed_clock_and_writer_attempts_still_enforce_per_submit_bound() {
        let _serial=SERIAL.lock().unwrap();reset();let _scope=begin_owned(id(1,1,1),true);let _core=enter_core();let clock_calls=AtomicU32::new(0);
        for _ in 0..1000 {observe_with(None,1,"core-hal-submit","START", |_|{clock_calls.fetch_add(1,O::Relaxed);None}, |_|panic!("missing clock reached sink"));}
        assert_eq!(clock_calls.load(O::Relaxed),MAX_SUBMIT);assert_eq!(GLOBAL.lock().unwrap().attempts,MAX_SUBMIT);
    }
    #[test] fn worst_five_reads_reserve_room_for_sixth_without_pre_fixture_consumption() {
        let _serial=SERIAL.lock().unwrap();reset();for _ in 0..3000 {assert!(!emit());}
        for read in 1..=5 {for submit in 1..=2 {let _scope=begin_owned(id(1,read,submit),true);let _core=enter_core();for _ in 0..100 {let _=emit();}}}
        assert_eq!(GLOBAL.lock().unwrap().attempts,640);
        for submit in 1..=2 {let _scope=begin_owned(id(1,6,submit),true);let _core=enter_core();for _ in 0..64 {assert!(emit());}}
        assert_eq!(GLOBAL.lock().unwrap().attempts,768);
        let _scope=begin_owned(id(1,6,3),true);assert!(_scope.token.is_none());assert_eq!(GLOBAL.lock().unwrap().attempts,768);
    }
    #[test] fn fixture_and_process_attempt_caps_are_shared_once_across_read_submissions() {
        let _serial=SERIAL.lock().unwrap();reset();for fixture in 1..=2 {for read in 1..=8 {for submit in 1..=2 {let _scope=begin_owned(id(fixture,read,submit),true);let _core=enter_core();for _ in 0..64 {assert!(emit());}}}}
        assert_eq!(GLOBAL.lock().unwrap().attempts,MAX_PROCESS);assert!(begin_owned(id(3,1,1),true).token.is_none());
    }
    #[test] fn metadata_uses_owned_identity_cpu_clocks_and_a_separate_bounded_lane() {
        let _serial=SERIAL.lock().unwrap();reset();let scope=begin_owned(id(1,6,2),true);let _core=enter_core();let text=RefCell::new(String::new());
        observe_with(None,1,"dx12-execute-command-lists","START",fake, |s|{*text.borrow_mut()=s.to_string();Ok(())});let text=text.into_inner();assert!(text.starts_with("DIAGNOSTIC native-submit fixture=1 scope=1 read=6 submit=2 managedThread=7 nativeThread=ThreadId("));assert!(text.contains("phase=dx12-execute-command-lists START utcUnixSeconds=1790000000 utcNanos=123456789 fixtureElapsedNs=200 submitElapsedNs=100 sequence=1"));assert!(text.len()<512);assert!(!text.contains("[wgpu-validation]"));drop(scope);
    }
}
