// HRESULT constants from Winerror.h; this pure policy is also compiled without
// a Windows SDK so regression coverage exercises the actual HAL decision code.
// https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/dxgi-error
// https://learn.microsoft.com/en-us/windows/win32/com/com-error-codes-10
#[derive(Debug, PartialEq)]
pub enum PresentOutcome { Success, Outdated, SurfaceLost, DeviceLost, OutOfMemory, Failure }

pub fn tearing_supported(query_succeeded: bool, value: i32) -> bool {
    query_succeeded && value != 0
}

pub fn present_outcome(result: i32) -> PresentOutcome {
    match result as u32 {
        0x087A0001 => PresentOutcome::Outdated, // DXGI_STATUS_OCCLUDED (no Occluded at this pin)
        0x887A0005 | 0x887A0006 | 0x887A0007 | 0x887A0020 => PresentOutcome::DeviceLost,
        0x887A0026 => PresentOutcome::SurfaceLost, // DXGI_ERROR_ACCESS_LOST
        0x8007000E => PresentOutcome::OutOfMemory,
        _ if result >= 0 => PresentOutcome::Success,
        _ => PresentOutcome::Failure,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn tearing_requires_success_and_true_output() {
        assert!(!tearing_supported(true, 0));
        assert!(tearing_supported(true, 1));
        assert!(tearing_supported(true, -1));
        assert!(!tearing_supported(false, 1));
    }
    #[test]
    fn present_propagates_failures_and_occlusion() {
        assert_eq!(present_outcome(0), PresentOutcome::Success);
        assert_eq!(present_outcome(1), PresentOutcome::Success);
        assert_eq!(present_outcome(0x087A0001), PresentOutcome::Outdated);
        for result in [0x887A0005u32, 0x887A0006, 0x887A0007, 0x887A0020] {
            assert_eq!(present_outcome(result as i32), PresentOutcome::DeviceLost);
        }
        assert_eq!(present_outcome(0x887A0026u32 as i32), PresentOutcome::SurfaceLost);
        assert_eq!(present_outcome(0x8007000Eu32 as i32), PresentOutcome::OutOfMemory);
        assert_eq!(present_outcome(0x887A0001u32 as i32), PresentOutcome::Failure);
    }
}
